// The files for a new X16 BASIC project. No vscode dependency, so it can be tested on its own.
import * as fs from 'fs';
import * as path from 'path';

/** The starter program: something to step through, with a GOSUB, a loop and an INPUT to type into. */
export const mainBas = `10 PRINT "{CLR}HELLO FROM X16 BASIC!"
20 INPUT "WHAT IS YOUR NAME";N$
30 PRINT "HI ";N$
40 FOR I=1 TO 5
50 GOSUB 1000
60 NEXT I
70 PRINT "TOTAL";T
80 END
1000 REM ADD I TO THE TOTAL
1010 T=T+I
1020 PRINT I,T
1030 RETURN
`;

export const launchJson = JSON.stringify({
    version: '0.2.0',
    configurations: [
        {
            type: 'x16basic',
            request: 'launch',
            name: 'Debug main.bas',
            program: '${workspaceFolder}/main.bas',
        },
        {
            type: 'x16basic',
            request: 'launch',
            name: 'Debug the open BASIC file',
            program: '${file}',
        },
    ],
}, null, 4) + '\n';

export const projectFiles: Record<string, string> = {
    'main.bas': mainBas,
    '.vscode/launch.json': launchJson,
};

/**
 * Writes the project's files into folder. Files that are already there are left alone.
 * @returns the files written and the ones skipped, relative to folder
 */
export function writeProject(folder: string): { written: string[]; skipped: string[] } {
    const written: string[] = [];
    const skipped: string[] = [];

    for (const [name, content] of Object.entries(projectFiles)) {
        const file = path.join(folder, name);
        if (fs.existsSync(file)) {
            skipped.push(name);
            continue;
        }

        fs.mkdirSync(path.dirname(file), { recursive: true });
        fs.writeFileSync(file, content);
        written.push(name);
    }

    return { written, skipped };
}
