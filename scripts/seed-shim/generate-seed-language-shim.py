#!/usr/bin/env python3
"""generate-seed-language-shim.py - write the seed shim that withdraws every language form the seed
cannot parse, and rewrites every site the seed's older rules refuse.

Two forms are withdrawn on every run. A `view` binding (`view x = e`, `view (a, b) = e`, `if view x = e`, a
`view` target of a tuple assignment) is written `let`, which the seed reads as a read-only binding of the same
value. A capability qualifier is withdrawn: `keep` and `sole` on a parameter of a function-type alias or an
interface requirement, and `sole` or `readonly` after `returns`.

With --older-seed, five more forms are withdrawn for a seed that predates them: a `typealias ... implements`
clause, a generic function (`function f(...) uses T`), a type body's inferred field (`var x = e as T`,
`var x = Type.member(...)`, `var x = "text"`, which the seed reads only as `var x as T = e`), a `readonly`
qualifier on a container's element type (`Array with readonly T`, `Map with (K, readonly V)`) and a `let` on a
union case's payload (`case(let name T)`). Such a seed enforces every ranged typealias nominally, so once the
clauses are gone each widening crossing the tree relies on is an E3005/E2028 there; a generic function is
withdrawn whole, which the tree affords because the compiler calls none; an inferred field is written back with
the type its initializer names; the two qualifiers are withdrawn, which leaves the container and union types the
seed always held.

This script copies the tree's `maxon-bin/`, `stdlib/` and `runtime/` into a scratch directory, stages every
other patch in `scripts/seed-shim/` in sorted order, strips the forms, and then builds with the seed for one
native target at a time until it is clean, and again for any lane
the tree changed under since its last clean build, so the code a lane's `#if` selects is judged by the seed as
well. The seed's parser ends a file's parse at its first type refusal, so a file that keeps failing is built
through a split view (`view/` in the work directory) that spreads its functions over small files, each of which
reports its own first refusal. Only the refusals a cast answers are read from those builds; one this script
cannot cast or place on a line of the real file, or a round that changes nothing, drops the view for the lane,
and the unsplit build decides. It writes at every site the seed refuses the cast to
the parent it names: on each arm of a match whose arms disagree, after an `otherwise` fallback, and,
where one operand of a binary operator is a number literal, on the other operand, cast to the
literal's alias. A cast the seed calls unneeded (E3010) is dropped. A `Compiler.`-qualified name is
written bare, which the seed resolves to the same declaration; a decimal integer literal above i64.max
is written in hex, and a typealias range bound above it as i64.max. The rules the tree no longer has
are answered at each site the seed refuses by the rewrite its older rule asks for:
  E3070  a `let` that holds an element of a collection across a call that mutates the collection is
         bound to a `.clone()` of it; a loop it reads as still borrowing what its body mutates
         iterates a clone held in a local, when the script can prove the body leaves the collection
         unmutated. A `let` written from a `view` is not cloned: the clone would stop it seeing the
         writes the tree lets it observe, so the site stops the script.
  E3078  an immutable value bound or assigned to a mutable binding, passed to a parameter that keeps
         or stores it, or stored into a `let` field, a `let` payload or a `readonly` element gets the
         `.clone()` the seed asks for.
  E3102  a `var` the seed reads as moved, by a `let` written from a `view` of it, is written `let`,
         which the seed aliases instead of moving, when nothing in its function reassigns it.
  E2013  a loop whose element is assigned through becomes an index loop that binds a `var` alias of
         the element it reads.
  E3077  a `var` the seed reads as never reassigned, because it only sees assignments where the tree
         counts a hand-off to a mutating parameter, is written `let`.
The difference is the patch.

Usage:
  python scripts/seed-shim/generate-seed-language-shim.py [--tree DIR] [--seed PATH] [--work DIR]
      [--output PATH] [--resume] [--older-seed]

--work must be empty, and defaults to a fresh system temp directory; with --resume it is the
directory a previous run left, and the build loop continues there. Keep it outside the checkout,
whose `temp/` carries a `.maxonignore`. --output defaults to the committed patch in
`scripts/seed-shim/`. --older-seed also withdraws the five forms above for a seed that predates them.

Exit codes: 0 the patch was written; 1 the tree holds what this script cannot shim: an integer
literal it cannot rewrite in a form the seed lexes, a `Compiler.` qualifier it cannot write bare
without changing the declaration it names, a field default with no inferable type, or a seed refusal
it does not cast, including a loop it cannot prove leaves its iterated collection unmutated, a `view`
the seed's older rule would clone, and a moved `var` it cannot write `let`.
"""
import argparse
import filecmp
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "library-cache"))

from maxon_source import FieldDefaultShapeError, field_default_type

PATCH_NAME = "0009-withdraw-the-language-forms-the-seed-cannot-parse-from-the-first-build.patch"
SOURCE_DIRS = ["maxon-bin", "stdlib", "runtime"]
NATIVE_TARGETS = ["x64-windows", "x64-linux", "arm64-macos", "arm64-linux"]
MAX_ROUNDS = 800
MAX_ATTEMPTS_PER_SITE = 10
VIEW_DIR = "view"
MAXON_EXTENSION = ".maxon"
RESERVED_NAME_PREFIX = "__"
SEED_DIR = ".bootstrap"
VIEW_PART_INFIX = "ViewPart"
VIEW_VISIBILITY = "module"
VIEW_VISIBILITY_WORDS = {"export", "module", "public"}
VIEW_CAST_CODES = {"E3005", "E2028", "E3059", "E3010"}
SPLIT_PART_LINES = 600
STUBBORN_ROUNDS = 2
STATEMENT_KEYWORDS = {"typealias", "let", "var"}
MEMBER_KEYWORDS = {"function", "let", "var"}
MEMBER_INDENT = "\t"
ARM_SERVED_INDEX_LABELS = {"get": "index", "set": "index", "resize": "newLength"}
WORD = re.compile(r"[A-Za-z_][A-Za-z0-9_]*")
CONSTANT = re.compile(r"^let (?P<name>[A-Za-z_][A-Za-z0-9_]*)(?: as [A-Za-z0-9_]+)? = (?P<value>.+)$")
STRING_LITERAL = re.compile(r'^b?"(?:[^"\\]|\\.)*"$')
SCOPE_OPENERS = ("if let ", "if var ", "while let ", "while var ", "for ")
PLAIN_BINDERS = ("let ", "var ")

HEADER = """# The seed cannot parse a `view` binding, so each is written `let`, which the seed reads as a read-only binding of
# the same value; a `var` the seed then reads as moved by that `let` is written `let` too where nothing reassigns it.
# The seed cannot parse a capability qualifier (`keep` or `sole` on a parameter of a function-type alias or an
# interface requirement, `returns sole`, `returns readonly`), so each is withdrawn.
# Against a seed older than the forms below (`--older-seed`), each of them is withdrawn as well.
# The seed cannot parse a `readonly` qualifier on a container's element type (`Array with readonly T`,
# `Map with (K, readonly V)`) or a `let` on a union case's payload (`case(let name T)`), so each is withdrawn. Without
# the qualifier two instances of a container are one type to the seed, which is what it always held.
# The seed cannot parse a generic function, so each is withdrawn whole; the compiler calls none of them.
# The seed cannot parse a `typealias X = int(range) implements Parent` clause either, and it enforces every ranged
# typealias nominally at every crossing. Each clause is withdrawn, and every place the tree widens a subtype
# to its parent without a cast gets the cast the seed names. A divisor cast to a parent that admits 0 makes the
# division throwing to the seed, so it is written `try (…) otherwise panic`; the tree proved it nonzero.
# The seed cannot infer a field's type from its initializer, so each type-body `var x = e as T`, `var x = Type.member(...)`
# and `var x = "text"` is written `var x as T = e`.
# The seed cannot lex a decimal integer literal above i64.max, so each is written in hex, and a typealias range bound is
# written i64.max.
# An `export` whose only reach beyond its file was through such a crossing is an E3092 to the seed once the crossing
# is a cast, so it is withdrawn too.
# The seed cannot parse a `Compiler.`-qualified name, so each is written bare; the seed resolves that to the same
# declaration.
# The declarations, their ranges and every table stay, so `C1` knows every withdrawn form and compiles the unshimmed
# tree into `C2`.
# The tree also no longer has rules the seed enforces, so each site the seed refuses is written the way its older
# rule accepts: the `.clone()` an E3078 names, at a binding, an argument or a store; a `let` that holds a collection's
# element across a call that mutates the collection is bound to a `.clone()` of it, and so is a loop's iterated
# collection where the body provably leaves it unmutated; a loop whose element is assigned through is an index loop
# over a `var` alias of the element it reads; and a `var` the seed sees no assignment to is a `let`.
# GENERATED by `scripts/seed-shim/generate-seed-language-shim.py`; regenerate it when the sources it touches move.
"""

