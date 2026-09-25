---
feature: url
status: experimental
keywords: [url, uri, parsing, rfc3986, scheme, host, port, path, query, fragment]
category: stdlib
---

# URL

## Documentation

The `URL` type provides RFC 3986 compliant URI parsing, serialization, and reference resolution.

### Parsing a URL

Use `URL.parse()` to decompose a URI string into its components:

```text
let url = try URL.parse("https://example.com:8080/path?q=1#top") otherwise 'err'
  // handle error
end 'err'
print(url.scheme())    // "https"
print(url.path())      // "/path"
```

### URL Components

- `scheme()` and `path()` are always available
- `host()`, `port()`, `userinfo()`, `query()`, `fragment()` throw `URLError.fieldNotPresent` if not set in the URL
- Use `try ... otherwise` to handle missing components:

```text
let port = try url.port() otherwise 443
let query = try url.query() otherwise ""
```

### Serialization

Use `toString()` to reconstruct a URL string from its components:

```text
let url = try URL.parse("https://example.com/path") otherwise return 1
print(url.toString())  // "https://example.com/path"
```

### Reference Resolution

Use `URL.resolve()` to resolve a relative reference against a base URL per RFC 3986 Section 5:

```text
let base = try URL.parse("http://a/b/c/d?q") otherwise return 1
let resolved = try URL.resolve(base, reference: "../g") otherwise return 1
print(resolved.toString())  // "http://a/b/g"
```

### Error Types

```text
enum URLError implements Error
  invalidScheme
  invalidHost
  invalidPort
  invalidEncoding
  emptyInput
  relativeWithoutBase
  fieldNotPresent
end 'URLError'
```

## Tests

### Parse Tests

<!-- test: url.parse-basic -->
```maxon
function main() returns ExitCode
	let url = try URL.parse("https://example.com") otherwise 'err'
		return 1
	end 'err'
	print("{url.scheme()}\n")
	print("{url.path()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
https

```

<!-- test: url.parse-path -->
```maxon
function main() returns ExitCode
	let url = try URL.parse("https://example.com/path/to/resource") otherwise 'err'
		return 1
	end 'err'
	print("{url.scheme()}\n")
	let host = try url.host() otherwise 'nohost'
		return 2
	end 'nohost'
	print("{host}\n")
	print("{url.path()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
https
example.com
/path/to/resource
```

<!-- test: url.parse-full -->
```maxon
function main() returns ExitCode
	let url = try URL.parse("https://example.com:8080/path?query=value#frag") otherwise 'err'
		return 1
	end 'err'
	print("{url.scheme()}\n")
	let host = try url.host() otherwise 'nh'
		return 2
	end 'nh'
	print("{host}\n")
	let port = try url.port() otherwise 'np'
		return 3
	end 'np'
	print("{port}\n")
	print("{url.path()}\n")
	let query = try url.query() otherwise 'nq'
		return 4
	end 'nq'
	print("{query}\n")
	let frag = try url.fragment() otherwise 'nf'
		return 5
	end 'nf'
	print("{frag}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
https
example.com
8080
/path
query=value
frag
```

<!-- test: url.parse-userinfo -->
```maxon
function main() returns ExitCode
	let url = try URL.parse("ftp://user:pass@ftp.example.com/files") otherwise 'err'
		return 1
	end 'err'
	print("{url.scheme()}\n")
	let ui = try url.userinfo() otherwise 'nui'
		return 2
	end 'nui'
	print("{ui}\n")
	let host = try url.host() otherwise 'nh'
		return 3
	end 'nh'
	print("{host}\n")
	print("{url.path()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
ftp
user:pass
ftp.example.com
/files
```

<!-- test: url.parse-ipv6 -->
```maxon
function main() returns ExitCode
	let url = try URL.parse("http://[::1]:8080/path") otherwise 'err'
		return 1
	end 'err'
	let host = try url.host() otherwise 'nh'
		return 2
	end 'nh'
	print("{host}\n")
	let port = try url.port() otherwise 'np'
		return 3
	end 'np'
	print("{port}\n")
	print("{url.path()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[::1]
8080
/path
```

<!-- test: url.parse-mailto -->
```maxon
function main() returns ExitCode
	let url = try URL.parse("mailto:user@example.com") otherwise 'err'
		return 1
	end 'err'
	print("{url.scheme()}\n")
	print("{url.path()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
mailto
user@example.com
```

<!-- test: url.parse-urn -->
```maxon
function main() returns ExitCode
	let url = try URL.parse("urn:isbn:0451450523") otherwise 'err'
		return 1
	end 'err'
	print("{url.scheme()}\n")
	print("{url.path()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
urn
isbn:0451450523
```

