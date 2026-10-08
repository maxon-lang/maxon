export interface Timers {
	setTimeout(callback: () => void, delayMs: number): unknown;
	clearTimeout(handle: unknown): void;
}

export const systemTimers: Timers = {
	setTimeout: (callback, delayMs) => setTimeout(callback, delayMs),
	clearTimeout: handle => clearTimeout(handle as ReturnType<typeof setTimeout>)
};

export class RestartCoordinator {
	private running: Promise<void> | undefined;
	private followUp: Promise<void> | undefined;
	private pendingChange: unknown;
	private hasPendingChange = false;

	constructor(
		private readonly restart: () => Promise<void>,
		private readonly delayMs: number,
		private readonly timers: Timers,
		private readonly onChangeFailure: (error: unknown) => void
	) { }

	request(): Promise<void> {
		if (this.followUp !== undefined) {
			return this.followUp;
		}

		if (this.running === undefined) {
			return this.start();
		}

		this.followUp = this.running
			.catch(() => undefined)
			.then(() => {
				this.followUp = undefined;
				return this.start();
			});

		return this.followUp;
	}

	changed(): void {
		this.cancelPendingChange();
		this.hasPendingChange = true;
		this.pendingChange = this.timers.setTimeout(() => {
			this.hasPendingChange = false;
			this.pendingChange = undefined;
			this.request().catch(error => this.onChangeFailure(error));
		}, this.delayMs);
	}

	dispose(): void {
		this.cancelPendingChange();
	}

	private cancelPendingChange(): void {
		if (this.hasPendingChange) {
			this.timers.clearTimeout(this.pendingChange);
		}
		this.hasPendingChange = false;
		this.pendingChange = undefined;
	}

	private start(): Promise<void> {
		const run: Promise<void> = (async () => this.restart())().finally(() => {
			if (this.running === run) {
				this.running = undefined;
			}
		});
		this.running = run;

		return run;
	}
}