DECLARATION = re.compile(r"^(?P<head>(?:[a-z]+ )*typealias (?P<name>[A-Za-z0-9_]+) = [^\r\n]*?) implements (?P<parent>[A-Za-z0-9_]+)[ \t]*(?P<eol>\r?)$", re.M)
GENERIC_FUNCTION = re.compile(r"^(?P<indent>[ \t]*)(?:[a-z]+ )*function (?P<name>[A-Za-z0-9_]+)\(.*\) uses [A-Z][^\r\n]*$")
RANGE_DECLARATION = re.compile(r"^(?:[a-z]+ )*typealias (?P<name>[A-Za-z0-9_]+) = int\((?P<range>[^)\r\n]*)\)", re.M)
RANGE_UPPER_BOUND_PREFIX = re.compile(r"^[ \t]*(?:[a-z]+ )*typealias [A-Za-z0-9_]+ = int\([^)]* to $")
WIDE_DECIMAL_LITERAL = re.compile(r"(?<![A-Za-z0-9_.])(?P<digits>[0-9]{19,20})(?![A-Za-z0-9_.])")
DIAGNOSTIC = re.compile(r"^error (?P<code>E\d+): (?P<path>.+?):(?P<line>\d+):(?P<column>\d+): (?P<message>.*)$")
BINARY = re.compile(r"^operator '(?P<op>[^']+)' requires both operands to be the same type: '(?P<left>[A-Za-z0-9_]+)' and '(?P<right>[A-Za-z0-9_]+)' are different typealiases")
MISMATCH = re.compile(r"^type mismatch: 'expected (?P<expected>[A-Za-z0-9_]+), got (?P<got>[A-Za-z0-9_]+)'")
MATCH_ARMS = re.compile(r"^match arms give incompatible types: '(?P<first>[A-Za-z0-9_]+)' vs '(?P<second>[A-Za-z0-9_]+)'")
UNNEEDED_CAST = re.compile(r"^unneeded cast: '(?P<source>[A-Za-z0-9_]+)' already fits in '(?P<target>[A-Za-z0-9_]+)'")
OTHERWISE = re.compile(r"^type mismatch: 'otherwise type '(?P<got>[A-Za-z0-9_]+)' does not match expected type '(?P<expected>[A-Za-z0-9_]+)'")
TERNARY = re.compile(r"^ternary expression type mismatch: true branch is '(?P<whenTrue>[A-Za-z0-9_]+)' but false branch is '(?P<whenFalse>[A-Za-z0-9_]+)'")
UNUSED_EXPORT = re.compile(r"^exported [a-z]+ '(?P<name>[A-Za-z0-9_]+)' is never referenced outside its declaring file")
ARGUMENT = re.compile(r"^argument type mismatch for '(?P<label>[A-Za-z0-9_]+)': expected '(?P<expected>[A-Za-z0-9_]+)', got '(?P<got>[A-Za-z0-9_]+)'")
IMMUTABLE_TO_MUTABLE = re.compile(r"^cannot assign immutable variable '(?P<source>[A-Za-z0-9_]+)' to mutable binding '(?P<target>[A-Za-z0-9_]+)'; .*use clone\(\)")
BORROWED_BY_LOOP = re.compile(r"^cannot mutate '(?P<owner>[A-Za-z0-9_]+)' via '(?P<via>[A-Za-z0-9_]+)' while it is borrowed by '(?P<element>[A-Za-z0-9_]+)' \(borrowed at line (?P<line>\d+)\)")
PASSED_TO_KEEPER = re.compile(r"^cannot pass '[A-Za-z0-9_]+', which is read-only \(.*\), to parameter '(?P<parameter>[A-Za-z0-9_]+)' of '.*', which keeps it where something may write it; pass a clone\(\)")
STORED_INTO_READONLY_ELEMENT = re.compile(r"^cannot store a value other names may still write into a `readonly` element of the container written by '[A-Za-z0-9_]+'")
PASSED_TO_STORER = re.compile(r"^cannot pass (?:the `var` '[A-Za-z0-9_]+', which is read again,|a value other names may still write) to parameter '(?P<parameter>[A-Za-z0-9_]+)' of '[^']+', which stores it into a `let` (?:payload|field)")
STORED_INTO_LET_SLOT =re.compile(r"^cannot store a value other names may still write into a `let` (?:payload|field) '")
STORED_ARGUMENT_LABEL = "value"
SHARES_LET_STORAGE = re.compile(r"^cannot assign (?!immutable variable )[^;]+? to mutable binding '(?P<name>[A-Za-z0-9_]+)'; use 'let' instead of 'var', or use clone\(\)")
NEVER_REASSIGNED = re.compile(r"^variable '(?P<name>[A-Za-z0-9_]+)' is never reassigned; use 'let' instead of 'var'")
IMMUTABLE_LOOP_ELEMENT = re.compile(r"^cannot assign to immutable variable: '(?P<name>[A-Za-z0-9_]+)'$")
SEED_PREFIX = "seed"
POSITION_IN_RANGE = "the loop's own position is below the count it was bounded by"
LET_BINDING = re.compile(r"^(?P<head>[ \t]*let (?P<name>[A-Za-z0-9_]+) = )(?P<value>.+?)(?P<eol>\r?)$")
FOR_LOOP = re.compile(r"^(?P<indent>[ \t]*)for (?P<element>[A-Za-z0-9_]+) in (?P<iterated>.+?) (?P<label>'[A-Za-z0-9_]+')(?P<eol>\r?)$")
READONLY_WORD = "readonly"
READONLY_ARGUMENT = re.compile(r"(?:\bwith |, |\()(?P<modifier>readonly (?=[A-Za-z(]))")
UNION_OPEN = re.compile(r"^(?P<indent>[ \t]*)(?:[a-z]+ )*union (?P<name>[A-Z][A-Za-z0-9_]*)")
UNION_CASE = re.compile(r"^[ \t]*[a-z][A-Za-z0-9_]*\(")
PAYLOAD_LET = re.compile(r"(?:(?<=\()|(?<=, ))let (?=[a-z_][A-Za-z0-9_]* [A-Z(])")
VIEW_WORD = "view"
BINDING_WORD = "let"
LEXER_SOURCE = os.path.join("maxon-bin", "Compiler", "Lexer.maxon")
KEYWORD_ROW = re.compile(r'^\s*b"(?P<word>[A-Za-z]+)": KeywordInfo\.create\(', re.M)
VIEW_WORD_AT = re.compile(r"\bview\b")
VIEW_PRECEDER = re.compile(r"(?:^|\s)(?:if|then)\s*$")
BINDING_NAME = re.compile(r"\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)")
DESTRUCTURE_OPEN = re.compile(r"\s*\(")
ASSIGNED_TO_NEXT = re.compile(r"\s*=(?!=)")
VIEW_INITIALIZER = re.compile(r"^(?:\s+as\s+[A-Za-z_][A-Za-z0-9_.]*)?\s*=\s*(?P<value>.+?)\s*$")
BARE_LOCAL_READ = re.compile(r"^\(*(?P<name>[A-Za-z_][A-Za-z0-9_]*)\)*$")
TUPLE_TARGET = r"(?:(?:let|var|view) )?[A-Za-z_][A-Za-z0-9_]*"
TUPLE_ASSIGNMENT = re.compile(rf"\(\s*{TUPLE_TARGET}(?:\s*,\s*{TUPLE_TARGET})*\s*\)\s*=(?!=)")
TUPLE_ASSIGNMENT_HEAD = re.compile(r"(?:^[ \t]*|\bthen )(?=\()")
TUPLE_VIEW_TARGET = re.compile(r"(?P<lead>[(,]\s*)view (?P<name>[A-Za-z_][A-Za-z0-9_]*)")
TUPLE_CLOSED_ASSIGNMENT = re.compile(r"\)\s*=(?!=)")
VIEWED_VAR_NOTE = "; `{name}` is the bare initializer of a `view` in this file, and a `view` of a `var` the code reads again names data the tree lets it observe changing, so no seed-legal binding keeps both names"
MOVED_VALUE = re.compile(r"^use of moved value '(?P<name>[A-Za-z0-9_]+)'")
VIEW_REGISTRY = "view-bindings.json"
CAPABILITY_HINT = re.compile(r"\b(?:keep|sole|returns readonly)\b")
RESULT_QUALIFIER = re.compile(r"\breturns (?:sole|readonly) (?=[A-Za-z_(])")
FUNCTION_TYPE_ALIAS = re.compile(r"^[ \t]*(?:[a-z]+ )*typealias [A-Za-z0-9_]+ = function(?=\()")
INTERFACE_OPEN = re.compile(r"^(?P<indent>[ \t]*)(?:[a-z]+ )*interface (?P<name>[A-Z][A-Za-z0-9_]*)")
INTERFACE_METHOD = re.compile(r"^[ \t]+(?:[a-z]+ )*function [A-Za-z_][A-Za-z0-9_]*(?=\()")
UNNAMED_QUALIFIED_PARAMETER = re.compile(r"^(?P<lead>\s*)(?:keep|sole) (?=[A-Za-z_(])")
NAMED_QUALIFIED_PARAMETER = re.compile(r"^(?P<head>\s*[A-Za-z_][A-Za-z0-9_]* )(?:keep|sole) (?=[A-Za-z_(])")
CLONE_CALL = ".clone()"
HELD_PREFIX = "seedHeld"
PROVEN_LOOP_HOISTS = {
	("maxon-bin/Compiler/Parser.maxon", "'eachCandidate'", "self.signatures.overloadedDeclsOf(callee)", "signatures", "slotTypeThroughCallReceiver"): "44f273abe7bed010dabdfb1805f7ab19f3b34d24066d5312fcdf4b5159bd2c89",
}
FIELD = re.compile(r"^cannot assign a value of type '(?P<got>[A-Za-z0-9_]+)' to (?:field|variable) '(?P<field>[A-Za-z0-9_]+)'(?: of '[A-Za-z0-9_]+')?, which holds '(?P<expected>[A-Za-z0-9_]+)'")

AUTHOR_SOURCE_DIR = "maxon-bin"
NAMESPACE_ROOT = "Compiler"
NAMESPACE_QUALIFIER = NAMESPACE_ROOT + "."
QUALIFIED = re.compile(re.escape(NAMESPACE_QUALIFIER) + r"[A-Z][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*")
DECLARED_NAME = re.compile(r"^(?:[a-z]+ )*(?:type|enum|union|typealias|interface|function|let|var) (?P<name>[A-Za-z_][A-Za-z0-9_]*)", re.M)

SELF = "self"
MEMBER_CHAIN = re.compile(r"[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*")
ASSIGNMENT = re.compile(r"\s*[-+*/%]?=(?!=)")
STDLIB_DIR = "stdlib"
COLLECTION_TYPES = {"Array", "List", "Map", "Set", "Vector"}
READ_ONLY_COLLECTION_METHODS = {"capacity", "clone", "contains", "count", "equals", "first", "get", "getCapacity", "hash", "isEmpty", "last", "slice"}
SOURCE_DECLARATION = re.compile(r"^(?P<indent>\t*)(?P<modifiers>(?:[a-z]+ )*)(?P<keyword>type|extension|interface|enum|union|function|typealias|let|var) (?P<name>[A-Za-z_][A-Za-z0-9_]*)")

BLOCK_CLOSE = re.compile(r"^(?P<indent>\t*)end '")
FIELD_DECLARATION = re.compile(r"^(?P<indent>\t+)(?P<modifiers>(?:[a-z]+ )*)(?:var|let) (?P<name>[A-Za-z_][A-Za-z0-9_]*)(?P<separator> = )(?P<value>.*)$")
BARE_LITERAL = re.compile(r"^(?:true|false|-?(?:0x[0-9A-Fa-f_]+|[0-9][0-9_]*(?:\.[0-9][0-9_]*)?))$")
STATIC_MODIFIER = "static"
TYPE_KEYWORD = "type"
FUNCTION_KEYWORD = "function"

KEYWORDS = {"if", "else", "while", "return", "not", "and", "or", "then", "gives", "let", "var", "for", "in", "upto", "downto", "match", "try", "otherwise", "throw", "as"}
IDENT = re.compile(r"[A-Za-z0-9_]")
OPENERS = {"(": ")", "[": "]", "{": "}"}
CLOSERS = {v: k for k, v in OPENERS.items()}
OPERATOR_LEVEL = {"+": 1, "-": 1, "*": 2, "/": 2, "%": 2, "mod": 2}
COMPARISON = {"==", "!=", "<", "<=", ">", ">="}
DIVISION_WITHOUT_TRY = re.compile(r"^throwing division requires try")
DIVISION_OPERATORS = ["/", "mod"]
RANGE_KEYWORDS = ("upto", "downto")
SEED_INT_LITERAL_MAX = 2**63 - 1
U64_MAX = 2**64 - 1
SEED_RANGE_MAX = "i64.max"
ARM_SEPARATOR = " gives "
OTHERWISE_KEYWORD = " otherwise "
NUMBER_LITERAL = re.compile(r"-?[0-9][0-9_]*(?:\.[0-9][0-9_]*)?")
CAST_SUFFIX = re.compile(r"\s+as\s+[A-Za-z_][A-Za-z0-9_.]*")
MULTIPLICATIVE_OPERATORS = ["*", "/", "%", " mod"]
DIVISOR_NEVER_ZERO = "the tree's divisor type excludes 0; only the cast to its parent admits it"


class Unhandled(Exception):
	pass


def run(args, cwd, check=True):
	result = subprocess.run(args, cwd=cwd, capture_output=True, text=True, encoding="utf-8", errors="replace")

	if check and result.returncode != 0:
		raise SystemExit(f"generate-seed-language-shim.py: {' '.join(args)} exited {result.returncode}\n{result.stdout}{result.stderr}")

	return result


def git(work, *args):
	return run(["git", "-c", "core.autocrlf=false", "-c", "user.name=shim", "-c", "user.email=shim@localhost", *args], cwd=work)


def stage_work(tree, work, seed, output):
	listed = run(["git", "ls-files", "-co", "--exclude-standard", "-z", "--", *SOURCE_DIRS], cwd=tree).stdout.split("\0")

	for rel in filter(None, listed):
		src = os.path.join(tree, rel)

		if not os.path.isfile(src):
			continue

		dst = os.path.join(work, rel)
		os.makedirs(os.path.dirname(dst), exist_ok=True)
		shutil.copyfile(src, dst)

	os.makedirs(os.path.join(work, SEED_DIR), exist_ok=True)
	shutil.copy2(seed, os.path.join(work, SEED_DIR, os.path.basename(seed)))

	git(work, "init", "-q")

	with open(os.path.join(work, ".git", "info", "exclude"), "a", encoding="utf-8") as exclude:
		exclude.write(f"/{SEED_DIR}/\n/maxon-bin/.maxon/\n/out/\n")

	shim_dir = os.path.join(tree, "scripts", "seed-shim")
	patches = sorted(p for p in os.listdir(shim_dir) if p.endswith(".patch"))

	for patch in patches:
		path = os.path.join(shim_dir, patch)

		if patch == PATCH_NAME or os.path.abspath(path) == os.path.abspath(output):
			continue

		git(work, "apply", "--", path)

	git(work, "add", "-A")
	git(work, "commit", "-q", "-m", "base")


def tree_files(work, directories=SOURCE_DIRS):
	for directory in directories:
		for dirpath, _, files in os.walk(os.path.join(work, directory)):
			for name in sorted(files):
				yield os.path.join(dirpath, name)


def maxon_sources(work, directories):
	return (path for path in tree_files(work, directories) if path.endswith(MAXON_EXTENSION))


