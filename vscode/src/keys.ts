// Maps a browser KeyboardEvent.code to the Silk.NET.Input key name X16D's keyboardInput request takes
// (the keys BitMagic's SMC keyboard knows, see BitMagic.X16Emulator/SmcBuffer.cs). No vscode dependency.

const named: Record<string, string> = {
    Space: 'Space', Enter: 'Enter', Escape: 'Escape', Backspace: 'Backspace', Tab: 'Tab',
    ShiftLeft: 'ShiftLeft', ShiftRight: 'ShiftRight', ControlLeft: 'ControlLeft', ControlRight: 'ControlRight',
    AltLeft: 'AltLeft', AltRight: 'AltRight', MetaLeft: 'SuperLeft', MetaRight: 'SuperRight', ContextMenu: 'Menu',
    ArrowUp: 'Up', ArrowDown: 'Down', ArrowLeft: 'Left', ArrowRight: 'Right',
    Home: 'Home', End: 'End', PageUp: 'PageUp', PageDown: 'PageDown', Insert: 'Insert', Delete: 'Delete',
    Minus: 'Minus', Equal: 'Equal', BracketLeft: 'LeftBracket', BracketRight: 'RightBracket', Backslash: 'BackSlash',
    Semicolon: 'Semicolon', Quote: 'Apostrophe', Backquote: 'GraveAccent', Comma: 'Comma', Period: 'Period', Slash: 'Slash',
    CapsLock: 'CapsLock', NumLock: 'NumLock', ScrollLock: 'ScrollLock', Pause: 'Pause',
    NumpadAdd: 'KeypadAdd', NumpadSubtract: 'KeypadSubtract', NumpadMultiply: 'KeypadMultiply', NumpadDivide: 'KeypadDivide',
    NumpadDecimal: 'KeypadDecimal', NumpadEnter: 'KeypadEnter',
};

/** The X16D key name for a KeyboardEvent.code, or undefined if the X16 has no such key. */
export function x16Key(code: string): string | undefined {
    let m: RegExpMatchArray | null;
    if ((m = code.match(/^Key([A-Z])$/)))
        return m[1];
    if ((m = code.match(/^Digit([0-9])$/)))
        return 'Number' + m[1];
    if ((m = code.match(/^Numpad([0-9])$/)))
        return 'Keypad' + m[1];
    if ((m = code.match(/^F([1-9]|1[0-2])$/)))
        return code;
    return named[code];
}
