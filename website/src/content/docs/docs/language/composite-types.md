---
title: Composite Types
description: Structs, methods, interfaces, extensions, conditional conformance, and tuples.
sidebar:
  order: 4
---

### Declaration
Types are user-defined composite types containing named fields. Use `var` for mutable fields and `let` for immutable fields:

```maxon
type Point
		var x as int
		var y as int
end 'Point'
```

### Type Literals
Create type instances using field initializers:

```maxon
var p = Point{x: 10, y: 20}
var origin = Point{x: 0, y: 0}
```

### Required Field Initialization

Every field of a type must be initialized when the type is constructed. A field counts as initialized if any of the following is true:

1. **The declaration supplies a default value**: `var count = 0`. Two forms are accepted:
   - **Shorthand** — `var name = literal`. The type is inferred from the literal, which must be an integer, float, `true`/`false`, or an enum case (`Priority.low`).
   - **Full form** — `var name Type = expression`. The type annotation is required whenever the default is something other than a literal. The expression can be any valid expression and is re-evaluated at every struct literal that omits the field: `var items IntArray = IntArray.create()`, `var origin Point = Point.create(0, y: 0)`.
2. **The literal provides the field**: `Counter{count: 5}`. A value provided here always wins over a declared default (the default expression is not evaluated when the field is provided).
3. **The literal is the direct return expression of a `static` factory** whose return type is the enclosing type, and the field is assigned via `self.field = expr` on every control-flow path that reaches the literal. In that case the field can be omitted from the literal. Prefer rule 1 (field default) when the value doesn't depend on factory parameters; reach for rule 3 when the default needs access to `create`'s arguments.

A `Self{}` literal is only legal when every field has a default or is supplied via rule 3. Otherwise the compiler emits **E3086 `SemanticFieldNotInitialized`** listing the uninitialized fields.

```maxon
type Counter
	export var value as Integer
	export var version = 0           // default

	export static function create(initial Integer) returns Self
		self.value = initial          // proof of initialization (rule 3)
		return Self{}                 // OK: value proven by self-assign; version defaulted
	end 'create'
end 'Counter'
```

The self-assignment form requires **definite assignment**: the write must reach the return on every control-flow path. A write in only one branch of an `if/else`, or only inside a loop body (which may execute zero times), is not sufficient and triggers E3086.

### Field Access
Access fields using dot notation:

```maxon
var p = Point{x: 10, y: 20}
var xVal = p.x           // Read field
p.x = 15                 // Write field (if var, not let)
```

### Methods

Methods are defined **inside the type body** and can access fields directly (implicit `self`):

```maxon
type Point
		var x as int
		var y as int

		function add(other Point) returns Point
				return Point{x: x + other.x, y: y + other.y}
		end 'add'

		export function magnitude() returns float
				return sqrt((x * x + y * y) as Real)
		end 'magnitude'
end 'Point'
```

**Method Syntax Rules:**
- Methods must be declared inside the type body
- Methods access type fields directly without explicit `self`
- Use `export` keyword before `function` to export individual methods
- Methods are called using dot notation: `instance.method(args)`
- A local declaration inside an instance method (`let`/`var`, parameter, match-pattern binding, tuple destructure, for-in loop variable, try/otherwise error binding) MUST NOT collide with a self-field name. Such a shadow is rejected at parse time with **E3006** because reads and writes of the local would silently route through `self.field` and produce type confusion when the local's type differs from the field's type.
- `self` is a reserved identifier and cannot be bound by user code in any declaration (parameter, `let`/`var`, `for`-in variable, function name, type name, etc.). Lexer-strict positions reject it with **E2010**; positions that accept keyword-shaped names reject it with **E2051**. This prevents accidental shadowing of the implicit receiver.

### Calling Methods

Methods are called using dot notation on instances. The receiver (`self`) is implicit:

```maxon
var p1 = Point{x: 10, y: 20}
var p2 = Point{x: 5, y: 10}
var p3 = p1.add(p2)
var mag = p1.magnitude()
```

#### Sibling Method Calls

Inside a type body, instance methods can call other instance methods on `self` using a bare name (no explicit receiver):

```maxon
type Calculator
	var base as Integer

	function double() returns Integer
		return base * 2
	end 'double'

	function quadruple() returns Integer
		return double() * 2    // bare call — implicitly calls self.double()
	end 'quadruple'
end 'Calculator'
```

