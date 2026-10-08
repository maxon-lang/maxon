import * as assert from 'assert';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import {
	CheckoutDirectories,
	CompilerLookup,
	CompilerSearch,
	compilerToRestartOn,
	isCheckoutLayout,
	isExecutableFile,
	locateCompiler,
	slotCompilerOf,
	watchedCompilers
} from '../../compilerLocator';

const BINARY_NAME = 'maxon.exe';
const INSTALL_ROOT = path.join(path.sep, 'home', 'user', '.maxon');
const INSTALLED = path.join(INSTALL_ROOT, 'bin', BINARY_NAME);
const ON_PATH = path.join(path.sep, 'usr', 'local', 'bin', BINARY_NAME);
const CONFIGURED = path.join(path.sep, 'opt', 'chosen', BINARY_NAME);
const CHECKOUT = path.join(path.sep, 'work', 'maxon');
const OTHER_CHECKOUT = path.join(path.sep, 'work', 'maxon-second');
const PLAIN_FOLDER = path.join(path.sep, 'work', 'app');
const EXTENSION_CHECKOUT = path.join(path.sep, 'ext', 'maxon');

interface World {
	checkouts: string[];
	executables: string[];
	onPath: string;
}

function searchIn(world: World, overrides: Partial<CompilerSearch>): CompilerSearch {
	return {
		configuredPath: '',
		workspaceFolders: [],
		extensionCheckout: EXTENSION_CHECKOUT,
		installRoot: INSTALL_ROOT,
		binaryName: BINARY_NAME,
		findOnPath: async () => world.onPath,
		isExecutable: async (candidate: string) => world.executables.includes(candidate),
		isCheckout: (directory: string) => world.checkouts.includes(directory),
		...overrides
	};
}

function builtCheckoutWorld(): World {
	return {
		checkouts: [CHECKOUT],
		executables: [slotCompilerOf(CHECKOUT, BINARY_NAME), ON_PATH, INSTALLED],
		onPath: ON_PATH
	};
}

suite('locateCompiler', () => {
	test('a checkout folder with a built slot wins over a compiler on PATH', async () => {
		const found = await locateCompiler(searchIn(builtCheckoutWorld(), { workspaceFolders: [CHECKOUT] }));

		assert.deepStrictEqual(found.choice, { executable: slotCompilerOf(CHECKOUT, BINARY_NAME), source: 'checkout' });
		assert.deepStrictEqual(found.notices, []);
	});

	test('a checkout folder with a built slot wins over the installed compiler', async () => {
		const world = builtCheckoutWorld();
		world.onPath = '';

		const found = await locateCompiler(searchIn(world, { workspaceFolders: [CHECKOUT] }));

		assert.strictEqual(found.choice?.source, 'checkout');
	});

	test('a checkout with an empty slot falls through to PATH and says the slot is empty', async () => {
		const world = builtCheckoutWorld();
		world.executables = [ON_PATH, INSTALLED];

		const found = await locateCompiler(searchIn(world, { workspaceFolders: [CHECKOUT] }));

		assert.deepStrictEqual(found.choice, { executable: ON_PATH, source: 'path' });
		assert.deepStrictEqual(found.notices, [
			{ kind: 'checkoutHasNoBuild', checkout: CHECKOUT, slot: slotCompilerOf(CHECKOUT, BINARY_NAME) }
		]);
	});

	test('a folder that is not a checkout keeps PATH first, even when its maxon-bin holds a binary', async () => {
		const world = builtCheckoutWorld();
		world.checkouts = [];

		const found = await locateCompiler(searchIn(world, { workspaceFolders: [CHECKOUT] }));

		assert.deepStrictEqual(found.choice, { executable: ON_PATH, source: 'path' });
		assert.deepStrictEqual(found.notices, []);
	});

	test('PATH is followed by the install directory', async () => {
		const world = builtCheckoutWorld();
		world.onPath = '';

		const found = await locateCompiler(searchIn(world, { workspaceFolders: [PLAIN_FOLDER] }));

		assert.deepStrictEqual(found.choice, { executable: INSTALLED, source: 'install' });
	});

	test('the setting beats a built checkout, PATH and the install directory', async () => {
		const world = builtCheckoutWorld();
		world.executables.push(CONFIGURED);

		const found = await locateCompiler(searchIn(world, { configuredPath: CONFIGURED, workspaceFolders: [CHECKOUT] }));

		assert.deepStrictEqual(found.choice, { executable: CONFIGURED, source: 'setting' });
		assert.deepStrictEqual(found.notices, []);
	});

	test('a setting that points at nothing is reported and the search goes on', async () => {
		const found = await locateCompiler(searchIn(builtCheckoutWorld(), { configuredPath: CONFIGURED, workspaceFolders: [CHECKOUT] }));

		assert.deepStrictEqual(found.choice, { executable: slotCompilerOf(CHECKOUT, BINARY_NAME), source: 'checkout' });
		assert.deepStrictEqual(found.notices, [{ kind: 'settingPointsAtNothing', configured: CONFIGURED }]);
	});

	test('every workspace folder is considered, not only the first', async () => {
		const world: World = {
			checkouts: [OTHER_CHECKOUT],
			executables: [slotCompilerOf(OTHER_CHECKOUT, BINARY_NAME), ON_PATH],
			onPath: ON_PATH
		};

		const found = await locateCompiler(searchIn(world, { workspaceFolders: [PLAIN_FOLDER, OTHER_CHECKOUT] }));

		assert.deepStrictEqual(found.choice, { executable: slotCompilerOf(OTHER_CHECKOUT, BINARY_NAME), source: 'checkout' });
	});

	test('an empty slot in one folder does not hide a built slot in the next', async () => {
		const world: World = {
			checkouts: [CHECKOUT, OTHER_CHECKOUT],
			executables: [slotCompilerOf(OTHER_CHECKOUT, BINARY_NAME), ON_PATH],
			onPath: ON_PATH
		};

		const found = await locateCompiler(searchIn(world, { workspaceFolders: [CHECKOUT, OTHER_CHECKOUT] }));

		assert.deepStrictEqual(found.choice, { executable: slotCompilerOf(OTHER_CHECKOUT, BINARY_NAME), source: 'checkout' });
		assert.deepStrictEqual(found.notices, [
			{ kind: 'checkoutHasNoBuild', checkout: CHECKOUT, slot: slotCompilerOf(CHECKOUT, BINARY_NAME) }
		]);
	});

	test('the checkout the extension itself lives in is the last resort', async () => {
		const world: World = {
			checkouts: [],
			executables: [slotCompilerOf(EXTENSION_CHECKOUT, BINARY_NAME)],
			onPath: ''
		};

		const found = await locateCompiler(searchIn(world, { workspaceFolders: [PLAIN_FOLDER] }));

		assert.deepStrictEqual(found.choice, { executable: slotCompilerOf(EXTENSION_CHECKOUT, BINARY_NAME), source: 'extension' });
	});

	test('with no compiler anywhere nothing is chosen', async () => {
		const world: World = { checkouts: [], executables: [], onPath: '' };

		const found = await locateCompiler(searchIn(world, { workspaceFolders: [PLAIN_FOLDER] }));

		assert.strictEqual(found.choice, undefined);
	});
});

