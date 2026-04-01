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
	export let value int(0 to 100)
end 'Inner'

type Outer
	export let inner Inner
	export let flag bool
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
	export let latency Latency
end 'OpMeta'

type X64OpMeta
	export let meta OpMeta
	export let setsFlags bool
end 'X64OpMeta'

enum X64Op
	add = X64OpMeta{meta: OpMeta{latency: 1}, setsFlags: true}
	mov = X64OpMeta{meta: OpMeta{latency: 3}, setsFlags: false}
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
	export let latency Latency
end 'OpMeta'

type TargetMeta
	export let meta OpMeta
	export let setsFlags bool
end 'TargetMeta'

enum Op
	add = TargetMeta{meta: OpMeta{latency: 1}, setsFlags: true}
	mov = TargetMeta{meta: OpMeta{latency: 2}, setsFlags: false}
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
	export let latency Latency
	export let isMemory bool
	export let isCall bool
end 'OpMeta'

type X64OpMeta
	export let meta OpMeta
	export let setsFlags bool
end 'X64OpMeta'

enum X64Op
	load = X64OpMeta{meta: OpMeta{latency: 4, isMemory: true, isCall: false}, setsFlags: false}
	add = X64OpMeta{meta: OpMeta{latency: 1, isMemory: false, isCall: false}, setsFlags: true}
	call = X64OpMeta{meta: OpMeta{latency: 5, isMemory: true, isCall: true}, setsFlags: false}
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
	export let latency Latency
end 'Inner'

type Outer
	export let inner Inner
	export let fast bool
end 'Outer'

enum Op
	add = Outer{inner: Inner{latency: 1}, fast: true}
	div = Outer{inner: Inner{latency: 40}, fast: false}
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
