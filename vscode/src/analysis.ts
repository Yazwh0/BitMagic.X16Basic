// Checks on BASIC source that don't need the debugger: line numbers and the lines GOTO and friends
// go to. No vscode dependency, so it can be tested on its own.

export interface Problem {
    line: number;       // 0 based
    start: number;      // column
    end: number;
    message: string;
    error: boolean;
}

export interface NumberedLine {
    line: number;       // 0 based source line
    number: number;     // BASIC line number
    numberStart: number;
    numberEnd: number;
    body: string;       // the text after the number
    bodyStart: number;
}

export interface LineReference {
    line: number;
    start: number;
    end: number;
    target: number;
}

export const maxLineNumber = 63999;
export const maxInputLength = 88;

/** The parts of a line outside strings and REM, where keywords count, as [start, end) ranges. */
export function codeRanges(text: string, from = 0): [number, number][] {
    const ranges: [number, number][] = [];
    let start = from;
    let inString = false;
    for (let i = from; i < text.length; i++) {
        const c = text[i];
        if (c === '"') {
            if (!inString)
                ranges.push([start, i]);
            else
                start = i + 1;
            inString = !inString;
            continue;
        }
        if (!inString && /^rem/i.test(text.slice(i, i + 3))) {
            ranges.push([start, i + 3]);
            return ranges;
        }
    }
    if (!inString)
        ranges.push([start, text.length]);
    return ranges.filter(([s, e]) => e > s);
}

/** Splits source into its numbered lines, reporting lines that aren't. */
export function parseLines(source: string, problems: Problem[]): NumberedLine[] {
    const result: NumberedLine[] = [];
    const lines = source.split(/\r?\n/);

    for (let i = 0; i < lines.length; i++) {
        const text = lines[i];
        if (text.trim().length === 0)
            continue;

        // like the ROM, spaces before and between the digits don't matter
        const m = /^(\s*)([0-9][0-9 ]*)/.exec(text);
        if (!m) {
            const start = text.length - text.trimStart().length;
            problems.push({ line: i, start, end: text.length, message: 'Line has no line number.', error: true });
            continue;
        }

        const numberStart = m[1].length;
        const digits = m[2].replace(/ /g, '');
        const number = Number(digits);
        const numberEnd = numberStart + m[2].trimEnd().length;

        if (digits.length > 5 || number > maxLineNumber) {
            problems.push({ line: i, start: numberStart, end: numberEnd, message: `Line number is over ${maxLineNumber}.`, error: true });
            continue;
        }

        result.push({ line: i, number, numberStart, numberEnd, body: text.slice(m[0].length), bodyStart: m[0].length });
    }
    return result;
}

/** Where a line refers to another by number: GOTO, GOSUB, THEN, RUN, RESTORE, ON ... GOTO lists and so on. */
export function lineReferences(line: NumberedLine, text: string): LineReference[] {
    const references: LineReference[] = [];
    const keyword = /(GO\s*TO|GOSUB|THEN|RUN|RESTORE)\s*/gi;

    for (const [start, end] of codeRanges(text, line.bodyStart)) {
        const code = text.slice(start, end);
        keyword.lastIndex = 0;
        let m: RegExpExecArray | null;
        while ((m = keyword.exec(code)) !== null) {
            // a list of numbers, as after ON X GOTO
            const list = /^(\d+)(\s*,\s*(\d+))*/.exec(code.slice(m.index + m[0].length));
            if (!list)
                continue;

            let offset = start + m.index + m[0].length;
            for (const part of list[0].split(',')) {
                const numberText = part.trim();
                const at = text.indexOf(numberText, offset);
                references.push({ line: line.line, start: at, end: at + numberText.length, target: Number(numberText) });
                offset = at + numberText.length;
            }
        }
    }
    return references;
}

/** Visible length as BASIC sees it: a {CODE} is one character. */
export function inputLength(text: string): number {
    return text.replace(/\{[^}]*\}/g, 'x').length;
}

export function analyse(source: string): Problem[] {
    const problems: Problem[] = [];
    const lines = parseLines(source, problems);
    const text = source.split(/\r?\n/);
    const numbers = new Map<number, NumberedLine>();

    let previous: NumberedLine | undefined;
    for (const line of lines) {
        const existing = numbers.get(line.number);
        if (existing)
            problems.push({ line: line.line, start: line.numberStart, end: line.numberEnd, message: `Line ${line.number} is also on line ${existing.line + 1}; this one replaces it.`, error: false });
        else if (previous && line.number < previous.number)
            problems.push({ line: line.line, start: line.numberStart, end: line.numberEnd, message: `Line ${line.number} comes after line ${previous.number}, but BASIC will put it before.`, error: false });

        if (line.body.trim().length === 0)
            problems.push({ line: line.line, start: line.numberStart, end: line.numberEnd, message: `Line ${line.number} is empty, so it isn't stored.`, error: false });

        const length = inputLength(text[line.line]);
        if (length > maxInputLength)
            problems.push({ line: line.line, start: 0, end: text[line.line].length, message: `Line is ${length} characters; BASIC can only take ${maxInputLength} when it's typed in.`, error: false });

        numbers.set(line.number, line);
        if (!previous || line.number > previous.number)
            previous = line;
    }

    for (const line of lines) {
        for (const reference of lineReferences(line, text[line.line])) {
            if (!numbers.has(reference.target))
                problems.push({ line: reference.line, start: reference.start, end: reference.end, message: `There's no line ${reference.target}.`, error: true });
        }
    }

    return problems;
}
