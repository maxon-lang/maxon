---
title: "A tour: building grep"
description: A guided read of examples/maxgrep.maxon — a parallel recursive grep with its own regex engine, in about 800 lines of Maxon.
sidebar:
  order: 4
---

[Your first program](/docs/getting-started/first-program/) showed the language one feature at a
time. This page reads a whole program instead: `examples/maxgrep.maxon`, a working `grep` that
searches directories in parallel. It is the same file you can find in the repository and on the
[Examples](/examples/) page, and nothing in it is a toy — it compiles its own regular expressions,
walks its own directory trees, and hands work to a pool of workers.

The program carries no comments. That is deliberate: Maxon's bet is that code should answer for
itself, so a tour like this one is where the *why* goes. Read it top to bottom and you will have
seen most of the language.

## What it does

`maxgrep PATTERN [PATH...]` searches each file, everything under each directory, or standard input
when no path is given. It supports the flags a person actually types:

| Flag | Meaning |
| --- | --- |
| `-i` | ignore ASCII letter case |
| `-v` | select the lines that do *not* match |
| `-n` | prefix each line with its line number |
| `-c` | print only how many lines each file selected |
| `-l` | print only the names of files with a selection |
| `-F` | take `PATTERN` as plain text, not a regular expression |
| `--stats` | report files, lines, workers and elapsed time on stderr |
| `--jobs=N` | search with N parallel workers |
| `--` | end the flags, so `PATTERN` may start with `-` |

The pattern language is Pike's tiny regex — `.`, `^`, `$`, `*`, `+`, `?`, `[a-z]`, `[^…]` and `\`
to take the next byte literally — matched byte by byte, so it works on any file. Exit codes follow
`grep`: `0` when something was selected, `1` when nothing was, `2` when something went wrong.

## Domain aliases, container aliases

The file opens with nothing but names.

```maxon
typealias LineNumber = int(1 to u64.max)
typealias MatchCount = int(0 to u64.max)
typealias FileCount = int(0 to u64.max)
typealias WorkerCount = int(1 to 256)
typealias BufferPos = int(0 to u64.max)
typealias PieceIndex = int(0 to u64.max)

typealias PathArray = Array with FilePath
typealias NameSet = Set with String
typealias PieceArray = Array with Piece
typealias LineMatchArray = Array with LineMatch
typealias ByteMembership = Vector with 256 bool
typealias ByteFold = function(Byte) returns Byte
typealias WalkPromise = Promise with (PathArray, WalkError)
typealias WalkPromiseArray = Array with WalkPromise
typealias SearcherArray = Array with Searcher.handle
typealias ReportPromise = Promise with (FileReport, Searcher.search.errors)
typealias ReportPromiseArray = Array with ReportPromise
```

The first group are **[ranged type aliases](/docs/language/ranged-typealiases/)**. In Maxon a
numeric type used in a type position states its range, and the range is part of the contract:
`WorkerCount = int(1 to 256)` cannot hold `0`, so no function down the line has to ask whether a
worker count of zero is possible. `--jobs=0` is rejected once, in the parser, and after that the
type carries the guarantee. `LineNumber = int(1 to u64.max)` says the same thing about line
numbering, which starts at 1. Where there is genuinely no upper bound — a byte offset into a file —
`u64.max` is the honest answer; the rule is to be as narrow as the domain really is, not narrower.

The second group are **container aliases**. Maxon's generic containers are instantiated by naming
them: `Array with FilePath` is an array of paths, `Set with String` a set of strings, and
`Vector with 256 bool` a *fixed-size* table of 256 booleans — exactly one slot per byte value,
which is how a character class is going to be stored. A generic type is instantiated *through* a
`typealias`, so every container in the program arrives with a name that reads as a domain concept
(`PathArray`, `ByteMembership`) rather than as machinery. See
[Collections](/docs/stdlib/collections/) for what each container offers.

Three of these aliases name things that do not exist yet:

- `ByteFold = function(Byte) returns Byte` is a **function type** — a value that takes a byte and
  gives one back. It will be either "lower-case this" or "leave it alone".
- `WalkPromise = Promise with (PathArray, WalkError)` is the result of a coroutine that is still
  running: it will produce a `PathArray`, or throw a `WalkError`.
- `ReportPromise = Promise with (FileReport, Searcher.search.errors)` reaches into a type to name
  an error set: *whatever `Searcher.search` can throw*. Writing the set out by hand would rot the
  moment `search` learned a new failure; naming it cannot.

