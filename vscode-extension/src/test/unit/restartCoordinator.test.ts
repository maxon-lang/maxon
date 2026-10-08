import * as assert from 'assert';
import { RestartCoordinator, Timers } from '../../restartCoordinator';

const DELAY_MS = 1000;

class FakeClock implements Timers {
	private now = 0;
	private nextHandle = 1;
	private scheduled = new Map<number, { due: number; callback: () => void; }>();

	setTimeout(callback: () => void, delayMs: number): number {
		const handle = this.nextHandle++;
		this.scheduled.set(handle, { due: this.now + delayMs, callback });
		return handle;
	}

	clearTimeout(handle: number): void {
		this.scheduled.delete(handle);
	}

	advance(milliseconds: number): void {
		this.now += milliseconds;
		for (const [handle, entry] of [...this.scheduled]) {
			if (entry.due <= this.now) {
				this.scheduled.delete(handle);
				entry.callback();
			}
		}
	}

	get pendingCount(): number {
		return this.scheduled.size;
	}
}

interface Run {
	index: number;
	finish(): void;
	fail(error: Error): void;
}

class ControlledRestart {
	started: Run[] = [];
	active = 0;
	mostActive = 0;

	run = (): Promise<void> => {
		this.active++;
		this.mostActive = Math.max(this.mostActive, this.active);

		return new Promise<void>((resolve, reject) => {
			this.started.push({
				index: this.started.length,
				finish: () => {
					this.active--;
					resolve();
				},
				fail: (error: Error) => {
					this.active--;
					reject(error);
				}
			});
		});
	};
}

async function settle(): Promise<void> {
	for (let turn = 0; turn < 10; turn++) {
		await Promise.resolve();
	}
}

function coordinatorFor(restart: ControlledRestart, clock: FakeClock, failures: unknown[] = []): RestartCoordinator {
	return new RestartCoordinator(restart.run, DELAY_MS, clock, error => failures.push(error));
}

suite('RestartCoordinator.request', () => {
	test('a request made as the running restart settles joins the queued follow-up instead of starting a second run', async () => {
		const restart = new ControlledRestart();
		const coordinator = coordinatorFor(restart, new FakeClock());

		const first = coordinator.request();
		const queued = coordinator.request();
		const rejoined = first.then(() => coordinator.request());

		restart.started[0].finish();
		await settle();

		assert.strictEqual(restart.started.length, 2);
		assert.strictEqual(restart.mostActive, 1);

		restart.started[1].finish();
		await Promise.all([queued, rejoined]);
		await settle();

		assert.strictEqual(restart.started.length, 2);
	});

	test('a request with nothing running starts a restart at once', async () => {
		const restart = new ControlledRestart();
		const coordinator = coordinatorFor(restart, new FakeClock());

		const done = coordinator.request();
		assert.strictEqual(restart.started.length, 1);

		restart.started[0].finish();
		await done;
	});

	test('requests after a restart finished each start their own', async () => {
		const restart = new ControlledRestart();
		const coordinator = coordinatorFor(restart, new FakeClock());

		const first = coordinator.request();
		restart.started[0].finish();
		await first;

		const second = coordinator.request();
		assert.strictEqual(restart.started.length, 2);
		restart.started[1].finish();
		await second;
	});

	test('requests that arrive during a restart coalesce into exactly one follow-up', async () => {
		const restart = new ControlledRestart();
		const coordinator = coordinatorFor(restart, new FakeClock());

		const first = coordinator.request();
		const second = coordinator.request();
		const third = coordinator.request();
		await settle();

		assert.strictEqual(restart.started.length, 1, 'the follow-up must wait for the restart in flight');

		restart.started[0].finish();
		await first;
		await settle();

		assert.strictEqual(restart.started.length, 2, 'both late requests share one follow-up');

		restart.started[1].finish();
		await Promise.all([second, third]);
		await settle();

		assert.strictEqual(restart.started.length, 2);
		assert.strictEqual(restart.mostActive, 1, 'two restarts were never in flight together');
	});

	test('a request that arrives during the follow-up schedules one more', async () => {
		const restart = new ControlledRestart();
		const coordinator = coordinatorFor(restart, new FakeClock());

		const first = coordinator.request();
		const second = coordinator.request();

		restart.started[0].finish();
		await first;
		await settle();

		const third = coordinator.request();
		await settle();
		assert.strictEqual(restart.started.length, 2);

		restart.started[1].finish();
		await second;
		await settle();
		assert.strictEqual(restart.started.length, 3);

		restart.started[2].finish();
		await third;
		assert.strictEqual(restart.mostActive, 1);
	});

	test('a failed restart rejects its own request and does not stop the follow-up', async () => {
		const restart = new ControlledRestart();
		const coordinator = coordinatorFor(restart, new FakeClock());

		const first = coordinator.request();
		const second = coordinator.request();
		const firstOutcome = assert.rejects(first, /the server did not start/);

		restart.started[0].fail(new Error('the server did not start'));
		await firstOutcome;
		await settle();

		assert.strictEqual(restart.started.length, 2);
		restart.started[1].finish();
		await second;
	});
});

