import { test } from 'node:test';
import * as assert from 'node:assert/strict';
import * as path from 'path';
import { basPathFor, prgPathFor } from '../prgPaths';

test('a .PRG goes next to its BASIC file', () => {
    assert.equal(prgPathFor(path.join('work', 'game.bas')), path.join('work', 'game.prg'));
    assert.equal(prgPathFor(path.join('work', 'GAME.X16BAS')), path.join('work', 'GAME.prg'));
});

test('BASIC from a .PRG goes next to it', () => {
    assert.equal(basPathFor(path.join('work', 'GAME.PRG')), path.join('work', 'GAME.bas'));
});