`SearcherArray = Array with Searcher.handle` is the same trick for services — `Searcher.handle` is
the type of a handle to a spawned `Searcher`, which the next-to-last section gets to.

## Constants, and a set built before `main`

```maxon
let programPathArgument = 0
let firstLineNumber = 1
let fewestWorkers = 1
let mostWorkers = 256
let nanosPerMillisecond = 1000000
let jobsOptionPrefix = "--jobs="
let standardInputLabel = "(standard input)"
let skippedDirectoryNames = NameSet from [".git", "node_modules", ".maxon"]
```

Every literal that means something gets a name. `mostWorkers` is `256` in one place, and
`WorkerCount`'s range quotes it; `nanosPerMillisecond` is the only `1000000` in the file.

`skippedDirectoryNames` is a **top-level `let`** holding a real collection, built from an array
literal before `main` runs. A module-level `let` is readable everywhere, including from inside a
service handler — a module-level `var` is not, which is the rule that keeps parallel code honest
(see [Variables](/docs/language/variables/)).

## Enums that carry a value

```maxon
enum Outcome
	selected = 0
	nothingSelected = 1
	troubled = 2
end 'Outcome'

enum Flag
	ignoreCase = "-i"
	invertMatch = "-v"
	lineNumbers = "-n"
	countOnly = "-c"
	filesWithMatches = "-l"
	fixedString = "-F"
	stats = "--stats"
	endOfFlags = "--"
```

Both are **[raw-value enums](/docs/language/enums-unions/)**. `Outcome` is backed by `int` and its
cases are the process exit codes, which is why the last line of the program can be
`return summary.outcome()` — an `int`-backed enum coerces to its backing primitive, and `ExitCode`
takes it.

`Flag` is backed by `String`, and its raw values are the flags as typed. That single declaration is
the whole flag table: `Flag.fromRawValue("-i")` turns text into a case, `flag.rawValue` turns it
back, and `Flag.allCases` walks them in declaration order — which is how the usage message stays in
step with the parser without a second list to forget to update.

An enum can carry methods:

```maxon
	function summary() returns String
		return match self 'describe'
			ignoreCase gives "ignore ASCII letter case"
			invertMatch gives "select the lines that do not match"
			lineNumbers gives "put the line number before each line"
			countOnly gives "print how many lines each file selected"
			filesWithMatches gives "print only the name of each file with a selected line"
			fixedString gives "treat PATTERN as plain text, not a regular expression"
			stats gives "report files, selected lines, workers and time on standard error"
			endOfFlags gives "end the flags, so PATTERN may start with -"
		end 'describe'
	end 'summary'
```

`match self` over an enum must be **exhaustive**: add a case to `Flag` and this method stops
compiling until it is described. The usage text and the flag set cannot drift apart, because the
compiler will not let them. Note the block label — `'describe'` opens the match and closes it, so a
long `match` nested in a long function still reads unambiguously.

## Parsing the command line

`Options.parse` is a static function on the `Options` record that either produces a fully-formed
`Options` or throws a `UsageError`.

```maxon
	static function parse(args StringArray) returns Options throws UsageError
		var options = Self{jobs: availableWorkers()}
		var patternSeen = false
		var flagsEnded = false

		for (argument, text) in args.withIterator() 'eachArgument'
			if argument.index() == programPathArgument 'programPath'
				continue
			end 'programPath'

			if patternSeen 'everyArgumentAfterThePatternIsAPath'
				options.paths.push(try FilePath.from(text) otherwise throw UsageError.invalidPath)
			end 'everyArgumentAfterThePatternIsAPath' else if flagsEnded or not text.startsWith("-") 'theFirstArgumentThatIsNotAFlagIsThePattern'
				options.pattern = text
				patternSeen = true
			end 'theFirstArgumentThatIsNotAFlagIsThePattern' else if text.startsWith(jobsOptionPrefix) 'jobsOption'
				options.jobs = try parseJobs(text)
			end 'jobsOption' else 'namedFlag'
				let flag = try Flag.fromRawValue(text) otherwise throw UsageError.unknownFlag
```

Several things worth slowing down for.

`args.withIterator()` gives each element *and* its position; `argument.index()` is how the loop
skips `argv[0]` without a counter of its own.

The `if` / `else if` chain is argument-order precedence written out, and **the labels state the
rule** rather than naming the branch: `'everyArgumentAfterThePatternIsAPath'` comes first because
it outranks everything below it, and
`'theFirstArgumentThatIsNotAFlagIsThePattern'` outranks `'jobsOption'` and `'namedFlag'`. Read top
to bottom and the precedence is the prose. Every label is repeated on its `end`, so a reviewer who
lands in the middle of the chain knows which case they are in. Labels are not decoration; they are
the structure made visible ([Statements](/docs/language/statements/)).

