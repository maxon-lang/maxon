# Variables

A variable is a unit of Memory storage.

It is generally preferable to use const rather than var when declaring a variable. This causes less work for both humans and computers to do when reading code, and creates more optimization opportunities.

The extern keyword or @extern builtin function can be used to link against a variable that is exported from another object. The export keyword or @export builtin function can be used to make a variable available to other objects at link time. In both cases, the type of the variable must be C ABI compatible.

See also:

Exporting a C Library


## Identifiers 
Variable identifiers are never allowed to shadow identifiers from an outer scope.

~~~
function main() int
    var x = 1
    return x
end 'main'

ExitCode: 1
~~~

Identifiers must start with an alphabetic character or underscore and may be followed by any number of alphanumeric characters or underscores. They must not overlap with any keywords. See Keyword Reference.

~~~
function main() int
    var x = 4
    return x * 3
end 'main'

ExitCode: 12
~~~
