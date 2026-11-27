# Plan: debugging X16 BASIC in VS Code

## Status

Phases 0 to 3 are done, and most of 4 and 5. What's left: BASLOAD source, publishing (pushing the NuGet packages, the extension, and a BitMagic release with debug modes). Where the build differs from the plan below:

- **Addresses are found in the ROM** when there are no symbols (`BasicRomSymbols.Detect`), as BitMagic's own `rom.bin` has none. It finds `newstt`/`gone` from their code, which also gives `txtptr`, `curlin` and the variables around it, plus `error`, `ready`, `nmain` (and EXEC's variables), `stxtpt` (`txttab`) and the keyword lists. On the x16-rom build it gives exactly what `basic.sym` says (`DetectTests`). Symbols still win when given, and anything used is checked against the ROM's keywords first.
- **No native line bitmap.** Stopping at every statement was 200 times too slow, but the cost was X16D's per stop work (a snapshot compare of all memory), not leaving the core (22us). X16D now goes straight back into the core when a mode's breakpoint doesn't stop (`ModeCarriesOn`), and BASIC runs at full speed under the debugger (`Speed` test).
- **Breakpoints are by line number**, decided at `gone` from `curlin` and whether `txtptr` is on the line's header, so they work without a source map, including on the listing.
- **Pause stops the CPU** at once rather than at the next statement, as a program waiting for input never reaches one; the frames still show the BASIC line.
- **Extra pieces of the extension point:** `setBreakpoints` waits for a launch that's loading a mode (VS Code sends breakpoints before launch finishes); an `x16Mode` custom request with `IX16DebugMode.CustomRequest` for the live view; `ApiVersion` (against `DebugModeApi.Version`, compiled into the mode) so X16D refuses a mode built for a different version; and `debugMode.package` (with `version` and `source`), which X16D downloads from NuGet into a cache (`Modes/DebugModePackages.cs`), skipping dependencies X16D already has. The extension still bundles the mode, so VS Code needs no network.
- **The extension** contributes an `x16basic` debug type that writes an X16D project (with `debugMode`) and hands VS Code a `bmasm` configuration, so BitMagic's own adapter and views run it.

### Changes in the BitMagic repo

All in `BitMagic.X16Debugger` except the last, none committed:

- `Modes/IX16DebugMode.cs`, `Modes/DebugModeLoader.cs`, `Modes/DebugModePackages.cs`: the extension point, and loading a mode from a path or a NuGet package.
- `X16Debug.Modes.cs`: loading the mode, its host, the fast path, step and request plumbing.
- `X16Debug.cs`: `partial`, launch waits/loads the mode, hooks in the request handlers and debug loop.
- `X16DebugProject.cs`: `debugMode`. `ExceptionManager.cs`: `MODE` exception info. `CustomMessage/ModeRequest.cs`: `x16Mode`.
- `BitMagic.VSC/.../debugProject.schema.json`: `debugMode` added by hand (regenerating would also change other hand edited properties).

BitMagic's `BitMagic.X16Debugger.Tests` all pass with these (294).

## Goal

Debug Commander X16 BASIC in VS Code the way BitMagic debugs assembly: breakpoints, stepping, a call stack, variables and watches, stopping on errors, and live feedback while the program runs.

## Decisions

- **New repo and extension**, `BitMagic.X16Basic`, next to `BitMagic`. The extension declares `extensionDependencies` on BitMagic and uses its X16D, so it gets the emulator, the downloader and the existing views (memory, sprites, history) without copying them.
- **The debugging logic runs in X16D**, as a mode loaded from this repo's `BitMagic.X16Basic.Debugger`. X16D gets a small public extension point for it (below). It has to be public rather than `InternalsVisibleTo`, because the code will be published to NuGet.
- **References:** `Directory.Build.props` has `UseLocalBitMagic`: true references `../BitMagic` projects, false uses NuGet packages at `BitMagicPackageVersion`.
- **Our own tokeniser**, checked byte for byte against the ROM's `crunch` by the ROM tests. We need it anyway for the source map.
- **End of program is a launch option**, `onProgramEnd: "ready" | "terminate"`, default `ready`: stay at READY so `RUN` works again.
- **Numbered lines first**; BASLOAD style source (labels, no numbers) later.

## How the ROM tells us where we are

Checked live in the phase 0 spike and now by the ROM tests. Addresses are from `basic.sym` and are always loaded from the ROM's own file (`BasicRomSymbols.Load`), never hardcoded.