`try FilePath.from(text) otherwise throw UsageError.invalidPath` is the shape you will see all
over: a fallible call must be resolved right there. There is no null to forget, and no unchecked
result to carry around ([Error handling](/docs/language/error-handling/)).

The last branch is the flag table in action:

```maxon
				match flag 'apply'
					ignoreCase then options.ignoreCase = true
					invertMatch then options.invertMatch = true
					lineNumbers then options.lineNumbers = true
					countOnly then options.countOnly = true
					filesWithMatches then options.filesWithMatches = true
					fixedString then options.fixedString = true
					stats then options.stats = true
					endOfFlags then flagsEnded = true
				end 'apply'
```

`Flag.fromRawValue` fails for anything that is not a flag — `otherwise throw` converts the failure
into this program's own error — and the `match` that follows must name every case, so a new flag
cannot be silently ignored.

Numbers get the same treatment:

```maxon
	static function parseJobs(argument String) returns WorkerCount throws UsageError
		var requested = 0

		try 'readNumber'
			requested = int.fromString(CommandLine.optionValue(argument))
		end 'readNumber' otherwise throws UsageError.invalidJobs

		if requested < fewestWorkers or requested > mostWorkers 'outOfRange'
			throw UsageError.invalidJobs
		end 'outOfRange'

		return requested
	end 'parseJobs'
```

`try 'readNumber' … end 'readNumber' otherwise throws UsageError.invalidJobs` is the block form: a
whole region of code whose failures become one named error. Inside it, `int.fromString` and
`CommandLine.optionValue` (which returns everything after the first `=`, see
[Input and output](/docs/stdlib/io/)) can each fail, and neither needs its own handler.

The range check that follows is not redundant with `WorkerCount`'s range: `requested` is a plain
`int` read from text, and the check is what turns it into a value the alias will accept — with a
*usage error* rather than a panic, because a bad `--jobs=` is the user's mistake, not the
program's.

## Two values, built once

`Options` is the parsed command line, and nothing downstream sees it. It is boiled down first into
two records:

```maxon
	function searchRequest() returns SearchRequest
		return SearchRequest.create(self.pattern, ignoreCase: self.ignoreCase, fixedString: self.fixedString, invertMatch: self.invertMatch, keepsLines: not (self.countOnly or self.filesWithMatches), stopsAtFirstSelection: self.filesWithMatches)
	end 'searchRequest'

	function outputStyle() returns OutputStyle
		return OutputStyle.create(namesEachFile(), lineNumbers: self.lineNumbers, countOnly: self.countOnly, filesWithMatches: self.filesWithMatches)
	end 'outputStyle'
```

```maxon
type SearchRequest
	export var pattern as String
	export var ignoreCase as bool
	export var fixedString as bool
	export var invertMatch as bool
	export var keepsLines as bool
	export var stopsAtFirstSelection as bool

	static function create(pattern String, ignoreCase bool, fixedString bool, invertMatch bool, keepsLines bool, stopsAtFirstSelection bool) returns Self
		return Self{pattern: pattern, ignoreCase: ignoreCase, fixedString: fixedString, invertMatch: invertMatch, keepsLines: keepsLines, stopsAtFirstSelection: stopsAtFirstSelection}
	end 'create'
end 'SearchRequest'

type OutputStyle
	export var namesFiles as bool
	export var lineNumbers as bool
	export var countOnly as bool
	export var filesWithMatches as bool

	static function create(namesFiles bool, lineNumbers bool, countOnly bool, filesWithMatches bool) returns Self
		return Self{namesFiles: namesFiles, lineNumbers: lineNumbers, countOnly: countOnly, filesWithMatches: filesWithMatches}
	end 'create'
end 'OutputStyle'
```

A **`SearchRequest`** is everything needed to *find* lines: the pattern text and the four booleans
that change what counts as a selection. An **`OutputStyle`** is everything needed to *print* them.
The split matters because the two halves have different fates — the request crosses into every
worker, the style never leaves `main`'s side of the program.

It is also where two rules stop being scattered. `namesEachFile()` — the "is a filename prefix
printed?" question, which is yes for several paths or for any directory — is asked once, here, and
the answer is a field. The renderer takes a style and asks `style.namesFiles`; it never sees
`Options`, so there is no second place where the rule could be decided differently. And
`SearchRequest` carrying the pattern text is what lets one value be *both* validated in `main` and
spawned into the workers, which the `Searcher` section comes back to.