The compiler detects that `double` is an instance method of the current type and automatically prepends `self` as the receiver.

### Static Methods

Static methods belong to a type but don't have access to instance data. They are declared with the `static` keyword and called using `TypeName.method()` syntax:

```maxon
type Point
		var x as int
		var y as int

		static function origin() returns Point
				return Point{x: 0, y: 0}
		end 'origin'

		static function create(x int, y int) returns Point
				return Point{x: x, y: y}
		end 'create'

		function magnitude() returns float
				return sqrt((x * x + y * y) as Real)
		end 'magnitude'
end 'Point'
```

**Calling Static Methods:**
```maxon
var p1 = Point.origin()           // Static method call
var p2 = Point.create(10, y: 20)  // First positional, second named
var mag = p2.magnitude()          // Instance method call
```

**Static Method Rules:**
- Declared with `static function` inside a type body
- No implicit `self` parameter - cannot access instance fields
- Called on the type name, not on instances: `TypeName.method()`
- Can be exported with `export static function`
- Commonly used for factory methods and utility functions

**Differences from Instance Methods:**

| Feature | Instance Method | Static Method |
|---------|----------------|---------------|
| Has `self` | Yes (implicit) | No |
| Can access fields | Yes | No |
| Call syntax | `instance.method()` | `Type.method()` |
| Declaration | `function name()` | `static function name()` |

### Static Fields

Static fields are shared across all instances of a type. They are declared using `static var` (mutable) or `static let` (immutable):

```maxon
type Counter
		static var count = 0
		static let MAX_COUNT = 1000

		var id as int

		static function create() returns Counter
				Counter.count = Counter.count + 1
				return Counter{id: Counter.count}
		end 'create'
end 'Counter'
```

**Accessing Static Fields:**
```maxon
var c1 = Counter.create()    // Counter.count becomes 1
var c2 = Counter.create()    // Counter.count becomes 2
print(Counter.count)         // Prints: 2
print(Counter.MAX_COUNT)     // Prints: 1000
```

**Static Field Rules:**
- Declared with `static var` or `static let` inside a type body
- Must have an initializer value (no uninitialized static fields)
- Accessed using `TypeName.fieldName` syntax (not instance syntax)
- `static var` fields can be reassigned; `static let` fields are immutable