| Hook (bank 4) | Meaning |
|---|---|
| `gone` ($CC36) | About to run a statement. `curlin` is the line, the statement starts at `txtptr`+1 (`txtptr` is on the `:` or the last byte of the line header). GOTO, GOSUB, RETURN, NEXT and IF all pass through it. `curlin` high byte $FF means direct mode. |
| `newstt` ($CC00) | Start of each statement, before `curlin` is updated for a new line. Not needed now `gone` is used. |
| `error` ($C6C8) | BASIC error, number in X (`BasicErrors`). |
| `ready` ($E2DC) | Program ended, broke or errored; back to direct mode. |
| `nmain` ($C719) | Waiting for a line (or taking the next from EXEC). |

`IF A THEN D=4:E=5` runs `D=4` inside the IF statement, so the statement after THEN isn't a separate stop; `E=5` is.

**Stack frames**, newest first from S+1, when stopped at `gone`:

- GOSUB: `$8D`, `curlin`, `txtptr` (5 bytes), then the 2 byte return address `jsr gone3` pushed (`ngone1`+2).
- FOR: `$81`, variable address, STEP unpacked (exponent, 4 mantissa bytes with the top bit kept), STEP sign, TO limit (MFLPT), `curlin`, `txtptr` (18 bytes). FOR pops its return address, so nothing follows it.

**Variables:** 7 byte entries from `vartab` to `arytab`. The type is in bit 7 of the two name bytes. Floats are MFLPT; integers are high byte first; strings are length then address; FN entries hold the definition and parameter addresses. Arrays run from `arytab` to `strend`: name, total size, dimension count, the sizes high byte first and last subscript first, then elements with the first subscript changing fastest.

**Breakpoints in ROM:** the emulator core only checks the active address, and copies a bank's entries in when the bank changes, so a ROM breakpoint has to be set in the per bank table (`0x210000 + bank * 0x4000 + address - 0xC000`), as `AddressFunctions.GetMemoryLocations` does.

## Components

1. **`src/BitMagic.X16Basic`** (done): no debugger dependency.
   - `Petscii`: source text to PETSCII and back, with petcat style `{CLR}`, `{$93}` escapes; `Upper` or `Mixed` case for strings.
   - `KeywordTable`: the ROM's keyword lists, built in or read from a ROM (`FromRom`).
   - `Cruncher`: a port of `ncrnch`, quirks included.
   - `BasicTokeniser` / `BasicProgram` / `SourceMap`: numbered source to a memory image or PRG, with source columns per byte and statement offsets; `Locate(curlin, txtptr)`.
   - `Detokeniser`: LIST, from bytes or straight from memory.
   - `BasicFloat`, `BasicVariables`, `BasicStack`, `BasicErrors`, `BasicRomSymbols`.
2. **`src/BitMagic.X16Basic.Debugger`** (phase 2): implements the X16D extension point.
3. **`vscode/`** (phase 2): the extension.
4. **X16D extension point** (phase 2, in the BitMagic repo).

## X16D extension point (design)

X16D picks a mode from the launch request (`"mode": "basic"`, or by the debug type). The mode gets a host to drive the machine and answers the DAP requests it owns. Anything it returns null for falls through to X16D's normal handling, so memory views, disassembly and the rest keep working.

```csharp
public interface IX16DebugMode
{
    string Name { get; }

    // after the ROM is loaded and before the machine runs: load the program, set system breakpoints
    void Launch(IX16DebugHost host, JObject launchArguments);

    // a system breakpoint this mode set was hit; decide whether to stop
    StopDecision OnSystemBreakpoint(int bank, int address);

    // DAP requests; null means not handled here
    SetBreakpointsResponse? SetBreakpoints(SetBreakpointsArguments arguments);
    StackTraceResponse? StackTrace(StackTraceArguments arguments);
    ScopesResponse? Scopes(ScopesArguments arguments);
    VariablesResponse? Variables(VariablesArguments arguments);
    SetVariableResponse? SetVariable(SetVariableArguments arguments);
    EvaluateResponse? Evaluate(EvaluateArguments arguments);
    SourceResponse? Source(SourceArguments arguments);   // the virtual listing when there's no file

    // stepping and pause: arm the mode's own state and let the machine run; false = use X16D's
    bool Step(StepKind kind);
}

public interface IX16DebugHost
{
    Emulator Emulator { get; }
    IMemoryWriter Memory { get; }               // CPU view, RAM bank 0 at $A000
    IdManager Ids { get; }
    void SendEvent(ProtocolEvent e);
    void Log(string message);
    void SetSystemBreakpoint(int bank, int address, bool set);
    string? RomSymbolFile(int bank);             // so the mode can load basic.sym
}

public sealed record StopDecision(bool Stop, StoppedEvent.ReasonValue Reason = default, string? Description = null, IReadOnlyList<int>? HitBreakpointIds = null);
```

