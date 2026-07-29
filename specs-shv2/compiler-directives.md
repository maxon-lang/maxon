---
feature: compiler-directives
status: stable
keywords: directive, if, else, endif, conditional, preprocessor
category: language
---
# Compiler directives

## Documentation

Maxon supports conditional compilation via `#if` / `#else` / `#endif` directives. The condition is evaluated at parse time against the active build target.

**Syntax:**
```maxon
#if os(Windows)
	// Windows-only code
#else
	// non-Windows code
#endif
```

**Supported predicates:**
- `os(Name)` — matches the build target's OS (`Windows`, `Linux`, `Macos`, `Wasi`).
- `arch(Name)` — matches the build target's CPU (`x64`, `arm64`, `wasm32`).
- `testing(true|false)` — true when the compiler is running under the spec test harness.

**Boolean operators:** `and`, `or`, `not`, plus parentheses for grouping.

**Nesting:** `#if`/`#endif` pairs may nest. Each `#endif` closes the most recent `#if`.

**Directives are valid at any of the following positions:**
- Top-level (between declarations)
- Inside a `type`, `enum`, `union`, `interface`, or `extension` body
- Inside a function body (between statements)

## Tests

<!-- test: directives.both-branches-balance -->
```maxon
function main() returns ExitCode
	#if os(Wasi)
		return 42
	#else
		return 42
	#endif
end 'main'
```
```exitcode
42
```

<!-- test: directives.not-doubles-back -->
```maxon
function main() returns ExitCode
	#if not os(Wasi)
		#if os(Wasi)
			return 99
		#else
			return 10
		#endif
	#else
		#if not os(Wasi)
			return 99
		#else
			return 10
		#endif
	#endif
end 'main'
```
```exitcode
10
```

<!-- test: directives.or-always-true -->
```maxon
function main() returns ExitCode
	#if os(Windows) or os(Linux) or os(Macos) or os(Wasi)
		return 5
	#else
		return 99
	#endif
end 'main'
```
```exitcode
5
```

<!-- test: directives.dead-branch-unparsed -->
```maxon
function main() returns ExitCode
	#if not os(Windows) and not os(Linux) and not os(Macos) and not os(Wasi)
		this_function_does_not_exist_and_should_not_be_parsed()
	#else
		return 13
	#endif
end 'main'
```
```exitcode
13
```

<!-- test: directives.arch-supported -->
```maxon
function main() returns ExitCode
	#if arch(x64) or arch(arm64) or arch(wasm32)
		return 7
	#else
		return 99
	#endif
end 'main'
```
```exitcode
7
```