## A pattern is a list of pieces

```maxon
union Atom
	literal(value Byte)
	anyByte
	byteClass(members ByteMembership, negated bool)
end 'Atom'

enum Repeat
	once
	zeroOrMore
	oneOrMore
	optional
end 'Repeat'

type Piece
	export var atom as Atom
	export var repeat as Repeat

	static function create(atom Atom, repeat Repeat) returns Self
		return Self{atom: atom, repeat: repeat}
	end 'create'
end 'Piece'
```

`Atom` is a **[union](/docs/language/enums-unions/)**: a value that is exactly one of these shapes,
and the cases carry data. A literal byte carries the byte; `anyByte` carries nothing; a byte class
carries the 256-slot membership table and whether it was negated. Matching on a union binds the
payload by name, and — like an enum — must cover every case.

`Repeat` is a plain enum with no raw value, and `Piece` pairs the two. So a compiled pattern is just
an array of (atom, repetition) pairs, plus two anchor flags. That is the entire representation.

## Extending a built-in type

```maxon
extension int
	function asciiLowered() returns Byte
		return match self 'letterCase'
			'A' to 'Z' gives self - 'A' + 'a'
			default gives self
		end 'letterCase'
	end 'asciiLowered'
end 'int'
```

An `extension` adds methods to an existing type, here `int`. The interesting line is the match arm:
`'A' to 'Z'` is a **range pattern** over byte literals, and `default gives self` covers everything
else. Character literals are integers, so `self - 'A' + 'a'` is the usual ASCII arithmetic with the
magic numbers spelled as the characters they mean.

## Case folding as a function value

```maxon
function byteFold(ignoreCase bool) returns ByteFold
	if ignoreCase 'foldCase'
		return function(b Byte) gives b.asciiLowered()
	end 'foldCase'

	return function(b Byte) gives b
end 'byteFold'
```

`byteFold` returns a **closure** — one of two, decided once, when the pattern is compiled. Neither
closure captures anything, so calling one costs nothing extra, and every place that needs to fold a
byte (compiling the pattern, matching a line, building the fixed-string needle) just calls the
`ByteFold` it was handed. The alternative — threading an `ignoreCase` boolean through nine call
sites and branching on it in the inner loop — is exactly the kind of decision-repeated-everywhere
the language is trying to discourage. See [Functions](/docs/language/functions/).

## Reading the pattern: tuples and `throws`

```maxon
function readByte(source ByteArray, at BufferPos) returns (Byte, BufferPos) throws PatternError
	let symbol = byteAt(source, position: at)

	if symbol != '\\' 'plain'
		return (symbol, at + 1)
	end 'plain'

	if at + 1 >= source.count() 'nothingEscaped'
		throw PatternError.trailingBackslash
	end 'nothingEscaped'

	return (byteAt(source, position: at + 1), at + 2)
end 'readByte'
```

A parser function here returns **a tuple**: the thing it read, and the position after it. There is
no cursor object and no hidden mutable state, so `readByte` is a pure function of `(source, at)`.
Callers destructure the result:

```maxon
		while cursor < source.count() 'eachPiece'
			if cursor == source.count() - 1 and byteAt(source, position: cursor) == '$' 'endAnchor'
				anchoredAtEnd = true
				break
			end 'endAnchor'

			let (atom, afterAtom) = try readAtom(source, at: cursor, fold: fold)
			let (repeat, afterRepeat) = readRepeat(source, at: afterAtom)
			pieces.push(Piece.create(atom, repeat: repeat))
			cursor = afterRepeat
		end 'eachPiece'
```

`let (atom, afterAtom) = try readAtom(…)` binds both halves in one statement. `try` is required
because `readAtom` is declared `throws PatternError`; here there is no `otherwise`, which means
"propagate" — and that is legal only because `compile` itself is declared `throws PatternError`. A
`try` with no handler in a function that does not declare the error is a compile error, so the
error set in a signature is always the truth ([Patterns and errors](/docs/language/patterns-and-errors/)).

The two anchors are handled outside the piece list: a leading `^` sets `anchoredAtStart` and is
consumed before the loop; a `$` that is the *last* byte sets `anchoredAtEnd` and ends it. Anywhere
else, `$` is an ordinary literal — which is what `grep` does too.

## One interface, two matchers

