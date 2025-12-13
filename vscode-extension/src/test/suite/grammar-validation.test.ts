import * as assert from 'assert';
import * as fs from 'fs';
import * as path from 'path';

suite('TextMate Grammar Validation', () => {
	let grammarPath: string;
	let grammar: any;

	suiteSetup(() => {
		// Load the grammar file
		grammarPath = path.join(__dirname, '../../../syntaxes/maxon.tmLanguage.json');
		const grammarContent = fs.readFileSync(grammarPath, 'utf8');
		grammar = JSON.parse(grammarContent);
	});

	test('Grammar file exists and is valid JSON', () => {
		assert.ok(grammar, 'Grammar should be loaded');
		assert.strictEqual(typeof grammar, 'object', 'Grammar should be an object');
	});

	test('Grammar has correct structure', () => {
		assert.strictEqual(grammar.name, 'Maxon', 'Grammar name should be Maxon');
		assert.strictEqual(grammar.scopeName, 'source.maxon', 'Scope name should be source.maxon');
		assert.ok(grammar.patterns, 'Grammar should have patterns');
		assert.ok(grammar.repository, 'Grammar should have repository');
	});

	test('Grammar includes all pattern types', () => {
		const patterns = grammar.patterns.map((p: any) => p.include);
		const expectedPatterns = ['#comments', '#keywords', '#block-labels', '#strings', '#characters', '#numbers', '#operators', '#functions', '#types'];

		for (const expected of expectedPatterns) {
			assert.ok(patterns.includes(expected), `Grammar should include ${expected}`);
		}
	});

	test('Keywords pattern includes control flow keywords', () => {
		const keywordsRepo = grammar.repository.keywords;
		assert.ok(keywordsRepo, 'Keywords repository should exist');

		const controlFlowPattern = keywordsRepo.patterns.find((p: any) => p.name === 'keyword.control.maxon');
		assert.ok(controlFlowPattern, 'Control flow keyword pattern should exist');

		const controlFlowKeywords = ['if', 'else', 'while', 'end', 'return', 'break', 'continue'];
		for (const kw of controlFlowKeywords) {
			assert.ok(controlFlowPattern.match.includes(kw), `Control flow pattern should include ${kw}`);
		}
	});

	test('Keywords pattern includes declaration keywords', () => {
		const keywordsRepo = grammar.repository.keywords;
		const declarationPattern = keywordsRepo.patterns.find((p: any) => p.name === 'keyword.other.maxon');
		assert.ok(declarationPattern, 'Declaration keyword pattern should exist');

		const declarationKeywords = ['var', 'function', 'let', 'export', 'type', 'extern'];
		for (const kw of declarationKeywords) {
			assert.ok(declarationPattern.match.includes(kw), `Declaration pattern should include ${kw}`);
		}
	});

	test('Keywords pattern includes boolean literals', () => {
		const keywordsRepo = grammar.repository.keywords;
		const boolPattern = keywordsRepo.patterns.find((p: any) => p.name === 'constant.language.boolean.maxon');
		assert.ok(boolPattern, 'Boolean literal pattern should exist');
		assert.ok(boolPattern.match.includes('true'), 'Boolean pattern should include true');
		assert.ok(boolPattern.match.includes('false'), 'Boolean pattern should include false');
	});

	test('Keywords pattern includes math intrinsics', () => {
		const keywordsRepo = grammar.repository.keywords;
		const mathPattern = keywordsRepo.patterns.find((p: any) => p.name === 'support.function.math.maxon');
		assert.ok(mathPattern, 'Math intrinsic pattern should exist');

		const mathFunctions = ['floor', 'trunc', 'sqrt', 'abs', 'ceil', 'round', 'sin', 'cos'];
		for (const func of mathFunctions) {
			assert.ok(mathPattern.match.includes(func), `Math pattern should include ${func}`);
		}
	});

	test('Type keywords pattern exists', () => {
		const typesRepo = grammar.repository.types;
		assert.ok(typesRepo, 'Types repository should exist');

		const typePattern = typesRepo.patterns.find((p: any) => p.name === 'storage.type.maxon');
		assert.ok(typePattern, 'Type keyword pattern should exist');

		// Note: 'string', 'character', and 'map' are stdlib types, not built-in keywords, so they're not in this list
		// 'character' is now a grapheme cluster struct defined in stdlib/string/character.maxon
		const types = ['int', 'bool', 'float', 'byte'];
		for (const type of types) {
			assert.ok(typePattern.match.includes(type), `Type pattern should include ${type}`);
		}
	});

	test('Comments patterns exist', () => {
		const commentsRepo = grammar.repository.comments;
		assert.ok(commentsRepo, 'Comments repository should exist');

		const lineCommentPattern = commentsRepo.patterns.find((p: any) => p.name === 'comment.line.double-slash.maxon');
		assert.ok(lineCommentPattern, 'Line comment pattern should exist');
		assert.ok(lineCommentPattern.match, 'Line comment should have match pattern');

		const blockCommentPattern = commentsRepo.patterns.find((p: any) => p.name === 'comment.block.maxon');
		assert.ok(blockCommentPattern, 'Block comment pattern should exist');
		assert.ok(blockCommentPattern.begin, 'Block comment should have begin pattern');
		assert.ok(blockCommentPattern.end, 'Block comment should have end pattern');
	});

	test('String patterns exist', () => {
		const stringsRepo = grammar.repository.strings;
		assert.ok(stringsRepo, 'Strings repository should exist');

		const doubleQuotePattern = stringsRepo.patterns.find((p: any) => p.name === 'string.quoted.double.maxon');
		assert.ok(doubleQuotePattern, 'Double-quoted string pattern should exist');
		assert.ok(doubleQuotePattern.begin, 'Double-quoted string should have begin');
		assert.ok(doubleQuotePattern.end, 'Double-quoted string should have end');

		// Character literals are separate from strings (single-quoted)
		const charactersRepo = grammar.repository.characters;
		assert.ok(charactersRepo, 'Characters repository should exist');
		const charPattern = charactersRepo.patterns.find((p: any) => p.name === 'constant.character.maxon');
		assert.ok(charPattern, 'Character literal pattern should exist');
	});

	test('Number patterns exist', () => {
		const numbersRepo = grammar.repository.numbers;
		assert.ok(numbersRepo, 'Numbers repository should exist');

		const floatPattern = numbersRepo.patterns.find((p: any) => p.name === 'constant.numeric.float.maxon');
		assert.ok(floatPattern, 'Float pattern should exist');

		const intPattern = numbersRepo.patterns.find((p: any) => p.name === 'constant.numeric.integer.maxon');
		assert.ok(intPattern, 'Integer pattern should exist');
	});

	test('Operator patterns exist', () => {
		const operatorsRepo = grammar.repository.operators;
		assert.ok(operatorsRepo, 'Operators repository should exist');

		const arithmeticPattern = operatorsRepo.patterns.find((p: any) => p.name === 'keyword.operator.arithmetic.maxon');
		assert.ok(arithmeticPattern, 'Arithmetic operator pattern should exist');

		const comparisonPattern = operatorsRepo.patterns.find((p: any) => p.name === 'keyword.operator.comparison.maxon');
		assert.ok(comparisonPattern, 'Comparison operator pattern should exist');
	});

	test('Function call pattern exists', () => {
		const functionsRepo = grammar.repository.functions;
		assert.ok(functionsRepo, 'Functions repository should exist');

		const functionPattern = functionsRepo.patterns.find((p: any) => p.name === 'entity.name.function.maxon');
		assert.ok(functionPattern, 'Function pattern should exist');
		assert.ok(functionPattern.match, 'Function pattern should have match');
	});

	test('Regex patterns use proper word boundaries', () => {
		// Check that keyword patterns use \b word boundaries (not \\b which would be literal)
		const keywordsRepo = grammar.repository.keywords;
		const controlFlowPattern = keywordsRepo.patterns.find((p: any) => p.name === 'keyword.control.maxon');

		// The pattern should contain \b (which in JSON is represented as \\b)
		assert.ok(controlFlowPattern.match.includes('\\b'), 'Pattern should use word boundaries');
		// But not \\\\b which would be double-escaped
		assert.ok(!controlFlowPattern.match.includes('\\\\\\\\b'), 'Pattern should not be double-escaped');
	});
});
