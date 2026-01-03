---
feature: init-from-literal
status: stable
keywords: [InitableFromStringLiteral, InitableFromCharLiteral, literals, interface, cast]
category: type-system
---

# InitableFrom*Literal Interfaces

## Developer Notes

The `InitableFromStringLiteral` and `InitableFromCharLiteral` interfaces allow user-defined types to be initialized from string and character literals using cast syntax.

Implementation:
- Interfaces defined in `stdlib/Interfaces.maxon`
- Compiler checks if target type conforms to `InitableFromStringLiteral` or `InitableFromCharLiteral`
- For `InitableFromStringLiteral`: compiler creates a `String` and passes it to `Type.init(value)`
- For `InitableFromCharLiteral`: compiler creates a `__ManagedString` and passes it to `Type.init(managed)`

Key points:
- Uses cast syntax: `"hello" as MyType` or `'A' as MyCharType`
- `InitableFromStringLiteral.init()` receives a `String` (following Swift's ExpressibleByStringLiteral pattern)
- `InitableFromCharLiteral.init()` receives a `__ManagedString` containing the character's UTF-8 bytes
- `String` itself is special-cased by the compiler (uses `Builtin.init` with `__ManagedString` directly)

## Documentation

### InitableFromStringLiteral

Types conforming to `InitableFromStringLiteral` can be initialized from string literals using cast syntax. The `init` method receives a `String`:

```maxon
type MyString is InitableFromStringLiteral
    var _value String

    static function InitableFromStringLiteral.init(value String) returns MyString
        return MyString{_value: value}
    end 'init'

    export function len() returns int
        return _value.byteLength()
    end 'len'
end 'MyString'

function main()
    var ms = "hello" as MyString
    print("{ms.len()}\n")
end 'main'
```
```exitcode
0
```
```stdout
5
```

### InitableFromCharLiteral

Types conforming to `InitableFromCharLiteral` can be initialized from character literals. The `init` method receives a `Character`:

```maxon
type MyChar is InitableFromCharLiteral
    var _value Character

    static function InitableFromCharLiteral.init(value Character) returns MyChar
        return MyChar{_value: value}
    end 'init'

    export function len() returns int
        return _value.byteLength()
    end 'len'
end 'MyChar'

function main()
    var mc = 'A' as MyChar
    print("{mc.len()}\n")
end 'main'
```
```exitcode
0
```
```stdout
1
```

## Tests

<!-- test: init-from-string-literal-basic -->
```maxon
// User-defined type that wraps a String and can be created from string literals
type Wrapper is InitableFromStringLiteral
    var _value String

    static function InitableFromStringLiteral.init(value String) returns Wrapper
        return Wrapper{_value: value}
    end 'init'

    export function len() returns int
        return _value.byteLength()
    end 'len'
end 'Wrapper'

function main()
    var w = "hello" as Wrapper
    print("{w.len()}\n")
end 'main'
```
```stdout
5
```

<!-- test: init-from-string-literal-empty -->
```maxon
type Wrapper is InitableFromStringLiteral
    var _value String

    static function InitableFromStringLiteral.init(value String) returns Wrapper
        return Wrapper{_value: value}
    end 'init'

    export function len() returns int
        return _value.byteLength()
    end 'len'
end 'Wrapper'

function main()
    var w = "" as Wrapper
    print("len: {w.len()}\n")
end 'main'
```
```stdout
len: 0
```

<!-- test: init-from-char-literal-basic -->
```maxon
type CharWrapper is InitableFromCharLiteral
    var _value Character

    static function InitableFromCharLiteral.init(value Character) returns CharWrapper
        return CharWrapper{_value: value}
    end 'init'

    export function len() returns int
        return _value.byteLength()
    end 'len'
end 'CharWrapper'

function main()
    var cw = 'X' as CharWrapper
    print("{cw.len()}\n")
end 'main'
```
```stdout
1
```

