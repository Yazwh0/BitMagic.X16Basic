// Writes syntaxes/x16basic.tmLanguage.json. The keyword lists match KeywordTable.Default in
// src/BitMagic.X16Basic; run this again if they change.
const fs = require('fs');
const path = require('path');

const control = ['END', 'FOR', 'NEXT', 'GOTO', 'GO', 'TO', 'STEP', 'RUN', 'IF', 'THEN', 'GOSUB', 'RETURN', 'STOP', 'ON', 'CONT'];
const statements = [
    'DATA', 'INPUT#', 'INPUT', 'DIM', 'READ', 'LET', 'RESTORE', 'WAIT', 'LOAD', 'SAVE', 'VERIFY', 'DEF', 'POKE',
    'PRINT#', 'PRINT', 'LIST', 'CLR', 'CMD', 'SYS', 'OPEN', 'CLOSE', 'GET', 'NEW',
    // extended
    'MON', 'DOS', 'OLD', 'GEOS', 'VPOKE', 'VLOAD', 'SCREEN', 'PSET', 'LINE', 'FRAME', 'RECT', 'CHAR', 'MOUSE',
    'COLOR', 'TEST', 'RESET', 'CLS', 'CODEX', 'LOCATE', 'BOOT', 'KEYMAP', 'BLOAD', 'BVLOAD', 'BVERIFY', 'BANK',
    'FMINIT', 'FMNOTE', 'FMDRUM', 'FMINST', 'FMVIB', 'FMFREQ', 'FMVOL', 'FMPAN', 'FMPLAY', 'FMCHORD', 'FMPOKE',
    'PSGINIT', 'PSGNOTE', 'PSGVOL', 'PSGWAV', 'PSGFREQ', 'PSGPAN', 'PSGPLAY', 'PSGCHORD',
    'REBOOT', 'POWEROFF', 'I2CPOKE', 'SLEEP', 'BSAVE', 'MENU', 'REN', 'LINPUT#', 'LINPUT', 'BINPUT#', 'HELP',
    'BANNER', 'EXEC', 'TILE', 'EDIT', 'SPRITE', 'SPRMEM', 'MOVSPR', 'BASLOAD', 'OVAL', 'RING',
];
const functions = [
    'TAB(', 'SPC(', 'FN', 'SGN', 'INT', 'ABS', 'USR', 'FRE', 'POS', 'SQR', 'RND', 'LOG', 'EXP', 'COS', 'SIN', 'TAN',
    'ATN', 'PEEK', 'LEN', 'STR$', 'VAL', 'ASC', 'CHR$', 'LEFT$', 'RIGHT$', 'MID$',
    'VPEEK', 'MX', 'MY', 'MB', 'JOY', 'HEX$', 'BIN$', 'I2CPEEK', 'POINTER', 'STRPTR', 'RPT$', 'MWHEEL', 'TDATA', 'TATTR',
];
const operators = ['AND', 'OR', 'NOT'];

// longest first, so INPUT# wins over INPUT as it does in the ROM
const alternation = words => words
    .slice()
    .sort((a, b) => b.length - a.length)
    .map(w => w.replace(/[$#(]/g, c => '\\' + c))
    .join('|');

const grammar = {
    $schema: 'https://raw.githubusercontent.com/martinring/tmlanguage/master/tmlanguage.json',
    name: 'X16 BASIC',
    scopeName: 'source.x16basic',
    patterns: [
        { include: '#lineNumber' },
        { include: '#statement' },
    ],
    repository: {
        lineNumber: {
            match: '^\\s*([0-9][0-9 ]*)',
            captures: { 1: { name: 'entity.name.label.line-number.x16basic' } },
        },
        statement: {
            patterns: [
                { include: '#string' },
                { include: '#rem' },
                { include: '#data' },
                { include: '#keywords' },
                { include: '#number' },
                { include: '#variable' },
                { include: '#operator' },
                { match: ':', name: 'punctuation.separator.statement.x16basic' },
                { match: ',|;', name: 'punctuation.separator.x16basic' },
            ],
        },
        string: {
            begin: '"',
            end: '"|$',
            name: 'string.quoted.double.x16basic',
            patterns: [
                { match: '\\{[^}]*\\}', name: 'constant.character.escape.x16basic' },
            ],
        },
        // like the ROM, REM is found anywhere outside quotes, and everything after it is the comment
        rem: {
            match: '(?i)(REM)(.*)$',
            captures: {
                1: { name: 'keyword.other.rem.x16basic' },
                2: { name: 'comment.line.rem.x16basic' },
            },
        },
        data: {
            begin: '(?i)(DATA)',
            beginCaptures: { 1: { name: 'keyword.other.data.x16basic' } },
            end: '(?=:)|$',
            patterns: [
                { include: '#string' },
                { include: '#number' },
                { match: ',', name: 'punctuation.separator.x16basic' },
                { match: '[^,:"]+', name: 'string.unquoted.data.x16basic' },
            ],
        },
        keywords: {
            patterns: [
                { match: `(?i)(${alternation(control)})`, name: 'keyword.control.x16basic' },
                { match: `(?i)(${alternation(functions)})`, name: 'support.function.x16basic' },
                { match: `(?i)(${alternation(operators)})`, name: 'keyword.operator.word.x16basic' },
                { match: `(?i)(${alternation(statements)})`, name: 'keyword.other.x16basic' },
                { match: '\\?', name: 'keyword.other.print.x16basic' },
                { match: 'π|\\{PI\\}', name: 'constant.language.pi.x16basic' },
            ],
        },
        number: {
            patterns: [
                { match: '\\$[0-9A-Fa-f]+', name: 'constant.numeric.hex.x16basic' },
                { match: '%[01]+', name: 'constant.numeric.binary.x16basic' },
                { match: '(?:[0-9]+\\.?[0-9]*|\\.[0-9]+)(?:[eE][-+]?[0-9]+)?', name: 'constant.numeric.decimal.x16basic' },
            ],
        },
        variable: {
            match: '[A-Za-z][A-Za-z0-9]*[$%]?',
            name: 'variable.other.x16basic',
        },
        operator: {
            match: '<>|<=|>=|[-+*/^=<>]',
            name: 'keyword.operator.x16basic',
        },
    },
};

const out = path.join(__dirname, '..', 'syntaxes', 'x16basic.tmLanguage.json');
fs.writeFileSync(out, JSON.stringify(grammar, null, 4) + '\n');
console.log(`Wrote ${out}`);
