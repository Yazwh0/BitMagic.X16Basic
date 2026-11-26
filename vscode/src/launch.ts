// Turns an x16basic launch configuration into the BitMagic X16D project that runs it. No vscode
// dependency, so it can be tested on its own.
import * as path from 'path';

export interface BasicLaunchConfiguration {
    program: string;
    project?: string;
    charset?: 'upper' | 'mixed';
    onProgramEnd?: 'ready' | 'terminate';
    stopOnEntry?: boolean;
    run?: boolean;
    renderOnStop?: boolean;
    romFile?: string;
    symbols?: string;
}

/** The settings the debug mode reads, see BasicSettings.cs. */
export interface DebugModeSettings {
    source: string;
    charset: string;
    onProgramEnd: string;
    stopOnEntry: boolean;
    run: boolean;
    renderOnStop: boolean;
    symbols?: string;
}

/**
 * Builds the X16D project: the user's project if they gave one, otherwise a minimal one, with its
 * debugMode set to the BASIC debug mode.
 * @param baseProject the parsed contents of config.project, if there is one
 * @param projectFolder where config.project is, as relative paths in it are from there
 */
export function buildProject(config: BasicLaunchConfiguration, debugModeAssembly: string, baseProject?: any, projectFolder?: string): any {
    const project: any = baseProject ? { ...baseProject } : {};

    // the BASIC mode does its own stepping and loading, so X16D shouldn't stop at the reset vector
    // or write an AUTOBOOT.X16 for a program that isn't there
    project.startStepping = false;
    project.autobootRun = false;

    if (baseProject && projectFolder && !project.basePath)
        project.basePath = projectFolder;

    if (config.romFile)
        project.romFile = config.romFile;

    const settings: DebugModeSettings = {
        source: path.resolve(config.program),
        charset: config.charset ?? 'upper',
        onProgramEnd: config.onProgramEnd ?? 'ready',
        stopOnEntry: config.stopOnEntry ?? false,
        run: config.run ?? true,
        renderOnStop: config.renderOnStop ?? true,
    };
    if (config.symbols)
        settings.symbols = path.resolve(config.symbols);

    project.debugMode = { assembly: debugModeAssembly, settings };
    return project;
}

/**
 * The bmasm configuration BitMagic's own debug adapter is started with.
 */
export function bitMagicConfiguration(name: string, projectFile: string, workspaceFolder: string): any {
    return {
        type: 'bmasm',
        request: 'launch',
        name,
        program: projectFile,
        cwd: workspaceFolder,
        stopOnEntry: false,
        debugArgs: [],
        x16basic: true,
    };
}

/**
 * Parses JSON with comments and trailing commas, as project.json files are allowed to have.
 */
export function parseJsonWithComments(text: string): any {
    let result = '';
    let inString = false;
    for (let i = 0; i < text.length; i++) {
        const c = text[i];
        const next = text[i + 1];

        if (inString) {
            result += c;
            if (c === '\\') {
                result += next ?? '';
                i++;
            } else if (c === '"') {
                inString = false;
            }
            continue;
        }

        if (c === '"') {
            inString = true;
            result += c;
        } else if (c === '/' && next === '/') {
            while (i < text.length && text[i] !== '\n')
                i++;
            result += '\n';
        } else if (c === '/' && next === '*') {
            i += 2;
            while (i < text.length && !(text[i] === '*' && text[i + 1] === '/'))
                i++;
            i++;
        } else {
            result += c;
        }
    }

    // trailing commas
    result = result.replace(/,(\s*[}\]])/g, '$1');
    return JSON.parse(result);
}

/** Where the debug mode assembly is, from the setting (a dll or the folder it's in) or the extension. */
export function debugModeAssembly(setting: string | undefined, extensionPath: string): string {
    const name = 'BitMagic.X16Basic.Debugger.dll';
    if (setting && setting.trim().length > 0)
        return setting.toLowerCase().endsWith('.dll') ? setting : path.join(setting, name);
    return path.join(extensionPath, 'debugmode', name);
}
