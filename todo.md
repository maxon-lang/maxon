https://benchmarksgame-team.pages.debian.net/benchmarksgame/index.html

add "run" to maxon
add "build" to maxon
add "repl" to maxon
add "test" to maxon
add "lint" to maxon
add package manager to maxon

self hosting
mcp server

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


pointers should only be used for FFI

add tests for compiler will all kinds of malformed inputs

this aint right var buffer [12]char = 0

- must use fully qualified name if there is a collision

## Future Enhancements

1. **More numeric types:** f32 (single precision), i64 (long), u32 (unsigned), etc.
2. **Advanced math:** sin, cos, log, exp, etc.
3. **String type:** Proper string handling beyond char arrays
4. **Array return values:** Transfer ownership when returning arrays from functions
5. **Reference counting:** For complex ownership scenarios
6. **Generics:** Type-parameterized functions
7. **SIMD support:** Vector operations for performance
8. **Array slicing:** Sub-array references without copying
