---
feature: collection
status: stable
keywords: collection, array, map, transform, functional, higher-order, get, set, count
category: stdlib
---

## Developer Notes

The `Collection` interface provides indexed access and functional operations for ordered collections. Arrays implement Collection automatically.

**Interface Definition** (`stdlib/interfaces.maxon`):
```text
export interface Collection uses Element extends Iterable
    function count() int
    function get(index int) Element
    function set(index int, value Element) Self
    function map(transform (Element) Element) Self
end 'Collection'
```

**Methods:**
- `count()` - Returns the number of elements
- `get(index)` - Returns element at index (nil if out of bounds)
- `set(index, value)` - Sets element at index, returns self for chaining
- `map(transform)` - Transforms each element using a function, returns new collection

**map() Implementation:**
- Method call `arr.map(fn)` is parsed and transformed to `map(arr, fn)` call
- Semantic analysis in `semantic_analyzer_expr.cpp`:
  - Validates first argument is an array type
  - Validates second argument is a function type `(ElementType) ElementType`
  - Returns same array type as result
- Code generation in `codegen_mir_expr_array.cpp`:
  - `generateMapIntrinsic()` creates inline loop
  - Allocates result array via `malloc`
  - Iterates source array, calls transform function, stores results
  - Returns new array

**Transform Function:**
- Can be a named function reference: `arr.map(double)`
- Can be a closure: `arr.map((x int) gives x * 2)`
- Must accept one parameter matching array element type
- Must return same type as input (Element -> Element)

**Memory:**
- Result array is heap-allocated
- Caller is responsible for eventual cleanup
- Source array is not modified

## Documentation

# Collection

The `Collection` interface provides indexed access and functional operations for ordered collections like arrays.

**Interface:**
```text
interface Collection uses Element extends Iterable
    function count() int
    function get(index int) Element
    function set(index int, value Element) Self
    function map(transform (Element) Element) Self
end 'Collection'
```

Arrays automatically implement the Collection interface.

## count

Returns the number of elements in the collection.

```maxon
function main() int
    var arr = [1, 2, 3, 4, 5]
    printInt(arr.count())
    return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```

## get

Returns the element at the specified index, or nil if out of bounds.

```maxon
function main() int
    var arr = [10, 20, 30]
    if let val = arr.get(1) 'get'
        return val
    end 'get'
    return -1
end 'main'
```
```exitcode
20
```

## set

Sets the element at the specified index. Returns self for method chaining.

```maxon
function main() int
    var arr = [1, 2, 3]
    arr.set(1, 99)
    printInt(arr[1])
    return 0
end 'main'
```
```exitcode
0
```
```stdout
99
```

## map

Transforms each element of a collection by applying a function, returning a new collection with the transformed elements.

**Signature:**
```text
collection.map(transform) collection
```

**Parameters:**
- `transform` - A function that takes an element and returns a transformed value

**Returns:**
A new array containing the transformed elements.

### Using Named Functions

Transform an array using a named function:

```maxon
function double(x int) int
    return x * 2
end 'double'

function main() int
    var numbers = [1, 2, 3, 4, 5]
    var doubled = numbers.map(double)
    printInt(doubled[2])
    return 0
end 'main'
```
```exitcode
0
```
```stdout
6
```

### Using Closures

Transform using an inline closure with `gives`:

```maxon
function main() int
    var numbers = [1, 2, 3]
    var squared = numbers.map((x int) gives x * x)
    printInt(squared[0])
    printInt(squared[1])
    printInt(squared[2])
    return 0
end 'main'
```
```exitcode
0
```
```stdout
1
4
9
```

## Tests

<!-- test: count-basic -->
```maxon
function main() int
    var arr = [1, 2, 3, 4, 5]
    printInt(arr.count())
    return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```

<!-- test: count-empty -->
```maxon
function main() int
    var arr = array of int
    printInt(arr.count())
    return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: get-valid -->
```maxon
function main() int
    var arr = [10, 20, 30]
    var sum = 0
    if let val = arr.get(0) 'get0'
        sum = sum + val
    end 'get0'
    if let val = arr.get(2) 'get2'
        sum = sum + val
    end 'get2'
    return sum
end 'main'
```
```exitcode
40
```

<!-- test: get-out-of-bounds -->
```maxon
function main() int
    var arr = [1, 2, 3]
    if let val = arr.get(10) 'get'
        return val
    else 'get'
        return -1
    end 'get'
end 'main'
```
```exitcode
-1
```

<!-- test: set-basic -->
```maxon
function main() int
    var arr = [1, 2, 3]
    arr.set(0, 100)
    arr.set(2, 300)
    printInt(arr[0])
    printInt(arr[1])
    printInt(arr[2])
    return 0
end 'main'
```
```exitcode
0
```
```stdout
100
2
300
```

<!-- test: map-basic-transform -->
```maxon
function double(x int) int
    return x * 2
end 'double'

function main() int
    var arr = [1, 2, 3, 4, 5]
    var result = arr.map(double)
    printInt(result[0])
    printInt(result[1])
    printInt(result[2])
    printInt(result[3])
    printInt(result[4])
    return 0
end 'main'
```
```exitcode
0
```
```stdout
2
4
6
8
10
```

<!-- test: map-closure-multiply -->
```maxon
function main() int
    var arr = [2, 3, 4]
    var result = arr.map((x int) gives x * 3)
    printInt(result[0])
    printInt(result[1])
    printInt(result[2])
    return 0
end 'main'
```
```exitcode
0
```
```stdout
6
9
12
```

<!-- test: map-closure-square -->
```maxon
function main() int
    var arr = [1, 2, 3, 4]
    var squared = arr.map((n int) gives n * n)
    printInt(squared[0])
    printInt(squared[1])
    printInt(squared[2])
    printInt(squared[3])
    return 0
end 'main'
```
```exitcode
0
```
```stdout
1
4
9
16
```

<!-- test: map-identity-function -->
```maxon
function identity(x int) int
    return x
end 'identity'

function main() int
    var arr = [10, 20, 30]
    var result = arr.map(identity)
    printInt(result[0])
    printInt(result[1])
    printInt(result[2])
    return 0
end 'main'
```
```exitcode
0
```
```stdout
10
20
30
```

<!-- test: map-negate -->
```maxon
function negate(x int) int
    return 0 - x
end 'negate'

function main() int
    var arr = [1, 2, 3]
    var result = arr.map(negate)
    printInt(result[0])
    printInt(result[1])
    printInt(result[2])
    return 0
end 'main'
```
```exitcode
0
```
```stdout
-1
-2
-3
```

<!-- test: map-single-element -->
```maxon
function main() int
    var arr = [42]
    var result = arr.map((x int) gives x + 8)
    printInt(result[0])
    return 0
end 'main'
```
```exitcode
0
```
```stdout
50
```
