---
feature: rdata-section
status: experimental
keywords: [rdata, rodata, const, section, layout, descriptor, linker]
category: codegen
---
# The `.rdata` Section

## Documentation

A program's READ-ONLY data — string-literal blobs and records, float constants, jump
tables, witness tables and `__layout_*` descriptors — is laid down by the linker in the
target's read-only section: `.rdata` in a PE, `.rodata` in an ELF, `__DATA,__const` in a
Mach-O image.

### Pinning the bytes: ```RequiredRdata

A spec pins the section's leading bytes with a ```RequiredRdata block of typed values,
one per line — the exact twin of ```RequiredData, which pins `.data` the same way:

```
i64 16
i64 8
```

The block is compared as a PREFIX, so a program may hold more read-only data after the
bytes a spec names. It is read back OUT of the linked binary, never out of the compiler's
opinion of it, which is what makes it the only gate that can see a section header with a
wrong RVA or a payload the writer moved.

### Why it exists

A `.rdata` payload registered under a STRUCTURAL label — a `__layout_*` descriptor, a
witness table, a Unicode category table — does not mint a `{prefix}{nextStringId}` name,
so nothing a golden fragment prints moves when its bytes or its offset change. The
fragment shows which label a `lea` names; only this block shows what is IN it.

## Tests

<!-- test: layout-descriptor-payload -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
`Sizer with bool`'s layout descriptor is the program's whole read-only image: eight
machine words — size, alignment, elementSize, four zero slots, elementLogicalSize.

```maxon
typealias Integer = int(i64.min to i64.max)

type Sizer uses T
	export var dummy as T

	export static function create(dummy T) returns Self
		return Self{dummy: dummy}
	end 'create'

	export function typeSize() returns Integer
		return sizeof(T)
	end 'typeSize'
end 'Sizer'

typealias BoolSizer = Sizer with bool

function main() returns ExitCode
	let s = BoolSizer.create(false)
	return s.typeSize()
end 'main'
```
```exitcode
1
```
```RequiredRdata
i64 8
i64 8
i64 8
i64 0
i64 0
i64 0
i64 0
i64 1
```
