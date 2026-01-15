---
feature: init-from-literal
status: stable
keywords: [InitableFromStringLiteral, InitableFromCharLiteral, literals, interface, cast]
category: type-system
---

# InitableFrom*Literal Interfaces

## Documentation

### InitableFromStringLiteral

Types conforming to `InitableFromStringLiteral` can be initialized from string literals using cast syntax. The `init` method receives a `__ManagedArray` containing the UTF-8 bytes:

```maxon
type MyString is InitableFromStringLiteral
    var _value String

    static function InitableFromStringLiteral.init(value __ManagedArray) returns MyString
        return MyString{_value: String.init(value)}
    end 'init'

    export function len() returns int
        return _value.byteLength()
    end 'len'
end 'MyString'

function main() returns int
    var ms = "hello" as MyString
    print("{ms.len()}\n")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```

### InitableFromCharLiteral

Types conforming to `InitableFromCharLiteral` can be initialized from character literals. The `init` method receives a `__ManagedArray` containing the UTF-8 bytes:

```maxon
type MyChar is InitableFromCharLiteral
    var _value Character

    static function InitableFromCharLiteral.init(value __ManagedArray) returns MyChar
        return MyChar{_value: Character.init(value)}
    end 'init'

    export function len() returns int
        return _value.byteLength()
    end 'len'
end 'MyChar'

function main() returns int
    var mc = 'A' as MyChar
    print("{mc.len()}\n")
    return 0
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

    static function InitableFromStringLiteral.init(value __ManagedArray) returns Wrapper
        return Wrapper{_value: String.init(value)}
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

    static function InitableFromStringLiteral.init(value __ManagedArray) returns Wrapper
        return Wrapper{_value: String.init(value)}
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

    static function InitableFromCharLiteral.init(value __ManagedArray) returns CharWrapper
        return CharWrapper{_value: Character.init(value)}
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

