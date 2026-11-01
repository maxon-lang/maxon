import { execFile } from 'child_process';
import * as fs from 'fs';
import { promisify } from 'util';

const execFileAsync = promisify(execFile);

const ProcessListBufferBytes = 64 * 1024 * 1024;
const WindowsListScript = [
    '[Console]::OutputEncoding = [System.Text.Encoding]::UTF8',
    'ConvertTo-Json -Compress -InputObject @(Get-CimInstance Win32_Process | ForEach-Object { @{ pid = [int]$_.ProcessId; parentPid = [int]$_.ParentProcessId; commandLine = [string]$_.CommandLine } })'
].join('; ');
const PosixListLine = /^\s*(\d+)\s+(\d+)(?:\s+(.*))?$/;
const PathNameCharacter = /[A-Za-z0-9._-]/;
const TrailingSeparators = /[\\/]+$/;
const ForwardSlashes = /\//g;

export const DefaultReleaseTimeoutMs = 60_000;
export const DefaultReleasePollMs = 250;

export interface ProcessRecord {
    pid: number;
    parentPid: number;
    commandLine: string;
}

export type ProcessLister = (timeoutMs: number) => Promise<ProcessRecord[]>;

export interface ReleaseOptions {
    list?: ProcessLister;
    timeoutMs?: number;
    pollMs?: number;
}

interface WindowsProcessRow {
    pid: number;
    parentPid: number;
    commandLine: string | null;
}

interface ExecFailure extends Error {
    killed?: boolean;
}

async function runLister(command: string, args: string[], timeoutMs: number): Promise<string> {
    try {
        const { stdout } = await execFileAsync(command, args, { maxBuffer: ProcessListBufferBytes, timeout: timeoutMs });

        return stdout;
    } catch (error) {
        if ((error as ExecFailure).killed) {
            throw new Error(`Listing the running processes with ${command} did not finish within ${timeoutMs} ms`);
        }

        throw error;
    }
}

export async function listProcesses(timeoutMs: number): Promise<ProcessRecord[]> {
    if (process.platform === 'win32') {
        const stdout = await runLister('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', WindowsListScript], timeoutMs);
        const rows = JSON.parse(stdout) as WindowsProcessRow[];

        return rows.map(row => ({ pid: row.pid, parentPid: row.parentPid, commandLine: row.commandLine ?? '' }));
    }

    return parsePsListing(await runLister('ps', ['-axww', '-o', 'pid=,ppid=,args='], timeoutMs));
}

export function parsePsListing(listing: string): ProcessRecord[] {
    const records: ProcessRecord[] = [];
    for (const line of listing.split('\n')) {
        const parts = PosixListLine.exec(line);
        if (parts) {
            records.push({ pid: Number(parts[1]), parentPid: Number(parts[2]), commandLine: parts[3] ?? '' });
        }
    }

    return records;
}

export function directoryNames(directory: string): string[] {
    return [...new Set([directory, fs.realpathSync.native(directory)])];
}

function spelledFor(text: string, platform: NodeJS.Platform): string {
    if (platform === 'win32') {
        return text.replace(ForwardSlashes, '\\').toLowerCase();
    }

    return text;
}

function commandLineNames(commandLine: string, names: string[]): boolean {
    for (const name of names) {
        let from = 0;
        for (;;) {
            const at = commandLine.indexOf(name, from);
            if (at < 0) {
                break;
            }

            const following = commandLine.charAt(at + name.length);
            if (following === '' || !PathNameCharacter.test(following)) {
                return true;
            }

            from = at + 1;
        }
    }

    return false;
}

export function holdersOf(directories: string[], processes: ProcessRecord[], ownPid: number, platform: NodeJS.Platform = process.platform): ProcessRecord[] {
    const names = directories.map(directory => spelledFor(directory.replace(TrailingSeparators, ''), platform));
    const holders = new Map<number, ProcessRecord>();
    for (const candidate of processes) {
        if (candidate.pid !== ownPid && commandLineNames(spelledFor(candidate.commandLine, platform), names)) {
            holders.set(candidate.pid, candidate);
        }
    }

    let grew = true;
    while (grew) {
        grew = false;
        for (const candidate of processes) {
            if (candidate.pid !== ownPid && !holders.has(candidate.pid) && holders.has(candidate.parentPid)) {
                holders.set(candidate.pid, candidate);
                grew = true;
            }
        }
    }

    return [...holders.values()];
}

function listing(holders: ProcessRecord[]): string {
    return holders.map(holder => `${holder.pid}: ${holder.commandLine}`).join('\n');
}

function messageOf(failure: unknown): string {
    return failure instanceof Error ? failure.message : String(failure);
}

function sleep(milliseconds: number): Promise<void> {
    return new Promise(resolve => setTimeout(resolve, milliseconds));
}

// Each listing is given only the time that is left, so a listing that hangs cannot carry the wait past
// `timeoutMs`.
export async function waitForHoldersToExit(directory: string, options: ReleaseOptions = {}): Promise<void> {
    const list = options.list ?? listProcesses;
    const timeoutMs = options.timeoutMs ?? DefaultReleaseTimeoutMs;
    const pollMs = options.pollMs ?? DefaultReleasePollMs;
    const names = directoryNames(directory);
    const deadline = Date.now() + timeoutMs;
    let lastHolders: ProcessRecord[] = [];

    for (;;) {
        const remaining = deadline - Date.now();
        if (remaining <= 0) {
            throw new Error(`${lastHolders.length} process(es) still run against ${directory} ${timeoutMs} ms after the test run ended:\n${listing(lastHolders)}`);
        }

        lastHolders = holdersOf(names, await list(remaining), process.pid);
        if (lastHolders.length === 0) {
            return;
        }

        await sleep(Math.min(pollMs, Math.max(deadline - Date.now(), 0)));
    }
}

// `runTests` resolves when the process it launched exits. On Windows that is a `cmd.exe` launcher that
// leaves before Code's helper processes, which still hold files under the user-data directory open, so
// removing the directory at once would collide with them. The directory is removed after every holder
// has exited, and also when one outlives the bound, which is then reported.
export async function removeWhenReleased(directory: string, options: ReleaseOptions = {}): Promise<void> {
    let waitFailure: unknown;
    try {
        await waitForHoldersToExit(directory, options);
    } catch (failure) {
        waitFailure = failure;
    }

    try {
        fs.rmSync(directory, { recursive: true, force: true });
    } catch (removeFailure) {
        if (waitFailure === undefined) {
            throw removeFailure;
        }

        throw new Error(`${messageOf(waitFailure)}\nRemoving ${directory} also failed: ${messageOf(removeFailure)}`);
    }

    if (waitFailure !== undefined) {
        throw waitFailure;
    }
}
