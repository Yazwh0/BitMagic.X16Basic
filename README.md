# BitMagic.X16Basic

Debugging Commander X16 BASIC in VS Code, built on the [BitMagic](https://bitmagic.org) debugger and emulator: breakpoints (with conditions and log points), stepping, the GOSUB call stack, variables and arrays, watches, stop on error, stepping into SYS, and a live view while the program runs.

See [plan.md](plan.md) for the design and where it's up to, and [vscode/README.md](vscode/README.md) for what the extension does.

## Layout

- `src/BitMagic.X16Basic`: the BASIC library. Tokeniser (byte for byte the same as the ROM's), detokeniser, source maps, decoders for BASIC's variables, arrays, numbers and stack frames, an expression evaluator, and finding BASIC's addresses in a ROM.
- `src/BitMagic.X16Basic.Debugger`: the debug mode X16D loads.
- `vscode`: the VS Code extension.
- `tests/BitMagic.X16Basic.Tests`: unit tests.
- `tests/BitMagic.X16Basic.RomTests`: checks the library against the real ROM running on the BitMagic emulator.
- `tests/BitMagic.X16Basic.DapTests`: end to end, X16D driven over DAP.
- `samples/hello`: `hello.bas` and a longer `demo.bas`, with launch configurations for the extension. Its `project.json` is only for running X16D without the extension (eg from X16M): the extension passes the debug mode itself, and no ROM or symbols need setting, as X16D uses the official emulator's (or its own) and the debug mode finds BASIC in the ROM.
- `spike/`: the phase 0 project used to check the ROM hooks.

## Building

BitMagic comes from a checkout next to this one (`../BitMagic`) if there is one, and from its NuGet packages if not (as in CI), at `BitMagicPackageVersion`. To choose for yourself, set `UseLocalBitMagic` to `true` or `false`, and `BitMagicRoot` for a checkout somewhere else. Put them in `Directory.Build.user.props` (copy `Directory.Build.user.props.example`; it isn't in git), the environment, or on the command line (`-p:UseLocalBitMagic=false`). The packages need a BitMagic release with debug modes. The library and its unit tests build with `dotnet`; anything that touches the emulator needs MSBuild (x64), as BitMagic's own projects do.

```sh
dotnet test tests/BitMagic.X16Basic.Tests     # unit tests
./romtests.sh                                 # against the ROM: needs ../x16-rom/build/x16 or X16_ROM_DIR
./daptests.sh                                 # builds X16D and the debug mode, then end to end

cd vscode
npm install
npm run compile && npm test
npm run debugmode                             # copies the built debug mode into the extension
```

`daptests.sh` also packs `BitMagic.X16Basic` and `BitMagic.X16Basic.Debugger` into `artifacts/packages`.

## Releasing

`.github/workflows/workflow.yml` builds and tests every push and pull request against BitMagic's NuGet packages. Push a tag such as `v0.2.0` to publish that version: both NuGet packages, and the extension to the VS Marketplace and Open VSX. NuGet and the Marketplace use trusted publishing, so their policies name `workflow.yml`; Open VSX uses the `VSCODIUM` secret, and NuGet's login the `NUGET_USER` one.

## The debug mode in a project

The VS Code extension passes the debug mode to X16D itself, so a project only needs `debugMode` when X16D runs without it (eg from X16M). X16D can get it from NuGet, downloading it once into `%LOCALAPPDATA%\BitMagic\DebugModes`:

```json
"debugMode": {
    "package": "BitMagic.X16Basic.Debugger",
    "settings": { "source": "game.bas" }
}
```

`version` picks a version (exact, or a range such as `0.2.*`; the latest release if left out) and `source` another feed or a folder of packages. While developing the mode, `assembly` points at the built DLL instead, as `samples/hello/project.json` does.

To try the extension, open `vscode` in VS Code and run "Run Extension": it opens `samples/hello`, whose settings point BitMagic at the X16D built from `../BitMagic`.

## Licence

GPL-3.0, the same as BitMagic: see [LICENSE.txt](LICENSE.txt).
