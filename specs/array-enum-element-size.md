---
feature: array-enum-element-size
status: stable
keywords: [array, enum, element-size, push, grow, memory-management]
category: memory
---
# Array of Enum: Element Size and Push Correctness

## Documentation

When an array holds elements of an enum type (with associated values), the backing `__ManagedMemory` must have `element_size = 8` (heap pointer size). If `element_size` is incorrectly set to 0, every push computes `buffer + index * 0 = buffer`, always overwriting slot 0, and grow computes `newCap * 0 = 0` bytes, never actually growing the buffer. The array appears to have the right `count()` but only the last-pushed element survives; all earlier elements were decref'd and freed. Cascading cleanup then crashes on the stale pointers.

## Tests

<!-- test: enum-array-push-count -->
### Push to array of enum preserves count
Basic verification that pushing multiple enum values gives the correct count.
```maxon
typealias Integer = int(i64.min to i64.max)

union Op
		add(value Integer)
		sub(value Integer)
		nop
end 'Op'

typealias OpArray = Array with Op

function main() returns ExitCode
		var ops = OpArray.create()
		ops.push(Op.add(1))
		ops.push(Op.sub(2))
		ops.push(Op.nop)
		ops.push(Op.add(3))
		ops.push(Op.sub(4))
		return ops.count()
end 'main'
```
```exitcode
5
```

<!-- test: enum-array-push-get -->
### Push then get retrieves correct elements
Verifies that earlier pushed elements are still accessible (not overwritten).
```maxon
typealias Integer = int(i64.min to i64.max)

union Op
		add(value Integer)
		sub(value Integer)
		nop
end 'Op'

typealias OpArray = Array with Op

function main() returns ExitCode
		var ops = OpArray.create()
		ops.push(Op.add(10))
		ops.push(Op.sub(20))
		ops.push(Op.add(30))

		let first = try ops.get(0) otherwise Op.nop
		match first 'check'
				add(v) then return v
				sub then return 99
				nop then return 98
		end 'check'
end 'main'
```
```exitcode
10
```

<!-- test: enum-array-push-get-last -->
### Get last element from enum array
```maxon
typealias Integer = int(i64.min to i64.max)

union Op
		add(value Integer)
		sub(value Integer)
		nop
end 'Op'

typealias OpArray = Array with Op

function main() returns ExitCode
		var ops = OpArray.create()
		ops.push(Op.add(10))
		ops.push(Op.sub(20))
		ops.push(Op.add(42))

		let last = try ops.get(2) otherwise Op.nop
		match last 'check'
				add(v) then return v
				sub then return 99
				nop then return 98
		end 'check'
end 'main'
```
```exitcode
42
```

<!-- test: nested-enum-array-push-get -->
### Nested enum (enum wrapping enum) in array
This mirrors the IrOp pattern from the self-hosted compiler.
```maxon
typealias Integer = int(i64.min to i64.max)

union CfOp
		br(target Integer)
		condBr(cond Integer)
end 'CfOp'

union IrOp
		cf(op CfOp)
		arith(value Integer)
end 'IrOp'

typealias IrOpArray = Array with IrOp

function checkFirst(ops IrOpArray) returns Integer
		let first = try ops.get(0) otherwise IrOp.arith(0)
		match first 'checkFirst'
				arith(v) then return v
				cf then return 99
		end 'checkFirst'
end 'checkFirst'

function checkMid(ops IrOpArray) returns Integer
		let mid = try ops.get(2) otherwise IrOp.arith(0)
		match mid 'checkMid'
				cf then return 1
				arith then return 0
		end 'checkMid'
end 'checkMid'

function main() returns ExitCode
		var ops = IrOpArray.create()
		ops.push(IrOp.arith(10))
		ops.push(IrOp.arith(20))
		ops.push(IrOp.cf(CfOp.br(99)))
		ops.push(IrOp.arith(30))
		ops.push(IrOp.arith(40))

		if ops.count() != 5 'badCount'
				return 1
		end 'badCount'

		// First element must still be arith(10), not overwritten
		let v = checkFirst(ops)
		if v != 10 'wrong'
				return 2
		end 'wrong'

		// Middle element must be cf variant
		let m = checkMid(ops)
		if m != 1 'wrongMid'
				return 3
		end 'wrongMid'

		return 0
end 'main'
```
```exitcode
0
```

