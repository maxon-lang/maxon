---
feature: field-declared-unknown-type
status: stable
keywords: field, type, resolution, E3011, declaration-site
category: type-system
---
# A Field Declared With An Undeclared Type Is E3011

## Documentation

A program names a type in exactly THREE places: a parameter, a return type, and a FIELD. The
first two reach `TypeResolution.resolveNamedType` — the one place that knows what a `named`
type denotes — and report `E3011` when no registry declares the name. The third did not.

`resolveTypes` walked `func.maxonReturnType` and `func.maxonParamTypes` and nothing else, so
`StructLayout.fieldTypes` was never read by the authority. A field declared `as Nonexistent`
therefore reached NO check at all, and `Parser.fieldStorageType`'s recovery value — `integer`,
handed back so the `loadIndirect` stays well-formed for the instant before the pipeline's error
gate throws — became the **final answer**: the program compiled clean and the field silently
typed itself `i64`.

That made `fieldStorageType`'s own comment false. It said the recovery was safe *because*
"E3011, which `TypeResolution` reports against the merged registry, the authority" would fire —
and nothing did. The comment described the right design; the implementation never had it. The
fix is the walk that makes the sentence true, not the deletion of the sentence.

⚠ **The check cannot live at the layout COMMIT point**, which is the obvious other home
(`ParseStaging.commitStructTypes`, where the layouts arrive in declaration order). `mergeArtifact`
folds one artifact at a time, so a field naming a type declared in a file folded LATER would
report a false E3011. The registry is only complete once every artifact is folded — which is
exactly where `resolveTypes` runs.

The diagnostic is UNPOSITIONED, identically to the parameter/return-type E3011 above it and for
that one's stated reason: nothing records a source span for a type REFERENCE (only ops and
parameter NAMES carry one). That is an accepted, documented limitation, not a new one.

## Tests

<!-- test: field-unknown-type-read -->
<!-- targets: wasm32-wasi -->
```maxon

type Value
	export var n as Nonexistent

	static function create() returns Self
		return Value{n: 7}
	end 'create'
end 'Value'

function main() returns ExitCode
	let v = Value.create()
	return v.n
end 'main'
```
```maxoncstderr
error E3011: Unknown type 'Nonexistent'
```

<!-- test: field-unknown-type-unread -->
<!-- targets: wasm32-wasi -->
An undeclared field type is a DECLARATION error, so it is reported even when no code ever reads
the field. The walk is over the registry, not over the uses — which is what makes it a check on
the declaration rather than an accident of a load site being present.
```maxon

typealias Integer = int(i64.min to i64.max)

type Value
	export var n as Integer
	export var bad as Nonexistent

	static function create() returns Self
		return Value{n: 42, bad: 0}
	end 'create'
end 'Value'

function main() returns ExitCode
	let v = Value.create()
	return v.n
end 'main'
```
```maxoncstderr
error E3011: Unknown type 'Nonexistent'
```
