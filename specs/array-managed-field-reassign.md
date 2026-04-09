---
feature: array-managed-field-reassign
status: stable
keywords: [array, struct, enum, managed, refcount, reassign, field, destructor]
category: memory
---
# Array Field Reassignment with Managed Elements

## Documentation

When a struct field holding an array of managed elements is reassigned, the old array must be properly cleaned up. This involves:

1. Incrementing the new array's refcount
2. Decrementing the old array's refcount
3. If the old array's refcount reaches zero, its destructor runs:
   - Decrefs each element in the backing buffer
   - Each element's destructor decrefs its own managed fields (e.g., enum associated values containing Strings)
   - Frees the backing buffer

The critical invariant is that during destructor cleanup of the old array, the backing buffer and its elements must remain valid until all elements have been processed. A use-after-free can occur if the backing buffer is freed before all elements are decreffed.

## Tests

<!-- test: reassign-array-of-struct-with-string-enum -->
### Reassign Array Field Containing Structs with String Enums
Filters an array of structs (each containing an enum with String associated values)
into a new array, then reassigns the field. The old array's destructor must safely
decref all elements including their enum payloads.
```maxon
export union QueryKey
		sourceFile(path String)
		tokens(path String)
		allModule
		codeResult
end 'QueryKey'

export type Dependency
		export var dependent QueryKey
		export var dependency QueryKey

		static function create(dependent QueryKey, dependency QueryKey) returns Self
			return Self{dependent: dependent, dependency: dependency}
		end 'create'
end 'Dependency'

typealias DependencyArray = Array with Dependency

export type Database
		export var dependencies DependencyArray

		static function create(dependencies DependencyArray) returns Self
			return Self{dependencies: dependencies}
		end 'create'
end 'Database'

function clearDepsFor(db Database)
		var newDeps = DependencyArray.create()
		for dep in db.dependencies 'scan'
				match dep.dependent 'check'
						sourceFile(_) then newDeps.push(dep)
						tokens(_) then newDeps.push(dep)
						allModule then continue
						codeResult then newDeps.push(dep)
				end 'check'
		end 'scan'
		db.dependencies = newDeps
end 'clearDepsFor'

function main() returns ExitCode
		var deps = DependencyArray.create()
		deps.push(Dependency.create(dependent: QueryKey.allModule, dependency: QueryKey.sourceFile("test.maxon")))
		deps.push(Dependency.create(dependent: QueryKey.codeResult, dependency: QueryKey.tokens("test.maxon")))
		deps.push(Dependency.create(dependent: QueryKey.allModule, dependency: QueryKey.sourceFile("other.maxon")))
		deps.push(Dependency.create(dependent: QueryKey.sourceFile("a.maxon"), dependency: QueryKey.tokens("a.maxon")))
		let db = Database.create(dependencies: deps)
		clearDepsFor(db)
		return db.dependencies.count()
end 'main'
```
```exitcode
2
```

<!-- test: reassign-array-field-simple-managed -->
### Reassign Array Field with Simple Managed Structs
Array of structs with String fields, filtered and reassigned.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
		export var name String
		export var value Integer

		static function create(name String, value Integer) returns Self
			return Self{name: name, value: value}
		end 'create'
end 'Item'

typealias ItemArray = Array with Item

type Container
		export var items ItemArray

		static function create(items ItemArray) returns Self
			return Self{items: items}
		end 'create'
end 'Container'

function keepBigValues(c Container)
		var newItems = ItemArray.create()
		for item in c.items 'scan'
				if item.value > 3 'big'
						newItems.push(item)
				end 'big'
		end 'scan'
		c.items = newItems
end 'keepBigValues'

function main() returns ExitCode
		var items = ItemArray.create()
		items.push(Item.create(name: "alpha string long enough for heap allocation", value: 1))
		items.push(Item.create(name: "beta string long enough for heap allocation", value: 2))
		items.push(Item.create(name: "gamma string long enough for heap allocation", value: 3))
		items.push(Item.create(name: "delta string long enough for heap allocation", value: 4))
		items.push(Item.create(name: "epsilon string long enough for heap allocation", value: 5))
		let c = Container.create(items: items)
		keepBigValues(c)
		return c.items.count()
end 'main'
```
```exitcode
2
```

<!-- test: reassign-array-field-multiple-times -->
### Reassign Array Field Multiple Times
Repeatedly filter and reassign an array field to stress destructor cleanup.
```maxon
typealias Integer = int(i64.min to i64.max)

export union Tag
		name(s String)
		id(n Integer)
		none
end 'Tag'

type Entry
		export var tag Tag

		static function create(tag Tag) returns Self
			return Self{tag: tag}
		end 'create'
end 'Entry'

typealias EntryArray = Array with Entry

type Store
		export var entries EntryArray

		static function create(entries EntryArray) returns Self
			return Self{entries: entries}
		end 'create'
end 'Store'

function removeNone(store Store)
		var kept = EntryArray.create()
		for e in store.entries 'scan'
				match e.tag 'check'
						none then continue
						name(_) then kept.push(e)
						id(_) then kept.push(e)
				end 'check'
		end 'scan'
		store.entries = kept
end 'removeNone'

function main() returns ExitCode
		var entries = EntryArray.create()
		entries.push(Entry.create(tag: Tag.name("first long string for heap allocation purposes")))
		entries.push(Entry.create(tag: Tag.none))
		entries.push(Entry.create(tag: Tag.id(42)))
		entries.push(Entry.create(tag: Tag.none))
		entries.push(Entry.create(tag: Tag.name("second long string for heap allocation purposes")))
		entries.push(Entry.create(tag: Tag.none))
		let store = Store.create(entries: entries)
		removeNone(store)
		removeNone(store)
		removeNone(store)
		return store.entries.count()
end 'main'
```
```exitcode
3
```

<!-- test: reassign-empty-array-field -->
### Reassign to Empty Array
Replace a populated array with an empty one — all elements must be cleaned up.
```maxon
export union Key
		file(path String)
		module
end 'Key'

type Dep
		export var source Key
		export var target Key

		static function create(source Key, target Key) returns Self
			return Self{source: source, target: target}
		end 'create'
end 'Dep'

typealias DepArray = Array with Dep

type State
		export var deps DepArray

		static function create(deps DepArray) returns Self
			return Self{deps: deps}
		end 'create'
end 'State'

function clearAll(state State)
		state.deps = DepArray.create()
end 'clearAll'

function main() returns ExitCode
		var deps = DepArray.create()
		deps.push(Dep.create(source: Key.module, target: Key.file("a.maxon long enough for heap")))
		deps.push(Dep.create(source: Key.module, target: Key.file("b.maxon long enough for heap")))
		deps.push(Dep.create(source: Key.file("c.maxon long enough for heap"), target: Key.module))
		let state = State.create(deps: deps)
		clearAll(state)
		return state.deps.count()
end 'main'
```
```exitcode
0
```
