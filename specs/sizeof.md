---
feature: sizeof
status: stable
keywords: sizeof, type size, memory, intrinsic
category: intrinsic
---
# sizeof

## Documentation

Returns the size of a type in bytes as a compile-time integer constant.

**Signature:** `sizeof(TypeName) int`

**Parameters:**
- `TypeName` - The name of the type to measure

**Returns:** The size of the type in bytes

**Example:**

```maxon
var x = sizeof(int)       // 8
var y = sizeof(bool)      // 1
var z = sizeof(byte)      // 1
```

**Notes:**
- `sizeof(int)` returns 8 (64-bit integer)
- `sizeof(float)` returns 8 (64-bit float)
- `sizeof(bool)` returns 1
- `sizeof(byte)` returns 1
- For structs, returns the total size (each field occupies 8 bytes)

## Tests

<!-- test: sizeof.int -->
```maxon
function main() returns ExitCode
	return sizeof(int)
end 'main'
```
```exitcode
8
```

<!-- test: sizeof.float -->
```maxon
function main() returns ExitCode
	return sizeof(float)
end 'main'
```
```exitcode
8
```

<!-- test: sizeof.bool -->
```maxon
function main() returns ExitCode
	return sizeof(bool)
end 'main'
```
```exitcode
1
```

<!-- test: sizeof.byte -->
```maxon
function main() returns ExitCode
	return sizeof(byte)
end 'main'
```
```exitcode
1
```

<!-- test: sizeof.struct -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x Integer
	export var y Integer
end 'Point'

function main() returns ExitCode
	return sizeof(Point)
end 'main'
```
```exitcode
16
```

<!-- test: sizeof.struct-three-fields -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Vec3
	export var x Integer
	export var y Integer
	export var z Integer
end 'Vec3'

function main() returns ExitCode
	return sizeof(Vec3)
end 'main'
```
```exitcode
24
```

<!-- test: sizeof.enum -->
```maxon
enum Color
	red
	green
	blue
end 'Color'

function main() returns ExitCode
	return sizeof(Color)
end 'main'
```
```exitcode
8
```

<!-- test: sizeof.arithmetic -->
```maxon
function main() returns ExitCode
	return sizeof(int) + sizeof(bool)
end 'main'
```
```exitcode
9
```

<!-- test: sizeof.let-binding -->
```maxon
function main() returns ExitCode
	let size = sizeof(int)
	return size
end 'main'
```
```exitcode
8
```
