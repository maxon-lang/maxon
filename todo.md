*You* Aren't Going To Write It
You *Are* Going To Read It

- if x is a float "x as int" should not work, use trunc() or round() or something
- simplify fmt formatting, print("{a} foo {b}", a, b) should just be print("{a} foo {b}")

add "run" to maxon
add "build" to maxon
add "repl" to maxon
add "test" to maxon
add "lint" to maxon
add "profile" to maxon
add "docs" to maxon
add "fmt" to maxon
add package manager to maxon

self hosting
mcp server
AI optimized debugger

get running on linux (devcontainer)
get running on macos

memory safety (arenas)

vscode extention 
	- checks (unneeded fully qualitied name) 
	- quick fixes
		- unneeded type declaration
		- unneeded casts
		- unused parameters
		
	- highlight block identifiers differently than strings
	- enable "go to definition" for variables
	- enable intellisense variable type
	- refactoring: if you rename a struct or function it should also change the end block identifier

- automatically convert int to float parameter

pointers should only be used for FFI

add tests for compiler will all kinds of malformed inputs


@embedFile from zig
\\ for multiline strings (zig)


## Future Enhancements

1. **More numeric types:** f32 (single precision), i64 (long), u32 (unsigned), etc.
3. **String type:** Proper string handling beyond char arrays
4. **Array return values:** Transfer ownership when returning arrays from functions
5. **Reference counting:** For complex ownership scenarios
6. **Generics:** Type-parameterized functions
7. **SIMD support:** Vector operations for performance
8. **Array slicing:** Sub-array references without copying

https://llvm.org/docs/Frontend/PerformanceTips.html

- platform specific optimization for runtime.ll
- warnings as errors in release mode
- LLVM autovectorization
- multiple verbosity levels during compiling
- compiler error codes
- break up large files like codegen.cpp
- formatter
- tests for debugging (ie step into, step over, etc)
- extensive math function tests
- no duplicate block identifiers
