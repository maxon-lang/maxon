*You* Aren't Going To Write It
You *Are* Going To Read It

## TODO

 WARNING  This extension consists of 330 files, out of which 164 are JavaScript files. For performance reasons, you should bundle your extension: https://aka.ms/vscode-bundle-extension. You should also exclude unnecessary files by adding them to your .vscodeignore: https://aka.ms/vscode-vscodeignore.
 
- simplify fmt formatting, print("{a} foo {b}", a, b) should just be print("{a} foo {b}")
- https://llvm.org/docs/Frontend/PerformanceTips.html
- platform specific optimization for runtime.ll
- warnings as errors in release mode
- LLVM autovectorization
- compiler error codes
- tests for debugging (ie step into, step over, etc)
- extensive math function tests

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

