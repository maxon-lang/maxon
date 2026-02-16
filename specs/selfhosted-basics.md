---
feature: selfhosted-basics
status: stable
keywords: [selfhosted, basics]
category: selfhosted
---

## Tests

<!-- test: return-42 -->
```maxon
function main() returns ExitCode
  return 42
end 'main'
```
```exitcode
42
```

<!-- test: return-0 -->
```maxon
function main() returns ExitCode
  return 0
end 'main'
```
```exitcode
0
```

<!-- test: print-hello -->
```maxon
function main() returns ExitCode
  print("hello\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```