```maxon
interface Matcher
	function matchesLine(text ByteArray, lineStart BufferPos, lineEnd BufferPos) returns bool
end 'Matcher'

type FixedMatcher implements Matcher
	var needle as ByteArray
	var fold as ByteFold

	static function create(patternText String, fold ByteFold) returns Self
		var needle = ByteArray.create()

		for b in patternText.bytes() 'eachByte'
			needle.push(fold(b))
		end 'eachByte'

		return Self{needle: needle, fold: fold}
	end 'create'
```

`Matcher` is an [interface](/docs/language/composite-types/) with a single method. `FixedMatcher`
implements it by scanning for a byte sequence; `RegexMatcher` implements it by running the pattern.
`Searcher` then holds a field typed as the *interface*, so `-F` picks the fixed matcher and skips
the regex engine entirely while everything downstream calls `matchesLine` and never learns which
one it got.

## Pike's matcher

This is the one part of the program that is not obvious on sight, so here is the algorithm before
the code. Rob Pike's regex matcher is a recursive backtracker of about twenty lines. Two mutually
recursive functions:

- **`matchHere(text, at, piece)`** — "does the pattern from `piece` onwards match the text from
  `at` onwards?" If there are no pieces left, the match succeeded (unless `$` demands that we also
  be at the end of the line). Otherwise look at the piece's repetition and dispatch.
- **`matchStar(atom, atLeastOnce, …)`** — handles `*` and `+`: consume the atom zero or more times,
  and after each step ask whether the *rest* of the pattern matches from here.

```maxon
	function matchHere(text ByteArray, at BufferPos, lineEnd BufferPos, piece PieceIndex) returns bool
		if piece == self.pieces.count() 'patternDone'
			return at == lineEnd or not self.anchoredAtEnd
		end 'patternDone'

		let current = try self.pieces.get(piece) otherwise panic("matchHere: piece is below the piece count")
		let next = piece + 1

		return match current.repeat 'repetition'
			once gives atomMatchesAt(current.atom, text: text, at: at, lineEnd: lineEnd) and matchHere(text, at: at + 1, lineEnd: lineEnd, piece: next)
			optional gives (atomMatchesAt(current.atom, text: text, at: at, lineEnd: lineEnd) and matchHere(text, at: at + 1, lineEnd: lineEnd, piece: next)) or matchHere(text, at: at, lineEnd: lineEnd, piece: next)
			zeroOrMore gives matchStar(current.atom, atLeastOnce: false, text: text, at: at, lineEnd: lineEnd, next: next)
			oneOrMore gives matchStar(current.atom, atLeastOnce: true, text: text, at: at, lineEnd: lineEnd, next: next)
		end 'repetition'
	end 'matchHere'
```

The `match` on `current.repeat` is where the four repetitions live, each as a single expression.
`once` requires the atom and then the rest. `optional` tries "atom then rest" and, failing that,
"rest" — which is `?` exactly. `zeroOrMore` and `oneOrMore` are the same call with
`atLeastOnce: false` and `atLeastOnce: true`.

```maxon
	function matchStar(atom Atom, atLeastOnce bool, text ByteArray, at BufferPos, lineEnd BufferPos, next PieceIndex) returns bool
		var position = at

		if atLeastOnce 'oneIsRequired'
			if not atomMatchesAt(atom, text: text, at: position, lineEnd: lineEnd) 'atomMissing'
				return false
			end 'atomMissing'

			position = position + 1
		end 'oneIsRequired'

		var restMatches = matchHere(text, at: position, lineEnd: lineEnd, piece: next)

		while not restMatches and atomMatchesAt(atom, text: text, at: position, lineEnd: lineEnd) 'takeOneMore'
			position = position + 1
			restMatches = matchHere(text, at: position, lineEnd: lineEnd, piece: next)
		end 'takeOneMore'

		return restMatches
	end 'matchStar'
```

That reads as three steps. First, `+` and only `+` must consume one atom up front — if it is not
there, nothing can save the match. Then `restMatches` asks the question the whole function exists to
answer: *does the rest of the pattern match from where we now stand?* The loop keeps consuming one
more atom while the answer is no and another atom is available, and the answer it ends up with is
the answer returned. There is no negation to unwind and no separate success path: `restMatches` is
true exactly when the match succeeded.

This is the lazy (leftmost, shortest) variant — it stops at the first position where the remainder
matches — which is all `grep` needs, since `grep` only asks *whether* a line matched, never which
span.

`matchesLine` drives it: when the pattern is anchored with `^` it tries exactly one starting
position, and otherwise every position in the line.

## `Searcher`: the same type, with and without a thread

