---
feature: float-register-pressure
status: stable
keywords: [float, xmm, register-allocator, rex, roundsd, sqrtsd, floor, ceil, round, encoding]
category: codegen
---

# Float register pressure

## Documentation

x64 has sixteen XMM registers, and the eight above `xmm7` are only addressable through the
REX prefix. That makes the encoding of a float instruction **operand-dependent**: the same
`floor` compiles to `66 0F 3A 0B` for `xmm0` and to `66 4x 0F 3A 0B` for `xmm8`, with the
mandatory `66` prefix BEFORE the REX byte rather than after it — an ordering the manual
requires and an encoder can easily get backwards.

Nothing pins that path unless a program holds enough floats live at once to force the
allocator past `xmm7`. Every other float case in the suite fits in `xmm0`–`xmm2`, so the
REX form is unreachable from them: this spec exists to reach it.

The value is verified against the bootstrap, which agrees at 64.

## Tests

### Eleven simultaneously live floats through every rounding intrinsic

Each `v0`…`v10` is defined before the sum and read by it, so all eleven are live at the
same point and the allocator must reach the REX-addressed half of the register file. The
intrinsics are spread across `floor`, `ceil`, `round` (the three `roundsd` modes) and
`sqrt` (`sqrtsd`), so the prefix is exercised on every float instruction that takes one.

<!-- test: eleven-live-floats-through-rounding-intrinsics -->
```maxon
typealias Wide = float(f64.min to f64.max)

function scatter(a Wide) returns Wide
	let v0 = floor(a + 0.5)
	let v1 = ceil(a + 1.5)
	let v2 = round(a + 2.5)
	let v3 = sqrt(a + 3.0)
	let v4 = floor(a + 4.5)
	let v5 = ceil(a + 5.5)
	let v6 = round(a + 6.5)
	let v7 = sqrt(a + 7.0)
	let v8 = floor(a + 8.5)
	let v9 = ceil(a + 9.5)
	let v10 = round(a + 10.5)
	return v0 + v1 + v2 + v3 + v4 + v5 + v6 + v7 + v8 + v9 + v10
end 'scatter'

function main() returns ExitCode
	return trunc(scatter(1.0))
end 'main'
```
```exitcode
64
```
