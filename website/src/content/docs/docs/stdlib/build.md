---
title: Build
description: The API a build.maxon program uses to describe a build.
sidebar:
  order: 9
---

`Build` is how a target of a `.maxproj` project file, or a task of a `.maxtasks` file, describes what
`maxon build` compiles. A target is an exported function of no parameters returning `ExitCode`; these
calls write the description as JSON to the file the driver names in `MAXON_BUILD_DESCRIPTION`, and the
compiler reads it back. With that variable unset the description goes to stdout, so a project file run
by hand can be inspected. A target describes one build, and a second description in the same run is
refused. The contract with the command line is documented under `maxon build` in
[CLI_REFERENCE.md](/docs/cli/).

```maxon
// app.maxproj
export function app() returns ExitCode
	Build.build("src")
	return 0
end 'app'

export function gen_tool() returns ExitCode
	Build.build("tools/gen.maxon", output: ".maxon/gen", debugInfo: false)
	return 0
end 'gen_tool'
```

## Build

| Function | Returns | Description |
|----------|---------|-------------|
| `Build.build(source String, output String = "", debugInfo bool = true, version String = "", defines StringArray = empty)` | — | Describe one source file or directory compiled to one output. An empty `output` builds `.maxon/<name>`, `<name>` being the project or task file's own name. |
| `Build.buildWithConfig(config BuildConfig)` | — | Describe one full `BuildConfig`, which may list several sources. |
| `Build.delegate(directory String, target String = "")` | — | Describe a build by handing it to a target of `directory`'s own `.maxproj` file, run with that directory as its working directory. `target` names one of its targets as `maxon build` does, each `_` written `-`; empty means its sole one. |
| `Build.emitBuildConfig(config BuildConfig)` | — | Write one configuration as a JSON object, every string JSON-escaped; `build`, `buildWithConfig` and `delegate` use it. |

`output` omits the extension; the compiler adds `.exe` on Windows and `.wasm` for `wasm32-wasi`.
`debugInfo` controls the `<output>.mxdbg` sidecar that `maxon debug` and `maxon profile` read, and
`maxon build --no-debug-info` turns it off whatever the description says.

## BuildConfig

| Field | Type | Description |
|-------|------|-------------|
| `output` | `String` | Where the executable goes, without an extension. Empty means `.maxon/<name>`, as for `Build.build`. |
| `sources` | `StringArray` | Files and directories compiled as one program, in order. An empty list is refused. |
| `debug_info` | `bool` | Write the `.mxdbg` sidecar. |
| `version` | `String` | A dotted product version stamped into the binary (a `VS_VERSIONINFO` resource on Windows, `LC_SOURCE_VERSION` on macOS); empty means unversioned. A component that is not a number, or that the target's version field cannot hold, refuses the build. |
| `defines` | `StringArray` | `name=value` pairs, each replacing a top-level `String` constant's default, as `maxon build --define` does. |
| `directory` | `String` | The directory whose own `.maxproj` file describes this build. Empty for an ordinary build; stating it alongside `sources` is refused. |
| `delegateTarget` | `String` | With `directory`, which of the delegated project's targets to build. |

`BuildConfig.create(sources StringArray, output String = "", debug_info bool = true, version String = "",
defines StringArray = empty, directory String = "", delegateTarget String = "")` builds one.

`defines` is how a project file puts something it computed into the binary, such as a version derived
from git. A define whose name matches no constant, or more than one, is refused (E3149, E3150), as is a
constant whose initializer is something other than a plain string literal (E3151). A `--define` on the
command line wins over the described build's.
