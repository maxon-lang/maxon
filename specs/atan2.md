---
feature: atan2
status: experimental
keywords: atan2, arctangent, trigonometry, math, radians
category: math-intrinsic
---
# atan2

## Documentation

Computes the arc tangent of y/x using the signs of both arguments to determine the quadrant of the result.

**Signature:** `Math.atan2(y float, x float) float`

**Parameters:**
- `y` - The y-coordinate (numerator)
- `x` - The x-coordinate (denominator)

**Returns:** The angle in radians between the positive x-axis and the point (x, y), in the range [-π, π]

**Example:**

```maxon
// Point on positive x-axis: angle = 0
var a = Math.atan2(0.0, x: 1.0)    // 0.0

// Point on positive y-axis: angle = π/2
var b = Math.atan2(1.0, x: 0.0)    // 1.5708 (≈ π/2)

// Point on negative x-axis: angle = π
var c = Math.atan2(0.0, x: -1.0)   // 3.14159 (≈ π)

// Point on negative y-axis: angle = -π/2
var d = Math.atan2(-1.0, x: 0.0)   // -1.5708 (≈ -π/2)
```

**Notes:**
- Unlike `atan(y/x)`, `atan2` correctly handles all quadrants
- `atan2(0.0, 0.0)` is implementation-defined (typically returns 0)
- Result is always in the range [-π, π]
- Useful for converting Cartesian coordinates to polar coordinates

## Tests

<!-- test: atan2.positive-x-axis -->
```maxon
function main() returns int
  var angle = Math.atan2(0.0, x: 1.0)
  if angle == 0.0 'check'
    return 0
  end 'check'
  return 1
end 'main'
```
```exitcode
0
```

<!-- test: atan2.positive-y-axis -->
```maxon
function main() returns int
  // π/2 ≈ 1.5708
  var angle = Math.atan2(1.0, x: 0.0)
  print("{angle}\n")
  // Should be approximately π/2
  if angle > 1.57 'check1'
    if angle < 1.58 'check2'
      return 0
    end 'check2'
  end 'check1'
  return 1
end 'main'
```
```exitcode
0
```
```stdout
1.570796
```

<!-- test: atan2.negative-x-axis -->
```maxon
function main() returns int
  // π ≈ 3.14159
  var angle = Math.atan2(0.0, x: -1.0)
  print("{angle}\n")
  // Should be approximately π
  if angle > 3.14 'check1'
    if angle < 3.15 'check2'
      return 0
    end 'check2'
  end 'check1'
  return 1
end 'main'
```
```exitcode
0
```
```stdout
3.141593
```

<!-- test: atan2.negative-y-axis -->
```maxon
function main() returns int
  // -π/2 ≈ -1.5708
  var angle = Math.atan2(-1.0, x: 0.0)
  print("{angle}\n")
  // Should be approximately -π/2
  if angle < -1.57 'check1'
    if angle > -1.58 'check2'
      return 0
    end 'check2'
  end 'check1'
  return 1
end 'main'
```
```exitcode
0
```
```stdout
-1.570796
```

<!-- test: atan2.first-quadrant -->
```maxon
function main() returns int
  // 45 degrees = π/4 ≈ 0.7854
  var angle = Math.atan2(1.0, x: 1.0)
  print("{angle}\n")
  return 0
end 'main'
```
```exitcode
0
```

<!-- test: atan2.third-quadrant -->
```maxon
function main() returns int
  // Third quadrant: -3π/4 ≈ -2.356
  var angle = Math.atan2(-1.0, x: -1.0)
  print("{angle}\n")
  return 0
end 'main'
```
```exitcode
0
```

<!-- test: atan2.origin -->
```maxon
function main() returns int
  var angle = Math.atan2(0.0, x: 0.0)
  print("{angle}\n")
  // Origin is typically 0
  if angle == 0.0 'check'
    return 0
  end 'check'
  return 1
end 'main'
```
```exitcode
0
```
```stdout
0.0
```
