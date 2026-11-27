// Runs x16basic, the command line tool on the BASIC library (src/BitMagic.X16Basic.Tool), which comes with the
// extension in tool/. It needs .NET 10: the same one BitMagic uses, from Microsoft's .NET Install Tool extension,
// which BitMagic depends on.
import * as vscode from 'vscode';
import * as path from 'path';
import { execFile } from 'child_process';

interface DotnetAcquireResult { dotnetPath: string; }

let dotnet: string | undefined;

async function findDotnet(context: vscode.ExtensionContext): Promise<string> {
    if (dotnet)
        return dotnet;

    const request = { version: '10.0', requestingExtensionId: context.extension.id };
    let result = await vscode.commands.executeCommand<DotnetAcquireResult | undefined>('dotnet.acquireStatus', request);
    if (!result?.dotnetPath)
        result = await vscode.commands.executeCommand<DotnetAcquireResult | undefined>('dotnet.acquire', request);
    if (!result?.dotnetPath)
        throw new Error("Couldn't find or install .NET 10, which saving and opening .PRG files needs.");

    return dotnet = result.dotnetPath;
}

export interface ToolResult { code: number; stdout: Buffer; stderr: string; }

/** Runs x16basic with these arguments; stdout comes back as bytes, as a .PRG can be written there. */
export async function runTool(context: vscode.ExtensionContext, args: string[]): Promise<ToolResult> {
    const exe = await findDotnet(context);
    const dll = path.join(context.extensionPath, 'tool', 'x16basic.dll');

    return new Promise(resolve => {
        execFile(exe, [dll, ...args], { encoding: 'buffer', maxBuffer: 16 * 1024 * 1024 }, (error, stdout, stderr) => {
            const code = error ? (typeof error.code === 'number' ? error.code : 1) : 0;
            resolve({ code, stdout, stderr: stderr.toString() });
        });
    });
}
