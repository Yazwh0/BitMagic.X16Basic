import { test } from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { launchJson, projectFiles, writeProject } from '../template';
import { analyse } from '../analysis';

test('writes the project into an empty folder', () => {
    const folder = fs.mkdtempSync(path.join(os.tmpdir(), 'x16basic-template-'));
    const { written, skipped } = writeProject(folder);

    assert.deepEqual(written.sort(), Object.keys(projectFiles).sort());
    assert.deepEqual(skipped, []);
    assert.ok(fs.existsSync(path.join(folder, 'main.bas')));
    assert.equal(JSON.parse(fs.readFileSync(path.join(folder, '.vscode', 'launch.json'), 'utf8')).configurations[0].type, 'x16basic');
});

test('leaves files that are already there', () => {
    const folder = fs.mkdtempSync(path.join(os.tmpdir(), 'x16basic-template-'));
    fs.writeFileSync(path.join(folder, 'main.bas'), '10 REM MINE\n');

    const { written, skipped } = writeProject(folder);

    assert.deepEqual(skipped, ['main.bas']);
    assert.deepEqual(written, ['.vscode/launch.json']);
    assert.equal(fs.readFileSync(path.join(folder, 'main.bas'), 'utf8'), '10 REM MINE\n');
});

test('the starter program has no problems', () => {
    assert.deepEqual(analyse(projectFiles['main.bas']), []);
    assert.ok(JSON.parse(launchJson));
});
