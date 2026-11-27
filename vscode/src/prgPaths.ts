// Default file names for saving and opening .PRG files. No vscode dependency, so it can be tested on its own.
import * as path from 'path';

/** Where a .PRG for a BASIC file goes by default: next to it, with the .prg extension. */
export function prgPathFor(file: string): string {
    return path.join(path.dirname(file), path.basename(file, path.extname(file)) + '.prg');
}

/** Where a BASIC file for a .PRG goes by default: next to it, with the .bas extension. */
export function basPathFor(file: string): string {
    return path.join(path.dirname(file), path.basename(file, path.extname(file)) + '.bas');
}
