---
feature: internal-types
status: stable
keywords: [internal, opaque, __ManagedMemory, stdlib, restriction]
category: type-system
---

# Internal Types

## Documentation

### Internal Types

Types starting with an underscore are internal to the standard library and cannot be used in user code. These types provide low-level implementation details that are not part of the public API.

The most common internal type is `__ManagedMemory`, which provides the underlying storage for the stdlib `Array` type. While user code cannot directly use `__ManagedMemory`, it is automatically used behind the scenes when working with arrays.

### Using Arrays in User Code

User code should use the public `Array` type from the standard library with a type alias:

```text
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

var arr = IntArray{}
arr.push(42)
print(arr.count())
```

The internal `__ManagedMemory` type is used internally by the `Array` implementation but is not accessible to user code.

## Tests

<!-- test: array-public-api -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
  var arr = IntArray{}
  arr.push(10)
  arr.push(20)
  arr.push(30)
  return arr.count()
end 'main'
```
```exitcode
3
```

<!-- test: array-literal-works -->
```maxon
function main() returns ExitCode
  var arr = [1, 2, 3, 4, 5]
  return arr.count()
end 'main'
```
```exitcode
5
```