def collect_aliases(root):
	parents = {}
	ranges = {}

	for path in maxon_sources(root, SOURCE_DIRS):
		text = read(path)

		for m in DECLARATION.finditer(text):
			parents[m.group("name")] = m.group("parent")

		for m in RANGE_DECLARATION.finditer(text):
			ranges[m.group("name")] = m.group("range")

	return parents, ranges


def strip_clauses(work):
	stripped_count = 0

	for path in maxon_sources(work, SOURCE_DIRS):
		text = read(path)

		if " implements " not in text:
			continue

		stripped = DECLARATION.sub(lambda m: m.group("head") + m.group("eol"), text)

		if stripped != text:
			stripped_count += len(DECLARATION.findall(text))
			write(path, stripped)

	return stripped_count


def write_wide_literals_in_hex(work):
	rewritten_count = 0
	refused = []

	for path in maxon_sources(work, SOURCE_DIRS):
		text = read(path)

		if WIDE_DECIMAL_LITERAL.search(text) is None:
			continue

		lines = text.split("\n")

		try:
			regions = CodeRegions(lines).per_line
		except Unhandled as e:
			refused.append(f"{os.path.relpath(path, work)}: {e}")
			continue

		for index, line in enumerate(lines):
			out = []
			last = 0

			for start, end in regions[index]:
				for m in WIDE_DECIMAL_LITERAL.finditer(line, start, end):
					value = int(m.group("digits"))

					if value <= SEED_INT_LITERAL_MAX:
						continue

					if value > U64_MAX:
						refused.append(f"{os.path.relpath(path, work)}:{index + 1}: the literal {value} is wider than any integer type")
						continue

					out.append(line[last:m.start()])
					out.append(SEED_RANGE_MAX if RANGE_UPPER_BOUND_PREFIX.match(line[:m.start()]) else f"0x{value:X}")
					last = m.end()
					rewritten_count += 1

			out.append(line[last:])
			lines[index] = "".join(out)

		write(path, "\n".join(lines))

	return rewritten_count, refused


def strip_generic_functions(work):
	withdrawn = 0

	for path in maxon_sources(work, SOURCE_DIRS):
		lines = read(path).split("\n")
		kept = []
		i = 0
		changed = False

		while i < len(lines):
			m = GENERIC_FUNCTION.match(lines[i])

			if m is None:
				kept.append(lines[i])
				i += 1
				continue

			end = re.compile(r"^" + re.escape(m.group("indent")) + r"end '" + re.escape(m.group("name")) + r"'\r?$")
			close = next((j for j in range(i + 1, len(lines)) if end.match(lines[j])), None)

			if close is None:
				raise SystemExit(f"generate-seed-language-shim.py: {path}:{i + 1}: generic function '{m.group('name')}' has no `end '{m.group('name')}'`")

			while kept and kept[-1].lstrip().startswith("//"):
				kept.pop()

			i = close + 1

			if i < len(lines) and lines[i].strip() == "":
				i += 1

			withdrawn += 1
			changed = True

		if changed:
			write(path, "\n".join(kept))

	return withdrawn


class LineRewrite:
	def edits(self, line, masked):
		raise NotImplementedError

	def finish(self):
		return None


def rewrite_code(work, mentions, rewrite_for):
	total = 0

	for path in maxon_sources(work, SOURCE_DIRS):
		text = read(path)

		if not mentions(text):
			continue

		rel = os.path.relpath(path, work).replace("\\", "/")
		lines = text.split("\n")

		try:
			regions = CodeRegions(lines)
		except Unhandled as e:
			raise SystemExit(f"generate-seed-language-shim.py: {rel}: {e}")

		rewrite = rewrite_for(rel)

		for index, line in enumerate(lines):
			try:
				edits = rewrite.edits(line, regions.view(index, line))
			except Unhandled as e:
				raise SystemExit(f"generate-seed-language-shim.py: {rel}:{index + 1}: {e}")

			for start, end, replacement in sorted(edits, reverse=True):
				line = line[:start] + replacement + line[end:]

			if edits:
				lines[index] = line
				total += len(edits)

		try:
			rewrite.finish()
		except Unhandled as e:
			raise SystemExit(f"generate-seed-language-shim.py: {rel}: {e}")

		rewritten = "\n".join(lines)

		if rewritten != text:
			write(path, rewritten)

	return total


class ReadonlyArguments(LineRewrite):
	def edits(self, line, masked):
		return [(m.start("modifier"), m.end("modifier"), "") for m in READONLY_ARGUMENT.finditer(masked)]


class PayloadLets(LineRewrite):
	def __init__(self):
		self.end = None
		self.case_indent = ""

	def edits(self, line, masked):
		if self.end is None:
			opened = UNION_OPEN.match(masked)

			if opened:
				self.end = re.compile(r"^" + re.escape(opened.group("indent")) + r"end '" + re.escape(opened.group("name")) + r"'\r?$")
				self.case_indent = opened.group("indent") + "\t"

			return []

		if self.end.match(line):
			self.end = None
			return []

		declared = UNION_CASE.match(masked)

		if declared is None or not masked.startswith(self.case_indent) or masked[len(self.case_indent):len(self.case_indent) + 1] in (" ", "\t"):
			return []

		open_index = declared.end() - 1
		close = match_forward(masked, open_index)

		return [(m.start(), m.end(), "") for m in PAYLOAD_LET.finditer(masked, open_index, close)]

	def finish(self):
		if self.end is not None:
			raise Unhandled("a union is never closed")


def strip_readonly_arguments(work):
	return rewrite_code(work, lambda text: READONLY_WORD in text, lambda rel: ReadonlyArguments())


def strip_payload_lets(work):
	return rewrite_code(work, lambda text: "let " in text and "union " in text, lambda rel: PayloadLets())


def lexer_keywords(work):
	return {m.group("word") for m in KEYWORD_ROW.finditer(read(os.path.join(work, LEXER_SOURCE)))}


def view_binding_heads(masked, keywords):
	heads = []

	for m in VIEW_WORD_AT.finditer(masked):
		before = masked[:m.start()]

		if before.endswith(".") or (before.strip() and VIEW_PRECEDER.search(before) is None):
			continue

		after = masked[m.end():]
		named = BINDING_NAME.match(after)

		if named and named.group("name") not in keywords:
			initializer = VIEW_INITIALIZER.match(after[named.end():])
			source = BARE_LOCAL_READ.match(initializer.group("value")) if initializer else None
			heads.append((m.start(), m.end(), [named.group("name")], source.group("name") if source else None))
			continue

		opened = DESTRUCTURE_OPEN.match(after)

		if opened is None:
			continue

		open_index = m.end() + opened.end() - 1
		try:
			close = match_forward(masked, open_index)
		except Unhandled:
			continue

		if ASSIGNED_TO_NEXT.match(masked, close + 1) is None:
			continue

		heads.append((m.start(), m.end(), WORD.findall(masked[open_index + 1:close]), None))

	return heads


def tuple_view_targets(masked, keywords):
	targets = []

	for head in TUPLE_ASSIGNMENT_HEAD.finditer(masked):
		assignment = TUPLE_ASSIGNMENT.match(masked, head.end())

		if assignment is None:
			continue

		for target in TUPLE_VIEW_TARGET.finditer(masked, head.end(), assignment.end()):
			if target.group("name") not in keywords:
				targets.append((target.end("lead"), target.end("lead") + len(VIEW_WORD), target.group("name")))

	return targets


class ViewBindings(LineRewrite):
	def __init__(self, rel, keywords, registry):
		self.rel = rel
		self.keywords = keywords
		self.registry = registry

	def edits(self, line, masked):
		if VIEW_WORD not in line:
			return []

		edits = []

		for start, end, bound, source in view_binding_heads(masked, self.keywords):
			edits.append((start, end, BINDING_WORD))
			self.registry.names.setdefault(self.rel, set()).update(bound)

			if source is not None:
				self.registry.sources.setdefault(self.rel, set()).add(source)
				self.registry.bare += 1

		for start, end, bound in tuple_view_targets(masked, self.keywords):
			edits.append((start, end, BINDING_WORD))
			self.registry.names.setdefault(self.rel, set()).add(bound)

		return edits


class ViewRegistry:
	def __init__(self):
		self.names = {}
		self.sources = {}
		self.bare = 0


def strip_view_bindings(work):
	keywords = lexer_keywords(work)
	registry = ViewRegistry()
	withdrawn = rewrite_code(work, lambda text: VIEW_WORD in text, lambda rel: ViewBindings(rel, keywords, registry))
	return withdrawn, registry.bare, registry.names, registry.sources


def save_view_registry(work, names, sources):
	registry = {"names": {rel: sorted(found) for rel, found in names.items()}, "sources": {rel: sorted(found) for rel, found in sources.items()}}
	write(os.path.join(work, VIEW_REGISTRY), json.dumps(registry, indent="\t"))


def load_view_registry(work):
	path = os.path.join(work, VIEW_REGISTRY)

	if not os.path.isfile(path):
		raise SystemExit(f"generate-seed-language-shim.py: {work} holds no {VIEW_REGISTRY}, so it is not a work directory this version of the script left; start a run without --resume")

	registry = json.loads(read(path))
	return {rel: set(found) for rel, found in registry["names"].items()}, {rel: set(found) for rel, found in registry["sources"].items()}


def top_level_parts(masked, start, end):
	parts = []
	depth = 0
	begin = start

	for i in range(start, end):
		if masked[i] in OPENERS:
			depth += 1
		elif masked[i] in CLOSERS:
			depth -= 1
		elif masked[i] == "," and depth == 0:
			parts.append((begin, i))
			begin = i + 1

	parts.append((begin, end))
	return parts


def parameter_qualifier_edits(masked, open_index, anonymous):
	close = match_forward(masked, open_index)
	edits = []

	for begin, end in top_level_parts(masked, open_index + 1, close):
		part = masked[begin:end]
		qualified = (UNNAMED_QUALIFIED_PARAMETER.match(part) if anonymous else None) or NAMED_QUALIFIED_PARAMETER.match(part)

		if qualified is not None:
			edits.append((begin + qualified.end(qualified.lastgroup), begin + qualified.end()))

	return edits


class CapabilityQualifiers(LineRewrite):
	def __init__(self):
		self.interface_end = None

	def edits(self, line, masked):
		edits = [(m.start() + len("returns "), m.end(), "") for m in RESULT_QUALIFIER.finditer(masked)]
		alias = FUNCTION_TYPE_ALIAS.match(masked)
		method = INTERFACE_METHOD.match(masked) if self.interface_end is not None else None

		if self.interface_end is None:
			opened = INTERFACE_OPEN.match(masked)

			if opened:
				self.interface_end = re.compile(r"^" + re.escape(opened.group("indent")) + r"end '" + re.escape(opened.group("name")) + r"'\r?$")
		elif self.interface_end.match(line):
			self.interface_end = None

		if alias:
			edits += [(start, end, "") for start, end in parameter_qualifier_edits(masked, alias.end(), anonymous=True)]
		elif method:
			edits += [(start, end, "") for start, end in parameter_qualifier_edits(masked, method.end(), anonymous=False)]

		return edits


def strip_capability_qualifiers(work):
	return rewrite_code(work, lambda text: CAPABILITY_HINT.search(text) is not None, lambda rel: CapabilityQualifiers())


class Lexeme:
	STRING = "string"
	BYTE_STRING = "byte string"
	QUOTED = "single-quoted literal"
	BLOCK_COMMENT = "block comment"
	FORMAT_SPECIFIER = "format specifier"
	INTERPOLATION = "interpolation"


class Interpolation:
	def __init__(self):
		self.kind = Lexeme.INTERPOLATION
		self.braces = 1
		self.groups = 0


