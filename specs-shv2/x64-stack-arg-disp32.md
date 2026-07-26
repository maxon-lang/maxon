---
feature: x64-stack-arg-disp32
status: stable
keywords: [function, parameters, calling-convention]
category: functions
---

# Many-Parameter Stack Argument Passing

## Documentation

The x64 calling convention this compiler uses passes the first seven integer
parameters in registers and spills the remainder onto the caller's stack, where
the callee reads them at positive `[rbp + N]` displacements (the first stack arg
sits at `[rbp + 0x10]`). The FIFTEENTH stack argument — the 22nd integer
parameter — lands at exactly `[rbp + 128]`.

A signed-byte (disp8) memory displacement only spans `-128..+127`, so a `+128`
displacement must be encoded with a 32-bit displacement (disp32). Encoding it as
a disp8 writes the byte `0x80`, which the CPU sign-extends to `-128`, silently
reading `[rbp - 128]` (a callee local slot) instead of the argument — a value
miscompile, not a crash. These tests pin correct value flow across, and past, the
disp8/disp32 boundary. Every parameter is summed so a corrupted load of any one
of them changes the result.

## Tests

<!-- test: twenty-second-param-at-rbp-128 -->
```maxon

typealias Integer = int(i64.min to i64.max)

// 22 integer parameters: 1-7 arrive in registers, 8-22 on the stack. The 22nd
// (a22) sits at [rbp + 128] — the exact disp8/disp32 boundary.
function sum22(a1 Integer, a2 Integer, a3 Integer, a4 Integer, a5 Integer, a6 Integer, a7 Integer, a8 Integer, a9 Integer, a10 Integer, a11 Integer, a12 Integer, a13 Integer, a14 Integer, a15 Integer, a16 Integer, a17 Integer, a18 Integer, a19 Integer, a20 Integer, a21 Integer, a22 Integer) returns Integer
	return a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12 + a13 + a14 + a15 + a16 + a17 + a18 + a19 + a20 + a21 + a22
end 'sum22'

function main() returns ExitCode
	return sum22(0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0, a13: 0, a14: 0, a15: 0, a16: 0, a17: 0, a18: 0, a19: 0, a20: 0, a21: 0, a22: 200)
end 'main'
```
```exitcode
200
```


<!-- test: params-straddling-rbp-128-boundary -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Sum the three parameters straddling the boundary: a20 at [rbp+112], a21 at
// [rbp+120], a22 at [rbp+128]. A miscompiled a22 load corrupts the total.
function sum22(a1 Integer, a2 Integer, a3 Integer, a4 Integer, a5 Integer, a6 Integer, a7 Integer, a8 Integer, a9 Integer, a10 Integer, a11 Integer, a12 Integer, a13 Integer, a14 Integer, a15 Integer, a16 Integer, a17 Integer, a18 Integer, a19 Integer, a20 Integer, a21 Integer, a22 Integer) returns Integer
	return a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12 + a13 + a14 + a15 + a16 + a17 + a18 + a19 + a20 + a21 + a22
end 'sum22'

function main() returns ExitCode
	return sum22(0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0, a13: 0, a14: 0, a15: 0, a16: 0, a17: 0, a18: 0, a19: 0, a20: 40, a21: 50, a22: 60)
end 'main'
```
```exitcode
150
```