<!-- test: url.parse-scheme-lowercase -->
```maxon
function main() returns ExitCode
	let url = try URL.parse("HTTP://Example.COM/Path") otherwise 'err'
		return 1
	end 'err'
	print("{url.scheme()}\n")
	let host = try url.host() otherwise 'nh'
		return 2
	end 'nh'
	print("{host}\n")
	print("{url.path()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
http
Example.COM
/Path
```

<!-- test: url.parse-empty-query -->
```maxon
function main() returns ExitCode
	let url = try URL.parse("https://example.com/path?") otherwise 'err'
		return 1
	end 'err'
	let query = try url.query() otherwise 'nq'
		return 2
	end 'nq'
	print("query='{query}'\n")
	print("{url.path()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
query=''
/path
```

<!-- test: url.parse-empty-fragment -->
```maxon
function main() returns ExitCode
	let url = try URL.parse("https://example.com/path#") otherwise 'err'
		return 1
	end 'err'
	let frag = try url.fragment() otherwise 'nf'
		return 2
	end 'nf'
	print("fragment='{frag}'\n")
	print("{url.path()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
fragment=''
/path
```

<!-- test: url.parse-percent-encoding -->
```maxon
function main() returns ExitCode
	let url = try URL.parse("https://example.com/a%20b") otherwise 'err'
		return 1
	end 'err'
	print("{url.path()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
/a%20b
```

<!-- test: url.parse-port -->
```maxon
function main() returns ExitCode
	let url = try URL.parse("https://example.com:443/") otherwise 'err'
		return 1
	end 'err'
	let port = try url.port() otherwise 'np'
		return 2
	end 'np'
	print("{port}\n")
	print("{url.path()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
443
/
```

<!-- test: url.parse-file-uri -->
```maxon
function main() returns ExitCode
	let url = try URL.parse("file:///home/user/file.txt") otherwise 'err'
		return 1
	end 'err'
	print("{url.scheme()}\n")
	let host = try url.host() otherwise 'nh'
		return 2
	end 'nh'
	print("host='{host}'\n")
	print("{url.path()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
file
host=''
/home/user/file.txt
```

<!-- test: url.parse-scheme-chars -->
```maxon
function main() returns ExitCode
	let url = try URL.parse("custom+scheme.v2://host/path") otherwise 'err'
		return 1
	end 'err'
	print("{url.scheme()}\n")
	let host = try url.host() otherwise 'nh'
		return 2
	end 'nh'
	print("{host}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
custom+scheme.v2
host
```

<!-- test: url.parse-relative -->
```maxon
function main() returns ExitCode
	let url = try URL.parse("/relative/path?q=1#f") otherwise 'err'
		return 1
	end 'err'
	print("scheme='{url.scheme()}'\n")
	print("{url.path()}\n")
	let query = try url.query() otherwise 'nq'
		return 2
	end 'nq'
	print("{query}\n")
	let frag = try url.fragment() otherwise 'nf'
		return 3
	end 'nf'
	print("{frag}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
scheme=''
/relative/path
q=1
f
```

<!-- test: url.parse-protocol-relative -->
```maxon
function main() returns ExitCode
	let url = try URL.parse("//example.com/path") otherwise 'err'
		return 1
	end 'err'
	print("scheme='{url.scheme()}'\n")
	let host = try url.host() otherwise 'nh'
		return 2
	end 'nh'
	print("{host}\n")
	print("{url.path()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
scheme=''
example.com
/path
```

<!-- test: url.parse-ipv4 -->
```maxon
function main() returns ExitCode
	let url = try URL.parse("http://192.168.1.1:3000/api") otherwise 'err'
		return 1
	end 'err'
	let host = try url.host() otherwise 'nh'
		return 2
	end 'nh'
	print("{host}\n")
	let port = try url.port() otherwise 'np'
		return 3
	end 'np'
	print("{port}\n")
	print("{url.path()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
192.168.1.1
3000
/api
```

<!-- test: url.parse-userinfo-no-pass -->
```maxon
function main() returns ExitCode
	let url = try URL.parse("https://user@example.com/path") otherwise 'err'
		return 1
	end 'err'
	let ui = try url.userinfo() otherwise 'nui'
		return 2
	end 'nui'
	print("{ui}\n")
	let host = try url.host() otherwise 'nh'
		return 3
	end 'nh'
	print("{host}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
user
example.com
```

### Parse Error Tests

<!-- test: url.parse-err-empty -->
```maxon
function main() returns ExitCode
	try URL.parse("") otherwise 'err'
		print("emptyInput\n")
		return 0
	end 'err'
	return 1
end 'main'
```
```exitcode
0
```
```stdout
emptyInput
```

