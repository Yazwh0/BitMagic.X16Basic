// Editor support for BASIC files: problems as you type, and hovers for keywords and line numbers.
import * as vscode from 'vscode';
import { analyse, lineReferences, parseLines } from './analysis';
import { helpFor } from './keywords';

export function registerEditorSupport(context: vscode.ExtensionContext) {
    const diagnostics = vscode.languages.createDiagnosticCollection('x16basic');
    const pending = new Map<string, NodeJS.Timeout>();

    const check = (document: vscode.TextDocument) => {
        if (document.languageId !== 'x16basic')
            return;
        diagnostics.set(document.uri, analyse(document.getText()).map(p => {
            const d = new vscode.Diagnostic(new vscode.Range(p.line, p.start, p.line, p.end), p.message,
                p.error ? vscode.DiagnosticSeverity.Error : vscode.DiagnosticSeverity.Warning);
            d.source = 'X16 BASIC';
            return d;
        }));
    };

    // as you type, but not on every key
    const checkSoon = (document: vscode.TextDocument) => {
        const key = document.uri.toString();
        clearTimeout(pending.get(key));
        pending.set(key, setTimeout(() => { pending.delete(key); check(document); }, 300));
    };

    context.subscriptions.push(
        diagnostics,
        vscode.workspace.onDidOpenTextDocument(check),
        vscode.workspace.onDidChangeTextDocument(e => checkSoon(e.document)),
        vscode.workspace.onDidCloseTextDocument(d => diagnostics.delete(d.uri)),
        vscode.languages.registerHoverProvider('x16basic', { provideHover }),
        vscode.languages.registerDefinitionProvider('x16basic', { provideDefinition }),
    );

    vscode.workspace.textDocuments.forEach(check);
}

function findTarget(document: vscode.TextDocument, position: vscode.Position): { target: number; range: vscode.Range } | undefined {
    const text = document.lineAt(position.line).text;
    const lines = parseLines(document.getText(), []);
    const line = lines.find(l => l.line === position.line);
    if (!line)
        return undefined;

    const reference = lineReferences(line, text).find(r => position.character >= r.start && position.character <= r.end);
    return reference ? { target: reference.target, range: new vscode.Range(position.line, reference.start, position.line, reference.end) } : undefined;
}

function provideHover(document: vscode.TextDocument, position: vscode.Position): vscode.Hover | undefined {
    // GOTO 100: show line 100
    const reference = findTarget(document, position);
    if (reference) {
        const target = parseLines(document.getText(), []).find(l => l.number === reference.target);
        if (!target)
            return undefined;
        const md = new vscode.MarkdownString();
        md.appendCodeblock(document.lineAt(target.line).text.trim(), 'x16basic');
        return new vscode.Hover(md, reference.range);
    }

    const range = document.getWordRangeAtPosition(position, /[A-Za-z][A-Za-z0-9]*\$?/);
    if (!range)
        return undefined;

    const word = document.getText(range);
    const next = document.getText(new vscode.Range(range.end, range.end.translate(0, 1)));
    const help = helpFor(word, next);
    if (!help)
        return undefined;

    const md = new vscode.MarkdownString();
    md.appendCodeblock(help.syntax, 'x16basic');
    md.appendMarkdown(help.help);
    return new vscode.Hover(md, range);
}

/** Go to definition on a GOTO or GOSUB's number goes to that line. */
function provideDefinition(document: vscode.TextDocument, position: vscode.Position): vscode.Location | undefined {
    const reference = findTarget(document, position);
    if (!reference)
        return undefined;
    const target = parseLines(document.getText(), []).find(l => l.number === reference.target);
    return target ? new vscode.Location(document.uri, new vscode.Position(target.line, target.numberStart)) : undefined;
}
