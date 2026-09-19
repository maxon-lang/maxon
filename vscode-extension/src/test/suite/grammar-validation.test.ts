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
		const expectedPatterns = ['#comments', '#keywords', '#block-labels', '#strings', '#characters', '#numbers', '#operators', '#function-definitions', '#function-calls', '#types'];

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

		const declarationKeywords = ['var', 'function', 'let', 'export', 'public', 'type', 'union', 'enum', 'interface', 'typealias', 'extension', 'static'];
		for (const kw of declarationKeywords) {
			assert.ok(declarationPattern.match.includes(kw), `Declaration pattern should include ${kw}`);
		}
	});

	test('Keywords pattern includes boolean literals', () => {
		const keywordsRepo = grammar.repository.keywords;
		const boolPattern = keywordsRepo.patterns.find((p: any) => p.name === 'constant.language.maxon');
		assert.ok(boolPattern, 'Boolean literal pattern should exist');
		assert.ok(boolPattern.match.includes('true'), 'Boolean pattern should include true');
		assert.ok(boolPattern.match.includes('false'), 'Boolean pattern should include false');
	});

	// The word-spelled operators. Maxon has no `&&` or `||`, so these ARE the operators a reader sees
	// most, and nothing else in the grammar would colour them as anything but ordinary identifiers.
	test('Keywords pattern includes the word operators', () => {
		const keywordsRepo = grammar.repository.keywords;
		const operatorPattern = keywordsRepo.patterns.find((p: any) => p.name === 'keyword.operator.logical.maxon');
		assert.ok(operatorPattern, 'Word operator pattern should exist');

		const wordOperators = ['mod', 'and', 'or', 'not', 'as', 'is', 'shl', 'shr', 'xor'];
		for (const operator of wordOperators) {
			assert.ok(operatorPattern.match.includes(operator), `Word operator pattern should include ${operator}`);
		}
	});

	// ⚠ THE BUILT-IN TYPES ARE KEYWORDS AND THE STDLIB'S TYPES ARE NOT, which is why they sit in two
	// repositories under two scopes. 'string', 'character' and 'map' are stdlib types, so they are
	// `support.type.maxon` under their real spellings and never `storage.type.maxon`.
	test('Type keywords pattern exists', () => {
		const keywordsRepo = grammar.repository.keywords;
		const typePattern = keywordsRepo.patterns.find((p: any) => p.name === 'storage.type.maxon' && p.match.includes('int'));
		assert.ok(typePattern, 'Built-in type keyword pattern should exist');

		for (const type of ['int', 'bool', 'float', 'byte']) {
			assert.ok(typePattern.match.includes(type), `Type pattern should include ${type}`);
		}

		const typesRepo = grammar.repository.types;
		assert.ok(typesRepo, 'Types repository should exist');

		const stdlibPattern = typesRepo.patterns.find((p: any) => p.name === 'support.type.maxon');
		assert.ok(stdlibPattern, 'Stdlib type pattern should exist');

		for (const type of ['String', 'Character', 'Array', 'Map']) {
			assert.ok(stdlibPattern.match.includes(type), `Stdlib type pattern should include ${type}`);
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

	// A definition and a call are scoped apart, so a name being declared reads differently from the same
	// name being used. Both carry their scope on a CAPTURE rather than on the pattern: the match covers
	// the `function` keyword or the trailing paren too, which must not take the name's colour.
	test('Function definition and call patterns exist', () => {
		const definitionsRepo = grammar.repository['function-definitions'];
		assert.ok(definitionsRepo, 'Function definition repository should exist');

		const definitionPattern = definitionsRepo.patterns.find((p: any) => p.captures?.['2']?.name === 'entity.name.function.maxon');
		assert.ok(definitionPattern, 'Function definition pattern should name the declared function');
		assert.ok(definitionPattern.match, 'Function definition pattern should have match');

		const callsRepo = grammar.repository['function-calls'];
		assert.ok(callsRepo, 'Function call repository should exist');

		const callPattern = callsRepo.patterns.find((p: any) => p.captures?.['1']?.name === 'entity.name.function.call.maxon');
		assert.ok(callPattern, 'Function call pattern should name the called function');

		const methodPattern = callsRepo.patterns.find((p: any) => p.captures?.['1']?.name === 'entity.name.function.method.maxon');
		assert.ok(methodPattern, 'Method call pattern should name the called method');
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
