---
feature: codegen-internals
status: draft
keywords: [rdata, cow, managed-memory, strings, stack-probing]
category: codegen
---

## Documentation

### Stack Probing

On Windows x64, functions with stack allocations exceeding 4KB (one page) require stack probing via `__chkstk`. Without it, a large `sub rsp, N` can skip multiple guard pages and crash.

**Note:** The `stack-probing-large-struct-recursive` test is a runtime-execution test that requires allocating a struct with 2000 fields (16KB) and calling it recursively. This test verifies the program does not crash — it cannot be expressed as an IR or rdata check. It is documented here but should be tested programmatically.

### Managed Memory

Heap-allocated arrays require automatic cleanup (`maxon_free`) when they go out of scope. The compiler inserts heap management operations:

- `maxon_free` — cleanup at scope exit
- `maxon_realloc` — array growth (e.g., in loops)
- `maxon_alloc` — heap allocation for mutable arrays

### Rdata and Copy-on-Write

Constant array literals (declared with `let`) are stored in the `.rdata` section and accessed via `lea_rdata`. When a mutable copy is needed (e.g., `var` + mutation), copy-on-write allocates a heap copy. Non-constant arrays (containing variables) go directly to heap.

### Managed Strings

String literals are stored in `.rdata` with null termination. The compiler handles:

- Heap string cleanup at scope exit
- Reassignment (old value cleanup)
- Substring slicing (retains parent reference)
- SSO (small string optimization) for short strings
- Loop concatenation with intermediate cleanup
- Literal deduplication (identical strings share `.rdata` entries)

## Tests

<!-- test: stack-probing-large-struct-recursive -->
<!-- NOTE: This test requires runtime execution with a struct that has 2000 fields.
     The source is generated programmatically and cannot be expressed inline.
     See docs/SPECS.md documentation section on stack probing. -->
```maxon
function main() returns int
    return 0
end 'main'
```
```exitcode
0
```

<!-- test: managed-memory-heap-array-generates-free -->
```maxon
typealias IntArray is Array with int

function main() returns int
    var arr = IntArray{}
    arr.push(1)
    arr.push(2)
    return arr.count()
end 'main'
```
```exitcode
2
```
<!-- TODO: add RequiredMLIR when managed memory is implemented (must contain maxon_free) -->

<!-- test: managed-memory-scope-cleanup-generates-free -->
```maxon
typealias IntArray is Array with int

function main() returns int
    if true 'outer'
        var outer_arr = IntArray{}
        outer_arr.push(100)
        if true 'inner'
            var inner_arr = IntArray{}
            inner_arr.push(200)
        end 'inner'
    end 'outer'
    return 0
end 'main'
```
```exitcode
0
```
<!-- TODO: add RequiredMLIR when managed memory is implemented (must contain >= 2 maxon_free) -->

<!-- test: managed-memory-loop-growth-generates-realloc -->
```maxon
typealias IntArray is Array with int

function main() returns int
    var arr = IntArray{}
    var i = 0
    while i < 10 'loop'
        arr.push(i)
        i = i + 1
    end 'loop'
    return arr.count()
end 'main'
```
```exitcode
10
```
<!-- TODO: add RequiredMLIR when managed memory is implemented (must contain maxon_realloc) -->

<!-- test: managed-memory-fixed-size-array-literal-cleanup -->
```maxon
function main() returns int
    var arr = [10, 20, 30]
    return try arr.get(1) otherwise 0
end 'main'
```
```exitcode
20
```
<!-- TODO: add RequiredMLIR when managed memory is implemented (must contain maxon_free) -->

<!-- test: rdata-constant-array-uses-rdata -->
```maxon
function main() returns int
    let arr = [10, 20, 30]
    return try arr.get(1) otherwise 0
end 'main'
```
```exitcode
20
```
```RequiredRdata
i64[] 10, 20, 30
```
<!-- TODO: add RequiredMLIR when rdata/COW is implemented (must contain lea_rdata) -->