<!-- test: url.parse-err-whitespace -->
```maxon
function main() returns ExitCode
	try URL.parse("   ") otherwise 'err'
		print("emptyInput\n")
		return 0
	end 'err'
	return 1
end 'main'
```
```exitcode
0
```
```stdout
emptyInput
```

<!-- test: url.parse-err-port-overflow -->
```maxon
function main() returns ExitCode
	try URL.parse("https://example.com:99999/path") otherwise 'err'
		print("invalidPort\n")
		return 0
	end 'err'
	return 1
end 'main'
```
```exitcode
0
```
```stdout
invalidPort
```

<!-- test: url.parse-err-port-alpha -->
```maxon
function main() returns ExitCode
	try URL.parse("https://example.com:abc/path") otherwise 'err'
		print("invalidPort\n")
		return 0
	end 'err'
	return 1
end 'main'
```
```exitcode
0
```
```stdout
invalidPort
```

<!-- test: url.parse-err-bad-percent -->
```maxon
function main() returns ExitCode
	try URL.parse("https://example.com/path%GG") otherwise 'err'
		print("invalidEncoding\n")
		return 0
	end 'err'
	return 1
end 'main'
```
```exitcode
0
```
```stdout
invalidEncoding
```

<!-- test: url.parse-err-truncated-percent -->
```maxon
function main() returns ExitCode
	try URL.parse("https://example.com/path%2") otherwise 'err'
		print("invalidEncoding\n")
		return 0
	end 'err'
	return 1
end 'main'
```
```exitcode
0
```
```stdout
invalidEncoding
```

<!-- test: url.parse-err-colon-start -->
```maxon
function main() returns ExitCode
	try URL.parse("://missing-scheme") otherwise 'err'
		print("invalidScheme\n")
		return 0
	end 'err'
	return 1
end 'main'
```
```exitcode
0
```
```stdout
invalidScheme
```

<!-- test: url.parse-err-digit-scheme -->
```maxon
function main() returns ExitCode
	try URL.parse("1http://example.com") otherwise 'err'
		print("invalidScheme\n")
		return 0
	end 'err'
	return 1
end 'main'
```
```exitcode
0
```
```stdout
invalidScheme
```

<!-- test: url.parse-authority-is-read-to-its-grammar -->
```maxon
function verdict(spelled String) returns String
	let url = try URL.parse(spelled) otherwise (e) 'refused'
		return e.name
	end 'refused'

	let host = try url.host() otherwise "no-host"
	let port = try url.port() otherwise 0

	return "host={host} port={port}"
end 'verdict'

function main() returns ExitCode
	let spellings = [
		"http://[zz]:1/",
		"http://a b/",
		"http://:80/",
		"http://[::1]x/",
		"http://exa^mple.com/",
		"http://host:99999/",
		"http://host:8x/",
		"http://[::1]:8080/p",
		"http://host:/p",
		"http://exa%41mple.com/",
		"http://[v1.fe80::a+en1]/",
		"file:///etc/hosts"
	]

	for spelled in spellings 'eachSpelling'
		print("{spelled} -> {verdict(spelled)}\n")
	end 'eachSpelling'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
http://[zz]:1/ -> invalidHost
http://a b/ -> invalidHost
http://:80/ -> invalidHost
http://[::1]x/ -> invalidHost
http://exa^mple.com/ -> invalidHost
http://host:99999/ -> invalidPort
http://host:8x/ -> invalidPort
http://[::1]:8080/p -> host=[::1] port=8080
http://host:/p -> host=host port=0
http://exa%41mple.com/ -> host=exa%41mple.com port=0
http://[v1.fe80::a+en1]/ -> host=[v1.fe80::a+en1] port=0
file:///etc/hosts -> host= port=0
```

### toString Tests

<!-- test: url.tostring-basic -->
```maxon
function main() returns ExitCode
	let url = try URL.parse("https://example.com/path") otherwise 'err'
		return 1
	end 'err'
	print("{url.toString()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
https://example.com/path
```

<!-- test: url.tostring-full -->
```maxon
function main() returns ExitCode
	let url = try URL.parse("https://example.com:8080/?q=1#top") otherwise 'err'
		return 1
	end 'err'
	print("{url.toString()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
https://example.com:8080/?q=1#top
```

<!-- test: url.tostring-userinfo -->
```maxon
function main() returns ExitCode
	let url = try URL.parse("ftp://user:pass@ftp.example.com/files") otherwise 'err'
		return 1
	end 'err'
	print("{url.toString()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
ftp://user:pass@ftp.example.com/files
```

