---
feature: challenge-array-of-structs
status: stable
keywords: array, struct, elements, memory
category: semantics
---
# Challenge Array Of Structs

## Documentation

## Arrays of Structs

Arrays can contain struct values. Each element is a complete copy of the struct.

## Tests

<!-- test: array-of-structs-literal -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
  export var x Integer
  export var y Integer
end 'Point'

function main() returns ExitCode
  var p1 = Point{x: 1, y: 2}
  var p2 = Point{x: 3, y: 4}
  var points = [p1, p2]
  var pt0 = try points.get(0) otherwise Point{x: 0, y: 0}
  var pt1 = try points.get(1) otherwise Point{x: 0, y: 0}
  return pt0.x + pt1.y
end 'main'
```
```exitcode
5
```

<!-- test: array-of-structs-indexed-access -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Pair
  export var first Integer
  export var second Integer
end 'Pair'

function main() returns ExitCode
  var p = Pair{first: 10, second: 20}
  var arr = [p]
  var elem = try arr.get(0) otherwise Pair{first: 0, second: 0}
  return elem.first + elem.second
end 'main'
```
```exitcode
30
```

<!-- test: array-of-structs-with-enum-field -->
```maxon
// Regression test: structs with enum fields stored correctly in arrays
// Previously, 8-byte structs were stored by pointer instead of by value
enum Color
  red
  green
  blue
end 'Color'

type Item
  export var color Color
end 'Item'

typealias ItemArray = Array with Item

function main() returns ExitCode
  var items = ItemArray{}
  items.push({color: Color.red})
  items.push({color: Color.green})
  items.push({color: Color.blue})

  // Verify enum values are stored correctly (not pointers)
  var item0 = try items.get(0) otherwise Item{color: Color.blue}
  var item1 = try items.get(1) otherwise Item{color: Color.blue}
  var item2 = try items.get(2) otherwise Item{color: Color.red}

  // red=0, green=1, blue=2
  return item0.color.rawValue + item1.color.rawValue * 10 + item2.color.rawValue * 100
end 'main'
```
```exitcode
210
```

<!-- test: array-of-structs-enum-for-in-loop -->
```maxon
// Regression test: enum comparison works in for-in loop over struct array
enum Status
  pending
  active
  done
end 'Status'

type Task
  export var status Status
end 'Task'

typealias TaskArray = Array with Task

function main() returns ExitCode
  var tasks = TaskArray{}
  tasks.push({status: Status.pending})
  tasks.push({status: Status.active})
  tasks.push({status: Status.done})

  var activeCount = 0
  for task in tasks 'loop'
    if task.status == Status.active 'check'
      activeCount = activeCount + 1
    end 'check'
  end 'loop'

  return activeCount
end 'main'
```
```exitcode
1
```
