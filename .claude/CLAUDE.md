You do not care if an issue is pre-existing. Just debug and fix it.

Do not use "cmd /c" to run commands

There are no time constraints. Complexity doesn't matter. If you are fixing an issue then fix it properly. No workarounds.

Do not use git commands that would change the working directory like stash or checkout

## Building and Testing

There is a `build.maxon` file in the project root that defines all build and test commands. Use `maxon run <command>` to invoke them:

| Command | Description |
|---------|-------------|
| `maxon run build-sharp` | Build the C# bootstrap compiler (maxon-sharp) |
| `maxon run spec-test-sharp` | Build the C# compiler and run its spec tests |
| `maxon run build-selfhosted` | Compile the self-hosted compiler using the bootstrap compiler |
| `maxon run spec-test-selfhosted` | Compile the self-hosted compiler and run its spec tests |
| `maxon run spec-test-wasm` | Compile the self-hosted compiler and run its Wasm spec tests |

Run `maxon run` (no arguments) to see all available commands.

Do NOT use `dotnet build`, `dotnet run`, `dotnet publish`, or invoke compiler binaries directly. Always use `maxon run`.
