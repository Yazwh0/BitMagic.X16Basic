# BitMagic X16 BASIC

Debug Commander X16 BASIC programs in VS Code, running on the BitMagic emulator.

- **Breakpoints** on lines, with conditions (`I=3`), hit counts and log points (`I is {I}`).
- **Stepping** a statement at a time: into, over (a GOSUB runs without stopping) and out (back to after the GOSUB).
- **Step into a `SYS`** to stop on the first instruction of the machine code, in BitMagic's assembly view.
- **Call stack** of GOSUBs, each showing the statement it came from.
- **Variables**, **arrays** (expand to see each element), active **FOR loops**, and BASIC's own pointers and free memory.
- **Watches and hovers** take BASIC expressions: `A+B%`, `D(2)`, `LEFT$(N$,3)`, `PEEK($9F20)`.
- **Change variables** while stopped, numbers and strings.
- **Stops on errors**, with BASIC's message: `?DIVISION BY ZERO ERROR IN 20`.
- **X16 BASIC Live** in the Run and Debug view shows the line and variables while the program runs, and the line it's on is highlighted in the editor.
- If the program in memory isn't your file any more (changed at READY, or LOADed), it's shown as a listing of what's really there.

Editing: syntax colouring for all X16 keywords, problems shown as you type (missing line numbers, duplicates, lines out of order, GOTOs to lines that don't exist), hovers for keywords and for the line a GOTO goes to, and Go to Definition on a line number.

## Use

Open a `.bas` file written with line numbers and press F5, or use the Debug button on the editor's title bar. To set it up in `launch.json`:

```json
{
    "type": "x16basic",
    "request": "launch",
    "name": "Debug BASIC",
    "program": "${workspaceFolder}/game.bas"
}
```

| Setting | |
|---|---|
| `program` | The BASIC file. |
| `project` | A BitMagic `project.json` to take the ROM, SD card and so on from. |
| `charset` | `upper` (default) or `mixed`: whether letters in strings keep their case. |
| `onProgramEnd` | `ready` (default) stays at READY so you can RUN again; `terminate` ends the session. |
| `stopOnEntry` | Stop on the first statement. |
| `run` | `false` to load the program and leave it at READY. |
| `romFile`, `symbols` | A different ROM, and its `basic.sym` if needed. Not normally needed: BASIC is found in the ROM. |

Strings can hold control codes the petcat way: `PRINT "{CLR}{RED}HELLO"`, or `{$93}` by number.

## Needs

The [BitMagic X16 Debugger](https://marketplace.visualstudio.com/items?itemName=yazwh0.bitmagic) extension, with an X16D that supports debug modes.
