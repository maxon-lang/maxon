---
feature: string-split
status: experimental
keywords: [string, split, delimiter, tokenize]
category: types
---

# String Split

## Documentation

The `split` method divides a string into an array of substrings, separated by a delimiter string.

**Signature:** `split(delimiter: String) returns Array with String`

```text
var parts = "hello world foo".split(" ")   // ["hello", "world", "foo"]
var csv = "a,b,c".split(",")              // ["a", "b", "c"]
```

If the delimiter is not found, the result is a single-element array containing the original string. If the delimiter is empty, the original string is returned as a single-element array.

Consecutive delimiters produce empty strings in the result:

```text
var parts = "a,,b".split(",")   // ["a", "", "b"]
```

## Tests

<!-- test: split-by-space -->
```maxon
function main() returns ExitCode
  var s = "hello world foo"
  var parts = s.split(" ")
  print("{parts.count()}\n")
  for p in parts 'loop'
    print("{p}\n")
  end 'loop'
  return 0
end 'main'
```
```exitcode
0
```
```stdout
3
hello
world
foo
```

<!-- test: split-by-comma -->
```maxon
function main() returns ExitCode
  var s = "a,b,c"
  var parts = s.split(",")
  print("{parts.count()}\n")
  for p in parts 'loop'
    print("{p}\n")
  end 'loop'
  return 0
end 'main'
```
```exitcode
0
```
```stdout
3
a
b
c
```

<!-- test: split-no-match -->
```maxon
function main() returns ExitCode
  var s = "hello"
  var parts = s.split("xyz")
  print("{parts.count()}\n")
  for p in parts 'loop'
    print("{p}\n")
  end 'loop'
  return 0
end 'main'
```
```exitcode
0
```
```stdout
1
hello
```

<!-- test: split-empty-string -->
```maxon
function main() returns ExitCode
  var s = ""
  var parts = s.split(",")
  print("{parts.count()}\n")
  for p in parts 'loop'
    print("[{p}]\n")
  end 'loop'
  return 0
end 'main'
```
```exitcode
0
```
```stdout
1
[]
```

<!-- test: split-consecutive-delimiters -->
```maxon
function main() returns ExitCode
  var s = "a,,b"
  var parts = s.split(",")
  print("{parts.count()}\n")
  for p in parts 'loop'
    print("[{p}]\n")
  end 'loop'
  return 0
end 'main'
```
```exitcode
0
```
```stdout
3
[a]
[]
[b]
```

<!-- test: split-delimiter-at-start -->
```maxon
function main() returns ExitCode
  var s = ",hello"
  var parts = s.split(",")
  print("{parts.count()}\n")
  for p in parts 'loop'
    print("[{p}]\n")
  end 'loop'
  return 0
end 'main'
```
```exitcode
0
```
```stdout
2
[]
[hello]
```

<!-- test: split-delimiter-at-end -->
```maxon
function main() returns ExitCode
  var s = "hello,"
  var parts = s.split(",")
  print("{parts.count()}\n")
  for p in parts 'loop'
    print("[{p}]\n")
  end 'loop'
  return 0
end 'main'
```
```exitcode
0
```
```stdout
2
[hello]
[]
```

<!-- test: split-empty-delimiter -->
```maxon
function main() returns ExitCode
  var s = "hello"
  var parts = s.split("")
  print("{parts.count()}\n")
  for p in parts 'loop'
    print("{p}\n")
  end 'loop'
  return 0
end 'main'
```
```exitcode
0
```
```stdout
1
hello
```

<!-- test: split-multi-char-delimiter -->
```maxon
function main() returns ExitCode
  var s = "one::two::three"
  var parts = s.split("::")
  print("{parts.count()}\n")
  for p in parts 'loop'
    print("{p}\n")
  end 'loop'
  return 0
end 'main'
```
```exitcode
0
```
```stdout
3
one
two
three
```

<!-- test: split-only-delimiters -->
```maxon
function main() returns ExitCode
  var s = ",,"
  var parts = s.split(",")
  print("{parts.count()}\n")
  for p in parts 'loop'
    print("[{p}]\n")
  end 'loop'
  return 0
end 'main'
```
```exitcode
0
```
```stdout
3
[]
[]
[]
```