<!-- test: enum-array-in-struct-cascade-free -->
### Struct with enum array field: cascade free must not crash
When a struct holding an enum array is freed, the cascade must correctly
walk the array elements. If element_size is 0, the array has stale pointers
at indices > 0 and the cascade crashes.
```maxon
typealias Integer = int(i64.min to i64.max)

union CfOp
		br(target Integer)
end 'CfOp'

union IrOp
		cf(op CfOp)
		arith(value Integer)
end 'IrOp'

typealias IrOpArray = Array with IrOp

type Block
		export var id as Integer
		export var ops as IrOpArray
		export var terminator as IrOp

		static function create(id Integer, ops IrOpArray, terminator IrOp) returns Self
			return Self{id: id, ops: ops, terminator: terminator}
		end 'create'
end 'Block'

function makeBlock() returns Block
		var b = Block.create(1, ops: IrOpArray.create(), terminator: IrOp.cf(CfOp.br(0)))
		b.ops.push(IrOp.arith(10))
		b.ops.push(IrOp.arith(20))
		b.ops.push(IrOp.arith(30))
		b.ops.push(IrOp.arith(40))
		b.ops.push(IrOp.arith(50))
		return b
end 'makeBlock'

function main() returns ExitCode
		// makeBlock returns a block; the local goes out of scope and is freed.
		// The cascade must correctly free 5 ops in the array.
		let b = makeBlock()
		let first = try b.ops.get(0) otherwise IrOp.arith(0)
		match first 'check'
				arith(v) then return v
				cf then return 99
		end 'check'
end 'main'
```
```exitcode
10
```

<!-- test: narrow-signed-enum-element-reaches-user-code-through-the-corpus -->
### A narrow SIGNED enum element keeps its sign when the CORPUS hands it to user code

⭐ **THE THIRD SIGN-EXTENSION DOOR (`enum-narrow-storage`).** A payload-free enum's array element occupies
the narrowest slot its raw values need, so `Signal` — whose `minus` is `-1` — rides ONE SIGNED BYTE. The
shared element load zero-extends (one compiled byte loop, strided by the record's `element_size@24`, with no
signedness in the record to consult), so the compiler has to put the sign back. Two of the three places it
does that are reads at a call site: `arr.get(0)` and a value a corpus body RETURNS. This case pins the
third, which neither of those stands in front of: **a corpus body reads a slot and passes the raw word INTO
user code as an ARGUMENT.**

`stdlib/Array.maxon`'s `sort(cmp)` and `map(transform)` are the two cheapest reaches — `sort`'s comparator
overload carries no conformance constraint at all — and `stdlib/helpers/sort/*` reads the slot at five sites
(`driftQuicksort.maxon:69-70`, `driftsort.maxon:143-160`, `mergeSort.maxon:52`, `pdqsort.maxon:36-128`).
Each body is compiled ONCE against an opaque `Element` and cannot know what the instance fixed, so the
extension has to happen at the CALLEE's entry — the one place every such arrival passes through.

⚠ **WITHOUT THE PARAMETER DOOR** the comparator is handed `-1` as **255**, so it sees a value no case of
its own type has, and the resulting ORDER is wrong — `Signal.minus` sorts LAST. The exit
code below carries all three facts at once, so either half failing changes it: the sorted first element
(`103` → `2xx`), the sorted last element, and whether `map`'s transform ever saw a negative (`1` → `0`).
```maxon
typealias Integer = int(i64.min to i64.max)

enum Signal
	minus = -1
	zero = 0
	plus = 1
end 'Signal'

typealias Signals = Array with Signal

function bySignal(x Signal, y Signal) returns Ordering
	if (x.rawValue as Integer) < (y.rawValue as Integer) 'less'
		return Ordering.lessThan
	end 'less'
	if (x.rawValue as Integer) > (y.rawValue as Integer) 'greater'
		return Ordering.greaterThan
	end 'greater'
	return Ordering.equalTo
end 'bySignal'

function markNegative(s Signal) returns Signal
	if (s.rawValue as Integer) < 0 'wasNegative'
		return Signal.plus
	end 'wasNegative'
	return Signal.zero
end 'markNegative'

function main() returns ExitCode
	var a = Signals.create()
	a.push(Signal.plus)
	a.push(Signal.zero)
	a.push(Signal.minus)
	a.sort(bySignal)

	let first = try a.first() otherwise Signal.zero
	let last = try a.last() otherwise Signal.zero

	var seenNegative = 0 as Integer
	for v in a.map(markNegative) 'each'
		seenNegative = seenNegative + (v.rawValue as Integer)
	end 'each'

	return ((((first.rawValue as Integer) + 2) * 100) + (((last.rawValue as Integer) + 2) * 10) + seenNegative) as ExitCode
end 'main'
```
```exitcode
131
```

### A declared enum byte with more than 256 cases keeps every value

