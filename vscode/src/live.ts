// While a BASIC program runs, shows which line it's on (a highlight in the editor) and its variables
// (the X16 BASIC Live view), by asking the debug mode a few times a second.
import * as vscode from 'vscode';

interface LiveVariable { name: string; value: string; type: string; }
interface LiveState {
    running: boolean;
    line: number | null;
    source?: string;
    sourceLine?: number;
    variables: LiveVariable[];
    arrays: LiveVariable[];
    freeMemory: number;
}

export function isBasicSession(session: vscode.DebugSession | undefined): session is vscode.DebugSession {
    return !!session && session.type === 'bmasm' && session.configuration.x16basic === true;
}

export class LiveMonitor implements vscode.WebviewViewProvider, vscode.Disposable {
    public static readonly viewId = 'x16basic.live';

    private readonly decoration = vscode.window.createTextEditorDecorationType({
        isWholeLine: true,
        backgroundColor: new vscode.ThemeColor('editor.rangeHighlightBackground'),
        overviewRulerColor: new vscode.ThemeColor('debugIcon.startForeground'),
        overviewRulerLane: vscode.OverviewRulerLane.Left,
    });

    private view?: vscode.WebviewView;
    private timer?: NodeJS.Timeout;
    private readonly running = new Set<string>();   // session ids that are running, not stopped
    private busy = false;
    private readonly disposables: vscode.Disposable[] = [];

    constructor() {
        this.disposables.push(
            this.decoration,
            vscode.debug.registerDebugAdapterTrackerFactory('bmasm', { createDebugAdapterTracker: s => this.tracker(s) }),
            vscode.debug.onDidChangeActiveDebugSession(() => this.refresh()),
            vscode.debug.onDidTerminateDebugSession(s => { this.running.delete(s.id); this.refresh(); }),
        );
    }

    private tracker(session: vscode.DebugSession): vscode.DebugAdapterTracker | undefined {
        if (!isBasicSession(session))
            return undefined;

        return {
            onWillReceiveMessage: (message: any) => {
                if (message.type === 'request' && ['continue', 'next', 'stepIn', 'stepOut', 'configurationDone'].includes(message.command))
                    this.setRunning(session, true);
            },
            onDidSendMessage: (message: any) => {
                if (message.type === 'event' && message.event === 'stopped')
                    this.setRunning(session, false);
                else if (message.type === 'event' && message.event === 'continued')
                    this.setRunning(session, true);
            },
        };
    }

    private setRunning(session: vscode.DebugSession, running: boolean) {
        if (running)
            this.running.add(session.id);
        else
            this.running.delete(session.id);
        this.refresh();
    }

    resolveWebviewView(view: vscode.WebviewView) {
        this.view = view;
        view.webview.options = { enableScripts: true };
        view.webview.html = this.html();
        view.onDidChangeVisibility(() => this.refresh());
        view.onDidDispose(() => this.view = undefined);
        this.refresh();
    }

    /** Polls while the active BASIC session is running; asks once when it stops so the view is current. */
    private refresh() {
        const session = vscode.debug.activeDebugSession;
        const polling = isBasicSession(session) && this.running.has(session.id);

        if (polling && !this.timer)
            this.timer = setInterval(() => this.poll(), 250);
        else if (!polling && this.timer) {
            clearInterval(this.timer);
            this.timer = undefined;
        }

        if (!polling)
            this.clearDecoration();

        if (isBasicSession(session))
            this.poll(!polling);
        else
            this.post(undefined);
    }

    private async poll(stopped = false) {
        const session = vscode.debug.activeDebugSession;
        if (!isBasicSession(session) || this.busy)
            return;

        this.busy = true;
        try {
            const response = await session.customRequest('x16Mode', { command: 'live' });
            const state = response?.Result as LiveState | undefined; // X16D's responses are PascalCase
            if (!state)
                return;

            if (!stopped && this.running.has(session.id))
                this.decorate(state);
            this.post(state, stopped);
        } catch {
            // the session is ending, or it's an X16D without debug modes
        } finally {
            this.busy = false;
        }
    }

    private decorate(state: LiveState) {
        for (const editor of vscode.window.visibleTextEditors) {
            const here = state.running && state.source && state.sourceLine &&
                editor.document.uri.fsPath.toLowerCase() === state.source.toLowerCase();
            editor.setDecorations(this.decoration, here ? [new vscode.Range(state.sourceLine! - 1, 0, state.sourceLine! - 1, 0)] : []);
        }
    }

    private clearDecoration() {
        for (const editor of vscode.window.visibleTextEditors)
            editor.setDecorations(this.decoration, []);
    }

    private post(state: LiveState | undefined, stopped = false) {
        this.view?.webview.postMessage({ state, stopped });
    }

    private html(): string {
        const nonce = Math.random().toString(36).slice(2);
        return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<style>
    body { font-family: var(--vscode-font-family); font-size: var(--vscode-font-size); color: var(--vscode-foreground); padding: 0 8px; }
    .status { margin: 6px 0; color: var(--vscode-descriptionForeground); }
    .status b { color: var(--vscode-foreground); }
    h3 { font-size: 11px; text-transform: uppercase; letter-spacing: .04em; margin: 10px 0 4px; color: var(--vscode-descriptionForeground); }
    table { border-collapse: collapse; width: 100%; font-family: var(--vscode-editor-font-family); font-size: var(--vscode-editor-font-size); }
    td { padding: 1px 6px 1px 0; vertical-align: top; }
    td.name { color: var(--vscode-debugTokenExpression-name); white-space: nowrap; }
    td.value { color: var(--vscode-debugTokenExpression-value); word-break: break-all; }
    td.value.string { color: var(--vscode-debugTokenExpression-string); }
    td.value.changed { background: var(--vscode-editor-findMatchHighlightBackground); }
    .empty { color: var(--vscode-descriptionForeground); font-style: italic; }
</style>
</head>
<body>
<div class="status" id="status">No BASIC program is being debugged.</div>
<h3>Variables</h3><table id="variables"></table>
<h3>Arrays</h3><table id="arrays"></table>
<script nonce="${nonce}">
    const previous = new Map();
    function fill(id, rows) {
        const table = document.getElementById(id);
        table.textContent = '';
        if (!rows || rows.length === 0) {
            const td = table.insertRow().insertCell();
            td.className = 'empty';
            td.textContent = 'none yet';
            return;
        }
        for (const v of rows) {
            const row = table.insertRow();
            const name = row.insertCell();
            name.className = 'name';
            name.textContent = v.name;
            const value = row.insertCell();
            const key = id + ':' + v.name;
            value.className = 'value' + (v.type === 'string' ? ' string' : '') + (previous.has(key) && previous.get(key) !== v.value ? ' changed' : '');
            value.textContent = v.value;
            previous.set(key, v.value);
        }
    }
    window.addEventListener('message', e => {
        const { state, stopped } = e.data;
        const status = document.getElementById('status');
        if (!state) {
            status.textContent = 'No BASIC program is being debugged.';
            fill('variables', []); fill('arrays', []);
            return;
        }
        status.innerHTML = '';
        const where = document.createElement('b');
        where.textContent = state.running ? 'Line ' + state.line : 'At READY';
        status.append(where, document.createTextNode((stopped ? ' (stopped)' : state.running ? ' (running)' : '') + ' · ' + state.freeMemory + ' bytes free'));
        fill('variables', state.variables);
        fill('arrays', state.arrays);
    });
</script>
</body>
</html>`;
    }

    dispose() {
        if (this.timer)
            clearInterval(this.timer);
        this.disposables.forEach(d => d.dispose());
    }
}
