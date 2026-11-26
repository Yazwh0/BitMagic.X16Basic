import { test } from 'node:test';
import * as assert from 'node:assert/strict';
import * as path from 'path';
import { bitMagicConfiguration, buildProject, debugModeAssembly, parseJsonWithComments } from '../launch';

test('a program on its own gets a minimal project', () => {
    const project = buildProject({ program: 'C:/work/game.bas' }, 'C:/ext/debugmode/BitMagic.X16Basic.Debugger.dll');

    assert.equal(project.startStepping, false);
    assert.equal(project.autobootRun, false);
    assert.equal(project.debugMode.assembly, 'C:/ext/debugmode/BitMagic.X16Basic.Debugger.dll');
    assert.equal(project.debugMode.settings.source, path.resolve('C:/work/game.bas'));
    assert.deepEqual(
        { charset: project.debugMode.settings.charset, onProgramEnd: project.debugMode.settings.onProgramEnd, stopOnEntry: project.debugMode.settings.stopOnEntry, run: project.debugMode.settings.run },
        { charset: 'upper', onProgramEnd: 'ready', stopOnEntry: false, run: true });
});

test('settings are passed through', () => {
    const project = buildProject({ program: 'a.bas', charset: 'mixed', onProgramEnd: 'terminate', stopOnEntry: true, run: false, romFile: 'rom.bin', symbols: 'basic.sym' }, 'mode.dll');

    assert.equal(project.romFile, 'rom.bin');
    assert.equal(project.debugMode.settings.charset, 'mixed');
    assert.equal(project.debugMode.settings.onProgramEnd, 'terminate');
    assert.equal(project.debugMode.settings.stopOnEntry, true);
    assert.equal(project.debugMode.settings.run, false);
    assert.equal(project.debugMode.settings.symbols, path.resolve('basic.sym'));
});

test("a user's project is kept, with the debug mode added", () => {
    const base = { sdCardFiles: [{ source: 'x.prg' }], startStepping: true, debugMode: { assembly: 'old.dll' } };
    const project = buildProject({ program: 'a.bas' }, 'mode.dll', base, 'C:/work');

    assert.deepEqual(project.sdCardFiles, [{ source: 'x.prg' }]);
    assert.equal(project.startStepping, false);
    assert.equal(project.basePath, 'C:/work');
    assert.equal(project.debugMode.assembly, 'mode.dll');
    assert.equal(base.debugMode.assembly, 'old.dll', 'the original is not changed');
});

test('the BitMagic configuration runs the project', () => {
    const config = bitMagicConfiguration('Debug', 'C:/s/p.json', 'C:/work');
    assert.equal(config.type, 'bmasm');
    assert.equal(config.program, 'C:/s/p.json');
    assert.equal(config.cwd, 'C:/work');
});

test('project files can have comments and trailing commas', () => {
    const parsed = parseJsonWithComments(`{
        // a comment
        "source": "http://example/x", /* block */
        "list": [1, 2,],
        "text": "a // not a comment",
    }`);
    assert.deepEqual(parsed, { source: 'http://example/x', list: [1, 2], text: 'a // not a comment' });
});

test('the debug mode comes from the setting or the extension', () => {
    assert.equal(debugModeAssembly('', 'C:/ext'), path.join('C:/ext', 'debugmode', 'BitMagic.X16Basic.Debugger.dll'));
    assert.equal(debugModeAssembly('C:/dev/bin', 'C:/ext'), path.join('C:/dev/bin', 'BitMagic.X16Basic.Debugger.dll'));
    assert.equal(debugModeAssembly('C:/dev/x.dll', 'C:/ext'), 'C:/dev/x.dll');
});
