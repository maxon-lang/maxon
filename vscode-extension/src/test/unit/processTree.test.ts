import * as assert from 'assert';
import { ChildProcess, spawn } from 'child_process';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { directoryNames, holdersOf, listProcesses, parsePsListing, ProcessRecord, removeWhenReleased, waitForHoldersToExit } from '../processTree';

const Directory = path.join(os.tmpdir(), 'maxon-e2e-user-data-Abc123');
const OwnPid = 100;
const ImmediatePollMs = 1;
const ShortTimeoutMs = 50;
const LongTimeoutMs = 30_000;
const SurvivorSource = 'setInterval(() => {}, 1000)';

function record(pid: number, parentPid: number, commandLine: string): ProcessRecord {
	return { pid, parentPid, commandLine };
}

function exited(child: ChildProcess): Promise<void> {
	return new Promise(resolve => child.once('exit', () => resolve()));
}

function pidsOf(holders: ProcessRecord[]): number[] {
	return holders.map(holder => holder.pid).sort((a, b) => a - b);
}

suite('processTree', () => {
	let scratch: string;

	setup(() => {
		scratch = fs.mkdtempSync(path.join(os.tmpdir(), 'maxon-process-tree-'));
	});

	teardown(() => {
		fs.rmSync(scratch, { recursive: true, force: true });
	});

	suite('holdersOf', () => {
		test('a process that names the directory holds it, and so does every descendant of one', () => {
			const processes = [
				record(1, 0, 'init'),
				record(10, 1, `Code.exe --user-data-dir=${Directory}`),
				record(11, 10, 'Code.exe --type=utility --utility-sub-type=node.mojom.NodeService'),
				record(12, 11, 'node pty-host.js'),
				record(20, 1, 'Code.exe --user-data-dir=/elsewhere'),
				record(21, 20, 'node other.js'),
			];

			assert.deepStrictEqual(pidsOf(holdersOf([Directory], processes, OwnPid)), [10, 11, 12]);
		});

		test('a descendant is found whatever the order of the listing', () => {
			const processes = [
				record(12, 11, 'node pty-host.js'),
				record(11, 10, 'Code.exe --type=utility'),
				record(10, 1, `Code.exe --user-data-dir=${Directory}`),
			];

			assert.strictEqual(holdersOf([Directory], processes, OwnPid).length, 3);
		});

		test('the calling process is never a holder', () => {
			const processes = [record(OwnPid, 1, `node runTest.js ${Directory}`)];

			assert.deepStrictEqual(holdersOf([Directory], processes, OwnPid), []);
		});

		test('no process names the directory', () => {
			assert.deepStrictEqual(holdersOf([Directory], [record(1, 0, 'init')], OwnPid), []);
		});

		test('a longer directory name that starts with this one is another directory', () => {
			const processes = [
				record(10, 1, `Code.exe --user-data-dir=${Directory}2`),
				record(11, 1, `Code.exe --user-data-dir=${Directory}-other`),
				record(12, 1, `Code.exe --user-data-dir="${Directory}" --other`),
				record(13, 1, `Code.exe ${path.join(Directory, 'User', 'settings.json')}`),
			];

			assert.deepStrictEqual(pidsOf(holdersOf([Directory], processes, OwnPid)), [12, 13]);
		});

		test('on Windows either slash direction and either letter case names the directory', () => {
			const directory = 'C:\\Users\\RUNNER~1\\AppData\\Local\\Temp\\maxon-e2e-user-data-Qwinmr\\';
			const processes = [
				record(10, 1, 'Code.exe --user-data-dir=C:/Users/RUNNER~1/AppData/Local/Temp/maxon-e2e-user-data-Qwinmr'),
				record(11, 1, 'Code.exe --user-data-dir="c:\\users\\runner~1\\appdata\\local\\temp\\MAXON-E2E-USER-DATA-QWINMR\\logs"'),
				record(12, 1, 'Code.exe --user-data-dir=C:/Users/RUNNER~1/AppData/Local/Temp/maxon-e2e-user-data-Other'),
			];

			assert.deepStrictEqual(pidsOf(holdersOf([directory], processes, OwnPid, 'win32')), [10, 11]);
			assert.deepStrictEqual(pidsOf(holdersOf([directory], processes, OwnPid, 'linux')), []);
		});

		test('a process that names any one spelling of the directory holds it', () => {
			const processes = [
				record(10, 1, 'Code.exe --user-data-dir=/var/folders/x/maxon-e2e-user-data-Abc123'),
				record(11, 1, 'Code.exe --user-data-dir=/private/var/folders/x/maxon-e2e-user-data-Abc123'),
			];

			assert.deepStrictEqual(
				pidsOf(holdersOf(['/var/folders/x/maxon-e2e-user-data-Abc123', '/private/var/folders/x/maxon-e2e-user-data-Abc123'], processes, OwnPid, 'linux')),
				[10, 11]
			);
		});
	});

	suite('parsePsListing', () => {
		test('a process with no arguments is a record, and the continuation of arguments that hold a newline is skipped', () => {
			const listing = [
				'    1     0 /sbin/init',
				'  205     1',
				'  310     1 sh -c echo one',
				'two and three',
				'  400   310 node server.js --user-data-dir=/tmp/x',
				'',
			].join('\n');

			assert.deepStrictEqual(parsePsListing(listing), [
				record(1, 0, '/sbin/init'),
				record(205, 1, ''),
				record(310, 1, 'sh -c echo one'),
				record(400, 310, 'node server.js --user-data-dir=/tmp/x'),
			]);
		});
	});

	suite('directoryNames', () => {
		test('a link to the directory and the directory itself are both spellings of it', () => {
			const real = path.join(scratch, 'real');
			const link = path.join(scratch, 'link');
			fs.mkdirSync(real);
			fs.symlinkSync(real, link, process.platform === 'win32' ? 'junction' : 'dir');

			const names = directoryNames(link);

			assert.ok(names.includes(link), 'the spelling the caller passed');
			assert.ok(names.some(name => fs.realpathSync.native(name) === fs.realpathSync.native(real) && name !== link), 'the resolved spelling');
			assert.deepStrictEqual(pidsOf(holdersOf(names, [record(10, 1, `Code.exe --user-data-dir=${fs.realpathSync.native(real)}`)], OwnPid)), [10]);
		});
	});

	suite('waitForHoldersToExit', () => {
		test('it resolves only after the last holder has left the listing', async () => {
			const holder = record(10, 1, `Code.exe --user-data-dir=${scratch}`);
			const listings = [[holder], [holder], [holder], []];
			let polls = 0;

			await waitForHoldersToExit(scratch, {
				list: async () => listings[Math.min(polls++, listings.length - 1)],
				pollMs: ImmediatePollMs,
				timeoutMs: LongTimeoutMs,
			});

			assert.strictEqual(polls, listings.length);
		});

		test('a holder that never leaves is an error naming it', async () => {
			const holder = record(10, 1, `Code.exe --user-data-dir=${scratch}`);

			await assert.rejects(
				waitForHoldersToExit(scratch, { list: async () => [holder], pollMs: ImmediatePollMs, timeoutMs: ShortTimeoutMs }),
				(error: Error) => error.message.includes('10: Code.exe') && error.message.includes(scratch)
			);
		});

		test('each listing is given the time that is left, never more than the bound', async () => {
			const holder = record(10, 1, `Code.exe --user-data-dir=${scratch}`);
			const given: number[] = [];

			await assert.rejects(waitForHoldersToExit(scratch, {
				list: async timeoutMs => {
					given.push(timeoutMs);

					return [holder];
				},
				pollMs: ImmediatePollMs,
				timeoutMs: ShortTimeoutMs,
			}));

			assert.ok(given.length > 1);
			assert.ok(given.every(timeoutMs => timeoutMs > 0 && timeoutMs <= ShortTimeoutMs), `listing budgets ${given}`);
			assert.ok(given[given.length - 1] <= given[0]);
		});

		test('a listing that fails ends the wait with its error', async () => {
			await assert.rejects(
				waitForHoldersToExit(scratch, { list: async () => { throw new Error('the listing hung'); }, timeoutMs: LongTimeoutMs }),
				/the listing hung/
			);
		});
	});

	suite('removeWhenReleased', () => {
		test('a wait that times out still attempts the removal, then reports the holder', async () => {
			fs.writeFileSync(path.join(scratch, 'exthost.log'), '');
			const holder = record(10, 1, `Code.exe --user-data-dir=${scratch}`);

			await assert.rejects(
				removeWhenReleased(scratch, { list: async () => [holder], pollMs: ImmediatePollMs, timeoutMs: ShortTimeoutMs }),
				(error: Error) => error.message.includes('10: Code.exe')
			);

			assert.ok(!fs.existsSync(scratch), 'the directory was removed in spite of the failed wait');
		});

		test('a directory no process names is removed', async () => {
			fs.writeFileSync(path.join(scratch, 'exthost.log'), '');

			await removeWhenReleased(scratch, { list: async () => [], pollMs: ImmediatePollMs, timeoutMs: LongTimeoutMs });

			assert.ok(!fs.existsSync(scratch));
		});
	});

	suite('the system process list', () => {
		let survivor: ChildProcess | undefined;

		teardown(async () => {
			if (survivor && survivor.exitCode === null && survivor.signalCode === null) {
				const gone = exited(survivor);
				survivor.kill();
				await gone;
			}
		});

		test('the listing shows this process with its arguments', async function () {
			this.timeout(LongTimeoutMs);
			const records = await listProcesses(LongTimeoutMs);

			assert.ok(records.some(entry => entry.pid === process.pid && entry.commandLine !== ''));
		});

		test('a listing that does not finish within its budget is an error saying so', async function () {
			this.timeout(LongTimeoutMs);

			await assert.rejects(listProcesses(1), /did not finish within 1 ms/);
		});

		test('a directory a live process names is removed only after that process exits', async function () {
			this.timeout(LongTimeoutMs);
			survivor = spawn(process.execPath, ['-e', SurvivorSource, scratch], { stdio: 'ignore' });
			await new Promise<void>((resolve, reject) => {
				survivor!.once('spawn', () => resolve());
				survivor!.once('error', reject);
			});

			const removing = removeWhenReleased(scratch, { pollMs: ImmediatePollMs, timeoutMs: LongTimeoutMs });

			assert.ok(holdersOf(directoryNames(scratch), await listProcesses(LongTimeoutMs), process.pid).some(holder => holder.pid === survivor!.pid), 'the listing shows the live process');
			assert.ok(fs.existsSync(scratch), 'the directory stays while the process lives');

			const gone = exited(survivor);
			survivor.kill();
			await gone;
			await removing;

			assert.ok(!fs.existsSync(scratch), 'the directory is removed once the process has exited');
		});
	});
});
