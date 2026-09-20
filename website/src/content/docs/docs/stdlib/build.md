---
title: Build
description: The API a build.maxon program uses to describe a build.
sidebar:
  order: 9
---

`Build` is how a build manifest, `project.maxon`, describes what `maxon build` compiles. A manifest is a
program whose entry point is `build`; these calls write the description as JSON to the file the driver
names in `MAXON_BUILD_DESCRIPTION`, and the compiler reads it back. With that variable unset the
description goes to stdout instead, so a manifest run by hand can be inspected. The manifest's contract with the command line is documented under `maxon build` in
[CLI_REFERENCE.md](/docs/cli/).

```maxon
export function build() returns ExitCode
	var targets = BuildConfigArray.create()
	targets.push(Build.target("app", source: "src", output: ".maxon/app"))
	targets.push(Build.target("tool", source: "tools/gen.maxon", output: ".maxon/gen", debugInfo: false))
	Build.buildTargets(targets)
	return 0
end 'build'
```

## Build

| Function | Returns | Description |
|----------|---------|-------------|
| `Build.build(source String, output String, debugInfo bool = true, version String = "", defines StringArray = empty)` | — | Describe one source file or directory compiled to one output. The target's name is the source path. |
| `Build.target(name String, source String, output String, debugInfo bool = true, version String = "", defines StringArray = empty)` | `BuildConfig` | One named target, for a manifest with several. Prints nothing. |
| `Build.buildTargets(targets BuildConfigArray)` | — | Describe several named targets. |
| `Build.buildWithConfig(config BuildConfig)` | — | Describe one full `BuildConfig`, which may list several sources. |
| `Build.delegate(name String, directory String, target String = "")` | — | Describe a build by handing it to `directory`'s own `project.maxon`, run with that directory as its working directory. `target` selects one of its targets; empty means its sole one. |
| `Build.delegateTarget(name String, directory String, target String = "")` | `BuildConfig` | One delegated target, for `buildTargets`. Writes nothing. |
| `Build.emitBuildConfig(config BuildConfig)` | — | Write one configuration as a JSON object, every string JSON-escaped; `build`, `buildWithConfig`, `delegate` and `buildTargets` use it. |

`output` omits the extension; the compiler adds `.exe` on Windows and `.wasm` for `wasm32-wasi`.
`debugInfo` controls the `<output>.mxdbg` sidecar that `maxon debug` and `maxon profile` read, and
`maxon build --no-debug-info` turns it off regardless.

## BuildConfig

| Field | Type | Description |
|-------|------|-------------|
| `name` | `String` | What `maxon build <name>` selects. It is not the binary's product name, which is its file name. |
| `output` | `String` | Where the executable goes, without an extension. |
| `sources` | `StringArray` | Files and directories compiled as one program, in order. An empty list is refused. |
| `debug_info` | `bool` | Write the `.mxdbg` sidecar. |
| `version` | `String` | A dotted product version stamped into the binary (a `VS_VERSIONINFO` resource on Windows, `LC_SOURCE_VERSION` on macOS); empty means unversioned. A component that is not a number, or that the target's version field cannot hold, refuses the build. |
| `defines` | `StringArray` | `name=value` pairs, each replacing a top-level `String` constant's default, as `maxon build --define` does. |
| `directory` | `String` | The directory whose own `project.maxon` describes this build. Empty for an ordinary build; stating it alongside `sources` is refused. |
| `delegateTarget` | `String` | With `directory`, which of the delegated manifest's targets to build. |

`BuildConfig.create(name String, output String, sources StringArray, debug_info bool, version String = "",
defines StringArray = empty, directory String = "", delegateTarget String = "")` builds one. `BuildConfigArray` is `Array with BuildConfig`.

`defines` is how a manifest puts something it computed into the binary, such as a version derived from git.
A define whose name matches no constant, or more than one, is refused (E3149, E3150), as is a constant whose
initializer is not a plain string literal (E3151). A `--define` on the command line wins over the
manifest's.
