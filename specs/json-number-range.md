---
feature: json-number-range
status: stable
keywords: [json, number, exponent, overflow, range, parse]
category: stdlib
---

# JSON numbers that leave the float range

## Documentation

`Json.parse` reads a number as a `JsonFloat`. A number whose value is too large for a `float` is refused
as `JsonError.invalidNumber`, the way Go's `strconv.ParseFloat` answers `ErrRange` and `encoding/json`
refuses to decode it. A number too small for a `float` reads as zero, as it does in Go, and a zero
mantissa stays zero under any exponent.

An exponent too long to count is not allowed to wrap around: `1e99999999999999999999` and
`1e18446744073709551616` are both out of range, and neither reads as a small number.

## Tests

<!-- test: json-number-range.an-exponent-past-the-float-range-is-refused -->
```maxon
function show(text String)
	let doc = try Json.parse(text) otherwise 'refused'
		print("{text}: refused\n")
		return
	end 'refused'

	print("{text}: {doc.get(doc.root).numberValue}\n")
end 'show'

function main() returns ExitCode
	show("1e5")
	show("2.5e3")
	show("1e999")
	show("1e99999999999999999999")
	show("1e18446744073709551616")
	show("-1e18446744073709551616")
	show("1e-99999999999999999999")
	show("0e99999999999999999999")
	return 0
end 'main'
```
```stdout
1e5: 100000.0
2.5e3: 2500.0
1e999: refused
1e99999999999999999999: refused
1e18446744073709551616: refused
-1e18446744073709551616: refused
1e-99999999999999999999: 0.0
0e99999999999999999999: 0.0
```
```exitcode
0
```

<!-- test: json-number-range.a-number-is-read-to-the-nearest-float -->
A number reads as the float nearest to its decimal value, whatever the digits and the exponent are made of: a mantissa of 310 digits under `e-300` is a billion, the smallest subnormal is read as itself rather than zero, and the largest float reads back as itself.

```maxon
function numberOf(text String) returns JsonFloat throws JsonError
	let doc = try Json.parse(text)
	return doc.get(doc.root).numberValue
end 'numberOf'

function check(label String, text String, expected JsonFloat)
	let value = try numberOf(text) otherwise 'refused'
		print("{label}: refused\n")
		return
	end 'refused'

	print("{label}: {value == expected}\n")
end 'check'

function main() returns ExitCode
	var wide = "1"

	for _ in 0 upto 309 'zeros'
		wide = "{wide}0"
	end 'zeros'

	check("a 310-digit mantissa under e-300", text: "{wide}e-300", expected: 1000000000.0)
	check("the smallest subnormal", text: "4.9e-324", expected: 5.0e-324)
	check("the largest float", text: "1.7976931348623157e308", expected: 1.7976931348623157e308)
	check("the lowest float", text: "-1.7976931348623157e308", expected: -1.7976931348623157e308)
	check("capital E", text: "1E5", expected: 100000.0)
	check("a plus sign on the exponent", text: "1e+5", expected: 100000.0)
	check("a negative fraction", text: "-12.5e-1", expected: -1.25)
	check("one past the largest float", text: "1e309", expected: 0.0)
	check("far past the largest float", text: "-1e999", expected: 0.0)
	return 0
end 'main'
```
```stdout
a 310-digit mantissa under e-300: true
the smallest subnormal: true
the largest float: true
the lowest float: true
capital E: true
a plus sign on the exponent: true
a negative fraction: true
one past the largest float: refused
far past the largest float: refused
```
```exitcode
0
```