suite('isCheckoutLayout', () => {
	test('a folder holding every checkout directory is a checkout', () => {
		const present = new Set(CheckoutDirectories);

		assert.strictEqual(isCheckoutLayout(name => present.has(name)), true);
	});

	test('a folder missing any one of them is not', () => {
		for (const missing of CheckoutDirectories) {
			const present = new Set(CheckoutDirectories);
			present.delete(missing);

			assert.strictEqual(isCheckoutLayout(name => present.has(name)), false, `without ${missing}`);
		}
	});
});

suite('compilerToRestartOn', () => {
	function lookupChoosing(executable: string | undefined): CompilerLookup {
		return { choice: executable === undefined ? undefined : { executable, source: 'checkout' }, notices: [] };
	}

	test('a freshly built slot replaces the compiler the server was started with', () => {
		const slot = slotCompilerOf(CHECKOUT, BINARY_NAME);

		assert.strictEqual(compilerToRestartOn(ON_PATH, lookupChoosing(slot)), slot);
	});

	test('the same compiler is kept', () => {
		assert.strictEqual(compilerToRestartOn(ON_PATH, lookupChoosing(ON_PATH)), ON_PATH);
	});

	test('a search that finds nothing keeps the compiler the server was started with', () => {
		assert.strictEqual(compilerToRestartOn(ON_PATH, lookupChoosing(undefined)), ON_PATH);
	});
});

suite('watchedCompilers', () => {
	const isCheckout = (directory: string) => directory === CHECKOUT || directory === OTHER_CHECKOUT;

	test('the chosen compiler and the slot of every checkout folder are watched', () => {
		const watched = watchedCompilers(ON_PATH, [PLAIN_FOLDER, CHECKOUT, OTHER_CHECKOUT], BINARY_NAME, isCheckout);

		assert.deepStrictEqual(watched, [
			ON_PATH,
			slotCompilerOf(CHECKOUT, BINARY_NAME),
			slotCompilerOf(OTHER_CHECKOUT, BINARY_NAME)
		]);
	});

	test('a chosen slot is watched once', () => {
		const slot = slotCompilerOf(CHECKOUT, BINARY_NAME);

		assert.deepStrictEqual(watchedCompilers(slot, [CHECKOUT], BINARY_NAME, isCheckout), [slot]);
	});

	test('a folder that is not a checkout contributes nothing', () => {
		assert.deepStrictEqual(watchedCompilers(ON_PATH, [PLAIN_FOLDER], BINARY_NAME, isCheckout), [ON_PATH]);
	});
});

suite('isExecutableFile', () => {
	function inTemporaryDirectory(body: (directory: string) => Promise<void>): Promise<void> {
		const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'maxon-compiler-locator-'));

		return body(directory).finally(() => fs.rmSync(directory, { recursive: true, force: true }));
	}

	test('a path that does not exist is not executable', () => inTemporaryDirectory(async directory => {
		assert.strictEqual(await isExecutableFile(path.join(directory, 'absent')), false);
	}));

	test('a directory is not executable, whatever its permissions say', () => inTemporaryDirectory(async directory => {
		assert.strictEqual(await isExecutableFile(directory), false);
	}));

	test('a file made executable is executable', () => inTemporaryDirectory(async directory => {
		const compiler = path.join(directory, 'maxon');
		fs.writeFileSync(compiler, '');
		fs.chmodSync(compiler, 0o755);

		assert.strictEqual(await isExecutableFile(compiler), true);
	}));

	test('a file without the executable bit is not, on a host that has one', function () {
		if (os.platform() === 'win32') {
			this.skip();
		}

		return inTemporaryDirectory(async directory => {
			const data = path.join(directory, 'maxon');
			fs.writeFileSync(data, '');
			fs.chmodSync(data, 0o644);

			assert.strictEqual(await isExecutableFile(data), false);
		});
	});
});
