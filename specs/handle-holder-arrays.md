---
feature: handle-holder-arrays
status: stable
keywords: [array, handle, pin, destructor, wasm32-wasi]
category: memory
---
# Arrays Of Handle Holders Drop On Every Lane

## Documentation

An array of structs that hold a compiler-owned handle drops each element through the handle's destructor.
Every lane contains that destructor under one name, whether or not the lane can ever hold such a handle.

## Tests

<!-- test: an-array-of-process-handle-holders-is-dropped-on-every-lane -->
<!-- tier: stdlib -->
A lane with no process-sampling substrate cannot open a `__ManagedHandle`, so an array of structs holding one is empty there and its drop closes nothing.

```maxon
type Holder
	export var h as __ManagedHandle

	static function create(h __ManagedHandle) returns Self
		return Self{h: h}
	end 'create'
end 'Holder'

typealias Holders = Array with Holder

function main() returns ExitCode
	let a = Holders.create(0)
	print("{a.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: an-array-of-machine-pin-holders-is-dropped-on-every-lane -->
<!-- tier: stdlib -->
A lane with no green threads cannot take a `__ManagedPin`, so an array of structs holding one is empty there and its drop releases nothing.

```maxon
type Holder
	export var p as __ManagedPin

	static function create(p __ManagedPin) returns Self
		return Self{p: p}
	end 'create'
end 'Holder'

typealias Holders = Array with Holder

function main() returns ExitCode
	let a = Holders.create(0)
	print("{a.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```
