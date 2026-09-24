import * as assert from 'assert';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import {
	isIgnoredDirectory,
	isTestFileName,
	parseTestDeclarations,
	parseTestRunDocument,
	resultKey,
	TestResult,
	testFilterFor,
	testKey,
	testProjectDirectory,
	verdictFor
} from '../../unitTestModel';

// `tests/test-fixtures/json-face/expected.txt`: what `maxon test --json` printed for a real run.
const JSON_FACE_DOCUMENT = '{"total":2,"passed":1,"failed":1,"files":1,"results":[{"file":"tests/test-fixtures/json-face/parity.maxtest","name":"halving four gives two","symbol":"__test_halving_four_gives_two","line":14,"state":"passed"},{"file":"tests/test-fixtures/json-face/parity.maxtest","name":"halving five gives three","symbol":"__test_halving_five_gives_three","line":19,"state":"failed","output":"FAIL parity.maxtest:20: Expect.equal\\n  expected: 3\\n  received: 2"}]}\n';

function result(fields: Partial<TestResult>): TestResult {
	return { file: 'a.maxtest', name: 'a test', symbol: '__test_a_test', line: 1, state: 'passed', ...fields };
}

function withTree(files: string[], body: (root: string) => void): void {
	const root = fs.mkdtempSync(path.join(os.tmpdir(), 'maxon-unit-test-model-'));
	try {
		for (const file of files) {
			const full = path.join(root, file);
			fs.mkdirSync(path.dirname(full), { recursive: true });
			fs.writeFileSync(full, '');
		}
		body(root);
	} finally {
		fs.rmSync(root, { recursive: true, force: true });
	}
}

suite('parseTestDeclarations', () => {
	test('finds each declaration with its zero-based position', () => {
		const source = [
			'function halve(n Tally) returns Tally',
			'\treturn n / 2',
			"end 'halve'",
			'',
			"test 'halving four gives two'",
			'\ttry Expect.equal(halve(4) as AssertedInt, expected: 2)',
			"end 'halving four gives two'",
			'',
			"  test 'indented, with a comma'",
			"end 'indented, with a comma'"
		].join('\r\n');

		assert.deepStrictEqual(parseTestDeclarations(source), [
			{ name: 'halving four gives two', line: 4, column: 0 },
			{ name: 'indented, with a comma', line: 8, column: 2 }
		]);
	});

	test('skips comments, empty names and repeats', () => {
		const source = [
			"// test 'in a line comment'",
			"/* test 'on the comment line'",
			"test 'inside a block comment'",
			"*/ test 'after the comment closes mid-line'",
			"let s = \"/*\"",
			"test ''",
			"test 'real'",
			"test 'real'",
			"let test = 1"
		].join('\n');

		assert.deepStrictEqual(parseTestDeclarations(source), [{ name: 'real', line: 6, column: 0 }]);
	});
});

suite('parseTestRunDocument', () => {
	test('reads a real report', () => {
		const document = parseTestRunDocument(JSON_FACE_DOCUMENT);
		assert.strictEqual(document.total, 2);
		assert.deepStrictEqual(document.results.map(r => [r.name, r.state]), [
			['halving four gives two', 'passed'],
			['halving five gives three', 'failed']
		]);
	});

	test('reads the report of a run that found nothing', () => {
		const document = parseTestRunDocument('{"reason":"no test matched \'x\'","total":0,"passed":0,"failed":0,"files":0,"results":[]}');
		assert.strictEqual(document.reason, "no test matched 'x'");
	});

	test('refuses an unknown state', () => {
		assert.throws(() => parseTestRunDocument('{"results":[{"file":"a","name":"b","state":"exploded"}]}'), /unknown state 'exploded'/);
	});

	test('refuses a document without results', () => {
		assert.throws(() => parseTestRunDocument('{"total":0}'), /"results"/);
		assert.throws(() => parseTestRunDocument('compile failed'));
	});
});

suite('verdictFor', () => {
	const cwd = path.resolve('/workspace');
	const testFile = path.join(cwd, 'tests', 'parity.maxtest');

	test('a pass carries its duration in milliseconds', () => {
		assert.deepStrictEqual(verdictFor(result({ nanos: 2_500_000 }), testFile, cwd), { kind: 'passed', durationMs: 2.5 });
		assert.deepStrictEqual(verdictFor(result({}), testFile, cwd), { kind: 'passed', durationMs: undefined });
	});

	test('a failed assertion points at the assertion line', () => {
		const failed = parseTestRunDocument(JSON_FACE_DOCUMENT).results[1];
		assert.deepStrictEqual(verdictFor(failed, testFile, cwd), {
			kind: 'failed',
			message: 'FAIL parity.maxtest:20: Expect.equal\n  expected: 3\n  received: 2',
			durationMs: undefined,
			location: { file: testFile, line: 19 }
		});
	});

	test('an assertion in another file does not move the location', () => {
		const verdict = verdictFor(result({ state: 'failed', output: 'FAIL helpers.maxon:3: Expect.equal' }), testFile, cwd);
		assert.strictEqual(verdict.kind === 'failed' && verdict.location, undefined);
	});

	test('each state that is not a pass says what happened', () => {
		const expectations: [TestResult['state'], string, RegExp][] = [
			['failed', 'failed', /^Test failed$/],
			['crashed', 'failed', /^Crashed: /],
			['timedOut', 'failed', /^Timed out: /],
			['leaked', 'failed', /^Leaked: /],
			['didNotRun', 'errored', /^Did not run: /]
		];

		for (const [state, kind, message] of expectations) {
			const verdict = verdictFor(result({ state }), testFile, cwd);
			assert.strictEqual(verdict.kind, kind, state);
			assert.match(verdict.kind === 'passed' ? '' : verdict.message, message, state);
		}
	});

	test('an uncaught error is named, and located when its file exists', () => {
		withTree(['src/pricing.maxon'], root => {
			const thrown = result({
				state: 'failed',
				output: 'the program said this',
				threw: { errorType: 'PriceError', errorCase: 'negative', file: 'src/pricing.maxon', line: 7 }
			});

			assert.deepStrictEqual(verdictFor(thrown, path.join(root, 'pricing.maxtest'), root), {
				kind: 'failed',
				message: 'Threw PriceError.negative at src/pricing.maxon:7\nthe program said this',
				durationMs: undefined,
				location: { file: path.join(root, 'src', 'pricing.maxon'), line: 6 }
			});
		});
	});
});

