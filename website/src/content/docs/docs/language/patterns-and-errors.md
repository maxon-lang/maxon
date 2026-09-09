---
title: Patterns & Common Errors
description: Common code patterns, compile-time and runtime error examples, and best practices for writing Maxon.
sidebar:
  order: 16
---

### Main Function Template
```maxon
function main() returns ExitCode
		// program logic
		return 0
end 'main'
```

### Loops with Break
```maxon
var i = 0
while true 'forever'
		if i >= 10 'done'
				break
		end 'done'
		print("{i}")
		i = i + 1
end 'forever'
```

### Array Iteration
```maxon
var numbers = [1, 2, 3, 4, 5]
for i in numbers 'iter'
		var num = try numbers.get(i) otherwise 0
		print("{num}")
end 'iter'
```

### Factorial Example
```maxon
function factorial(n int) returns int
		if n <= 1 'base'
				return 1
		end 'base'
		return n * factorial(n: n - 1)
end 'factorial'

function main() returns ExitCode
		var result = factorial(n: 5)
		print("{result}")  // 120
		return 0
end 'main'
```

## Compile-Time & Runtime Errors

### Compile-Time Errors

**Type Errors**
```maxon
var x = 5 + "string"    // ERROR: Type mismatch
```

**Missing Return**
```maxon
function test() returns int
		var x = 5
		// ERROR: Missing return statement
end 'test'
```

**Immutable Assignment**
```maxon
let x = 5
x = 10                  // ERROR: Cannot assign to immutable variable
```

**Self-Assignment**
```maxon
var x = 5
x = x                   // ERROR: self-assignment has no effect
```

**Borrow Conflict**
```maxon
var arr = ["hello"]
var s = try arr.get(0) otherwise ""
arr.push("world")       // ERROR E3070: cannot mutate 'arr' while borrowed by 's'
```

**Var Never Reassigned**
```maxon
var x = 10
return x                // ERROR E3077: variable 'x' is never reassigned; use 'let' instead of 'var'
```

**Var From Immutable**
```maxon
let a = Point.create(x: 1, y: 2)
var b = a               // ERROR E3078: cannot assign immutable variable 'a' to mutable binding 'b'
```

**Useless Discard**
```maxon
_ = 42              // ERROR: discarding a non-call expression has no effect
```

**Unknown Keyword**
```maxon
functon test()          // ERROR: Unknown keyword 'functon'
```

**Mismatched Block Identifiers**
```maxon
if x > 0 'check'
		print("{x}")
end 'wrong'             // ERROR: Expected 'check', got 'wrong'
```

**Empty Block**
```maxon
if x > 0 'check'
end 'check'             // ERROR E3082: empty block
```
Every `if`, `else`, `while`, `for`, and `try...otherwise` block must contain at least one statement. Comment-only blocks are also considered empty since comments are not statements.

### Runtime Behavior

**Caught at runtime (clean panic, exit 1)**

CPU faults — division (or modulo) by zero, null pointer dereference, and stack overflow — are caught by the runtime safety handler, which writes a `panic: ...` line to stderr and exits with status 1.

**Undefined Behavior (no error)**
- Array out-of-bounds access
- Wrap-around on regular signed/unsigned integer arithmetic

## Best Practices for AI Agents

1. **Always match block identifiers**: `if x > 0 'check'` must end with `end 'check'`

2. **Initialize all variables**: No uninitialized variables allowed

3. **Use clear initializers**: Type is always inferred from the value
   ```maxon
	 var count = 0    // Type inferred as int
   ```

4. **Return from all code paths**:
   ```maxon
	 function test(x int) returns int
			 if x > 0 'pos'
					 return 1
			 end 'pos'
			 return -1        // Don't forget this
	 end 'test'
   ```

5. **Remember int/float distinction**:
   ```maxon
	 var x = 5           // int
	 var y = 5.0         // float (note decimal point)
   ```

7. **Prefer `let` for immutability**:
   ```maxon
	 let pi = 3.14159    // Prevents accidental modification
   ```

8. **Export only necessary functions**:
   ```maxon
	 export returns int    // Public API
	 returns int      // Private helper
   ```

9. **Handle array access errors**:
   ```maxon
	 // Use otherwise for safe access with default
	 var val = try arr.get(index) otherwise 0

	 // Or check bounds first
	 if index < arr.count() 'safe'
			 var val = try arr.get(index) otherwise 0
	 end 'safe'
   ```

10. **Use meaningful block identifiers**:
    ```maxon
		while not done 'process'     // 'process' describes the loop
				// ...
		end 'process'
    ```

11. **First argument positional, rest named**:
    ```maxon
		greet("Alice")                        // Single param is positional
		connect("localhost", port: 8080)      // First positional, rest named
		move(start, end: end)                 // First positional, rest named
    ```

12. **Parameters with defaults can be omitted**:
    ```maxon
		function greet(name String, title String = "Mr.")
		greet("Smith")                // Uses default
		greet("Smith", title: "Dr.")  // Override default
    ```

13. **Use `try otherwise` for error handling**:
    ```maxon
		let value = try mayFail() otherwise 42  // Default value on error
		try cleanup() otherwise ignore          // Ignore errors in cleanup

		// Use block handler for complex error handling
		try loadData() otherwise 'err'
				logError("Failed to load data")
				useDefaults()
		end 'err'
    ```

14. **Propagate errors with `try` in throwing functions**:
    ```maxon
		function process() returns Result throws WorkflowError
				let data = try loadData()  // Propagates error to caller
				return transform(data)
		end 'process'
    ```