```maxon
type Searcher
	var matcher as Matcher
	var request as SearchRequest

	static function prepare(request SearchRequest) returns Self throws PatternError
		let fold = byteFold(request.ignoreCase)

		if request.fixedString 'fixed'
			return Self{matcher: FixedMatcher.create(request.pattern, fold: fold), request: request}
		end 'fixed'

		return Self{matcher: try RegexMatcher.compile(request.pattern, fold: fold), request: request}
	end 'prepare'

	static function create(request SearchRequest) returns Self
		return try prepare(request) otherwise panic("main prepares this same request before it spawns a Searcher")
	end 'create'
```

`prepare` is `throws PatternError` — only the regex path can fail — and `create` is the same thing
with the failure turned into a panic. That pairing is the safety argument for spawning a worker, and
it is visible: `main` calls `prepare(request)` on exactly the request it later hands to
`spawn Searcher.create(…)`, so by the time a worker compiles that pattern it has already compiled
cleanly once. A bad pattern is reported before a single file is opened.

```maxon
	export function search(path FilePath) returns FileReport throws SearchError
		let contents = try File.readBinary(path) otherwise throw SearchError.unreadable
		return searchBuffer(contents)
	end 'search'
```

`searchBuffer` is the core: split the buffer at newlines, ask the matcher about each line, and
apply the request.

```maxon
	function searchBuffer(buffer ByteArray) returns FileReport
		let isBinary = buffer.contains(ControlByte.nul)
		var report = FileReport.create(isBinary)
		var lineStart = 0
		var lineNumber = firstLineNumber

		while lineStart < buffer.count() 'eachLine'
			let lineEnd = lineEndFrom(buffer, lineStart: lineStart)
			let selected = self.matcher.matchesLine(buffer, lineStart: lineStart, lineEnd: lineEnd) != self.request.invertMatch

			if selected 'selectLine'
				report.selectedCount = report.selectedCount + 1

				if self.request.keepsLines and not isBinary 'keepText'
					let text = try buffer.slice(lineStart, endIndex: lineEnd as ElementIndex) otherwise panic("searchBuffer: a line lies inside its buffer")
					report.lines.push(LineMatch.create(lineNumber, text: String.from(text)))
				end 'keepText'

				if self.request.stopsAtFirstSelection 'enough'
					break
				end 'enough'
			end 'selectLine'
```

`!= self.request.invertMatch` is `-v` in one operator — the line is selected when the match result
differs from the inversion flag. `keepsLines` is false for `-c` and `-l`, so those modes never
build a single line string; `stopsAtFirstSelection` is `-l`, which abandons the file at its first
hit. A file containing a NUL byte is reported as binary, and the renderer prints
`Binary file … matches` instead of the lines — again, what `grep` does.

The part worth dwelling on is `search`, marked `export`. `Searcher` is an ordinary record: build it
with `Searcher.prepare(request)` and call `searchBuffer` on it directly, which is what the
standard-input path does — no threads involved. Start the *same type* with `spawn` and you get a
**service**: a green thread that handles one message at a time, whose messages are exactly its
`export` methods. `search` is exported; `searchBuffer` is not, so it is callable on a plain value
and not over a handle.

That symmetry is the point. A service's logic is testable and debuggable as a plain value, with no
concurrency in the picture, and becomes concurrent only at the `spawn`.
[Async and services](/docs/language/async/) has the full rules.

## Walking the trees with `async`

```maxon
function findFiles(options Options, summary RunSummary) returns PathArray
	var walks = WalkPromiseArray.create()

	for root in options.paths 'startWalks'
		walks.push(async walkTree(root))
	end 'startWalks'

	var files = PathArray.create()

	for (walk, found) in walks.withIterator() 'finishWalks'
		let tree = try await found otherwise 'unreadable'
			let root = try options.paths.get(walk.index()) otherwise panic("findFiles: one walk per path")
			printError("maxgrep: {root}: cannot read\n")
			summary.troubled = true
			continue
		end 'unreadable'

		files.append(tree)
	end 'finishWalks'
```

`async walkTree(root)` starts the walk as a **coroutine** and immediately returns a promise. Each
root is started before any is awaited, so several directory walks are in flight at once; a
coroutine that is waiting on the filesystem parks its green thread instead of blocking, and another
walk runs meanwhile. (`async`-only coroutines share one green thread — the overlap here is I/O
waits, not CPU. The parallelism comes later, from services.)