class CodeRegions:
	def __init__(self, lines):
		self.stack = []
		self.per_line = []
		self.comments_per_line = []

		for number, line in enumerate(lines, start=1):
			self.per_line.append(self.lex_line(number, line))

		if self.stack:
			raise Unhandled(f"a {getattr(self.stack[-1], 'kind', self.stack[-1])} is still open at the end of the file")

	def in_code(self):
		return not self.stack or isinstance(self.stack[-1], Interpolation)

	def lex_line(self, number, line):
		regions = []
		comments = []
		start = 0 if self.in_code() else None
		comment_start = 0 if self.stack and self.stack[-1] == Lexeme.BLOCK_COMMENT else None
		continued = False
		i = 0

		def leave(at):
			nonlocal start

			if start is not None:
				regions.append((start, at))
				start = None

		while i < len(line):
			top = self.stack[-1] if self.stack else None
			c = line[i]

			if top is None or isinstance(top, Interpolation):
				if line.startswith("//", i):
					leave(i)
					comments.append((i, len(line)))
					i = len(line)
					break

				if line.startswith("/*", i):
					leave(i)
					comment_start = i
					self.stack.append(Lexeme.BLOCK_COMMENT)
					i += 2
					continue

				if c == '"':
					byte_string = i > 0 and line[i - 1] == "b" and (i < 2 or not IDENT.match(line[i - 2]))
					leave(i - 1 if byte_string else i)
					self.stack.append(Lexeme.BYTE_STRING if byte_string else Lexeme.STRING)
					i += 1
					continue

				if c == "'":
					leave(i)
					self.stack.append(Lexeme.QUOTED)
					i += 1
					continue

				if top is not None:
					if c == ":" and top.braces == 1 and top.groups == 0:
						leave(i)
						self.stack.append(Lexeme.FORMAT_SPECIFIER)
					elif c in "([":
						top.groups += 1
					elif c in ")]" and top.groups > 0:
						top.groups -= 1
					elif c == "{":
						top.braces += 1
					elif c == "}":
						top.braces -= 1

						if top.braces == 0:
							leave(i)
							self.stack.pop()

				i += 1
				continue

			if top == Lexeme.BLOCK_COMMENT:
				if line.startswith("*/", i):
					self.stack.pop()
					i += 2
					comments.append((comment_start, i))
					comment_start = None

					if self.in_code():
						start = i

					continue

				i += 1
				continue

			if top == Lexeme.FORMAT_SPECIFIER:
				if c == "}":
					self.stack.pop()
					self.stack.pop()

				i += 1
				continue

			if c == "\\":
				continued = i + 1 == len(line)
				i += 2
				continue

			closer = "'" if top == Lexeme.QUOTED else '"'

			if c == closer:
				self.stack.pop()
				i += 1

				if self.in_code():
					start = i

				continue

			if top == Lexeme.STRING and c == "{":
				self.stack.append(Interpolation())
				i += 1
				start = i
				continue

			i += 1

		leave(len(line))

		if comment_start is not None:
			comments.append((comment_start, len(line)))

		self.comments_per_line.append(comments)

		if self.stack and self.stack[-1] in (Lexeme.STRING, Lexeme.FORMAT_SPECIFIER) and not continued:
			raise Unhandled(f"line {number}: a {self.stack[-1]} is not closed on its line, which the Maxon lexer refuses")

		return regions

	def view(self, index, line):
		masked = [" "] * len(line)

		for start, end in self.per_line[index]:
			masked[start:end] = line[start:end]

		return "".join(masked)

	def uncommented(self, index, line):
		kept = list(line)

		for start, end in self.comments_per_line[index]:
			kept[start:end] = [" "] * (end - start)

		return "".join(kept)


class Namespaces:
	def __init__(self, work):
		self.root = os.path.normpath(os.path.join(work, AUTHOR_SOURCE_DIR))
		self.declarations = {}

		for path in maxon_sources(work, [AUTHOR_SOURCE_DIR]):
			directory = os.path.normpath(os.path.dirname(path))

			for m in DECLARED_NAME.finditer(read(path)):
				self.declarations.setdefault(m.group("name"), []).append(directory)

	def relative(self, directory):
		return os.path.relpath(directory, os.path.dirname(self.root)).replace("\\", "/")

	def unqualify(self, qualified):
		segments = qualified.split(".")
		directory = self.root

		for index, segment in enumerate(segments):
			sites = self.declarations.get(segment, [])
			subdirectory = os.path.join(directory, segment)
			names_a_directory = segment in os.listdir(directory) and os.path.isdir(subdirectory)

			if names_a_directory:
				if directory in sites:
					raise Unhandled(f"`{qualified}`: '{segment}' names both the directory {self.relative(subdirectory)} and a declaration in {self.relative(directory)}")

				directory = subdirectory
				continue

			if index == 0:
				raise Unhandled(f"`{qualified}`: '{segment}' is not a directory of {self.relative(directory)}")

			if len(sites) != 1:
				found = ", ".join(sorted(self.relative(site) for site in sites)) or "none"
				raise Unhandled(f"`{qualified}`: '{segment}' has {len(sites)} declarations among the {AUTHOR_SOURCE_DIR} sources ({found}), so its bare spelling is not one declaration to the seed")

			if sites[0] != directory:
				raise Unhandled(f"`{qualified}`: '{segment}' is declared in {self.relative(sites[0])}, not in {self.relative(directory)}")

			return ".".join(segments[index:])

		raise Unhandled(f"`{qualified}` names the directory {self.relative(directory)}, not a declaration")


def unqualify_code(line, regions, namespaces):
	out = []
	last = 0

	for start, end in regions:
		for m in QUALIFIED.finditer(line, start, end):
			if m.start() > 0 and (IDENT.match(line[m.start() - 1]) or line[m.start() - 1] == "."):
				continue

			out.append(line[last:m.start()])
			out.append(namespaces.unqualify(m.group(0)))
			last = m.end()

	out.append(line[last:])
	return "".join(out)


def strip_namespace_qualifiers(work):
	namespaces = Namespaces(work)
	unqualified = 0
	refused = []

	for path in maxon_sources(work, SOURCE_DIRS):
		text = read(path)

		if NAMESPACE_QUALIFIER not in text:
			continue

		lines = text.split("\n")
		rewritten = []

		try:
			regions = CodeRegions(lines).per_line
		except Unhandled as e:
			refused.append(f"{os.path.relpath(path, work)}: {e}")
			continue

		for line_number, line in enumerate(lines, start=1):
			try:
				rewritten.append(unqualify_code(line, regions[line_number - 1], namespaces))
			except Unhandled as e:
				refused.append(f"{os.path.relpath(path, work)}:{line_number}: {e}")
				rewritten.append(line)

		unqualified += sum(1 for a, b in zip(lines, rewritten) if a != b)

		if rewritten != lines:
			write(path, "\n".join(rewritten))

	return unqualified, refused


def field_annotation(value):
	if BARE_LITERAL.fullmatch(value):
		return None

	try:
		return field_default_type(value)
	except FieldDefaultShapeError as e:
		raise Unhandled(str(e))


def annotate_inferred_fields(work):
	annotated = 0
	refused = []

	for path in maxon_sources(work, SOURCE_DIRS):
		text = read(path)
		lines = text.split("\n")

		try:
			regions = CodeRegions(lines)
		except Unhandled as e:
			refused.append(f"{os.path.relpath(path, work)}: {e}")
			continue

		enclosing = []

		for index, line in enumerate(lines):
			close = BLOCK_CLOSE.match(line)

			if close:
				depth = len(close.group("indent"))
				enclosing = [entry for entry in enclosing if entry[0] < depth]
				continue

			field = FIELD_DECLARATION.match(line)

			if field and enclosing and enclosing[-1] == (len(field.group("indent")) - 1, TYPE_KEYWORD) and STATIC_MODIFIER not in field.group("modifiers").split():
				comments = regions.comments_per_line[index]
				value_start = field.start("value")
				value_end = min((start for start, _ in comments), default=len(line))
				value = line[value_start:value_end].rstrip()
				value_end = value_start + len(value)

				try:
					inferred = field_annotation(value)
				except Unhandled as e:
					refused.append(f"{os.path.relpath(path, work)}:{index + 1}: {e}")
					continue

				if inferred is not None:
					target, initializer = inferred
					lines[index] = line[:field.end("name")] + f" as {target}" + line[field.end("name"):value_start] + initializer + line[value_end:]
					annotated += 1

				continue

			opener = SOURCE_DECLARATION.match(line)

			if opener and opener.group("keyword") in (TYPE_KEYWORD, FUNCTION_KEYWORD):
				enclosing.append((len(opener.group("indent")), opener.group("keyword")))

		rewritten = "\n".join(lines)

		if rewritten != text:
			write(path, rewritten)

	return annotated, refused


def read(path):
	with open(path, "r", encoding="utf-8", newline="") as f:
		return f.read()


def write(path, text):
	with open(path, "w", encoding="utf-8", newline="") as f:
		f.write(text)


def ancestors(parents, name):
	chain = [name]

	while chain[-1] in parents:
		chain.append(parents[chain[-1]])

	return chain


def common_ancestor(parents, left, right):
	right_chain = ancestors(parents, right)

	for candidate in ancestors(parents, left):
		if candidate in right_chain:
			return candidate

	raise Unhandled(f"'{left}' and '{right}' share no ancestor through an `implements` clause")


def match_back(line, close_index):
	depth = 0
	closer = line[close_index]
	opener = CLOSERS[closer]

	i = close_index

	while i >= 0:
		if line[i] == "'":
			raise Unhandled("a character literal inside the operand")

		if line[i] == '"':
			i = line.rfind('"', 0, i)

			if i < 0:
				raise Unhandled("an unterminated string literal inside the operand")
		elif line[i] == closer:
			depth += 1
		elif line[i] == opener:
			depth -= 1

			if depth == 0:
				return i

		i -= 1

	raise Unhandled("unbalanced brackets before the operator")


def match_forward(line, open_index):
	depth = 0
	opener = line[open_index]
	closer = OPENERS[opener]
	i = open_index

	while i < len(line):
		c = line[i]

		if c == '"':
			i = line.find('"', i + 1)

			if i < 0:
				raise Unhandled("an unterminated string literal inside the brackets")
		elif c == opener:
			depth += 1
		elif c == closer:
			depth -= 1

			if depth == 0:
				return i

		i += 1

	raise Unhandled("unbalanced brackets")


def atom_before(line, end):
	i = end

	while i > 0 and line[i - 1] == " ":
		i -= 1

	stop = i

	while i > 0:
		if line[i - 1] in CLOSERS:
			i = match_back(line, i - 1)

		j = i

		while j > 0 and IDENT.match(line[j - 1]):
			j -= 1

		if line[j:i] not in KEYWORDS:
			i = j

		if i > 0 and line[i - 1] == "." and i < stop:
			i -= 1
			continue

		break

	if i == stop:
		raise Unhandled("no operand before the operator")

	return i, stop


def atom_after(line, start):
	i = start

	while i < len(line) and line[i] == " ":
		i += 1

	begin = i

	if i < len(line) and line[i] == "-":
		i += 1

	if i < len(line) and line[i] in OPENERS:
		i = match_forward(line, i) + 1
	else:
		j = i

		while j < len(line) and IDENT.match(line[j]):
			j += 1

		if j == i or line[i:j] in KEYWORDS:
			raise Unhandled("no operand after the operator")

		i = j

	while i < len(line):
		if line[i] in "([{":
			i = match_forward(line, i) + 1
		elif line[i] == "." and i + 1 < len(line) and IDENT.match(line[i + 1]):
			i += 1

			while i < len(line) and IDENT.match(line[i]):
				i += 1
		else:
			break

	return begin, i


def operator_level(op):
	if op in COMPARISON:
		return 0

	return OPERATOR_LEVEL.get(op)


def binary_operand_before(line, end, op):
	start, stop = atom_before(line, end)
	level = operator_level(op)

	if level is None:
		return start, stop

	while True:
		i = start

		while i > 0 and line[i - 1] == " ":
			i -= 1

		prior = next((a for a in OPERATOR_LEVEL if line[:i].endswith(" " + a) and OPERATOR_LEVEL[a] >= level), None)

		if prior is None:
			return start, stop

		start, _ = atom_before(line, i - len(prior))


def binary_operand_after(line, start, op):
	begin, end = atom_after(line, start)
	level = operator_level(op)

	if level is None:
		return begin, end

	while True:
		rest = line[end:]
		following = next((a for a in OPERATOR_LEVEL if rest.startswith(" " + a + " ") and OPERATOR_LEVEL[a] > level), None)

		if following is None:
			return begin, end

		_, end = atom_after(line, end + len(following) + 2)