<!-- test: rdata-cow-mutation-copies-to-heap -->
```maxon
function main() returns int
    var arr = [42]
    arr.set(0, value: 77)
    return try arr.get(0) otherwise 0
end 'main'
```
```exitcode
77
```
```RequiredRdata
i64 42
```
<!-- TODO: add RequiredMLIR when rdata/COW is implemented (must contain lea_rdata and maxon_alloc) -->

<!-- test: rdata-cow-multiple-mutations -->
```maxon
function main() returns int
    var arr = [1, 2, 3]
    arr.set(0, value: 10)
    arr.set(1, value: 20)
    arr.set(2, value: 30)
    var sum = 0
    sum = sum + (try arr.get(0) otherwise 0)
    sum = sum + (try arr.get(1) otherwise 0)
    sum = sum + (try arr.get(2) otherwise 0)
    return sum
end 'main'
```
```exitcode
60
```
```RequiredRdata
i64[] 1, 2, 3
```
<!-- TODO: add RequiredMLIR when rdata/COW is implemented (must contain exactly 1 maxon_alloc for COW) -->

<!-- test: rdata-non-constant-array-uses-heap -->
```maxon
function main() returns int
    var x = 5
    var arr = [1, x, 3]
    return try arr.get(1) otherwise 0
end 'main'
```
```exitcode
5
```
<!-- TODO: add RequiredMLIR when rdata/COW is implemented (must NOT contain lea_rdata, must contain maxon_alloc) -->

<!-- test: managed-string-heap-string-generates-cleanup -->
```maxon
function main() returns int
    var s = "this is a heap allocated string!"
    return s.byteLength()
end 'main'
```
```exitcode
31
```
```RequiredRdata
utf8 "this is a heap allocated string!\0"
```
<!-- TODO: add RequiredMLIR when managed strings are implemented (must contain lea_rdata) -->

<!-- test: managed-string-reassignment-handles-old-value -->
```maxon
function main() returns int
    var s = "first heap allocated value!!"
    s = "second heap allocated here!!"
    return s.byteLength()
end 'main'
```
```exitcode
27
```
```RequiredRdata
utf8 "first heap allocated value!!\0"
utf8 "second heap allocated here!!\0"
```
<!-- TODO: add RequiredMLIR when managed strings are implemented (must contain lea_rdata) -->

<!-- test: managed-string-substring-retains-parent -->
```maxon
function main() returns int
    var s = "hello world from heap!!"
    var subManaged = __managed_memory_slice(s._managed, 0, 5)
    return __managed_memory_len(subManaged)
end 'main'
```
```exitcode
5
```
<!-- TODO: add RequiredMLIR when managed strings are implemented (must contain maxon_alloc for slice) -->

<!-- test: managed-string-print-heap-string -->
```maxon
function main() returns int
    var s = "heap allocated string here!!"
    return s.byteLength()
end 'main'
```
```exitcode
27
```
```RequiredRdata
utf8 "heap allocated string here!!\0"
```
<!-- TODO: add RequiredMLIR when managed strings are implemented (must contain lea_rdata) -->

<!-- test: managed-string-short-string-sso -->
```maxon
function main() returns int
    var s = "short"
    return s.byteLength()
end 'main'
```
```exitcode
5
```
```RequiredRdata
utf8 "short\0"
```
<!-- TODO: add RequiredMLIR when managed strings are implemented (must contain lea_rdata) -->

<!-- test: managed-string-loop-concatenation-cleanup -->
```maxon
function main() returns int
    var s = ""
    var a = "a"
    var i = 0
    while i < 5 'loop'
        s = s.concat(a)
        i = i + 1
    end 'loop'
    return s.byteLength()
end 'main'
```
```exitcode
5
```
<!-- TODO: add RequiredMLIR when managed strings are implemented (must contain maxon_alloc for concat) -->

<!-- test: managed-string-literal-deduplication -->
```maxon
function main() returns int
    var a = "hello world"
    var b = "hello world"
    var c = "hello world"
    return a.byteLength() + b.byteLength() + c.byteLength()
end 'main'
```
```exitcode
33
```
```RequiredRdata
utf8 "hello world\0"
```