<!-- test: a-declared-enum-byte-with-more-than-256-cases-keeps-every-value -->
A user `enum Byte` beside the stdlib's `Byte` is an ordinary enum: its array slot is as wide as its case count needs, so an element of case 299 reads back as written.
```maxon
enum Byte
	c0
	c1
	c2
	c3
	c4
	c5
	c6
	c7
	c8
	c9
	c10
	c11
	c12
	c13
	c14
	c15
	c16
	c17
	c18
	c19
	c20
	c21
	c22
	c23
	c24
	c25
	c26
	c27
	c28
	c29
	c30
	c31
	c32
	c33
	c34
	c35
	c36
	c37
	c38
	c39
	c40
	c41
	c42
	c43
	c44
	c45
	c46
	c47
	c48
	c49
	c50
	c51
	c52
	c53
	c54
	c55
	c56
	c57
	c58
	c59
	c60
	c61
	c62
	c63
	c64
	c65
	c66
	c67
	c68
	c69
	c70
	c71
	c72
	c73
	c74
	c75
	c76
	c77
	c78
	c79
	c80
	c81
	c82
	c83
	c84
	c85
	c86
	c87
	c88
	c89
	c90
	c91
	c92
	c93
	c94
	c95
	c96
	c97
	c98
	c99
	c100
	c101
	c102
	c103
	c104
	c105
	c106
	c107
	c108
	c109
	c110
	c111
	c112
	c113
	c114
	c115
	c116
	c117
	c118
	c119
	c120
	c121
	c122
	c123
	c124
	c125
	c126
	c127
	c128
	c129
	c130
	c131
	c132
	c133
	c134
	c135
	c136
	c137
	c138
	c139
	c140
	c141
	c142
	c143
	c144
	c145
	c146
	c147
	c148
	c149
	c150
	c151
	c152
	c153
	c154
	c155
	c156
	c157
	c158
	c159
	c160
	c161
	c162
	c163
	c164
	c165
	c166
	c167
	c168
	c169
	c170
	c171
	c172
	c173
	c174
	c175
	c176
	c177
	c178
	c179
	c180
	c181
	c182
	c183
	c184
	c185
	c186
	c187
	c188
	c189
	c190
	c191
	c192
	c193
	c194
	c195
	c196
	c197
	c198
	c199
	c200
	c201
	c202
	c203
	c204
	c205
	c206
	c207
	c208
	c209
	c210
	c211
	c212
	c213
	c214
	c215
	c216
	c217
	c218
	c219
	c220
	c221
	c222
	c223
	c224
	c225
	c226
	c227
	c228
	c229
	c230
	c231
	c232
	c233
	c234
	c235
	c236
	c237
	c238
	c239
	c240
	c241
	c242
	c243
	c244
	c245
	c246
	c247
	c248
	c249
	c250
	c251
	c252
	c253
	c254
	c255
	c256
	c257
	c258
	c259
	c260
	c261
	c262
	c263
	c264
	c265
	c266
	c267
	c268
	c269
	c270
	c271
	c272
	c273
	c274
	c275
	c276
	c277
	c278
	c279
	c280
	c281
	c282
	c283
	c284
	c285
	c286
	c287
	c288
	c289
	c290
	c291
	c292
	c293
	c294
	c295
	c296
	c297
	c298
	c299
end 'Byte'

typealias Bs = Array with Byte

function main() returns ExitCode
	var a = Bs.create()
	a.push(Byte.c299)
	a.push(Byte.c0)
	a.push(Byte.c260)
	var b = Bs.create()
	b.push(Byte.c299)
	b.push(Byte.c0)
	b.push(Byte.c260)
	let first = try a.get(0) otherwise return 9
	let last = try a.get(2) otherwise return 9
	print("{first == Byte.c299} {last == Byte.c260} {a == b} {a.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true true true 3
```

### A declared enum byte with a few cases keeps every value

<!-- test: a-declared-enum-byte-with-a-few-cases-keeps-every-value -->
A user `enum Byte` with a few plain cases round-trips through an array and compares equal element-wise.
```maxon
enum Byte
	low
	mid
	high
end 'Byte'

typealias Bs = Array with Byte

function main() returns ExitCode
	var a = Bs.create()
	a.push(Byte.high)
	a.push(Byte.low)
	a.push(Byte.mid)
	var b = Bs.create()
	b.push(Byte.high)
	b.push(Byte.low)
	b.push(Byte.mid)
	let first = try a.get(0) otherwise return 9
	let last = try a.get(2) otherwise return 9
	print("{first == Byte.high} {last == Byte.mid} {a == b} {a.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true true true 3
```