def statement_value(line, start):
	i = start
	depth = 0

	while i < len(line):
		c = line[i]

		if c == '"':
			i = line.index('"', i + 1)
		elif c in OPENERS:
			depth += 1
		elif c in CLOSERS:
			if depth == 0:
				break

			depth -= 1
		elif c == "," and depth == 0:
			break
		elif c == "'" and depth == 0:
			break

		i += 1

	end = i

	while end > start and line[end - 1] in " \r":
		end -= 1

	if end == start:
		raise Unhandled("no value to cast")

	return start, end


def cast(line, start, end, target):
	inner = line[start:end]

	if re.fullmatch(r"\([A-Za-z0-9_.]+\)", inner):
		inner = inner[1:-1]
	elif not is_member_chain(inner):
		inner = f"({inner})"

	return line[:start] + f"({inner} as {target})" + line[end:]


def find_operand(find, line, position, op):
	try:
		return find(line, position, op)
	except Unhandled as e:
		return e


def operand_text(line, span):
	if isinstance(span, Unhandled):
		return ""

	return line[span[0]:span[1]]


def require_operand(span):
	if isinstance(span, Unhandled):
		raise span

	return span


def require_castable(parents, ranges, source, target):
	if source == target or target in ancestors(parents, source)[1:]:
		return

	if source in ranges and ranges[source] == ranges.get(target):
		return

	raise Unhandled(f"'{source}' neither implements '{target}' nor shares its range, so casting to it could change a value")


def cast_match_arms(lines, line_number, message, parents):
	m = MATCH_ARMS.match(message)
	target = ancestors(parents, common_ancestor(parents, m.group("first"), m.group("second")))[-1]
	opener = lines[line_number - 1]
	indent = re.match(r"\t*", opener).group(0)
	label = re.search(r"'[A-Za-z0-9_]+'\r?$", opener)

	if label is None:
		raise Unhandled("a match expression whose label does not end its line")

	end = indent + "end " + label.group(0).rstrip("\r")
	close = next((j for j in range(line_number, len(lines)) if lines[j].rstrip("\r") == end), None)

	if close is None:
		raise Unhandled(f"the match {label.group(0)} has no `end {label.group(0)}`")

	# All arms or none: a refusal from the split view does not stop the run, so a partly cast match would
	# stay in the tree.
	cast_arms = {}

	for j in range(line_number, close):
		arm = lines[j]

		if not arm.startswith(indent + "\t") or arm.startswith(indent + "\t\t"):
			continue

		at = arm.find(ARM_SEPARATOR)

		if at < 0 or "//" in arm:
			raise Unhandled(f"the match arm `{arm.strip()}` is not a one-line `pattern gives value`")

		start = at + len(ARM_SEPARATOR)
		stop = len(arm.rstrip("\r"))
		cast_arms[j] = cast(arm, start, stop, target)

	for j, arm in cast_arms.items():
		lines[j] = arm


def fix_line(line, column, code, message, parents, ranges):
	at = column - 1
	m = BINARY.match(message)

	if code == "E3005" and m:
		op = m.group("op")
		range_keyword = next((k for k in RANGE_KEYWORDS if line.startswith(k, at)), None)

		left, right = m.group("left"), m.group("right")

		if range_keyword is not None:
			require_castable(parents, ranges, right, left)
			start, end = binary_operand_after(line, at + len(range_keyword), op)
			return cast(line, start, end, left)

		if not line.startswith(op, at):
			raise Unhandled(f"operator '{op}' is not at the reported column")

		right_span = find_operand(binary_operand_after, line, at + len(op), op)
		left_span = find_operand(binary_operand_before, line, at, op)

		if NUMBER_LITERAL.fullmatch(operand_text(line, right_span)):
			require_castable(parents, ranges, left, right)
			return cast(line, *require_operand(left_span), right)

		if NUMBER_LITERAL.fullmatch(operand_text(line, left_span)):
			require_castable(parents, ranges, right, left)
			return cast(line, *require_operand(right_span), left)

		target = common_ancestor(parents, left, right)

		if right != target:
			line = cast(line, *require_operand(right_span), target)

		if left != target:
			line = cast(line, *require_operand(left_span), target)

		return line

	m = MISMATCH.match(message)

	if code == "E3005" and m:
		require_castable(parents, ranges, m.group("got"), m.group("expected"))

		if at < len(line) and line[at] in ",)]}":
			start, end = atom_before(line, at)
		else:
			start, end = atom_after(line, at)

		return cast(line, start, end, m.group("expected"))

	m = TERNARY.match(message)

	if code == "E2028" and m:
		if not line.startswith("if ", at):
			raise Unhandled("a ternary's `if` is not at the reported column")

		when_true, when_false = m.group("whenTrue"), m.group("whenFalse")
		target = common_ancestor(parents, when_true, when_false)
		else_at = line.index(" else ", at)

		if when_false != target:
			start, end = atom_after(line, else_at + len(" else "))
			line = cast(line, start, end, target)

		if when_true != target:
			start, end = atom_before(line, at)
			line = cast(line, start, end, target)

		return line

	m = FIELD.match(message)

	if code == "E3005" and m:
		require_castable(parents, ranges, m.group("got"), m.group("expected"))
		field = m.group("field")

		if not line.startswith(field, at):
			raise Unhandled(f"field '{field}' is not at the reported column")

		after = line[at + len(field):]
		separator = re.match(r"\s*(:|=)\s*", after)

		if separator is None:
			raise Unhandled(f"field '{field}' is followed by neither `:` nor `=`")

		start, end = statement_value(line, at + len(field) + separator.end())
		return cast(line, start, end, m.group("expected"))

	m = OTHERWISE.match(message)

	if code == "E3059" and m:
		require_castable(parents, ranges, m.group("got"), m.group("expected"))
		keyword = line.find(OTHERWISE_KEYWORD, at)

		if keyword < 0:
			raise Unhandled("no `otherwise` after the reported column")

		start, end = statement_value(line, keyword + len(OTHERWISE_KEYWORD))
		return line[:end] + f" as {m.group('expected')}" + line[end:]

	m = UNUSED_EXPORT.match(message)

	if code == "E3092" and m:
		declaration = re.match(r"^(\s*)export ", line)

		if declaration is None or not line.startswith(m.group("name"), at):
			raise Unhandled("no `export` declaration of that name at the reported column")

		return declaration.group(1) + line[declaration.end():]

	m = ARGUMENT.match(message)

	if code == "E3005" and m:
		require_castable(parents, ranges, m.group("got"), m.group("expected"))
		start, end = argument_span(line, at, m.group("label"))
		return cast(line, start, end, m.group("expected"))

	if code == "E3057" and DIVISION_WITHOUT_TRY.match(message):
		op = next((o for o in DIVISION_OPERATORS if line.startswith(o, at)), None)

		if op is None:
			raise Unhandled("no division operator at the reported column")

		start, _ = binary_operand_before(line, at, op)
		_, end = binary_operand_after(line, at + len(op), op)
		preceding = line[:start].rstrip()

		if any(preceding.endswith(p) for p in MULTIPLICATIVE_OPERATORS) or preceding.endswith("-"):
			raise Unhandled(f"the dividend is the right operand of a tighter or equal-binding operator, so `{op}` does not head its own expression")

		return line[:start] + f"(try ({line[start:end]}) otherwise panic(\"{DIVISOR_NEVER_ZERO}\"))" + line[end:]

	if code == "E3010" and UNNEEDED_CAST.match(message):
		cast_at = CAST_SUFFIX.match(line, at - 1)

		if cast_at is None or not line.startswith("as ", at):
			raise Unhandled("no cast at the reported column")

		return line[:cast_at.start()] + line[cast_at.end():]

	m = NEVER_REASSIGNED.match(message) if code == "E3077" else SHARES_LET_STORAGE.match(message) if code == "E3078" else None

	if m:
		name = m.group("name")
		keyword = "var "

		if line.startswith(name, at) and line[:at].endswith(keyword):
			return line[:at - len(keyword)] + "let " + line[at:]

		assigned = ASSIGNMENT.match(line, at + len(name)) if line.startswith(name, at) else None

		if code != "E3078" or assigned is None:
			raise Unhandled(f"no `var {name}` ending at the reported column, and no assignment to `{name}` there")

		value_at = assigned.end()

		while value_at < len(line) and line[value_at] == " ":
			value_at += 1

		start, end = statement_value(line, value_at)
		return clone_span(line, start, end)

	m = PASSED_TO_KEEPER.match(message) or PASSED_TO_STORER.match(message) or STORED_INTO_READONLY_ELEMENT.match(message) if code == "E3078" else None

	if m:
		start, end = argument_span(line, at, m.groupdict().get("parameter", STORED_ARGUMENT_LABEL))
		return clone_span(line, start, end)

	if code == "E3078" and STORED_INTO_LET_SLOT.match(message):
		start, end = atom_after(line, at)
		return clone_span(line, start, end)

	m = IMMUTABLE_TO_MUTABLE.match(message)

	if code == "E3078" and m:
		assignment = re.compile(r"\b" + re.escape(m.group("target")) + r"\s*=\s*" + re.escape(m.group("source")) + r"\b(?![.(])").search(line, at)

		if assignment is None:
			raise Unhandled(f"no `{m.group('target')} = {m.group('source')}` at the reported column")

		return line[:assignment.end()] + CLONE_CALL + line[assignment.end():]

	raise Unhandled("a refusal this script does not cast")


class MemberDeclarations:
	def __init__(self, work):
		self.sites = {}

		for path in maxon_sources(work, SOURCE_DIRS):
			rel = os.path.relpath(path, work).replace("\\", "/")
			enclosing = None

			for line in read(path).split("\n"):
				declared = SOURCE_DECLARATION.match(line)

				if declared is None:
					continue

				if not declared.group("indent"):
					enclosing = (declared.group("keyword"), declared.group("name"))

				if declared.group("keyword") in MEMBER_KEYWORDS:
					self.sites.setdefault(declared.group("name"), set()).add((rel, enclosing))

	def proves_read_only(self, method):
		if method not in READ_ONLY_COLLECTION_METHODS:
			return False

		sites = self.sites.get(method)

		if not sites:
			return False

		return all(is_collection_member(rel, enclosing) for rel, enclosing in sites)


def is_collection_member(rel, enclosing):
	if enclosing is None:
		return False

	keyword, name = enclosing
	return keyword in ("type", "extension") and name in COLLECTION_TYPES and rel == f"{STDLIB_DIR}/{name}.maxon"


def require_read_only_via(borrow, iterated, members):
	owner, via = borrow.group("owner"), borrow.group("via")

	if members.proves_read_only(via):
		return

	if any(owner in segments for segments in chain_segments(iterated)):
		where = f"`{owner}`, which the loop iterates through"
	else:
		where = f"`{owner}`, which this script cannot place on the iterated `{iterated}`"

	raise Unhandled(f"the refused mutation reaches {where}, via `{via}`, which is not a read-only {'/'.join(sorted(COLLECTION_TYPES))} method of {STDLIB_DIR}/, so this script cannot prove it leaves the iterated collection unmutated and iterating a clone of it could change what the loop sees")


def enclosing_loop_of(lines, index, element):
	for j in range(index - 1, -1, -1):
		loop = FOR_LOOP.match(lines[j])

		if loop is None or loop.group("element") != element:
			continue

		if loop_end(lines, j, loop.group("indent"), loop.group("label")) > index:
			return j

	raise Unhandled(f"`{element}` is not the element of a loop enclosing line {index + 1}")


def index_loop_element(lines, loop_index):
	loop = FOR_LOOP.match(lines[loop_index])
	iterated = loop.group("iterated")

	if MEMBER_CHAIN.fullmatch(iterated) is None:
		raise Unhandled(f"the loop iterates `{iterated}`, which is not a field path an index loop can read twice")

	element = loop.group("element")
	indent = loop.group("indent")
	eol = loop.group("eol")
	position = f"{SEED_PREFIX}{element[0].upper()}{element[1:]}Position"

	lines[loop_index] = f"{indent}for {position} in 0 upto {iterated}.count() {loop.group('label')}{eol}"
	lines.insert(loop_index + 1, f'{indent}\tvar {element} = try {iterated}.get({position}) otherwise panic("{POSITION_IN_RANGE}"){eol}')

	return loop_index + 1