suite('matching results to tests', () => {
	test('a reported path is relative to where maxon test ran', () => {
		const cwd = path.resolve('/workspace');
		const reported = result({ file: 'tests/cli/help.maxtest', name: 'help lists commands' });
		assert.strictEqual(resultKey(reported, cwd), testKey(path.join(cwd, 'tests', 'cli', 'help.maxtest'), 'help lists commands'));
		assert.notStrictEqual(resultKey(reported, cwd), testKey(path.join(cwd, 'tests', 'cli', 'help.maxtest'), 'help lists'));
	});
});

suite('testFilterFor', () => {
	const cwd = path.resolve('/workspace');

	test('a whole project runs unfiltered', () => {
		assert.strictEqual(testFilterFor({ wholeFiles: [], testNames: ['x'] }, cwd, true), undefined);
	});

	test('whole files by reported path, then single tests by name', () => {
		const filter = testFilterFor({
			wholeFiles: [path.join(cwd, 'src', 'a.maxtest')],
			testNames: ['adds, then subtracts']
		}, cwd, false);
		assert.strictEqual(filter, 'src/a.maxtest,adds, then subtracts');
	});

	test('a partial run selecting nothing is refused', () => {
		assert.throws(() => testFilterFor({ wholeFiles: [], testNames: [] }, cwd, false));
	});
});

suite('isTestFileName', () => {
	test('a .maxtest file is a test file, and an old *.test.maxon file is an ordinary source', () => {
		assert.strictEqual(isTestFileName(path.join('src', 'pricing.maxtest')), true);
		assert.strictEqual(isTestFileName(path.join('src', 'pricing.test.maxon')), false);
		assert.strictEqual(isTestFileName(path.join('src', 'pricing.maxon')), false);
	});

	test('the extension is matched as the compiler matches it, case and all', () => {
		assert.strictEqual(isTestFileName(path.join('src', 'pricing.MAXTEST')), false);
	});
});

suite('testProjectDirectory', () => {
	test('climbs through directories holding sources, stopping at one that holds none', () => {
		withTree(['tests/README.md', 'tests/cli/Harness.maxon', 'tests/cli/help.maxtest'], root => {
			assert.strictEqual(testProjectDirectory(path.join(root, 'tests', 'cli', 'help.maxtest'), root), path.join(root, 'tests', 'cli'));
		});
	});

	test('the nearest directory holding a .maxproj is the project, past a directory holding no source', () => {
		withTree(['app.maxproj', 'main.maxon', 'src/README.md', 'src/pricing/pricing.maxon', 'src/pricing/pricing.maxtest'], root => {
			assert.strictEqual(testProjectDirectory(path.join(root, 'src', 'pricing', 'pricing.maxtest'), root), root);
		});
	});

	test('a .maxproj above the workspace folder is not consulted', () => {
		withTree(['app.maxproj', 'workspace/README.md', 'workspace/cli/help.maxtest'], root => {
			const workspace = path.join(root, 'workspace');
			assert.strictEqual(testProjectDirectory(path.join(workspace, 'cli', 'help.maxtest'), workspace), path.join(workspace, 'cli'));
		});
	});

	test('a test beneath the sources it tests runs with them', () => {
		withTree(['main.maxon', 'lib/math.maxon', 'lib/math.maxtest'], root => {
			assert.strictEqual(testProjectDirectory(path.join(root, 'lib', 'math.maxtest'), root), root);
		});
	});

	test('never climbs above the workspace folder', () => {
		withTree(['outer.maxon', 'workspace/main.maxon', 'workspace/main.maxtest'], root => {
			const workspace = path.join(root, 'workspace');
			assert.strictEqual(testProjectDirectory(path.join(workspace, 'main.maxtest'), workspace), workspace);
		});
	});
});

suite('isIgnoredDirectory', () => {
	test('a marker excludes its directory and everything beneath it', () => {
		withTree(['fixtures/.maxonignore', 'fixtures/deep/a.maxtest', 'src/a.maxtest'], root => {
			assert.strictEqual(isIgnoredDirectory(path.join(root, 'fixtures')), true);
			assert.strictEqual(isIgnoredDirectory(path.join(root, 'fixtures', 'deep')), true);
			assert.strictEqual(isIgnoredDirectory(path.join(root, 'src')), false);
		});
	});
});
