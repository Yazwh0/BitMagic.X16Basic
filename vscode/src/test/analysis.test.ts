import { test } from 'node:test';
import * as assert from 'node:assert/strict';
import { analyse, codeRanges, inputLength, lineReferences, parseLines } from '../analysis';
import { helpFor } from '../keywords';

const messages = (source: string) => analyse(source).map(p => `${p.line + 1}: ${p.message}`);

test('a good program has no problems', () => {
    assert.deepEqual(messages('10 PRINT "HI"\n20 GOSUB 100:GOTO 10\n\n100 RETURN\n'), []);
});

test('lines without numbers, and numbers too big', () => {
    assert.deepEqual(messages('PRINT "HI"\n64000 END\n63999 END'), [
        '1: Line has no line number.',
        '2: Line number is over 63999.',
    ]);
});

test('duplicates, order and empty lines', () => {
    assert.deepEqual(messages('20 A=1\n10 B=2\n20 C=3\n30'), [
        '2: Line 10 comes after line 20, but BASIC will put it before.',
        '3: Line 20 is also on line 1; this one replaces it.',
        '4: Line 30 is empty, so it isn\'t stored.',
    ]);
});

test('missing targets for GOTO, GOSUB, THEN and ON lists', () => {
    assert.deepEqual(messages('10 GOTO 99\n20 IF A THEN 98\n30 ON X GOSUB 10,97,20\n40 GO TO 96'), [
        '1: There\'s no line 99.',
        '2: There\'s no line 98.',
        '3: There\'s no line 97.',
        '4: There\'s no line 96.',
    ]);
});

test('numbers in strings and REMs are not targets', () => {
    assert.deepEqual(messages('10 PRINT "GOTO 99"\n20 REM GOTO 98'), []);
});

test('spaces in line numbers, as the ROM allows', () => {
    const lines = parseLines('  1 0 PRINT', []);
    assert.equal(lines[0].number, 10);
    assert.equal(lines[0].body, 'PRINT');
});

test('references give the right columns', () => {
    const text = '10 ON X GOTO 100, 200';
    const [line] = parseLines(text, []);
    const refs = lineReferences(line, text);
    assert.deepEqual(refs.map(r => [r.target, text.slice(r.start, r.end)]), [[100, '100'], [200, '200']]);
});

test('code ranges skip strings and REM', () => {
    const text = '10 A$="X":REM HI';
    assert.deepEqual(codeRanges(text).map(([s, e]) => text.slice(s, e)), ['10 A$=', ':REM']);
});

test('long lines, counting a {CODE} as one', () => {
    assert.equal(inputLength('10 PRINT"{CLR}"'), 11);
    assert.equal(messages('10 REM ' + 'X'.repeat(90)).length, 1);
});

test('keyword help', () => {
    assert.equal(helpFor('print', '#')?.syntax, 'PRINT# file, [expression][;|,]...');
    assert.equal(helpFor('PRINT', ' ')?.syntax.startsWith('PRINT ['), true);
    assert.equal(helpFor('LEFT$', '(')?.syntax, 'LEFT$(s$, n)');
    assert.equal(helpFor('ZZ', ''), undefined);
});