def clone_borrowing_let(lines, borrow_index, element):
	bound = LET_BINDING.match(lines[borrow_index])

	if bound is None or bound.group("name") != element:
		raise Unhandled(f"no `let {element} = ...` at line {borrow_index + 1}")

	value = bound.group("value")

	if value.endswith(CLONE_CALL):
		raise Unhandled(f"`{element}` is already bound to a clone, which the seed still reads as borrowing")

	if not is_member_chain(value):
		value = f"({value})"

	lines[borrow_index] = bound.group("head") + value + CLONE_CALL + bound.group("eol")


def hoist_loop_borrow(lines, line_number, borrow, members, rel):
	borrow_index = int(borrow.group("line")) - 1

	if not borrow_index < line_number - 1:
		raise Unhandled("the borrow does not precede the refused mutation")

	loop = FOR_LOOP.match(lines[borrow_index])

	if loop is None or loop.group("element") != borrow.group("element"):
		raise Unhandled(f"no `for {borrow.group('element')} in ...` loop at line {borrow.group('line')}")

	iterated = loop.group("iterated")

	if re.fullmatch(r"[A-Za-z0-9_]+", iterated) or " upto " in iterated or " downto " in iterated:
		raise Unhandled("the loop does not iterate a borrowed collection")

	element = loop.group("element")
	indent = loop.group("indent")
	label = loop.group("label")

	if is_member_chain(iterated) and iterated.endswith(CLONE_CALL):
		snapshot = iterated
	else:
		proven = PROVEN_LOOP_HOISTS.get((rel, label, iterated, borrow.group("owner"), borrow.group("via")))
		close = loop_end(lines, borrow_index, indent, label)
		regions = CodeRegions(lines)

		if proven is None:
			require_read_only_via(borrow, iterated, members)
			body = [(lines[i], regions.view(i, lines[i])) for i in range(borrow_index + 1, close)]
			require_unmutated(body, collection_spellings(iterated), members)
		else:
			digest = loop_digest(lines, regions, borrow_index, close)

			if digest != proven:
				raise Unhandled(f"the loop {label} in {rel} is a proven hoist only while its text digests to {proven}, and it now digests to {digest}; prove again that nothing its body reaches mutates `{iterated}`, then record the new digest in PROVEN_LOOP_HOISTS")

		snapshot = f"({iterated}){CLONE_CALL}"

	held = HELD_PREFIX + element[0].upper() + element[1:]
	eol = loop.group("eol")
	lines[borrow_index] = f"{indent}for {element} in {held} {label}{eol}"
	lines.insert(borrow_index, f"{indent}let {held} = {snapshot}{eol}")

	return borrow_index


def loop_digest(lines, regions, loop_index, close):
	normalized = (" ".join(regions.uncommented(i, lines[i]).split()) for i in range(loop_index, close + 1))
	return hashlib.sha256("\n".join(line for line in normalized if line).encode("utf-8")).hexdigest()


def is_member_chain(expression):
	try:
		begin, end = atom_after(expression, 0)
	except Unhandled:
		return False

	return begin == 0 and end == len(expression)


def loop_end(lines, loop_index, indent, label):
	end = re.compile(r"^" + re.escape(indent) + r"end " + re.escape(label) + r"\r?$")
	close = next((j for j in range(loop_index + 1, len(lines)) if end.match(lines[j])), None)

	if close is None:
		raise Unhandled(f"the loop {label} has no `end {label}`")

	return close


def chain_segments(iterated):
	chains = [MEMBER_CHAIN.match(iterated)] if is_member_chain(iterated) else MEMBER_CHAIN.finditer(iterated)

	for m in chains:
		if m is None:
			continue

		segments = m.group(0).split(".")

		if m.end() < len(iterated) and iterated[m.end()] == "(":
			segments = segments[:-1]

		yield segments


def collection_spellings(iterated):
	spellings = {iterated}

	for segments in chain_segments(iterated):
		for count in range(1, len(segments) + 1):
			prefix = ".".join(segments[:count])

			if prefix not in KEYWORDS:
				spellings.add(prefix)

	return spellings


def require_unmutated(body, spellings, members):
	for line, code in body:
		for spelling in sorted(spellings, key=len, reverse=True):
			for m in re.finditer(re.escape(spelling), code):
				reason = mutation_at(code, m.start(), m.end(), members)

				if reason is not None:
					raise Unhandled(f"the loop body {reason.format(f'`{spelling}`')}, which the loop iterates, so iterating a clone of it could change what the loop sees: {line.strip()}")


def mutation_at(code, start, end, members):
	if start > 0 and (IDENT.match(code[start - 1]) or code[start - 1] == "."):
		return None

	if end < len(code) and IDENT.match(code[end]):
		return None

	i = end
	calls = False

	while i < len(code):
		if code[i] == "[":
			i = match_forward(code, i) + 1
		elif code[i] == "." and i + 1 < len(code) and IDENT.match(code[i + 1]):
			j = i + 1

			while j < len(code) and IDENT.match(code[j]):
				j += 1

			if j < len(code) and code[j] == "(":
				if not members.proves_read_only(code[i + 1:j]):
					return f"calls `{code[i + 1:j]}` on {{}}, which this script cannot prove leaves it unmutated"

				calls = True
				j = match_forward(code, j) + 1

			i = j
		else:
			break

	if ASSIGNMENT.match(code, i):
		return "assigns to or through {}"

	if calls:
		return None

	before = code[:start].rstrip()
	labelled = re.search(r"[A-Za-z_][A-Za-z0-9_]*:$", before)

	if labelled:
		before = before[:labelled.start()].rstrip()

	after = code[i:].lstrip()

	if before.endswith(("(", ",")) and after.startswith((")", ",")):
		return "passes {} to a call"

	assigned = re.search(r"(?:^|[^=!<>+\-*/%])=$", before)

	if assigned and not re.match(r"\s*let\b", code) and (after == "" or after.startswith("'")):
		return "binds a mutable alias to {}"

	return None


def unmove_var(lines, index, name):
	bounds = enclosing_function(lines, index)

	if bounds is None:
		raise Unhandled(f"line {index + 1} is outside any function, so the `var` that was moved cannot be found")

	pattern = re.compile(r"^[ \t]*var " + re.escape(name) + r"\b")
	declaration = next((j for j in range(index, bounds[0] - 1, -1) if pattern.match(lines[j])), None)

	if declaration is None:
		raise Unhandled(f"`{name}` is not a `var` declared in the function that uses it after it moved, so it cannot be made a `let` that the seed aliases instead of moving")

	regions = CodeRegions(lines[bounds[0]:bounds[1] + 1])
	rebinds = re.compile(r"(?<![A-Za-z0-9_.])" + re.escape(name) + r"\s*[-+*/%]?=(?!=)")
	word = re.compile(r"(?<![A-Za-z0-9_.])" + re.escape(name) + r"\b")

	for j in range(declaration + 1, bounds[1] + 1):
		masked = regions.view(j - bounds[0], lines[j])

		if rebinds.search(masked) or (masked.lstrip().startswith("(") and word.search(masked) and TUPLE_CLOSED_ASSIGNMENT.search(masked)):
			raise Unhandled(f"`{name}` is reassigned at line {j + 1}, so it cannot be a `let` the seed aliases instead of moving")

	lines[declaration] = re.sub(r"^([ \t]*)var ", r"\1let ", lines[declaration], count=1)


def clone_span(line, start, end):
	value = line[start:end]

	if value.endswith(CLONE_CALL):
		raise Unhandled(f"`{value}` is already a clone, which the seed still refuses")

	if not is_member_chain(value):
		value = f"({value})"

	return line[:start] + value + CLONE_CALL + line[end:]


def argument_span(line, at, label):
	i = at

	while i < len(line) and (IDENT.match(line[i]) or line[i] == "."):
		i += 1

	if i >= len(line) or line[i] != "(":
		raise Unhandled("no call at the reported column")

	close = match_forward(line, i)
	arguments = []
	begin = i + 1
	depth = 0
	j = begin

	while j < close:
		c = line[j]

		if c == '"':
			j = line.index('"', j + 1)
		elif c in OPENERS:
			depth += 1
		elif c in CLOSERS:
			depth -= 1
		elif c == "," and depth == 0:
			arguments.append((begin, j))
			begin = j + 1

		j += 1

	arguments.append((begin, close))
	spans = []

	for a, b in arguments:
		while a < b and line[a] == " ":
			a += 1

		labelled = re.match(r"([A-Za-z0-9_]+):\s*", line[a:b])

		if labelled and labelled.group(1) == label:
			return a + labelled.end(), b

		spans.append((a, b, labelled is not None))

	if spans and not spans[0][2]:
		return spans[0][0], spans[0][1]

	raise Unhandled(f"no argument for '{label}' in the call")


def build(work, seed_name, lanes, base=""):
	code = 0
	logs = []

	for target in lanes:
		result = run([os.path.join(work, base, SEED_DIR, seed_name), "build", os.path.join(base, AUTHOR_SOURCE_DIR).replace("\\", "/"), f"--target={target}", f"--output=out/c1-{target}"], cwd=work, check=False)
		code = code or result.returncode
		logs.append(result.stdout + result.stderr)

	return code, "\n".join(logs)


def resolve(work, path):
	path = path.replace("\\", "/")

	if not os.path.isabs(path):
		path = os.path.join(work, path)

	try:
		rel = os.path.relpath(path, work).replace("\\", "/")
	except ValueError:
		raise Unhandled(f"{path} lies on another drive than the tree the seed was given")

	if rel.startswith(".."):
		raise Unhandled(f"{path} lies outside the tree the seed was given")

	return os.path.join(work, rel), rel


def report_refusals(what, refused, work):
	print(f"generate-seed-language-shim.py: {what}:", file=sys.stderr)
	print("\n".join(refused), file=sys.stderr)
	print(f"work tree: {work}", file=sys.stderr)
	return 1


class Unit:
	def __init__(self, owner, name, start, end):
		self.owner = owner
		self.name = name
		self.start = start
		self.end = end


class FileScan:
	def __init__(self, lines):
		self.lines = lines
		self.units = []
		self.prefixed = set()
		self.constants = {}
		self.scan()

	def lacks_visibility(self, modifiers):
		return not VIEW_VISIBILITY_WORDS.intersection(modifiers.split())

	def find_end(self, start, closer):
		for j in range(start + 1, len(self.lines)):
			if self.lines[j] == closer:
				return j

		raise Unhandled(f"line {start + 1} opens a block with no `{closer.strip()}`")

	def constant_name(self, declared, line):
		if declared.group("keyword") != "let" or declared.group("modifiers"):
			return None

		constant = CONSTANT.match(line)

		if constant is None or constant.group("name").startswith(RESERVED_NAME_PREFIX):
			return None

		value = constant.group("value")
		return constant.group("name") if BARE_LITERAL.fullmatch(value) or STRING_LITERAL.match(value) else None

	def scan(self):
		lines = self.lines
		i = 0
		top = None

		while i < len(lines):
			line = lines[i]
			declared = SOURCE_DECLARATION.match(line)

			if top is None:
				if declared is None or declared.group("indent"):
					i += 1
					continue

				keyword, name = declared.group("keyword"), declared.group("name")

				if keyword in STATEMENT_KEYWORDS:
					constant = self.constant_name(declared, line)

					if constant is not None:
						self.constants[constant] = i
					elif self.lacks_visibility(declared.group("modifiers")):
						self.prefixed.add(i)

					i += 1
					continue

				if self.lacks_visibility(declared.group("modifiers")) and keyword != "extension":
					self.prefixed.add(i)

				if keyword == FUNCTION_KEYWORD:
					end = self.find_end(i, f"end '{name}'")

					if not name.startswith(RESERVED_NAME_PREFIX):
						self.units.append(Unit(None, name, i, end))

					i = end + 1
					continue

				top = (keyword, name)
				i += 1
				continue

			kind, name = top

			if line == f"end '{name}'":
				top = None
				i += 1
				continue

			if kind == "interface" or declared is None or declared.group("indent") != MEMBER_INDENT:
				i += 1
				continue

			keyword = declared.group("keyword")

			if keyword == FUNCTION_KEYWORD:
				member = declared.group("name")
				end = self.find_end(i, f"{MEMBER_INDENT}end '{member}'")

				if self.lacks_visibility(declared.group("modifiers")):
					self.prefixed.add(i)

				if kind == TYPE_KEYWORD and STATIC_MODIFIER not in declared.group("modifiers").split() and not member.startswith(RESERVED_NAME_PREFIX):
					self.units.append(Unit(name, member, i, end))

				i = end + 1
				continue

			if keyword in STATEMENT_KEYWORDS and self.lacks_visibility(declared.group("modifiers")):
				self.prefixed.add(i)

			i += 1


