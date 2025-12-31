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
- Interfaces defined in `stdlib/interfaces.maxon`
- Cast expression handling in `codegen_mir_expr.cpp` (around line 77-155)
- Compiler checks if target type conforms to `InitableFromStringLiteral` or `InitableFromCharLiteral`
- If conforming, generates a `_ManagedString` and calls `Type.init(managed)`

Key points:
- Uses cast syntax: `"hello" as MyType` or `'A' as MyCharType`
- The `init` method receives a `_ManagedString` containing the literal's UTF-8 bytes
- Built-in `string` and `character` types use optimized inline initialization
- User-defined types go through the interface's `init` method

`_ManagedString` layout:
- `_buffer`: pointer to UTF-8 byte data
- `_len`: byte length
- `_capacity`: 0 for constant data (no heap cleanup needed)

## Documentation

### InitableFromStringLiteral

Types conforming to `InitableFromStringLiteral` can be initialized from string literals using cast syntax:

```maxon
type MyString is InitableFromStringLiteral
    var _managed _ManagedString

    static function InitableFromStringLiteral.init(managed _ManagedString) returns MyString
        return MyString{_managed: managed}
    end 'init'

    export function len() returns int
        return __string_len(_managed)
    end 'len'
end 'MyString'

function main()
    var ms = "hello" as MyString
    print("{ms.len()}")
end 'main'
```
```exitcode
0
```
```stdout
5
```

### InitableFromCharLiteral

Types conforming to `InitableFromCharLiteral` can be initialized from character literals:

```maxon
type MyChar is InitableFromCharLiteral
    var _managed _ManagedString

    static function InitableFromCharLiteral.init(managed _ManagedString) returns MyChar
        return MyChar{_managed: managed}
    end 'init'

    export function len() returns int
        return __string_len(_managed)
    end 'len'
end 'MyChar'

function main()
    var mc = 'A' as MyChar
    print("{mc.len()}")
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
// User-defined type that wraps a string and can be created from string literals
type Wrapper is InitableFromStringLiteral
    var _managed _ManagedString

    static function InitableFromStringLiteral.init(managed _ManagedString) returns Wrapper
        return Wrapper{_managed: managed}
    end 'init'

    export function len() returns int
        return __string_len(_managed)
    end 'len'
end 'Wrapper'

function main()
    var w = "hello" as Wrapper
    print("{w.len()}")
end 'main'
```
```stdout
5
```

<!-- test: init-from-string-literal-empty -->
```maxon
type Wrapper is InitableFromStringLiteral
    var _managed _ManagedString

    static function InitableFromStringLiteral.init(managed _ManagedString) returns Wrapper
        return Wrapper{_managed: managed}
    end 'init'

    export function len() returns int
        return __string_len(_managed)
    end 'len'
end 'Wrapper'

function main()
    var w = "" as Wrapper
    print("len: {w.len()}")
end 'main'
```
```stdout
len: 0
```

<!-- test: init-from-char-literal-basic -->
```maxon
type CharWrapper is InitableFromCharLiteral
    var _managed _ManagedString

    static function InitableFromCharLiteral.init(managed _ManagedString) returns CharWrapper
        return CharWrapper{_managed: managed}
    end 'init'

    export function len() returns int
        return __string_len(_managed)
    end 'len'
end 'CharWrapper'

function main()
    var cw = 'X' as CharWrapper
    print("{cw.len()}")
end 'main'
```
```stdout
1
```

