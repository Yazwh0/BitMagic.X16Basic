import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import * as crypto from 'crypto';
import { BasicLaunchConfiguration, bitMagicConfiguration, buildProject, debugModeAssembly, parseJsonWithComments } from './launch';
import { LiveMonitor } from './live';
import { ScreenPanel } from './screen';
import { registerEditorSupport } from './editor';
import { writeProject } from './template';
import { registerPrgCommands } from './prg';

let output: vscode.OutputChannel;

export function activate(context: vscode.ExtensionContext) {
    output = vscode.window.createOutputChannel('X16 BASIC');

    // BitMagic only checks for a new X16D when it activates, and on its own it wouldn't until a debug session
    // starts, by when the old X16D is already running. So wake it up now, while a BASIC folder is opening.
    vscode.extensions.getExtension('yazwh0.bitmagic')?.activate().then(undefined, e => output.appendLine(`Couldn't start the BitMagic extension: ${e}`));
    const live = new LiveMonitor();
    const screen = new ScreenPanel();
    registerEditorSupport(context);
    registerPrgCommands(context, output);

    context.subscriptions.push(
        output,
        live,
        screen,
        vscode.window.registerWebviewViewProvider(LiveMonitor.viewId, live),
        vscode.debug.registerDebugConfigurationProvider('x16basic', new BasicConfigurationProvider(context)),
        vscode.debug.registerDebugConfigurationProvider('x16basic', new DynamicConfigurationProvider(), vscode.DebugConfigurationProviderTriggerKind.Dynamic),
        vscode.commands.registerCommand('x16basic.debugFile', (uri?: vscode.Uri) => startFile(uri, false)),
        vscode.commands.registerCommand('x16basic.runFile', (uri?: vscode.Uri) => startFile(uri, true)),
        vscode.commands.registerCommand('x16basic.showScreen', () => screen.show()),
        vscode.commands.registerCommand('x16basic.createProject', createProject),
    );
}

export function deactivate() {
}

async function startFile(uri: vscode.Uri | undefined, noDebug: boolean) {
    const file = uri ?? vscode.window.activeTextEditor?.document.uri;
    if (!file)
        return;

    await vscode.workspace.saveAll(false);
    const folder = vscode.workspace.getWorkspaceFolder(file);
    await vscode.debug.startDebugging(folder, {
        type: 'x16basic',
        request: 'launch',
        name: `Debug ${path.basename(file.fsPath)}`,
        program: file.fsPath,
    }, { noDebug });
}

/** Writes a starter project into a folder the user picks, then opens it. */
async function createProject() {
    const picked = await vscode.window.showOpenDialog({
        canSelectFolders: true,
        canSelectFiles: false,
        openLabel: 'Create X16 BASIC project here',
    });
    if (!picked || picked.length === 0)
        return;

    const folder = picked[0].fsPath;
    try {
        const { skipped } = writeProject(folder);
        if (skipped.length > 0)
            vscode.window.showWarningMessage(`X16 BASIC: ${skipped.join(' and ')} ${skipped.length > 1 ? 'were' : 'was'} already there, so left as it was.`);
    } catch (e: any) {
        vscode.window.showErrorMessage(`X16 BASIC: couldn't create the project: ${e.message}`);
        return;
    }

    await vscode.commands.executeCommand('vscode.openFolder', vscode.Uri.file(folder), { forceNewWindow: !!vscode.workspace.workspaceFolders?.length });
}

/** Offers "Debug <file>" for open BASIC files in the Run and Debug picker. */
class DynamicConfigurationProvider implements vscode.DebugConfigurationProvider {
    provideDebugConfigurations(): vscode.DebugConfiguration[] {
        return vscode.workspace.textDocuments
            .filter(d => d.languageId === 'x16basic' && !d.isUntitled)
            .map(d => ({ type: 'x16basic', request: 'launch', name: `Debug ${path.basename(d.fileName)}`, program: d.fileName }));
    }
}

class BasicConfigurationProvider implements vscode.DebugConfigurationProvider {
    constructor(private readonly context: vscode.ExtensionContext) {
    }

    resolveDebugConfiguration(_folder: vscode.WorkspaceFolder | undefined, config: vscode.DebugConfiguration): vscode.DebugConfiguration | undefined {
        // F5 with no launch.json: debug the BASIC file that's open
        if (!config.type && !config.request && !config.name) {
            const editor = vscode.window.activeTextEditor;
            if (editor?.document.languageId !== 'x16basic')
                return config;

            return { type: 'x16basic', request: 'launch', name: 'Debug BASIC', program: editor.document.fileName };
        }
        return config;
    }

    async resolveDebugConfigurationWithSubstitutedVariables(folder: vscode.WorkspaceFolder | undefined, config: vscode.DebugConfiguration): Promise<vscode.DebugConfiguration | undefined> {
        const basic = config as vscode.DebugConfiguration & BasicLaunchConfiguration;

        if (!basic.program || !fs.existsSync(basic.program)) {
            vscode.window.showErrorMessage(`X16 BASIC: the program '${basic.program ?? ''}' doesn't exist.`);
            return undefined;
        }

        const workspaceFolder = folder?.uri.fsPath ?? path.dirname(basic.program);
        const resolve = (p: string) => path.isAbsolute(p) ? p : path.join(workspaceFolder, p);

        const assembly = debugModeAssembly(vscode.workspace.getConfiguration().get<string>('bitMagicX16Basic.debugModePath'), this.context.extensionPath);
        if (!fs.existsSync(assembly)) {
            vscode.window.showErrorMessage(`X16 BASIC: the debugger isn't at '${assembly}'. Reinstall the extension, or check bitMagicX16Basic.debugModePath.`);
            return undefined;
        }

        let baseProject: any;
        let projectFolder: string | undefined;
        if (basic.project) {
            const projectPath = resolve(basic.project);
            try {
                baseProject = parseJsonWithComments(fs.readFileSync(projectPath, 'utf8'));
                projectFolder = path.dirname(projectPath);
            } catch (e: any) {
                vscode.window.showErrorMessage(`X16 BASIC: couldn't read '${projectPath}': ${e.message}`);
                return undefined;
            }
        }

        const project = buildProject(
            { ...basic, program: resolve(basic.program), romFile: basic.romFile ? resolve(basic.romFile) : undefined, symbols: basic.symbols ? resolve(basic.symbols) : undefined },
            assembly, baseProject, projectFolder);

        // the project goes in the extension's storage, named for the program so each has its own
        const storage = (this.context.storageUri ?? this.context.globalStorageUri).fsPath;
        fs.mkdirSync(storage, { recursive: true });
        const hash = crypto.createHash('sha1').update(path.resolve(basic.program).toLowerCase()).digest('hex').slice(0, 12);
        const projectFile = path.join(storage, `${path.basename(basic.program, path.extname(basic.program))}-${hash}.json`);
        fs.writeFileSync(projectFile, JSON.stringify(project, null, 4));

        output.appendLine(`Debugging ${basic.program} (project ${projectFile})`);

        // BitMagic's adapter runs X16D, which loads the BASIC debug mode from the project
        return bitMagicConfiguration(config.name ?? 'Debug BASIC', projectFile, workspaceFolder);
    }
}
