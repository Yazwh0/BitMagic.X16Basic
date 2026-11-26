// The X16's screen in a VS Code tab, with typing: X16D's getScreen request a number of times a second while
// the program runs (and once at each stop), and keys pressed in the tab sent with its keyboardInput request.
import * as vscode from 'vscode';
import { isBasicSession } from './live';
import { x16Key } from './keys';

const framesPerSecond = 20;

export class ScreenPanel implements vscode.Disposable {
    private panel?: vscode.WebviewPanel;
    private timer?: NodeJS.Timeout;
    private readonly running = new Set<string>();
    private busy = false;
    private keys: Promise<unknown> = Promise.resolve();   // key events go one after another, in order
    private readonly disposables: vscode.Disposable[] = [];

    constructor() {
        this.disposables.push(
            vscode.debug.registerDebugAdapterTrackerFactory('bmasm', { createDebugAdapterTracker: s => this.tracker(s) }),
            vscode.debug.onDidStartDebugSession(s => {
                if (isBasicSession(s) && vscode.workspace.getConfiguration('bitMagicX16Basic').get<boolean>('openScreen', true))
                    this.show(true);
            }),
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

    /** Opens the tab beside the code, or brings it forward. */
    show(preserveFocus = false) {
        if (this.panel) {
            this.panel.reveal(undefined, preserveFocus);
            return;
        }

        this.panel = vscode.window.createWebviewPanel('x16basic.screen', 'X16 Screen',
            { viewColumn: vscode.ViewColumn.Beside, preserveFocus },
            { enableScripts: true, retainContextWhenHidden: true });
        this.panel.webview.html = this.html();
        this.panel.webview.onDidReceiveMessage(m => this.onMessage(m));
        this.panel.onDidChangeViewState(() => this.refresh());
        this.panel.onDidDispose(() => { this.panel = undefined; this.refresh(); });
        this.refresh();
    }

    /** Polls while the active BASIC session runs and the tab can be seen; one frame when it stops. */
    private refresh() {
        const session = vscode.debug.activeDebugSession;
        const polling = !!this.panel?.visible && isBasicSession(session) && this.running.has(session.id);

        if (polling && !this.timer)
            this.timer = setInterval(() => this.frame(), 1000 / framesPerSecond);
        else if (!polling && this.timer) {
            clearInterval(this.timer);
            this.timer = undefined;
        }

        if (!this.panel)
            return;
        if (isBasicSession(session))
            this.frame();
        else
            this.panel.webview.postMessage({ type: 'none' });
    }

    private async frame() {
        const session = vscode.debug.activeDebugSession;
        if (!this.panel || !isBasicSession(session) || this.busy)
            return;

        this.busy = true;
        try {
            const response = await session.customRequest('getScreen', {});
            if (response?.display)
                this.panel?.webview.postMessage({ type: 'frame', png: response.display, running: this.running.has(session.id) });
        } catch {
            // the session is ending
        } finally {
            this.busy = false;
        }
    }

    private onMessage(message: any) {
        if (message?.type !== 'key')
            return;

        const session = vscode.debug.activeDebugSession;
        const key = x16Key(message.code);
        if (!isBasicSession(session) || !key)
            return;

        this.keys = this.keys
            .then(() => session.customRequest('keyboardInput', { key, down: !!message.down }))
            .catch(() => undefined);
    }

    private html(): string {
        const nonce = Math.random().toString(36).slice(2);
        return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data:; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<style>
    html, body { height: 100%; margin: 0; }
    body { display: flex; flex-direction: column; background: var(--vscode-editor-background); color: var(--vscode-descriptionForeground);
        font-family: var(--vscode-font-family); font-size: var(--vscode-font-size); }
    #screen { flex: 1; min-height: 0; display: flex; align-items: center; justify-content: center; outline: none; padding: 8px; }
    #screen img { max-width: 100%; max-height: 100%; aspect-ratio: 4 / 3; image-rendering: pixelated; background: #000; }
    #screen:focus img { outline: 1px solid var(--vscode-focusBorder); }
    #status { padding: 2px 8px 6px; }
</style>
</head>
<body>
<div id="screen" tabindex="0"><img id="image" alt="X16 screen"></div>
<div id="status">No BASIC program is being debugged.</div>
<script nonce="${nonce}">
    const vscode = acquireVsCodeApi();
    const screen = document.getElementById('screen');
    const image = document.getElementById('image');
    const status = document.getElementById('status');
    const held = new Set();
    let running = false;

    function showStatus() {
        const focus = document.activeElement === screen ? 'Typing goes to the X16' : 'Click the screen to type';
        status.textContent = (running ? 'Running' : 'Stopped') + ' · ' + focus;
    }

    function send(code, down) {
        if (down) held.add(code); else held.delete(code);
        vscode.postMessage({ type: 'key', code, down });
    }

    screen.addEventListener('keydown', e => {
        e.preventDefault();
        if (!e.repeat)  // the emulator repeats held keys itself
            send(e.code, true);
    });
    screen.addEventListener('keyup', e => { e.preventDefault(); send(e.code, false); });
    // let go of anything still held, or the X16 thinks it's still down
    screen.addEventListener('blur', () => { for (const code of [...held]) send(code, false); showStatus(); });
    screen.addEventListener('focus', showStatus);
    screen.addEventListener('mousedown', () => screen.focus());

    window.addEventListener('message', e => {
        const m = e.data;
        if (m.type === 'frame') {
            image.src = 'data:image/png;base64,' + m.png;
            running = m.running;
            showStatus();
        } else if (m.type === 'none') {
            status.textContent = 'No BASIC program is being debugged.';
        }
    });
</script>
</body>
</html>`;
    }

    dispose() {
        if (this.timer)
            clearInterval(this.timer);
        this.panel?.dispose();
        this.disposables.forEach(d => d.dispose());
    }
}
