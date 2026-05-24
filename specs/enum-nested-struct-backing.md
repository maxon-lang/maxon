---
feature: enum-nested-struct-backing
status: experimental
keywords: [enum, struct, backing, rawValue, nested, composition]
category: type-system
---

# Enum Nested Struct Backing

## Documentation

Struct-backed enum raw values support nested struct literals. This enables composition where a target-specific backing type embeds a shared metadata struct.

```text
type Inner
	export let value as int(0 to 100)
end 'Inner'

type Outer
	export let inner as Inner
	export let flag as bool
end 'Outer'

enum Op
	add = Outer{inner: Inner{value: 1}, flag: true}
	nop = Outer{inner: Inner{value: 0}, flag: false}
end 'Op'

let op = Op.add
let v = op.rawValue.inner.value  // 1
let f = op.rawValue.flag         // true
```

## Tests

### Basic nested struct field access

<!-- test: nested-struct-backing-basic -->
```maxon
typealias Latency = int(0 to 50)

type OpMeta
	export let latency as Latency

	static function create(latency Latency) returns Self
		return Self{latency: latency}
	end 'create'
end 'OpMeta'

type X64OpMeta
	export let meta as OpMeta
	export let setsFlags as bool

	static function create(meta OpMeta, setsFlags bool) returns Self
		return Self{meta: meta, setsFlags: setsFlags}
	end 'create'
end 'X64OpMeta'

enum X64Op
	add = X64OpMeta.create(OpMeta.create(1), setsFlags: true)
	mov = X64OpMeta.create(OpMeta.create(3), setsFlags: false)
end 'X64Op'

function main() returns ExitCode
	let op = X64Op.add
	return op.rawValue.meta.latency
end 'main'
```
```exitcode
1
```

### Access outer field alongside nested struct

<!-- test: nested-struct-backing-outer-field -->
```maxon
typealias Latency = int(0 to 50)

type OpMeta
	export let latency as Latency

	static function create(latency Latency) returns Self
		return Self{latency: latency}
	end 'create'
end 'OpMeta'

type TargetMeta
	export let meta as OpMeta
	export let setsFlags as bool

	static function create(meta OpMeta, setsFlags bool) returns Self
		return Self{meta: meta, setsFlags: setsFlags}
	end 'create'
end 'TargetMeta'

enum Op
	add = TargetMeta.create(OpMeta.create(1), setsFlags: true)
	mov = TargetMeta.create(OpMeta.create(2), setsFlags: false)
end 'Op'

function main() returns ExitCode
	let op = Op.add
	if op.rawValue.setsFlags 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

### Multiple fields in nested struct

<!-- test: nested-struct-backing-multi -->
```maxon
typealias Latency = int(0 to 50)

type OpMeta
	export let latency as Latency
	export let isMemory as bool
	export let isCall as bool

	static function create(latency Latency, isMemory bool, isCall bool) returns Self
		return Self{latency: latency, isMemory: isMemory, isCall: isCall}
	end 'create'
end 'OpMeta'

type X64OpMeta
	export let meta as OpMeta
	export let setsFlags as bool

	static function create(meta OpMeta, setsFlags bool) returns Self
		return Self{meta: meta, setsFlags: setsFlags}
	end 'create'
end 'X64OpMeta'

enum X64Op
	load = X64OpMeta.create(OpMeta.create(4, isMemory: true, isCall: false), setsFlags: false)
	add = X64OpMeta.create(OpMeta.create(1, isMemory: false, isCall: false), setsFlags: true)
	call = X64OpMeta.create(OpMeta.create(5, isMemory: true, isCall: true), setsFlags: false)
end 'X64Op'

function main() returns ExitCode
	let op = X64Op.load
	if op.rawValue.meta.isMemory 'check'
		return op.rawValue.meta.latency
	end 'check'
	return 0
end 'main'
```
```exitcode
4
```

### Match on nested struct-backed enum

<!-- test: nested-struct-backing-match -->
```maxon
typealias Latency = int(0 to 50)

type Inner
	export let latency as Latency

	static function create(latency Latency) returns Self
		return Self{latency: latency}
	end 'create'
end 'Inner'

type Outer
	export let inner as Inner
	export let fast as bool

	static function create(inner Inner, fast bool) returns Self
		return Self{inner: inner, fast: fast}
	end 'create'
end 'Outer'

enum Op
	add = Outer.create(Inner.create(1), fast: true)
	div = Outer.create(Inner.create(40), fast: false)
end 'Op'

function main() returns ExitCode
	let op = Op.div
	return match op 'dispatch'
		add gives 10
		div gives 20
	end 'dispatch'
end 'main'
```
```exitcode
20
```