The hooks this needs in X16D are where `HandleSourceBreakpointHit` handles `SystemBreakpoint` now, the launch sequence after ROM load (`X16Debug.cs` ~line 450), and the request handlers.

## BASIC mode behaviour (phase 2 and 3)

- **Load:** tokenise, write the image at `txttab`, set `vartab`/`arytab`/`strend` to its end, then `RUN` (through EXEC or by setting up as `run` does).
- **Breakpoints:** a set of BASIC line numbers (later line and statement). The `gone` handler stops when `curlin` is in the set. Conditions and logpoints use the expression evaluator.
- **Stepping**, all at `gone`:
  - into: stop at the next statement;
  - over: stop at the next statement with GOSUB depth no deeper than now;
  - out: stop at the first statement with GOSUB depth shallower than now;
  - pause: stop at the next statement.
- **Call stack:** the current statement, then one frame per GOSUB (its line). FOR loops are a scope rather than frames.
- **Scopes:** Variables, Arrays, FOR loops, and System (DATA pointer, `fretop`/`memsiz` string space, `oldlin`/`oldtxt`).
- **Errors:** `error` stops as an exception with the message and line. `ready` after a program ends follows `onProgramEnd`.
- **No source file:** detokenise memory into a virtual document. The same path covers a program edited at the READY prompt, which leaves the source map stale; spot that by comparing the image.

## Performance (phase 4)

Every statement leaves the native core for `gone`, so a program runs well below full speed while being debugged. The fix is a line number bitmap (8KB, one bit per line) in the core state, tested only when PC is `gone` in bank 4, so statements without a breakpoint don't leave the core. It changes `CpuState`, so the `.asm`, `.h` and `.cs` layouts must change together.

## Phases

| Phase | Scope | State |
|---|---|---|
| 0. Spike | `gone` hook, `curlin`/`txtptr`, variable and GOSUB frame layout on the live ROM | Done (the spike project has gone; the ROM tests cover it) |
| 1. Foundations | Repo, tokeniser and detokeniser, decoders, ROM reference tests, extension point design | Done |
| 2. MVP debugger | X16D extension point; load and run `.bas`; line breakpoints; continue, pause, step into; simple variables; stop on error; program end; extension with language, grammar and launch | Done |
| 3. Full debugging | GOSUB call stack, step over and out, arrays and FOR scopes, set variable, conditions, logpoints, watches, virtual source | Done |
| 4. Performance and polish | ~~Native line bitmap~~ (not needed, see Status), live monitor view, editor hovers and problems | Done |
| 5. Later | `SYS` mixed mode stepping into assembly | Done |
| | NuGet packaging (`artifacts/packages`, loaded from a feed by `LoadsTheModeFromANuGetPackage`) | Done |
| | BASLOAD source, pushing the packages and the extension | To do |

## Tests

- `tests/BitMagic.X16Basic.Tests`: plain unit tests, `dotnet test`.
- `tests/BitMagic.X16Basic.RomTests`: boots the real ROM on the BitMagic emulator, types programs in through EXEC (so they go through the ROM's line editor and `crunch`) and checks our bytes match, then runs programs and checks the variables, arrays, stack frames, statement locations and errors we read back. Needs the native core, so build with MSBuild: `./romtests.sh`. The ROM comes from `X16_ROM_DIR`, or `../x16-rom/build/x16` by default.
- `tests/BitMagic.X16Basic.DapTests`: end to end. Builds X16D from `../BitMagic` and the debug mode, starts X16D and drives it over DAP as VS Code would: breakpoints, conditions, log points, stepping, the GOSUB stack, variables, arrays, watches, set variable, errors, terminating, the listing, the live request, stepping into SYS, BitMagic's own ROM with no symbols, and speed. Opens the emulator window while it runs. `./daptests.sh`.
- `vscode`: `npm test`, for the launch translation and the source checks.
