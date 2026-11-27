// Saving BASIC as .PRG files the X16 can LOAD, and opening .PRG files (or the program in the emulator) as BASIC.
import * as vscode from 'vscode';
import * as path from 'path';
import { runTool } from './tool';
import { isBasicSession } from './live';
import { basPathFor, prgPathFor } from './prgPaths';

function charset(): string {
    return vscode.workspace.getConfiguration('bitMagicX16Basic').get<string>('charset', 'upper');
}

export function registerPrgCommands(context: vscode.ExtensionContext, output: vscode.OutputChannel) {
    // the tool's own messages, eg the tokeniser's problems, then a short one to show
    const report = (stderr: string, message: string) => {
        if (stderr.trim())
            output.appendLine(stderr.trimEnd());
        vscode.window.showErrorMessage(`X16 BASIC: ${message}`, 'Show Output').then(c => { if (c) output.show(); });
    };

    context.subscriptions.push(
        vscode.commands.registerCommand('x16basic.savePrg', async (uri?: vscode.Uri) => {
            const source = uri ?? vscode.window.activeTextEditor?.document.uri;
            if (!source || source.scheme !== 'file')
                return void vscode.window.showWarningMessage('X16 BASIC: open a BASIC file to save as a .PRG.');

            const open = vscode.workspace.textDocuments.find(d => d.uri.toString() === source.toString());
            if (open?.isDirty)
                await open.save();

            const target = await vscode.window.showSaveDialog({
                defaultUri: vscode.Uri.file(prgPathFor(source.fsPath)),
                filters: { 'X16 programs': ['prg', 'PRG'] },
                saveLabel: 'Save as PRG',
            });
            if (!target)
                return;

            const result = await runTool(context, ['tokenise', source.fsPath, '-o', target.fsPath, '--charset', charset()]);
            if (result.code !== 0)
                return report(result.stderr, `couldn't save ${path.basename(target.fsPath)}: ${firstLine(result.stderr)}`);

            output.appendLine(result.stderr.trimEnd());
            vscode.window.showInformationMessage(`X16 BASIC: saved ${path.basename(target.fsPath)}.`);
        }),

        vscode.commands.registerCommand('x16basic.openPrg', async (uri?: vscode.Uri) => {
            const picked = uri ? [uri] : await vscode.window.showOpenDialog({
                canSelectMany: false,
                filters: { 'X16 programs': ['prg', 'PRG'], 'All files': ['*'] },
                openLabel: 'Open as BASIC',
            });
            if (!picked?.length)
                return;

            const result = await runTool(context, ['detokenise', picked[0].fsPath, '-o', '-', '--charset', charset()]);
            if (result.code !== 0)
                return report(result.stderr, `couldn't open ${path.basename(picked[0].fsPath)}: ${firstLine(result.stderr)}`);

            await openBasic(result.stdout.toString('utf8'), basPathFor(picked[0].fsPath));
        }),

        vscode.commands.registerCommand('x16basic.openProgramInMemory', async () => {
            const program = await programInMemory();
            if (program)
                await openBasic(program.text);
        }),

        vscode.commands.registerCommand('x16basic.saveProgramInMemory', async () => {
            const program = await programInMemory();
            if (!program)
                return;

            const folder = vscode.workspace.workspaceFolders?.[0]?.uri;
            const target = await vscode.window.showSaveDialog({
                defaultUri: folder ? vscode.Uri.joinPath(folder, 'PROGRAM.prg') : undefined,
                filters: { 'X16 programs': ['prg', 'PRG'] },
                saveLabel: 'Save as PRG',
            });
            if (!target)
                return;

            await vscode.workspace.fs.writeFile(target, Buffer.from(program.prg, 'base64'));
            vscode.window.showInformationMessage(`X16 BASIC: saved ${path.basename(target.fsPath)}, ${program.lines} lines.`);
        }),
    );
}

interface ProgramInMemory { prg: string; text: string; lines: number; error?: string; }

/** The program in the emulator, from the debug mode; undefined (having said why) if there isn't one. */
async function programInMemory(): Promise<ProgramInMemory | undefined> {
    const session = vscode.debug.activeDebugSession;
    if (!isBasicSession(session)) {
        vscode.window.showWarningMessage('X16 BASIC: this needs a BASIC program being debugged.');
        return undefined;
    }

    try {
        const response = await session.customRequest('x16Mode', { command: 'program' });
        const program = response?.Result as ProgramInMemory | undefined; // X16D's responses are PascalCase
        if (!program || program.error) {
            vscode.window.showWarningMessage(`X16 BASIC: ${program?.error ?? "couldn't get the program from the emulator."}`);
            return undefined;
        }
        return program;
    } catch (e: any) {
        vscode.window.showErrorMessage(`X16 BASIC: couldn't get the program from the emulator: ${e.message ?? e}`);
        return undefined;
    }
}

/** A new BASIC editor with this text. With a path, saving it suggests that file (it isn't written until then). */
async function openBasic(text: string, suggested?: string) {
    const document = suggested
        ? await vscode.workspace.openTextDocument(vscode.Uri.file(suggested).with({ scheme: 'untitled' }))
        : await vscode.workspace.openTextDocument({ language: 'x16basic' });

    const editor = await vscode.window.showTextDocument(document);
    await editor.edit(e => e.insert(new vscode.Position(0, 0), text));
    if (document.languageId !== 'x16basic')
        await vscode.languages.setTextDocumentLanguage(document, 'x16basic');
}

function firstLine(text: string): string {
    return text.split(/\r?\n/).find(l => l.trim()) ?? 'unknown error';
}
