---
feature: array-managed-multi-call-lifecycle
status: stable
keywords: [array, struct, enum, managed, refcount, push, reassign, lifecycle]
category: memory
---
# Array of Managed Structs: Multi-Call Lifecycle

## Documentation

When an array of managed structs is populated across multiple function calls, and then the field is reassigned, the refcount cleanup must handle all elements correctly. This tests the interaction between:

- Push with potential buffer realloc/grow
- Function-scope cleanup of temporary variables
- Array field reassignment triggering old array destructor
- `~ManagedElements` iterating freed backing buffer

## Tests

<!-- test: managed-array-push-across-calls -->
### Push managed elements across separate function calls, then reassign
Simulates the pattern from the query engine: recordDependency pushes elements
between clearDepsFor calls.
```maxon
export enum QueryKey
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

function addDep(db Database, dep Dependency)
		db.dependencies.push(dep)
end 'addDep'

function clearAllModule(db Database)
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
end 'clearAllModule'

function main() returns ExitCode
		var db = Database.create(dependencies: DependencyArray.create())

		// First cycle: add a dep, then clear
		addDep(db, dep: Dependency.create(dependent: QueryKey.sourceFile("a.maxon long enough for heap"), dependency: QueryKey.tokens("a.maxon long enough for heap")))
		clearAllModule(db)

		// Second cycle: add another dep (now array has 2 elements total)
		addDep(db, dep: Dependency.create(dependent: QueryKey.allModule, dependency: QueryKey.sourceFile("b.maxon long enough for heap")))
		clearAllModule(db)

		// Third cycle: add more deps, then clear for allModule
		addDep(db, dep: Dependency.create(dependent: QueryKey.allModule, dependency: QueryKey.sourceFile("c.maxon long enough for heap")))
		clearAllModule(db)

		return db.dependencies.count()
end 'main'
```
```exitcode
1
```

<!-- test: managed-array-grow-then-reassign -->
### Array grows past initial capacity, then field is reassigned
Push enough managed elements to trigger multiple buffer reallocations,
then reassign the array field so the old array's destructor must clean up all elements.
```maxon
typealias Integer = int(i64.min to i64.max)

export enum Tag
		label(name String)
		index(n Integer)
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

function addEntry(store Store, entry Entry)
		store.entries.push(entry)
end 'addEntry'

function replaceAll(store Store)
		store.entries = EntryArray.create()
end 'replaceAll'

function main() returns ExitCode
		var store = Store.create(entries: EntryArray.create())
		// Push enough to trigger multiple reallocations (initial cap is usually 4)
		addEntry(store, entry: Entry.create(tag: Tag.label("entry zero long enough for heap allocation")))
		addEntry(store, entry: Entry.create(tag: Tag.label("entry one long enough for heap allocation")))
		addEntry(store, entry: Entry.create(tag: Tag.label("entry two long enough for heap allocation")))
		addEntry(store, entry: Entry.create(tag: Tag.label("entry three long enough for heap allocation")))
		addEntry(store, entry: Entry.create(tag: Tag.label("entry four long enough for heap allocation")))
		addEntry(store, entry: Entry.create(tag: Tag.label("entry five long enough for heap allocation")))
		addEntry(store, entry: Entry.create(tag: Tag.label("entry six long enough for heap allocation")))
		addEntry(store, entry: Entry.create(tag: Tag.label("entry seven long enough for heap allocation")))
		addEntry(store, entry: Entry.create(tag: Tag.label("entry eight long enough for heap allocation")))
		replaceAll(store)
		return store.entries.count()
end 'main'
```
```exitcode
0
```

<!-- test: managed-array-interleaved-push-clear -->
### Interleaved push and clear cycles on same array field
Multiple rounds of push-then-reassign, each round the old array is freed.
```maxon
export enum Key
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

function record(state State, dep Dep)
		state.deps.push(dep)
end 'record'

function clearFor(state State)
		var kept = DepArray.create()
		for dep in state.deps 'scan'
				match dep.source 'check'
						module then continue
						file(_) then kept.push(dep)
				end 'check'
		end 'scan'
		state.deps = kept
end 'clearFor'

function main() returns ExitCode
		var state = State.create(deps: DepArray.create())

		record(state, dep: Dep.create(source: Key.file("x.maxon long enough for heap"), target: Key.module))
		clearFor(state)

		record(state, dep: Dep.create(source: Key.module, target: Key.file("y.maxon long enough for heap")))
		clearFor(state)

		record(state, dep: Dep.create(source: Key.module, target: Key.file("z.maxon long enough for heap")))
		clearFor(state)

		return state.deps.count()
end 'main'
```
```exitcode
1
```