<!-- test: url.tostring-mailto -->
```maxon
function main() returns ExitCode
	let url = try URL.parse("mailto:user@example.com") otherwise 'err'
		return 1
	end 'err'
	print("{url.toString()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
mailto:user@example.com
```

<!-- test: url.tostring-ipv6 -->
```maxon
function main() returns ExitCode
	let url = try URL.parse("http://[::1]:8080/path") otherwise 'err'
		return 1
	end 'err'
	print("{url.toString()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
http://[::1]:8080/path
```

<!-- test: url.tostring-file -->
```maxon
function main() returns ExitCode
	let url = try URL.parse("file:///home/user/file.txt") otherwise 'err'
		return 1
	end 'err'
	print("{url.toString()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
file:///home/user/file.txt
```

### resolve Tests

<!-- test: url.resolve-relative -->
```maxon
function main() returns ExitCode
	let base = try URL.parse("http://a/b/c/d?q") otherwise 'err'
		return 1
	end 'err'
	let resolved = try URL.resolve(base, reference: "g") otherwise 'err2'
		return 2
	end 'err2'
	print("{resolved.toString()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
http://a/b/c/g
```

<!-- test: url.resolve-dot -->
```maxon
function main() returns ExitCode
	let base = try URL.parse("http://a/b/c/d?q") otherwise 'err'
		return 1
	end 'err'
	let resolved = try URL.resolve(base, reference: "./g") otherwise 'err2'
		return 2
	end 'err2'
	print("{resolved.toString()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
http://a/b/c/g
```

<!-- test: url.resolve-dotdot -->
```maxon
function main() returns ExitCode
	let base = try URL.parse("http://a/b/c/d?q") otherwise 'err'
		return 1
	end 'err'
	let resolved = try URL.resolve(base, reference: "../g") otherwise 'err2'
		return 2
	end 'err2'
	print("{resolved.toString()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
http://a/b/g
```

<!-- test: url.resolve-absolute -->
```maxon
function main() returns ExitCode
	let base = try URL.parse("http://a/b/c/d?q") otherwise 'err'
		return 1
	end 'err'
	let resolved = try URL.resolve(base, reference: "/g") otherwise 'err2'
		return 2
	end 'err2'
	print("{resolved.toString()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
http://a/g
```

<!-- test: url.resolve-authority -->
```maxon
function main() returns ExitCode
	let base = try URL.parse("http://a/b/c/d?q") otherwise 'err'
		return 1
	end 'err'
	let resolved = try URL.resolve(base, reference: "//other.com/g") otherwise 'err2'
		return 2
	end 'err2'
	print("{resolved.toString()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
http://other.com/g
```

<!-- test: url.resolve-query-only -->
```maxon
function main() returns ExitCode
	let base = try URL.parse("http://a/b/c/d?q") otherwise 'err'
		return 1
	end 'err'
	let resolved = try URL.resolve(base, reference: "?y") otherwise 'err2'
		return 2
	end 'err2'
	print("{resolved.toString()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
http://a/b/c/d?y
```

<!-- test: url.resolve-fragment-only -->
```maxon
function main() returns ExitCode
	let base = try URL.parse("http://a/b/c/d?q") otherwise 'err'
		return 1
	end 'err'
	let resolved = try URL.resolve(base, reference: "#s") otherwise 'err2'
		return 2
	end 'err2'
	print("{resolved.toString()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
http://a/b/c/d?q#s
```

<!-- test: url.resolve-empty -->
```maxon
function main() returns ExitCode
	let base = try URL.parse("http://a/b/c/d?q") otherwise 'err'
		return 1
	end 'err'
	let resolved = try URL.resolve(base, reference: "") otherwise 'err2'
		return 2
	end 'err2'
	print("{resolved.toString()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
http://a/b/c/d?q
```

<!-- test: url.resolve-full-relative -->
```maxon
function main() returns ExitCode
	let base = try URL.parse("http://a/b/c/d?q") otherwise 'err'
		return 1
	end 'err'
	let resolved = try URL.resolve(base, reference: "g?y#s") otherwise 'err2'
		return 2
	end 'err2'
	print("{resolved.toString()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
http://a/b/c/g?y#s
```

<!-- test: url.resolve-double-dotdot -->
```maxon
function main() returns ExitCode
	let base = try URL.parse("http://a/b/c/d?q") otherwise 'err'
		return 1
	end 'err'
	let resolved = try URL.resolve(base, reference: "../../g") otherwise 'err2'
		return 2
	end 'err2'
	print("{resolved.toString()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
http://a/g
```