The awaiting loop is the deterministic half: `walks.withIterator()` visits the promises **in the
order they were started**, so no matter which walk finishes first, the file list comes out in
argument order. `walk.index()` recovers the root that a failed promise belonged to, which is how the
error message names the right path.

`walkTree` itself sorts each tree with `files.sort(function(left FilePath, right FilePath) gives …)`
— a comparator passed as a closure — so the output is byte-ordered by path and does not depend on
what the operating system's directory listing happened to hand back.

## Spawning the workers

```maxon
function searchFiles(files PathArray, request SearchRequest, style OutputStyle, workers WorkerCount, summary RunSummary)
	var searchers = SearcherArray.create()

	for _ in 0 upto workers 'spawnWorkers'
		searchers.push(spawn Searcher.create(request.clone()))
	end 'spawnWorkers'

	var replies = ReportPromiseArray.create()
	var nextWorker = 0

	for path in files 'sendFiles'
		var searcher = try searchers.get(nextWorker) otherwise panic("searchFiles: nextWorker stays below the worker count")
		replies.push(searcher.search(path.clone()))
		nextWorker = (nextWorker + 1) mod workers
	end 'sendFiles'
```

`searchFiles` takes what it needs and nothing more: the files, the request the workers will run, the
style the reports will be printed in, and the worker count `main` already computed.
A `spawn` of `Searcher.create(…)` starts a service and returns a handle. Then the files are dealt
out **round-robin**, one message per file, and each `searcher.search(path)` call returns a promise
immediately — the loop never waits.

Services, unlike `async` coroutines, run on the scheduler's processors, so this is real
parallelism: by default one processor per logical CPU, and `MAXON_MAX_PROCS` overrides it.

## Replies in send order

```maxon
	for (received, reply) in replies.withIterator() 'receiveReports'
		let path = try files.get(received.index()) otherwise panic("searchFiles: one reply per file")

		let report = try await reply otherwise (e) 'failed'
			match e 'why'
				unreadable then printError("maxgrep: {path}: cannot read\n")
				stopped then printError("maxgrep: {path}: the searcher stopped before it answered\n")
			end 'why'

			summary.troubled = true
			continue
		end 'failed'

		print(renderReport(report, label: path.toString(), style: style))
		summary.record(report)
	end 'receiveReports'
```

The replies are awaited in the order they were sent, which is what makes the output deterministic
while the search itself is out of order: worker 7 may finish file 100 long before worker 0 finishes
file 1, but nothing is printed until file 1's report arrives. Run the same search with one worker
or with 256 and you get byte-identical output.

The error handler is the other detail. `await`ing a reply can fail two ways — the method's own
`SearchError.unreadable`, or `ServiceError.stopped` if the service is gone — and Maxon **merges the
two error sets** at the await. The `match` must name both, and it gives each its own arm and its own
message, because they are different accidents: a file the process could not read, and a worker that
went away before answering. Leaving either case out would not compile. That is why a plain `await`
on a service reply is an error: the "service stopped" case is not something the author gets to
overlook.

`searcher.shutdown()` on each handle stops the workers once their queues drain.

## Ownership at the boundary

Two calls in that code have a `.clone()` on them:

```maxon
		searchers.push(spawn Searcher.create(request.clone()))
```

```maxon
		replies.push(searcher.search(path.clone()))
```

A value sent to a service **moves** into it — the service becomes its one owner, and the sender may
not touch it again, at all. Neither of these values is the sender's to give away. `request` is a
parameter `searchFiles` is only borrowing, and it is needed again on the very next turn of the loop
to spawn the next worker (and `main` still holds it). `path` is an element of the `files` array,
which the reply loop reads again through `files.get(received.index())`. Sending either directly is a
compile error, not a data race discovered at run time; `.clone()` hands the service a copy it can
own outright, and each worker ends up with its own request to compile.

This is the whole ownership rule in practice — the compiler asks "who else can reach this?" at every
boundary where two green threads could see the same memory.
[Memory model](/docs/language/memory-model/) has the full treatment.

## Putting it together

The body of `main` reads as the summary of everything above:

```maxon
	let options = try Options.parse(CommandLine.args()) otherwise (problem) 'badUsage'
		printUsage(problem)
		return Outcome.troubled
	end 'badUsage'

	let request = options.searchRequest()
	let style = options.outputStyle()

	let searcher = try Searcher.prepare(request) otherwise (problem) 'badPattern'
		printError("maxgrep: bad PATTERN: {problem.message()}\n")
		return Outcome.troubled
	end 'badPattern'
```

