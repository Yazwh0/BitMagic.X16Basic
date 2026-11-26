import { test } from 'node:test';
import * as assert from 'node:assert/strict';
import { x16Key } from '../keys';

test('maps browser key codes to the X16 key names', () => {
    assert.equal(x16Key('KeyA'), 'A');
    assert.equal(x16Key('Digit7'), 'Number7');
    assert.equal(x16Key('Numpad3'), 'Keypad3');
    assert.equal(x16Key('F12'), 'F12');
    assert.equal(x16Key('Enter'), 'Enter');
    assert.equal(x16Key('ArrowLeft'), 'Left');
    assert.equal(x16Key('Quote'), 'Apostrophe');
    assert.equal(x16Key('Backquote'), 'GraveAccent');
    assert.equal(x16Key('MetaLeft'), 'SuperLeft');
});

test('ignores keys the X16 does not have', () => {
    assert.equal(x16Key('F13'), undefined);
    assert.equal(x16Key('AudioVolumeUp'), undefined);
    assert.equal(x16Key('KeyAB'), undefined);
});
