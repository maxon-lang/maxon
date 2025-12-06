---
feature: array-element-method-call
status: draft
keywords: array, method, call, indexing, chaining
category: expressions
---

## Developer Notes

This feature enables calling methods on array elements directly, like `arr[i].method(args)`.

**Use Case:**
The primary use case is in generic data structures like hash maps where you need to call interface methods on stored elements:
```maxon
// In map implementation:
if _keys[index].equals(key) 'found'
    return _values[index]
end 'found'
```

**Current Limitation:**
The parser currently supports:
- `arr[i].field` - member access on array element ✓
- `obj.method(args)` - method call on variable ✓
- `arr[i].method(args)` - method call on array element ✗ (this feature)

**Implementation:**
After parsing `arr[i].field`, check if followed by `(` and parse as method call.
The array index expression becomes the first argument (implicit self):
`arr[i].method(args)` → `method(arr[i], args)`

**Parser Changes:**
In `parsePrimary()`, after creating `MemberAccessExprAST` for array element member access,
check for `(` and convert to method call:
```cpp
// After: return std::make_unique<MemberAccessExprAST>(std::move(arrayExpr), member.value, ...)
if (check(TokenType::LPAREN)) {
    // Parse as method call: arr[i].method(args) → method(arr[i], args)
    ...
}
```

**Semantic Analysis:**
- Resolve element type from array type
- Look up method on element type
- Type-check arguments

**Codegen:**
Same as regular method calls - the array index expression is evaluated first,
then passed as the first argument to the method.

## Documentation

# Method Calls on Array Elements

You can call methods directly on array elements using the syntax `arr[i].method(args)`.

## Basic Usage

```maxon
struct Point
    var x int
    var y int
    
    function distance() int
        return x + y
    end 'distance'
end 'Point'

function main() int
    var points = array of 3 Point
    points[0] = Point{x: 10, y: 20}
    return points[0].distance()
end 'main'
```

## With Interface Methods

This is particularly useful when working with elements that conform to an interface:

```maxon
var keys = array of 10 string
// ... populate keys ...
if keys[index].equals(searchKey) 'found'
    // Key found at index
end 'found'
```

## Chaining

You can chain member access and method calls:

```maxon
arr[i].field.method()
arr[i].method1().method2()
```

## Tests

<!-- test: basic-method-call -->
```maxon
struct Counter
    var value int
    
    function get() int
        return value
    end 'get'
end 'Counter'

function main() int
    var items = array of 3 Counter
    items[0] = Counter{value: 42}
    items[1] = Counter{value: 100}
    return items[0].get()
end 'main'
```
```exitcode
42
```

<!-- test: method-with-args -->
```maxon
struct Adder
    var base int
    
    function add(x int) int
        return base + x
    end 'add'
end 'Adder'

function main() int
    var adders = array of 2 Adder
    adders[0] = Adder{base: 10}
    adders[1] = Adder{base: 20}
    return adders[1].add(5)
end 'main'
```
```exitcode
25
```

<!-- test: second-element -->
```maxon
struct Value
    var n int
    
    function doubled() int
        return n * 2
    end 'doubled'
end 'Value'

function main() int
    var vals = array of 3 Value
    vals[0] = Value{n: 5}
    vals[1] = Value{n: 10}
    vals[2] = Value{n: 15}
    return vals[2].doubled()
end 'main'
```
```exitcode
30
```

<!-- test: variable-index -->
```maxon
struct Item
    var id int
    
    function getId() int
        return id
    end 'getId'
end 'Item'

function main() int
    var items = array of 3 Item
    items[0] = Item{id: 100}
    items[1] = Item{id: 200}
    items[2] = Item{id: 300}
    var idx = 1
    return items[idx].getId()
end 'main'
```
```exitcode
200
```

<!-- test: computed-index -->
```maxon
struct Data
    var x int
    
    function getX() int
        return x
    end 'getX'
end 'Data'

function main() int
    var arr = array of 4 Data
    arr[0] = Data{x: 1}
    arr[1] = Data{x: 2}
    arr[2] = Data{x: 3}
    arr[3] = Data{x: 4}
    var base = 1
    return arr[base + 1].getX()
end 'main'
```
```exitcode
3
```