suite('RestartCoordinator.changed', () => {
	test('a burst of changes restarts once, after the delay from the last', async () => {
		const restart = new ControlledRestart();
		const clock = new FakeClock();
		const coordinator = coordinatorFor(restart, clock);

		coordinator.changed();
		clock.advance(DELAY_MS - 1);
		coordinator.changed();
		clock.advance(DELAY_MS - 1);
		assert.strictEqual(restart.started.length, 0);

		clock.advance(1);
		assert.strictEqual(restart.started.length, 1);
		restart.started[0].finish();
		await settle();
	});

	test('a pending change survives an unrelated request and still restarts afterwards', async () => {
		const restart = new ControlledRestart();
		const clock = new FakeClock();
		const coordinator = coordinatorFor(restart, clock);

		coordinator.changed();
		const unrelated = coordinator.request();
		restart.started[0].finish();
		await unrelated;

		assert.strictEqual(clock.pendingCount, 1, 'the pending change is still armed');

		clock.advance(DELAY_MS);
		assert.strictEqual(restart.started.length, 2);
		restart.started[1].finish();
		await settle();
	});

	test('a change that fires during a restart queues a single follow-up', async () => {
		const restart = new ControlledRestart();
		const clock = new FakeClock();
		const coordinator = coordinatorFor(restart, clock);

		const first = coordinator.request();
		coordinator.changed();
		clock.advance(DELAY_MS);
		await settle();
		assert.strictEqual(restart.started.length, 1);

		restart.started[0].finish();
		await first;
		await settle();

		assert.strictEqual(restart.started.length, 2);
		restart.started[1].finish();
		await settle();
		assert.strictEqual(restart.mostActive, 1);
	});

	test('a restart the change started that fails is reported, not thrown', async () => {
		const restart = new ControlledRestart();
		const clock = new FakeClock();
		const failures: unknown[] = [];
		const coordinator = coordinatorFor(restart, clock, failures);

		coordinator.changed();
		clock.advance(DELAY_MS);
		restart.started[0].fail(new Error('boom'));
		await settle();

		assert.strictEqual(failures.length, 1);
		assert.match(String(failures[0]), /boom/);
	});

	test('dispose cancels a pending change, and the coordinator can be armed again', async () => {
		const restart = new ControlledRestart();
		const clock = new FakeClock();
		const coordinator = coordinatorFor(restart, clock);

		coordinator.changed();
		coordinator.dispose();
		assert.strictEqual(clock.pendingCount, 0);

		clock.advance(DELAY_MS);
		assert.strictEqual(restart.started.length, 0);

		coordinator.changed();
		assert.strictEqual(clock.pendingCount, 1);
		clock.advance(DELAY_MS);
		assert.strictEqual(restart.started.length, 1);
		restart.started[0].finish();
		await settle();
	});
});