class SplitFile:
	def __init__(self, rel, lines):
		self.rel = rel
		self.scan = FileScan(lines)
		self.lines = lines
		self.outputs = {}

		groups = {}
		order = []

		for unit in self.scan.units:
			key = (unit.owner, unit.name)

			if key not in groups:
				groups[key] = []
				order.append(key)

			groups[key].append(unit)

		parts = [[]]
		size = 0

		for key in order:
			parts[-1].append(key)
			size += sum(u.end - u.start + 1 for u in groups[key])

			if size >= SPLIT_PART_LINES:
				parts.append([])
				size = 0

		moved = {i for units in groups.values() for u in units for i in range(u.start, u.end + 1)}
		self.emit(self.rel, [i for i in range(len(lines)) if i not in moved])

		stem, extension = os.path.splitext(self.rel)

		for number, keys in enumerate(parts, start=1):
			if not keys:
				continue

			units = sorted((u for key in keys for u in groups[key]), key=lambda u: u.start)
			body = []
			owner = None

			for unit in units:
				if unit.owner != owner:
					if owner is not None:
						body.append(f"end '{owner}'")

					if unit.owner is not None:
						body.append(f"extension {unit.owner}")

					owner = unit.owner

				body.extend(range(unit.start, unit.end + 1))
				body.append("")

			if owner is not None:
				body.append(f"end '{owner}'")

			code = "\n".join(self.lines[i] for i in body if isinstance(i, int))
			used = set(WORD.findall(code))
			copies = sorted(index for name, index in self.scan.constants.items() if name in used)
			self.emit(f"{stem}{VIEW_PART_INFIX}{number}{extension}", copies + [""] + body)

	def emit(self, rel, entries):
		text = []
		origin = []

		for entry in entries:
			if isinstance(entry, str):
				text.append(entry)
				origin.append((0, 0))
				continue

			index = entry
			line = self.lines[index]
			shift = 0

			if index in self.scan.prefixed:
				body = line.lstrip("\t")
				indent = line[:len(line) - len(body)]
				line = indent + VIEW_VISIBILITY + " " + body
				shift = len(VIEW_VISIBILITY) + 1

			text.append(line)
			origin.append((index + 1, shift))

		self.outputs[rel] = ("\n".join(text), origin)


class SplitView:
	def __init__(self, work):
		self.work = work
		self.root = os.path.join(work, VIEW_DIR)
		self.whole = set()
		self.discard()

	def discard(self):
		shutil.rmtree(self.root, ignore_errors=True)
		self.origins = {}
		self.written = set()

	def refresh(self, split_rels, seed_name):
		self.origins = {}
		written = set()
		seed = os.path.join(self.root, SEED_DIR, seed_name)

		if not os.path.isfile(seed):
			os.makedirs(os.path.dirname(seed), exist_ok=True)
			shutil.copy2(os.path.join(self.work, SEED_DIR, seed_name), seed)

		for path in tree_files(self.work):
			rel = os.path.relpath(path, self.work).replace("\\", "/")

			if not path.endswith(MAXON_EXTENSION):
				destination = os.path.join(self.root, rel)
				os.makedirs(os.path.dirname(destination), exist_ok=True)

				if not os.path.isfile(destination) or not filecmp.cmp(path, destination, shallow=False):
					shutil.copyfile(path, destination)

				written.add(os.path.normpath(destination))
				continue

			text = read(path)
			pieces = {rel: (text, None)}

			if rel in split_rels:
				try:
					pieces = SplitFile(rel, text.split("\n")).outputs
				except Unhandled as e:
					self.whole.add(rel)
					print(f"generate-seed-language-shim.py: {rel} stays whole in the split view: {e}", file=sys.stderr, flush=True)

			for out_rel, (out_text, origin) in pieces.items():
				destination = os.path.join(self.root, out_rel)
				os.makedirs(os.path.dirname(destination), exist_ok=True)

				if not os.path.isfile(destination) or read(destination) != out_text:
					write(destination, out_text)

				written.add(os.path.normpath(destination))

				if origin is not None:
					self.origins[out_rel] = (rel, origin)

		for stale in self.written - written:
			if os.path.isfile(stale):
				os.remove(stale)

		self.written = written

	def to_canonical(self, view_path, line, column):
		rel = resolve(self.work, view_path)[1]

		if rel.startswith(VIEW_DIR + "/"):
			rel = rel[len(VIEW_DIR) + 1:]

		if rel not in self.origins:
			return rel, line, column

		canonical, origin = self.origins[rel]

		if not 1 <= line <= len(origin):
			raise Unhandled(f"{rel}:{line} is outside the {len(origin)} lines the split view wrote")

		canonical_line, shift = origin[line - 1]

		if canonical_line == 0:
			raise Unhandled(f"{rel}:{line} is a line the split view wrote itself")

		return canonical, canonical_line, column - shift if column > shift else column


class MappedDiagnostic:
	def __init__(self, original, path, line, column):
		self.fields = {"code": original.group("code"), "path": path, "line": str(line), "column": str(column), "message": original.group("message")}
		self.text = f"error {self.fields['code']}: {path}:{line}:{column}: {self.fields['message']}"

	def group(self, key):
		return self.text if key == 0 else self.fields[key]


def enclosing_function(lines, index):
	for j in range(index, -1, -1):
		header = SOURCE_DECLARATION.match(lines[j])

		if header is None or header.group("keyword") != FUNCTION_KEYWORD or header.group("indent") not in ("", MEMBER_INDENT):
			continue

		close = f"{header.group('indent')}end '{header.group('name')}'"

		for k in range(j + 1, len(lines)):
			if lines[k] == close:
				return (j, k) if k >= index else None

		return None

	return None


def indent_of(line):
	return len(line) - len(line.lstrip("\t"))


def binding_roots(text):
	roots = []

	for word in WORD.finditer(text):
		before = text[word.start() - 1] if word.start() > 0 else ""
		after = text[word.end()] if word.end() < len(text) else ""

		if before == "." or word.group(0) in KEYWORDS:
			continue

		if after == "(":
			return None

		roots.append(word.group(0))

	return roots


def binder_lines(lines, bounds, word):
	start, end = bounds
	escaped = re.escape(word)
	parameter = re.compile(r"[(,] ?" + escaped + r" [A-Za-z(]")
	binders = [
		re.compile(r"\b(?:let|var)\b[^=\n]*\b" + escaped + r"\b"),
		re.compile(r"\bfor\b[^\n]*?\b" + escaped + r"\b[^\n]*?\bin\b"),
		re.compile(r"\([^()\n]*\b" + escaped + r"\b[^()\n]*\)\s*(?:then|gives)\b"),
		re.compile(r"\botherwise\s*\([^()\n]*\b" + escaped + r"\b"),
		re.compile(r"\bfunction\s*\([^()\n]*\b" + escaped + r"\b"),
	]
	found = []

	for i in range(start, end + 1):
		line = lines[i]

		if word not in line:
			continue

		if parameter.search(line) if i == start else any(binder.search(line) for binder in binders):
			found.append(i)

	return found


def binding_covers(lines, bounds, declaration, first, second):
	if declaration == bounds[0]:
		return True

	if declaration >= min(first, second):
		return False

	text = lines[declaration].lstrip("\t")

	if text.startswith(SCOPE_OPENERS):
		closes_at_depth = True
	elif text.startswith(PLAIN_BINDERS):
		closes_at_depth = False
	else:
		return False

	depth = indent_of(lines[declaration])

	for k in range(declaration + 1, bounds[1] + 1):
		if not lines[k].strip():
			continue

		level = indent_of(lines[k])

		if level < depth or (closes_at_depth and level <= depth):
			return k > max(first, second)

	return False


def same_bindings(lines, bounds, binders, first, second):
	for found in binders.values():
		if len(found) > 1 or (found and not binding_covers(lines, bounds, found[0], first, second)):
			return False

	return True


def receiver_of(line, method_start):
	if method_start < 1 or line[method_start - 1] != ".":
		raise Unhandled("a call with no receiver")

	start, stop = atom_before(line, method_start - 1)
	return line[start:stop]


class Twin:
	def __init__(self, index, method, receiver, text, expected):
		self.index = index
		self.method = method
		self.receiver = receiver
		self.text = text
		self.expected = expected

	def key(self):
		return (self.method, self.receiver, self.text, self.expected)


def twin_of(line, index, column, message):
	argument = ARGUMENT.match(message)

	if argument is None:
		return None

	method = WORD.match(line, column - 1)

	if method is None or ARM_SERVED_INDEX_LABELS.get(method.group(0)) != argument.group("label"):
		return None

	try:
		start, end = argument_span(line, column - 1, argument.group("label"))
		receiver = receiver_of(line, column - 1)
	except Unhandled:
		return None

	text = line[start:end]

	if not text or '"' in text or "'" in text or NUMBER_LITERAL.fullmatch(text):
		return None

	return Twin(index, method.group(0), receiver, text, argument.group("expected"))


class Speculation:
	def __init__(self, index, twin):
		self.index = index
		self.twin = twin.key()
		self.text = twin.text
		self.cast_text = cast(twin.text, 0, len(twin.text), twin.expected)


