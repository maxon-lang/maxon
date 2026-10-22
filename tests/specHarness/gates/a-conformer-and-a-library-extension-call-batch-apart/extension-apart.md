---
feature: harness-gate-a-conformer-and-a-library-extension-call-batch-apart
---
# The a-conformer-and-a-library-extension-call-batch-apart gate

Two cases declare an `Iterable` conformer whose cursor type is file-private, and two call a library `Iterable` extension method (`map`, `filter`) on an `Array`. Each compiles alone, but one program holding both kinds is refused (E3008: the extension's body is expanded over every conformer, and it names the cursor of a conformer the library cannot see), so the four run batched in two programs.

## Tests

<!-- test: extension-apart.walks-a-private-cursor -->

```maxon
typealias Int = int(i64.min to i64.max)

type BagIter uses E implements Iterator with Int
	typealias EArray = Array with E
	var items as EArray
	var i as Int

	static function create() returns Self
		return Self{items: EArray.create(), i: 3}
	end 'create'

	function current() returns Int
		return i
	end 'current'

	function advance() throws IterationError
		i = i - 1

		if i <= 0 'exhausted'
			throw IterationError.exhausted
		end 'exhausted'
	end 'advance'
end 'BagIter'

type Bag uses Element implements Iterable with (Int, BIter)
	export typealias BIter = BagIter with Element
	var n as Int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function createIterator() returns BIter throws IterationError
		return BIter.create()
	end 'createIterator'

	function walk() returns Int
		var total = 0 as Int

		for x in self 'loop'
			total = total + x
		end 'loop'

		return total
	end 'walk'
end 'Bag'

typealias IntBag = Bag with Int

function main() returns ExitCode
	var b = IntBag.create()
	return b.walk()
end 'main'
```

```exitcode
6
```

<!-- test: extension-apart.maps-an-array -->

```maxon
typealias Int = int(i64.min to i64.max)
typealias Ints = Array with Int

function main() returns ExitCode
	var a = Ints.create()
	a.push(1)
	a.push(2)
	let xs = a.map(function(n Int) gives n + 1)
	print("{xs.count()}\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
2
```

<!-- test: extension-apart.walks-another-private-cursor -->

```maxon
typealias Int = int(i64.min to i64.max)

type TrayIter uses E implements Iterator with Int
	typealias EArray = Array with E
	var items as EArray
	var i as Int

	static function create() returns Self
		return Self{items: EArray.create(), i: 2}
	end 'create'

	function current() returns Int
		return i
	end 'current'

	function advance() throws IterationError
		i = i - 1

		if i <= 0 'exhausted'
			throw IterationError.exhausted
		end 'exhausted'
	end 'advance'
end 'TrayIter'

type Tray uses Element implements Iterable with (Int, TIter)
	export typealias TIter = TrayIter with Element
	var n as Int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function createIterator() returns TIter throws IterationError
		return TIter.create()
	end 'createIterator'

	function walk() returns Int
		var total = 0 as Int

		for x in self 'loop'
			total = total + x
		end 'loop'

		return total
	end 'walk'
end 'Tray'

typealias IntTray = Tray with Int

function main() returns ExitCode
	var t = IntTray.create()
	return t.walk()
end 'main'
```

```exitcode
3
```

<!-- test: extension-apart.filters-an-array -->

```maxon
typealias Int = int(i64.min to i64.max)
typealias Ints = Array with Int

function main() returns ExitCode
	var a = Ints.create()
	a.push(1)
	a.push(2)
	a.push(3)
	let xs = a.filter(function(n Int) gives n > 1)
	print("{xs.count()}\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
2
```