Parse, boil the options down to a request and a style, then validate the pattern by preparing a
`Searcher` from that request. Both `otherwise` handlers name their failure — `(problem)` binds it —
print, and return `Outcome.troubled`, which *is* exit code 2.

```maxon
	var summary = RunSummary.create()

	if options.paths.isEmpty() 'standardInput'
		searchStandardInput(searcher, style: style, summary: summary)
	end 'standardInput' else 'paths'
		let files = findFiles(options, summary: summary)

		if not files.isEmpty() 'anyFiles'
			let workers = options.workersFor(files.count())
			summary.workers = workers
			searchFiles(files, request: request, style: style, workers: workers, summary: summary)
		end 'anyFiles'
	end 'paths'
```

No paths means standard input, and the prepared `searcher` does the work directly on this green
thread. Otherwise the walks run, the worker count is fixed once from the file count, and
`searchFiles` gets it along with the request and the style.

`--stats` closes it out on standard error, and the accumulated counts become the exit code:

```maxon
	if options.stats 'stats'
		let elapsed = Clock.elapsedNanos(startedAt) / nanosPerMillisecond
		printError("files searched: {summary.filesSearched}\nselected lines: {summary.linesSelected}\nworkers: {summary.workers}\nelapsed ms: {elapsed}\n")
	end 'stats'

	return summary.outcome()
```

`RunSummary.outcome` is where the three exit codes are decided:

```maxon
	function outcome() returns Outcome
		return Outcome.troubled if self.troubled else Outcome.selected if self.linesSelected > 0 else Outcome.nothingSelected
	end 'outcome'
```

Chained conditional expressions, read left to right: trouble wins, then anything selected, then
nothing selected.

## Running it

`maxon run` compiles (or reuses a cached build) and runs in one step. **Everything after the path
is the program's own command line** — there is no separator to remember, and `run` has no options
of its own:

```bash
maxon run examples/maxgrep.maxon -n 'return [a-z]+' stdlib
maxon run examples/maxgrep.maxon -i --jobs=8 --stats 'TODO' maxon-bin specs
```

Or build it once and run the executable:

```bash
maxon build examples/maxgrep.maxon -o maxgrep
./maxgrep -c 'function' stdlib
```

It reads standard input when no path is given:

```bash
cat README.md | maxon run examples/maxgrep.maxon -n 'maxon'
```

## What it measures

On a 12-logical-processor machine, searching `'return [a-z]+'` across `maxon-bin`, `specs` and
`stdlib` — 33,652 files:

| Run | Time |
| --- | --- |
| `maxgrep --jobs=1` | ≈ 9.2 s |
| `maxgrep` (12 workers) | ≈ 3.9 s |
| `grep -rE` | ≈ 7.0 s |

That is a 2.35× speed-up from the worker pool, on the same code path — the only difference between
the two runs is how many `Searcher`s were spawned.

Determinism holds across all of it: the output of `--jobs=1`, of `--jobs=256`, and of a run with
`MAXON_MAX_PROCS=1` is byte-identical, because replies are consumed in send order regardless of
which worker produced them.

## Two honest notes

- **Paths print with the host separator.** `FilePath` renders the way the operating system spells
  it, so the same search prints `stdlib/Array.maxon:60` on Linux and `stdlib\Array.maxon:60` on
  Windows. Nothing normalizes it.
- **Standard input drops a trailing `\r`.** The stdin path reads lines through
  `Console.stdin().readLine()`, which strips the line ending — including the `\r` of a CRLF — and
  the loop then appends a bare `\n`. Bytes read from a *file* are searched exactly as they are on
  disk. So piping a CRLF file in and searching it as a file can differ at the end of a line, for a
  pattern ending in `$` or `.`.

## Not shown here

The program needed none of these, which is its own kind of tour:

- **Generics you declare yourself** — it instantiates library containers but declares no generic
  type of its own ([Composite types](/docs/language/composite-types/)).
- **JSON** — reading and writing structured data ([Data](/docs/stdlib/data/)).
- **TCP and HTTP** — sockets, clients and servers ([Network](/docs/stdlib/network/)).
- **Shared memory between workers** — every worker here is handed a copy and replies with a value,
  so nothing is shared at all ([Async and services](/docs/language/async/)).
- **Tests** — `test` blocks and the test runner ([Testing](/docs/language/testing/),
  [test support](/docs/stdlib/testing/)).

## Where to go next

- [Language Reference](/docs/language/overview/) — the complete language, section by section.
- [Examples](/examples/) — the other programs, including this one in full.
- [CLI Reference](/docs/cli/) — every command, option and environment variable.