**Initialization behavior** depends on the initializer expression:
- **Constant initializers** (integer, float, bool literals) are evaluated at compile time
- **Complex initializers** (function calls, struct literals, array literals) are evaluated **lazily on first access** -- see [Lazy Static Initializers](#lazy-static-initializers) below

**Differences from Instance Fields:**

| Feature | Instance Field | Static Field |
|---------|---------------|--------------|
| Storage | Per instance | One per type |
| Access | `instance.field` | `Type.field` |
| Declaration | `var field type` | `static var field = value` |
| Requires initializer | No (can use type default) | Yes |

### Lazy Static Initializers

Static fields initialized with complex expressions -- function calls, struct literals, or array literals -- are evaluated lazily. The initializer runs the first time the field is accessed, and the result is cached for all subsequent accesses.

```maxon
typealias Tally = int(0 to u64.max)

type Config
		static var instance = Config.create()

		static function _create() returns Config
				return Config{value: 42}
		end '_create'

		export var value as Tally

		export static function instance() returns Config
				return Config.instance   // initializer runs on first call only
		end 'instance'
end 'Config'
```

**Lazy initialization guarantees:**
- The initializer executes exactly once, on the first access to the static field
- Subsequent accesses return the cached value without re-evaluating the initializer
- `static var` fields can be reassigned after initialization; the initializer does not run again
- `static let` fields are immutable after initialization (planned)
- Constant initializers (integer, float, bool literals) remain compile-time constants and are not lazy

**Common patterns:**

Caching expensive computations:
```maxon
type WSCache
		static var ws = CharacterSet.whitespacesAndNewlines()

		export static function isWhitespace(c Character) returns bool
				return WSCache.ws.contains(c)
		end 'isWhitespace'
end 'WSCache'
```

Struct literal defaults:
```maxon
typealias Coord = int(0 to u64.max)

type Point
		export var x as Coord
		export var y as Coord
end 'Point'

type Defaults
		static var origin = Point{x: 0, y: 0}

		export static function getOrigin() returns Point
				return Defaults.origin
		end 'getOrigin'
end 'Defaults'
```

Array literal initialization:
```maxon
typealias Integer = int(i64.min to i64.max)

type Lookup
		static var values = [10, 20, 30]

		export static function get(index Integer) returns Integer
				return try Lookup.values.get(index) otherwise -1
		end 'get'
end 'Lookup'
```

Multiple lazy statics in the same type are each initialized independently on first access:
```maxon
typealias Tally = int(0 to u64.max)

type Cache
		static var a = Cache.buildA()
		static var b = Cache.buildB()
		export var n as Tally

		static function _buildA() returns Cache
				return Cache{n: 10}
		end '_buildA'

		static function _buildB() returns Cache
				return Cache{n: 20}
		end '_buildB'
end 'Cache'
```

### Interfaces

Interfaces define a set of method signatures that types can implement:

```maxon
interface Hashable
		function hash() returns int
end 'Hashable'
```

Structs declare conformance using the `implements` keyword:

```maxon
type Point implements Hashable
		var x as int
		var y as int

		function hash() returns int
				return x + y * 31
		end 'hash'
end 'Point'
```

**Static Interface Methods**

Interfaces can declare static methods using the `static` keyword. Static interface methods don't receive an implicit `self` parameter and are typically used for factory methods:

**Interface Notes:**
- `Self` in interface method parameters/returns refers to the conforming type
- A type can conform to multiple interfaces: `type Foo implements A, B`
- Methods implementing interface requirements follow the same syntax as regular methods
- Static interface methods use `static function method()` syntax in implementations
- One interface can extend another with `interface Derived extends Base`. A type that lists `implements Derived` must declare every method from `Derived` and every method transitively inherited from `Base`; missing methods inherited via `extends` are reported with a `(from BaseName)` suffix in the diagnostic

**Interface-Typed Parameters**

Functions can use interface types directly as parameter types. Any concrete type that implements the interface can be passed as an argument:

```maxon
interface Drawable
	function draw() returns Integer
end 'Drawable'

function render(item Drawable) returns Integer
	return item.draw()
end 'render'
```

The compiler monomorphizes the function at compile time, creating specialized copies for each concrete type used at call sites. This provides static dispatch with no runtime overhead. If the argument's type does not implement the required interface, a compile error is reported. Interface inheritance is respected: a type implementing a derived interface also satisfies parameters typed with the base interface.

**Interface-Typed Return Values**

Functions can declare an interface as their return type. When every `return` in the body yields the same concrete implementing type, the compiler statically infers that type at the call site so chained method dispatch on the result resolves without runtime overhead:

```maxon
interface Producer
	function produce() returns Integer
end 'Producer'

type Widget implements Producer
	let value as Integer
	function produce() returns Integer
		return value
	end 'produce'
end 'Widget'

function makeProducer() returns Producer
	return Widget{value: 42}
end 'makeProducer'
```

**Interface-Typed Fields**

Struct fields can declare an interface as their type. The field stores any value of a type that conforms to the interface, and methods invoked on the field dispatch to the implementing type. When the compiler can trace the concrete type stored into the field at construction, dispatch is resolved statically with no runtime overhead:

```maxon
interface Tagged
	function tag() returns Integer
end 'Tagged'

type Holder
	export let t as Tagged

	static function create(t Tagged) returns Self
		return Self{t: t}
	end 'create'
end 'Holder'
```

### Where Clauses (Type Parameter Constraints)

The `where` clause constrains type parameters to require specific interface conformance. This enables the compiler to verify method calls on type parameters and to reject concrete types that don't satisfy the constraints.

```maxon
type Map uses Key, Value implements BuiltinDictionaryLiteral where Key is Hashable
		// Key is guaranteed to have hash() method
end 'Map'
```

Multiple interfaces on the same parameter use `and`:

```maxon
type Container uses T where T is Hashable and Equatable
```

Multiple constrained parameters use comma separation:

```maxon
type Pair uses A, B where A is Hashable, B is Cloneable
```

When creating a type alias, the compiler checks that concrete types satisfy the constraints:

```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringMap = Map with (String, Integer)  // OK: String implements Hashable
```

### Per-Instance Typealiases

A ranged typealias declared inside a generic type body produces a nominally distinct type for each concrete instantiation. This prevents accidentally mixing values between different instances of the same generic type.

```maxon
type Pool uses T
	export typealias Idx = int(0 to u64.max)

	export function push(item T) returns Idx
		// ...
	end 'push'

	export function get(index Idx) returns T
		// ...
	end 'get'
end 'Pool'

typealias Integer = int(i64.min to i64.max)
typealias PoolA = Pool with Integer
typealias PoolB = Pool with Integer
```

`PoolA.Idx` and `PoolB.Idx` are distinct types — passing one where the other is expected produces a compile error. Literal integers that fit the range are still accepted. To explicitly convert between compatible per-instance aliases, use `as`:

```maxon
let bIdx = aIdx as PoolB.Idx
```

Dot-syntax `as` casts are also supported:

```maxon
let idx = 0 as PoolA.Idx
```

### Interface Extensions

Extensions add methods to interfaces that are automatically available on all types conforming to that interface. Unlike regular interface methods that each conforming type must implement, extension methods have a single implementation that works for all conformers.

**Declaration:**

```maxon
extension Iterable
	function count() returns int
		var n = 0
		for _ in self 'loop'
			n = n + 1
		end 'loop'
		return n
	end 'count'
end 'Iterable'
```

**How Extensions Work:**
- The method becomes available on all types that conform to the interface
- The `self` keyword refers to the concrete type instance
- Extension methods can call any method required by the interface
- Associated types from the interface are resolved to the concrete type's bindings

**Using Associated Types:**

Extensions can use the interface's associated types. These are automatically substituted with the concrete type's associated type bindings:

```maxon
interface Container uses Element
	function get(index int) returns Element
end 'Container'

extension Container
	function first() returns Element
		return self.get(0)
	end 'first'
end 'Container'
```

When called on a type like `IntArray implements Container with Integer` (where `Integer` is a ranged typealias for `int`), the return type `Element` becomes `Integer`.

**Extension Method Synthesis:**

When a type conforms to an interface that has extensions, the compiler synthesizes concrete methods for that type. For example, if `IntArray` conforms to `Iterable`, calling `myArray.count()` invokes a method specialized for `IntArray`.

**Extension Rules:**
- Declared with `extension InterfaceName ... end 'InterfaceName'`
- Methods use `self` to access the conforming type instance
- Associated types resolve to the concrete type's bindings
- Extensions from parent interfaces are applied transitively

### Conditional Extensions

Extensions can include a `where` clause to restrict which conforming types receive the extension methods. Only types whose associated type bindings satisfy the constraints will have the methods synthesized.

**Syntax:**

```maxon
extension Iterable where Element is Equatable
	function contains(element Element) returns bool
		for item in self 'loop'
			if item == element 'found'
				return true
			end 'found'
		end 'loop'
		return false
	end 'contains'
end 'Iterable'
```

The `where` clause follows the same syntax as type-level where clauses: `where TypeParam is Interface`, with `and` for multiple interfaces on the same parameter.

**Behavior:**

When a type conforms to the extended interface, the compiler checks whether the type's associated type bindings satisfy the `where` constraints:
- If they do, the extension methods are synthesized for that type
- If they don't, the methods are silently skipped (no error)

For example, `Array with Integer` (where `Integer` is a ranged typealias for `int`) conforms to `Iterable`. Since `Integer` implements `Equatable`, the `contains` method is available on `Array with Integer`. A hypothetical `Array with SomeNonEquatableType` would not receive the `contains` method.

**Multiple Constraints:**

Multiple constraints on the same parameter use `and`:

```maxon
extension Container where Key is Hashable and Equatable
	// Methods available only when Key is both Hashable and Equatable
end 'Container'
```

**Mixing Unconditional and Conditional Extensions:**

An interface can have both unconditional extensions and conditional extensions. Types that don't satisfy the `where` clause still receive the unconditional extension methods:

```maxon
extension Seq
	function countItems() returns int
		var n = 0
		for _ in self 'loop'
			n = n + 1
		end 'loop'
		return n
	end 'countItems'
end 'Seq'

extension Seq where Element is Equatable
	function includes(target Element) returns bool
		for item in self 'loop'
			if item == target 'yes'
				return true
			end 'yes'
		end 'loop'
		return false
	end 'includes'
end 'Seq'
```

In this example, all types conforming to `Seq` get `countItems()`, but only those whose `Element` implements `Equatable` get `includes()`.

### Conditional Interface Conformance

Extensions can add interface conformance conditionally using both `implements` and `where` clauses. When a concrete type alias satisfies the `where` constraints, the type gains the declared interface conformance.

**Syntax:**

```maxon
extension Array implements Hashable, Equatable where Element is Hashable and Equatable
	function hash() returns HashValue
		// ...
	end 'hash'

	function equals(other Self) returns bool
		// ...
	end 'equals'
end 'Array'
```

**Behavior:**

When a concrete type alias is created (e.g., `typealias IntArr = Array with Integer`), the compiler checks whether the element type satisfies the `where` constraints. If `Integer` implements both `Hashable` and `Equatable`, then `IntArr` automatically conforms to `Hashable` and `Equatable`, enabling it to be used as a `Map` key or `Set` element.

This applies both to explicit `typealias` declarations and to auto-generated type aliases created during monomorphization.

---

## Tuples

Tuples are fixed-size, ordered collections of values with potentially different types. They use parenthesized syntax for both type annotations and literals.

### Tuple Literals

Create tuples using parenthesized, comma-separated expressions:

```maxon
var t = (10, 20)              // 2-element int tuple
var mixed = (42, "hello")     // int and String
var triple = (1, 2, 39)       // 3-element tuple
```

**Note:** A single parenthesized expression `(expr)` is NOT a tuple -- it is a parenthesized expression. Tuples require at least two elements.

### Element Access

Access tuple elements using positional dot syntax `.0`, `.1`, `.2`, etc.:

```maxon
var t = (10, 20)
t.0   // 10
t.1   // 20
```

### Field Assignment

Tuple fields are mutable and can be assigned individually:

```maxon
var t = (0, 0)
t.0 = 20
t.1 = 22
// t is now (20, 22)
```

### Tuple Types

In function parameters and return types, tuple types use parenthesized type lists:

```maxon
typealias Integer = int(i64.min to i64.max)

function sum(t (Integer, Integer)) returns Integer
	return t.0 + t.1
end 'sum'

function makePair(a Integer, b Integer) returns (Integer, Integer)
	return (a, b)
end 'makePair'
```

### Destructuring Declarations

Tuples can be destructured into new variables using `var` or `let`:

```maxon
var (x, y) = makePair(10, b: 32)   // x = 10, y = 32
let (a, b) = (10, 20)              // immutable bindings
```

Use `_` to discard individual elements:

```maxon
var (result, _) = compute()    // discard second element
var (_, status) = fetch()      // discard first element
```

If the function is pure, at least one element must be used:

```maxon
(_, _) = pureFunc()        // Error E3064: all elements discarded for pure function
```

### Tuple Assignment

Assign tuple values to **existing** mutable variables:

```maxon
var x = 0
var y = 0
(x, y) = makePair(10, b: 32)  // x = 10, y = 32
```

**Mixed declaration and assignment** -- combine existing variables with new declarations:

```maxon
var x = 0
(x, var y) = makePair(10, b: 32)     // x existing, y newly declared
(var a, var b) = makePair(10, b: 32)  // both newly declared
(x, let z) = makePair(3, b: 4)       // x existing, z immutable
```

**Discard elements:**

```maxon
(x, _) = makePair(42, b: 99)    // discard second element
```

**Rules:**
- All named variables (without `var`/`let`) must already be declared with `var`
- Immutable (`let`) variables cannot be reassignment targets (error E2013)
- The number of names must exactly match the tuple's element count (error E3005)
- Use `_` to discard individual elements
- If all elements are discarded and the function is pure, error E3064 is raised

### Destructuring in For Loops

Tuple destructuring works in `for` loops when the iterator yields tuples:

```maxon
var m = ["a": 1, "b": 2]
for (key, value) in m 'loop'
	print("{key}: {value}\n")
end 'loop'
```

Use `_` to discard loop variables:

```maxon
for (_, value) in m 'loop'
	sum = sum + value
end 'loop'
```

### Memory Semantics

- Tuples are heap-allocated structs with reference counting
- Each field occupies 8 bytes (primitives and pointers)
- Tuples containing managed types (strings, structs) have their reference counts managed automatically
- Tuples are assigned by reference (like structs). Use `.clone()` for an independent copy

---