class Rewriter:
	def __init__(self, work, parents, ranges, view_names, view_sources):
		self.work = work
		self.parents = parents
		self.ranges = ranges
		self.view_names = view_names
		self.view_sources = view_sources
		self.members = MemberDeclarations(work)
		self.casts = 0
		self.generation = 0
		self.seen = set()
		self.attempts = {}
		self.speculated = {}
		self.declined = set()

	def speculate(self, rel, lines, twins):
		if not twins:
			return

		try:
			regions = CodeRegions(lines).per_line
		except Unhandled:
			return

		snapshot = list(lines)

		for twin in twins:
			bounds = enclosing_function(lines, twin.index)
			roots = binding_roots(twin.receiver + " " + twin.text)

			if (rel, twin.key()) in self.declined or bounds is None or roots is None:
				continue

			binders = {word: binder_lines(lines, bounds, word) for word in set(roots)}
			label = ARM_SERVED_INDEX_LABELS[twin.method]
			call = re.compile(r"\." + re.escape(twin.method) + r"\(")

			for j in range(bounds[0], bounds[1] + 1):
				line = lines[j]

				if twin.text not in line:
					continue

				try:
					spans = regions[j] if line is snapshot[j] else CodeRegions([line]).per_line[0]
				except Unhandled:
					continue

				rewritten = line

				for m in reversed(list(call.finditer(line))):
					if not any(start <= m.start() < stop for start, stop in spans):
						continue

					try:
						receiver = receiver_of(rewritten, m.start() + 1)
						start, stop = argument_span(rewritten, m.start() + 1, label)
					except Unhandled:
						continue

					if receiver == twin.receiver and rewritten[start:stop] == twin.text and same_bindings(lines, bounds, binders, twin.index, j):
						rewritten = cast(rewritten, start, stop, twin.expected)
						self.speculated.setdefault(rel, []).append(Speculation(j, twin))

				if rewritten != line:
					lines[j] = rewritten
					self.casts += 1

	def regret(self, rel, lines, index):
		records = [record for record in self.speculated.get(rel, []) if record.index == index]

		if not records:
			return False

		line = lines[index]

		for record in records:
			line = line.replace(record.cast_text, record.text)
			self.speculated[rel].remove(record)
			self.declined.add((rel, record.twin))

		if line == lines[index]:
			return False

		lines[index] = line
		self.casts -= 1
		return True

	def forget(self, rel, index):
		if rel in self.speculated:
			self.speculated[rel] = [record for record in self.speculated[rel] if record.index != index]

	def insert_at(self, rel, position):
		for record in self.speculated.get(rel, []):
			if record.index >= position:
				record.index += 1

	def apply(self, diagnostics):
		by_file = {}
		refused = []

		for d in diagnostics:
			try:
				path, rel = resolve(self.work, d.group("path"))
			except Unhandled as e:
				refused.append(f"{d.group(0)}\n    {e}")
				continue

			by_file.setdefault(path, []).append((int(d.group("line")), int(d.group("column")), d, rel))

		for path, sites in by_file.items():
			text = read(path)
			lines = text.split("\n")
			rel = sites[0][3]
			hoists = []
			clones = {}
			twins = []

			for line_number, column, d, _ in sorted(sites, key=lambda s: (s[0], s[1]), reverse=True):
				index = line_number - 1

				if not 0 <= index < len(lines):
					refused.append(f"{d.group(0)}\n    the line is outside the {len(lines)} lines of {rel}")
					continue

				if not UNNEEDED_CAST.match(d.group("message")) and self.regret(rel, lines, index):
					continue

				key = (rel, line_number, lines[index], d.group("message"))
				site = (rel, line_number, d.group("message"))
				self.attempts[site] = self.attempts.get(site, 0) + 1

				if key in self.seen or self.attempts[site] > MAX_ATTEMPTS_PER_SITE:
					refused.append(f"{d.group(0)}\n    the cast written for it did not answer it")
					continue

				borrow = BORROWED_BY_LOOP.match(d.group("message"))

				if d.group("code") == "E3070" and borrow:
					borrow_index = int(borrow.group("line")) - 1

					if LET_BINDING.match(lines[borrow_index]) and borrow.group("element") in self.view_names.get(rel, ()):
						refused.append(f"{d.group(0)}\n    `{borrow.group('element')}` is a `view` the tree reads while it observes the collection being mutated; the seed's older rule is answered with a `.clone()`, which would stop the view seeing those writes, so this script does not write it")
					elif LET_BINDING.match(lines[borrow_index]):
						clones.setdefault(borrow_index, (borrow, d, []))[2].append(key)
					else:
						hoists.append((borrow_index + 1, d, lambda borrow=borrow, line_number=line_number, rel=rel: hoist_loop_borrow(lines, line_number, borrow, self.members, rel), key))

					continue

				moved = MOVED_VALUE.match(d.group("message"))

				if d.group("code") == "E3102" and moved:
					try:
						unmove_var(lines, index, moved.group("name"))
						self.seen.add(key)
						self.casts += 1
					except Unhandled as e:
						viewed = VIEWED_VAR_NOTE.format(name=moved.group("name")) if moved.group("name") in self.view_sources.get(rel, ()) else ""
						refused.append(f"{d.group(0)}\n    {e}{viewed}")

					continue

				immutable = IMMUTABLE_LOOP_ELEMENT.match(d.group("message"))

				if d.group("code") == "E2013" and immutable:
					try:
						loop_index = enclosing_loop_of(lines, index, immutable.group("name"))
						hoists.append((loop_index + 1, d, lambda loop_index=loop_index: index_loop_element(lines, loop_index), key))
					except Unhandled as e:
						refused.append(f"{d.group(0)}\n    {e}")

					continue

				try:
					if d.group("code") == "E3005" and MATCH_ARMS.match(d.group("message")):
						cast_match_arms(lines, line_number, d.group("message"), self.parents)
					else:
						twin = twin_of(lines[index], index, column, d.group("message")) if d.group("code") == "E3005" else None
						lines[index] = fix_line(lines[index], column, d.group("code"), d.group("message"), self.parents, self.ranges)

						if twin is not None:
							twins.append(twin)

					self.seen.add(key)
					self.casts += 1
				except Unhandled as e:
					refused.append(f"{d.group(0)}\n    {e}")

				if UNNEEDED_CAST.match(d.group("message")):
					self.forget(rel, index)

			self.speculate(rel, lines, twins)

			for borrow_index, (borrow, d, keys) in clones.items():
				try:
					clone_borrowing_let(lines, borrow_index, borrow.group("element"))
					self.seen.update(keys)
					self.casts += 1
				except Unhandled as e:
					refused.append(f"{d.group(0)}\n    {e}")

			hoisted = set()

			for anchor_line, d, rewrite, key in sorted(hoists, key=lambda s: s[0], reverse=True):
				if anchor_line in hoisted:
					self.seen.add(key)
					continue

				try:
					position = rewrite()
					hoisted.add(anchor_line)
					self.seen.add(key)
					self.insert_at(rel, position)
					self.casts += 1
				except Unhandled as e:
					refused.append(f"{d.group(0)}\n    {e}")

			rewritten = "\n".join(lines)

			if rewritten != text:
				write(path, rewritten)
				self.generation += 1

		return refused


def parse_diagnostics(log):
	return list({m.group(0): m for m in map(DIAGNOSTIC.match, log.splitlines()) if m}.values())


def view_diagnostics(view, diagnostics):
	mapped = {}
	unmapped = []

	for d in diagnostics:
		if d.group("code") not in VIEW_CAST_CODES:
			continue

		try:
			rel, line, column = view.to_canonical(d.group("path"), int(d.group("line")), int(d.group("column")))
		except Unhandled as e:
			unmapped.append(f"{d.group(0)}\n    {e}")
			continue

		site = MappedDiagnostic(d, rel.replace("/", "\\"), line, column)
		mapped[site.group(0)] = site

	return list(mapped.values()), unmapped


def converge(work, seed_name, lanes, rewriter, rounds, started, verified):
	view = SplitView(work)
	streaks = {}
	view_off = False

	try:
		while True:
			rounds += 1

			if rounds > MAX_ROUNDS:
				raise SystemExit(f"generate-seed-language-shim.py: the seed still refuses the tree after {MAX_ROUNDS} rounds")

			split = {rel for rel, count in streaks.items() if count >= STUBBORN_ROUNDS and rel not in view.whole}
			use_view = bool(split) and not view_off

			if use_view:
				view.refresh(split, seed_name)
				split -= view.whole
				use_view = bool(split)

			if use_view:
				code, log = build(work, seed_name, lanes, base=VIEW_DIR)
			else:
				code, log = build(work, seed_name, lanes)

			with open(os.path.join(work, f"round-{rounds:03}{'-view' if use_view else ''}.log"), "w", encoding="utf-8") as f:
				f.write(log)

			elapsed = time.monotonic() - started
			diagnostics = parse_diagnostics(log)

			if use_view:
				diagnostics, refused = view_diagnostics(view, diagnostics)
				generation = rewriter.generation
				refused += rewriter.apply(diagnostics)
				changed = rewriter.generation != generation

				if refused or not changed:
					view_off = True
					view.discard()
					print(f"round {rounds} ({', '.join(lanes)}): the split view of {len(split)} files is dropped for this lane, {len(refused)} refusals, {'tree changed' if changed else 'tree unchanged'}, {elapsed:.0f} s", flush=True)

					if refused:
						print("\n".join(refused), file=sys.stderr, flush=True)

					if not changed:
						continue

				label = f"{len(diagnostics)} refusals cast through a split view of {len(split)} files"
			else:
				if code == 0:
					print(f"round {rounds} ({', '.join(lanes)}): clean, {rewriter.casts} casts, {elapsed:.0f} s", flush=True)
					verified.update({lane: rewriter.generation for lane in lanes})
					return rounds

				if not diagnostics:
					raise SystemExit(f"generate-seed-language-shim.py: the seed exited {code} with no diagnostic\n{log}")

				refused = rewriter.apply(diagnostics)

				if refused:
					report_refusals("the seed refused the shimmed tree with what this script does not cast", refused, work)
					return None

				label = f"{len(diagnostics)} refusals cast"

			per_file = {}

			for d in diagnostics:
				rel = resolve(work, d.group("path"))[1]
				per_file[rel] = per_file.get(rel, 0) + 1

			# A split file reports several sites per round; counting every file in a view round keeps it split.
			refused_once = {rel for rel, count in per_file.items() if use_view or count == 1}
			streaks = {rel: streaks.get(rel, 0) + 1 for rel in refused_once}
			print(f"round {rounds} ({', '.join(lanes)}): {label}, {elapsed:.0f} s", flush=True)
	finally:
		view.discard()


def withdraw_older_seed_forms(work):
	generics = strip_generic_functions(work)
	print(f"withdrew {generics} generic functions", flush=True)
	print(f"stripped {strip_clauses(work)} implements clauses", flush=True)
	widened, refused = write_wide_literals_in_hex(work)

	if refused:
		return report_refusals("an integer literal this script cannot write in a form the seed lexes", refused, work)

	print(f"wrote {widened} integer literals past the seed's decimal range in hex", flush=True)
	unqualified, refused = strip_namespace_qualifiers(work)

	if refused:
		return report_refusals("a qualifier this script cannot write bare without changing the declaration it names", refused, work)

	print(f"unqualified {unqualified} lines naming a `{NAMESPACE_QUALIFIER}` declaration", flush=True)

	annotated, refused = annotate_inferred_fields(work)

	if refused:
		return report_refusals("a field default whose type this script cannot write back as an annotation", refused, work)

	print(f"annotated {annotated} inferred fields", flush=True)
	readonly = strip_readonly_arguments(work)
	print(f"withdrew {readonly} `readonly` element-type qualifiers", flush=True)
	payload_lets = strip_payload_lets(work)
	print(f"withdrew {payload_lets} `let` union payload qualifiers", flush=True)
	return 0


def main():
	started = time.monotonic()
	here = os.path.dirname(os.path.abspath(__file__))
	parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
	parser.add_argument("--tree", default=os.path.dirname(os.path.dirname(here)))
	parser.add_argument("--seed")
	parser.add_argument("--work")
	parser.add_argument("--output")
	parser.add_argument("--resume", action="store_true")
	parser.add_argument("--older-seed", action="store_true")
	args = parser.parse_args()

	tree = os.path.abspath(args.tree)
	seed = args.seed or os.path.join(tree, SEED_DIR, "maxon.exe" if os.name == "nt" else "maxon")
	output = os.path.abspath(args.output or os.path.join(tree, "scripts", "seed-shim", PATCH_NAME))
	work = os.path.abspath(args.work or tempfile.mkdtemp(prefix="seed-language-shim-"))

	if not os.path.isfile(seed):
		raise SystemExit(f"generate-seed-language-shim.py: no seed at {seed} - run scripts/fetch-seed.sh")

	if args.resume != (os.path.exists(work) and bool(os.listdir(work))):
		raise SystemExit(f"generate-seed-language-shim.py: {work} must be {'a work directory a previous run left' if args.resume else 'empty'}")

	parents, ranges = collect_aliases(tree)

	if not args.resume:
		os.makedirs(work, exist_ok=True)
		stage_work(tree, work, seed, output)

		if args.older_seed and withdraw_older_seed_forms(work) != 0:
			return 1

		views, bare, view_names, view_sources = strip_view_bindings(work)
		save_view_registry(work, view_names, view_sources)
		print(f"wrote {views} `view` binding keywords as `let`, {bare} of them over a bare local name", flush=True)
		qualifiers = strip_capability_qualifiers(work)
		print(f"withdrew {qualifiers} `keep`/`sole`/`readonly` parameter and result qualifiers", flush=True)

	view_names, view_sources = load_view_registry(work)
	rewriter = Rewriter(work, parents, ranges, view_names, view_sources)
	rounds = 0
	verified = {}

	while any(verified.get(lane) != rewriter.generation for lane in NATIVE_TARGETS):
		stale = next(lane for lane in NATIVE_TARGETS if verified.get(lane) != rewriter.generation)
		rounds = converge(work, os.path.basename(seed), [stale], rewriter, rounds, started, verified)

		if rounds is None:
			return 1

	print(f"the seed builds the shimmed tree for every lane after {rounds} rounds, {rewriter.casts} casts, {time.monotonic() - started:.0f} s", flush=True)

	diff = git(work, "diff", "--no-color", "--", *SOURCE_DIRS).stdout

	with open(output, "w", encoding="utf-8", newline="\n") as f:
		f.write(HEADER)
		f.write(diff)

	print(f"wrote {output}", flush=True)
	return 0


if __name__ == "__main__":
	sys.exit(main())
