# Maxon Standard Library Reference

## Table of Contents

1. [Core Functions](#core-functions)
2. [FilePath](#filepath)
3. [File](#file)
4. [URL](#url)
5. [CharacterSet](#characterset)
6. [Unicode](#unicode)
7. [String Trimming](#string-trimming)
8. [List](#list)
9. [Networking (TcpClient)](#networking-tcpclient)
10. [HttpClient](#httpclient)
11. [Crypto](#crypto)
12. [Range / OpenRange](#range--openrange)
13. [ArrayIterator](#arrayiterator)
14. [Builtin Managed Types](#builtin-managed-types)

---

## Crypto

### `sha256`

Compute the SHA-256 cryptographic hash of a byte array (FIPS 180-4).

```maxon
export function sha256(data ByteArray) returns ByteArray
```

**Parameters:**
- `data` — The input bytes to hash

**Returns:** A 32-byte `ByteArray` containing the SHA-256 digest

**Example:**

```maxon
var data = ByteArray.create()
data.push(0x61)
data.push(0x62)
data.push(0x63)
let hash = sha256(data)
// hash contains the SHA-256 of "abc" (32 bytes)
```

---

## Core Functions

**I/O Functions**
```maxon
print(value String)                     // Print string to stdout
```

**Math Functions**
```maxon
abs(x float) float              // Absolute value (int auto-promoted to float)
sqrt(x float) float             // Square root
floor(x float) float            // Round toward negative infinity
ceil(x float) float             // Round toward positive infinity
round(x float) float            // Round to nearest (banker's rounding)
trunc(x float) int              // Truncate toward zero
min(a float, b float) float     // Minimum of two values
max(a float, b float) float     // Maximum of two values

// Math library (stdlib) — called as Math.sin(x), Math.cos(x), etc.
Math.sin(x float) float         // Sine (radians)
Math.cos(x float) float         // Cosine (radians)
Math.tan(x float) float         // Tangent (radians)
Math.atan(z float) float        // Arc tangent
Math.atan2(y float, x float) float // Two-argument arc tangent
Math.exp(x float) float         // e^x
Math.log(x float) float         // Natural logarithm
Math.log2(x float) float        // Base-2 logarithm
Math.log10(x float) float       // Base-10 logarithm
Math.pow(base float, exponent float) float // Power
floor(x float) int              // Round down
ceil(x float) int               // Round up
round(x float) int              // Round to nearest
trunc(x float) int              // Truncate toward zero
```

**Compile-Time Functions**
```maxon
sizeof(TypeName) int            // Size of a type in bytes (compile-time constant)
```

`sizeof` accepts a type name and returns its storage size in bytes as a compile-time integer constant. No runtime cost. Primitive sizes: `int` (8), `float` (8), `bool` (1), `byte` (1). Struct types use 8 bytes per field (minimum 8). Enum types use 8 bytes. Ranged type aliases use the optimal storage width for their range.

**Concurrency Functions**
```maxon
sleep(milliseconds int)         // Suspend current green thread for given duration
```

**Formatting Functions**
```maxon
format_int(value int) String    // Format int as string
format_float(value float) String // Format float as string
```

---

## FilePath

`FilePath` is a type-safe wrapper around `String` for filesystem paths. It normalizes path separators to the platform-native format on construction and provides methods for path manipulation.

**Construction:**
```maxon
var p = FilePath from "C:\\Users\\test.txt"              // From string literal (panics on invalid chars)
var q = try FilePath.from("hello.maxon") otherwise ...   // From string (throws FilePathError)
var r = FilePath from "file:///C:/Users/test.txt"        // file:// URLs are converted to paths
var s = try FilePath.from("file:///home/user/f.txt") otherwise ...  // Also works with from()
```

Both `init()` and `from()` transparently accept `file://` URLs, parsing them with `URL.parse()` and extracting the filesystem path. On Windows, the leading `/` before drive letters is stripped (e.g. `/C:/path` becomes `C:\path`). Non-file URL schemes (e.g. `https://`) cause a panic in `init()` or throw `FilePathError.notFileURL` in `from()`.

**Component Extraction:**
```maxon
p.filename()         // "test.txt"
p.fileExtension()    // ".txt"
p.stem()             // "test"
try p.parent()       // FilePath("C:\\Users") — throws FilePathError.noParent if no parent
```

**Path Manipulation:**
```maxon
p.join("docs")                  // Append component with platform separator
p.join(otherFilePath)           // Join with another FilePath
p.changeExtension(".exe")       // Replace file extension
p.normalize()                   // Returns self (normalized on construction)
```

**Query Methods:**
```maxon
p.isEmpty()          // true if path is empty string
p.isAbsolute()       // true for drive paths (C:\) or UNC paths (\\server)
p.isRelative()       // opposite of isAbsolute
```

**Resolution:**
```maxon
p.resolve(base)      // resolve relative path against base; absolute paths returned unchanged
```

**Static Methods:**
```maxon
FilePath.separator()   // Platform-native separator ("\" on Windows, "/" on Linux)
```

`FilePath` implements `Equatable`, `Hashable`, `Stringable`, and `InitableFromStringLiteral`.

All `File` and `Directory` methods accept `FilePath` parameters:
```maxon
let fp = FilePath from "data.txt"
let content = try File.readText(fp) otherwise ...
try File.writeText(fp, content: "hello")
let files = try Directory.list(FilePath from "./") otherwise ...
```

---

## File

`File` provides static methods for reading, writing, deleting, and querying files. It is defined in `stdlib/File.maxon`. All methods accept `FilePath` parameters.

### Type Aliases

```maxon
export typealias FileSize = int(0 to u64.max)     // File size in bytes
export typealias Timestamp = int(0 to u64.max)    // Unix epoch seconds
```

### `FileInfo`

Metadata about a file, returned by `File.info()`. All fields are obtained from a single OS call.

```maxon
export type FileInfo
	export let size FileSize
	export let modifiedTime Timestamp
	export let createdTime Timestamp
	export let accessedTime Timestamp
	export let isDirectory bool
	export let isReadOnly bool
end 'FileInfo'
```

| Field | Type | Description |
|-------|------|-------------|
| `size` | `FileSize` | File size in bytes |
| `modifiedTime` | `Timestamp` | Last modification time (Unix epoch seconds) |
| `createdTime` | `Timestamp` | Creation time (Unix epoch seconds) |
| `accessedTime` | `Timestamp` | Last access time (Unix epoch seconds) |
| `isDirectory` | `bool` | `true` if the path is a directory |
| `isReadOnly` | `bool` | `true` if the file is read-only |

### `FilePermission` (enum)

| Case | Description |
|------|-------------|
| `normal` | Standard file permissions (0666) |
| `executable` | Executable permissions (0755, Unix) |

### Error Types

```maxon
export enum FileReadError implements Error
	notFound              // file not found when reading
end 'FileReadError'

export enum FileWriteError implements Error
	failed                // write operation failed
end 'FileWriteError'

export enum FileDeleteError implements Error
	notFound              // file not found when deleting
end 'FileDeleteError'

export enum FileInfoError implements Error
	notFound              // file not found when querying metadata
end 'FileInfoError'
```

### API Summary

| Method | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `File.readText(path FilePath)` | `String` | `FileReadError` | Read file as UTF-8 string |
| `File.readBinary(path FilePath)` | `ByteArray` | `FileReadError` | Read file as raw bytes |
| `File.writeText(path FilePath, content String, mode FilePermission = .normal)` | -- | `FileWriteError` | Write string to file |
| `File.writeBinary(path FilePath, content ByteArray, mode FilePermission = .normal)` | -- | `FileWriteError` | Write bytes to file |
| `File.exists(path FilePath)` | `bool` | -- | Check if file exists |
| `File.delete(path FilePath)` | -- | `FileDeleteError` | Delete a file |
| `File.info(path FilePath)` | `FileInfo` | `FileInfoError` | Get file metadata |

### `File.info`

```maxon
export static function info(path FilePath) returns FileInfo throws FileInfoError
```

**Parameters:**
- `path` -- The path to query

**Returns:** A `FileInfo` struct containing the file's size, timestamps, and attributes

**Throws:** `FileInfoError.notFound` when the file does not exist

**Example:**

```maxon
let fp = FilePath from "data.txt"
let info = try File.info(fp) otherwise 'err'
		print("file not found")
		return 1
end 'err'
print("size: {info.size}")
print("modified: {info.modifiedTime}")
print("is directory: {info.isDirectory}")
```

---

## URL

`URL` provides RFC 3986 compliant URI parsing, serialization, and reference resolution. It is defined in `stdlib/URL.maxon`.

**Parsing:**
```maxon
var url = try URL.parse("https://example.com:8080/path?q=1#top") otherwise 'err'
	// handle error
end 'err'
```

**Always-available accessors:**
```maxon
url.scheme()     // "https" (empty string for relative references)
url.path()       // "/path" (always present, may be empty)
```

**Throwing accessors** (throw `URLError.fieldNotPresent` if not set):
```maxon
var host = try url.host() otherwise "default"       // "example.com"
var port = try url.port() otherwise 443             // 8080
var ui = try url.userinfo() otherwise ""            // userinfo before @
var query = try url.query() otherwise ""            // "q=1"
var frag = try url.fragment() otherwise ""          // "top"
```

**Serialization:**
```maxon
url.toString()   // "https://example.com:8080/path?q=1#top"
```

**Reference Resolution** (RFC 3986 Section 5):
```maxon
var base = try URL.parse("http://a/b/c/d?q") otherwise ...
var resolved = try URL.resolve(base, reference: "../g") otherwise ...
resolved.toString()  // "http://a/b/g"
```

**Error Types:**

| Error | Description |
|-------|-------------|
| `URLError.emptyInput` | Input is empty or whitespace-only |
| `URLError.invalidScheme` | Scheme starts with non-alpha or contains invalid characters |
| `URLError.invalidHost` | Malformed host (e.g., unclosed IPv6 bracket) |
| `URLError.invalidPort` | Port is non-numeric or exceeds 65535 |
| `URLError.invalidEncoding` | Malformed percent-encoding (e.g., `%GG`, `%2`) |
| `URLError.relativeWithoutBase` | `resolve()` called with a base URL that has no scheme |
| `URLError.fieldNotPresent` | Accessor called for a component not present in the URL |

`URL` implements `Equatable` and `Stringable`.

---

## CharacterSet

`CharacterSet` represents a set of characters for use with string trimming and character classification. It is defined in `stdlib/CharacterSet.maxon`.

**Static Factory Methods**

Create a `CharacterSet` using one of the built-in factory methods:

```maxon
var ws = CharacterSet.whitespacesAndNewlines()  // All Unicode whitespace including newlines
var spaces = CharacterSet.whitespaces()         // Spaces and tabs only (no newlines)
var nl = CharacterSet.newlines()               // Newline characters only (LF, CR, CRLF, etc.)
var digits = CharacterSet.decimalDigits()       // Unicode decimal digits (Nd category)
var letters = CharacterSet.letters()            // Unicode letters and marks (L*, M* categories)
var alnum = CharacterSet.alphanumerics()        // Unicode letters, marks, and numbers
var punct = CharacterSet.punctuation()          // Unicode punctuation (P* categories)
var custom = CharacterSet.from(CharSet from ['a', 'e', 'i', 'o', 'u'])  // Custom set
```

**Instance Methods**

```maxon
ws.contains('A')    // false
ws.contains(' ')    // true
```

| Method | Returns | Description |
|--------|---------|-------------|
| `contains(c Character)` | `bool` | Check if the character is in the set |

---

## Unicode

`Unicode` provides Unicode character classification utilities. It is defined in `stdlib/Unicode.maxon`.

**Static Methods**

```maxon
Unicode.isWhitespace(32)    // true (space)
Unicode.isWhitespace(65)    // false ('A')
```

| Method | Returns | Description |
|--------|---------|-------------|
| `isWhitespace(cp Codepoint)` | `bool` | Check if a codepoint is Unicode whitespace |

---

## String Trimming

The `String` type provides methods for removing characters from the start and end of a string. Each method has two forms: one that accepts a `CharacterSet` parameter, and a convenience overload that trims whitespace by default.

**Trimming with CharacterSet**

```maxon
"123hello456".trim(CharacterSet.decimalDigits())     // "hello"
"...hello!!!".trim(CharacterSet.punctuation())       // "hello"
"xxxhelloxxx".trimStart(CharacterSet.from(CharSet from ['x']))     // "helloxxx"
"xxxhelloxxx".trimEnd(CharacterSet.from(CharSet from ['x']))       // "xxxhello"
```

**Trimming Whitespace (convenience)**

```maxon
"  hello  ".trim()          // "hello"
"  hello  ".trimStart()     // "hello  "
"  hello  ".trimEnd()       // "  hello"
```

| Method | Returns | Description |
|--------|---------|-------------|
| `trim(in CharacterSet)` | `String` | Remove matching characters from both ends |
| `trimStart(in CharacterSet)` | `String` | Remove matching characters from the start |
| `trimEnd(in CharacterSet)` | `String` | Remove matching characters from the end |
| `trim()` | `String` | Remove whitespace from both ends |
| `trimStart()` | `String` | Remove whitespace from the start |
| `trimEnd()` | `String` | Remove whitespace from the end |

The no-argument convenience methods are equivalent to calling the `CharacterSet` variants with `CharacterSet.whitespacesAndNewlines()`.

---

## String Search

Search for substrings within a string. Returns a `StringIndex` with both the character position and byte offset.

```maxon
var s = "hello world hello"
var first = try s.findFirst("hello") otherwise s.endIndex()
print("{first.charIndex()}\n")  // 0

var last = try s.findLast("hello") otherwise s.endIndex()
print("{last.charIndex()}\n")   // 12
```

| Method | Returns | Description |
|--------|---------|-------------|
| `findFirst(needle String)` | `StringIndex throws StringError` | Find first occurrence of needle |
| `findLast(needle String)` | `StringIndex throws StringError` | Find last occurrence of needle |
| `contains(needle String)` | `bool` | Check if string contains substring |
| `contains(character Character)` | `bool` | Check if string contains character |
| `startsWith(prefix String)` | `bool` | Check if string starts with prefix |
| `endsWith(suffix String)` | `bool` | Check if string ends with suffix |

---

## String Properties

```maxon
var s = "hello"
print("{s.count()}\n")        // 5 (grapheme cluster count)
print("{s.byteLength()}\n")   // 5 (UTF-8 byte count)
print("{s.isEmpty()}\n")      // false
print("{s.isAscii()}\n")      // true
```

| Method | Returns | Description |
|--------|---------|-------------|
| `count()` | `Count` | Number of user-perceived characters (grapheme clusters). Recomputed each call — O(n) in byte length; callers that need the count repeatedly should cache it. |
| `byteLength()` | `Count` | Number of UTF-8 bytes |
| `isEmpty()` | `bool` | True if the string has no content |
| `isAscii()` | `bool` | True if all bytes are in the ASCII range (< 128). Enables optimized code paths. |

---

## String Append

`String.append` grows a string's buffer in place, avoiding the allocation of a new string.

```maxon
var s = "Hello"
s.append(" World")       // s is now "Hello World"
```

| Method | Description |
|--------|-------------|
| `append(other String)` | Append another string's content in place |

---

## List

`List` is a generic doubly linked list backed by `__ManagedList` (a builtin compiler-synthesized type, like `Array` and `String`) for efficient node management with automatic memory cleanup. It provides O(1) insertion and removal at both ends, and O(n) indexed access.

**Creating a List**

Create a concrete List type with `typealias`, then initialize with `{}`:
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer

var list = IntList.create()             // Empty list
```

**Adding Elements**
```maxon
list.prepend(1)                  // Add to front — O(1)
list.append(2)                   // Add to back — O(1)
list.insert(1, value: 99)       // Insert at index — O(n)
```

**Accessing Elements**
```maxon
var first = try list.first() otherwise 0   // First element (throws ArrayError)
var last = try list.last() otherwise 0     // Last element (throws ArrayError)
var elem = try list.get(1) otherwise 0     // Element at index (throws ArrayError)
```

**Removing Elements**
```maxon
var removed = try list.removeFirst() otherwise 0  // Remove front — O(1)
var popped = try list.removeLast() otherwise 0    // Remove back — O(1)
var at2 = try list.remove(at: 2) otherwise 0      // Remove at index — O(n)
list.clear()                                       // Remove all elements
```

**Query**
```maxon
list.count()                     // Number of elements
list.isEmpty()                   // true if empty
```

**Iteration**

`List` implements `Iterable`, so it supports `for`-`in` loops:
```maxon
for item in list 'loop'
		print("{item}")
end 'loop'
```

**Complexity Summary**

| Operation | Time |
|-----------|------|
| `prepend` | O(1) |
| `removeFirst` | O(1) |
| `append` | O(1) |
| `removeLast` | O(1) |
| `get`, `insert`, `remove(at:)` | O(n) |
| `first`, `last`, `count`, `isEmpty` | O(1) |
| iteration (for-in) | O(n) total |

---

## Networking (TcpClient)

`TcpClient` provides TCP client networking with automatic resource cleanup. It is defined in `stdlib/TcpClient.maxon`. The socket is backed by `__ManagedSocket`, a builtin type whose destructor closes the file descriptor when the last reference goes out of scope.

**NetworkPort Alias**

The `NetworkPort` type alias constrains port numbers to the valid TCP range:
```maxon
typealias NetworkPort = int(1 to 65535)
```

**NetworkError**

All networking operations throw `NetworkError`, an enum conforming to `Error`:
```maxon
enum NetworkError implements Error
		resolveFailed       // DNS lookup failed
		connectFailed       // TCP connection refused or timed out
		sendFailed          // OS-level send error
		recvFailed          // OS-level recv error
		connectionClosed    // peer closed the connection
end 'NetworkError'
```

**Connecting**

`TcpClient.connect` resolves the hostname, creates a TCP socket, and connects:
```maxon
let client = try TcpClient.connect("example.com", port: 4242)
```

**Sending Data**

`send` transmits all bytes of a string, looping internally to handle partial sends. It returns the total number of bytes sent:
```maxon
let bytesSent = try client.send("Hello\n")
```

**Receiving Data**

`recv` reads up to `bufferSize` bytes from the connection and returns them as a `String`:
```maxon
let response = try client.recv(1024)
```

**Closing**

`close` is idempotent and safe to call multiple times. The socket also closes automatically when the `TcpClient` goes out of scope:
```maxon
client.close()
```

**API Summary**

| Method | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `TcpClient.connect(host String, port NetworkPort)` | `TcpClient` | `NetworkError` | Connect to a TCP server |
| `send(data String)` | `Count` | `NetworkError` | Send all bytes of a string |
| `recv(bufferSize Count)` | `String` | `NetworkError` | Receive up to bufferSize bytes |
| `close()` | — | — | Close the connection (idempotent) |

**Example: Simple TCP Client**
```maxon
function main() returns ExitCode
		let client = try TcpClient.connect("localhost", port: 8080) otherwise 'err'
				print("connection failed")
				return 1
		end 'err'
		_ = try client.send("GET / HTTP/1.0\r\n\r\n") otherwise 'err'
				print("send failed")
				return 1
		end 'err'
		let response = try client.recv(4096) otherwise 'err'
				print("recv failed")
				return 1
		end 'err'
		print(response)
		client.close()
		return 0
end 'main'
```

---

## HttpClient

HTTP/1.1 client for making HTTP requests over TCP connections. HTTP only (no HTTPS/TLS). Uses `Connection: close` for simple response reading.

### `HttpError` (enum, implements Error)

| Variant | Description |
|---------|-------------|
| `invalidUrl` | URL could not be parsed |
| `connectFailed` | TCP connection failed |
| `sendFailed` | Sending the request failed |
| `recvFailed` | Receiving the response failed |
| `invalidResponse` | Response could not be parsed |

### `HttpMethod` (enum)

| Variant |
|---------|
| `get` |
| `post` |
| `put` |
| `delete` |
| `head` |
| `patch` |

### `StatusCode` (enum)

| Variant | Value |
|---------|-------|
| `ok` | 200 |
| `created` | 201 |
| `noContent` | 204 |
| `movedPermanently` | 301 |
| `found` | 302 |
| `notModified` | 304 |
| `badRequest` | 400 |
| `unauthorized` | 401 |
| `forbidden` | 403 |
| `notFound` | 404 |
| `methodNotAllowed` | 405 |
| `conflict` | 409 |
| `gone` | 410 |
| `internalServerError` | 500 |
| `notImplemented` | 501 |
| `badGateway` | 502 |
| `serviceUnavailable` | 503 |

### `HttpHeaders`

Case-insensitive HTTP header map. Header names are lowercased on storage.

| Method | Signature | Description |
|--------|-----------|-------------|
| `create` | `static function create() returns HttpHeaders` | Create an empty header map |
| `set` | `function set(name String, value String)` | Set a header |
| `get` | `function get(name String) returns String throws HttpError` | Get a header value |
| `has` | `function has(name String) returns bool` | Check if a header exists |

### `HttpRequest`

| Method | Signature | Description |
|--------|-----------|-------------|
| `create` | `static function create(method HttpMethod, url String) returns HttpRequest throws HttpError` | Create a request |
| `setHeader` | `function setHeader(name String, value String)` | Set a request header |
| `setBody` | `function setBody(body String)` | Set the request body |
| `url` | `function url() returns URL` | Get the request URL |
| `method` | `function method() returns HttpMethod` | Get the request method |
| `headers` | `function headers() returns HttpHeaders` | Get the request headers |
| `body` | `function body() returns String` | Get the request body |

### `HttpResponse`

| Method | Signature | Description |
|--------|-----------|-------------|
| `statusCode` | `function statusCode() returns StatusCode` | Get the status code |
| `reason` | `function reason() returns String` | Get the reason phrase |
| `headers` | `function headers() returns HttpHeaders` | Get the response headers |
| `body` | `function body() returns String` | Get the response body |
| `header` | `function header(name String) returns String throws HttpError` | Get a response header by name |

### `HttpClient`

Stateless HTTP/1.1 client. All methods are static.

| Method | Signature | Description |
|--------|-----------|-------------|
| `send` | `static function send(request HttpRequest) returns HttpResponse throws HttpError` | Send an HTTP request |
| `get` | `static function get(url String) returns HttpResponse throws HttpError` | Perform a GET request |
| `post` | `static function post(url String, body String) returns HttpResponse throws HttpError` | Perform a POST request |
| `put` | `static function put(url String, body String) returns HttpResponse throws HttpError` | Perform a PUT request |
| `delete` | `static function delete(url String) returns HttpResponse throws HttpError` | Perform a DELETE request |

**Example: Simple GET**
```maxon
function fetchData() returns ExitCode throws HttpError
	let response = try HttpClient.get("http://httpbin.org/get")
	print(response.body())
	return 0
end 'fetchData'
```

**Example: POST with body**
```maxon
function postData() returns ExitCode throws HttpError
	var request = try HttpRequest.create(HttpMethod.post, url: "http://httpbin.org/post")
	request.setHeader("content-type", value: "application/json")
	request.setBody("{\"key\": \"value\"}")
	let response = try HttpClient.send(request)
	print(response.statusCode())
	return 0
end 'postData'
```

**Limitations:**
- HTTP only (no HTTPS/TLS)
- No chunked transfer encoding — uses `Connection: close`
- No redirect following (returns 3xx as-is)
- No streaming — entire response buffered in memory

---

## Range / OpenRange

Outside a `for-in` header, an integer range expression evaluates to a value:

| Expression | Type | Iteration |
|------------|------|-----------|
| `start to end` | `Range` | Inclusive — visits both endpoints |
| `start upto end` | `OpenRange` | Half-open — excludes `end` |

Both implement `Iterable with (RangeBound, RangeIterator)`. `RangeBound` is an alias for the full `int` range (`int(i64.min to i64.max)`).

```maxon
let r = 1 upto 5                           // OpenRange value
for x in r 'loop' ... end 'loop'           // 1, 2, 3, 4

let it = try (1 to 4).createIterator() otherwise return 0
for v in it 'loop' ... end 'loop'          // 1, 2, 3, 4

for (iter, v) in (10 upto 13).withIterator() 'loop'
		print("{iter.index()}:{v}\n")          // 0:10  1:11  2:12
end 'loop'
```

`createIterator()` throws `IterationError.exhausted` for an empty range (`finish < start` for `Range`, `endExclusive <= start` for `OpenRange`).

The `for i in start to end` form inside a loop header still desugars to a counted while-loop with no allocation — using `to`/`upto` in expression position is what triggers the constructor calls.

`RangeIterator` implements `Iterator with RangeBound, BidirectionalIterator`, providing `current()`, `index()`, `advance()`, and `retreat()`.

---

## ArrayIterator

`ArrayIterator` implements `BidirectionalIterator with Element` and provides cursor-style random access into an `Array`. The iterator always points at a valid element; navigation methods throw `IterationError` if they would move out of bounds, while `current()` is unchecked because the position is always valid.

### Declaration

```maxon
typealias MyIter = ArrayIterator with MyElement
```

### Creating an Iterator / Cursor

```maxon
var arr = [10, 20, 30]
var c = try arr.cursor() otherwise panic("empty array")
```

`cursor()` (alias of `createIterator()`) throws `IterationError.exhausted` if the array is empty.

### Methods

| Method | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `current()` | `Element` | -- | Element at the current position (no bounds check) |
| `index()` | `Index` | -- | Current position index |
| `advance()` | -- | `IterationError` | Move forward by 1. Throws `.exhausted` at end. |
| `advanceBy(n Offset)` | -- | `IterationError` | Move forward by `n` (from `Iterator` extension). Throws `.exhausted` if out of bounds. |
| `retreat()` | -- | `IterationError` | Move backward by 1. Throws `.atStart` at position 0. |
| `retreatBy(n Offset)` | -- | `IterationError` | Move backward by `n` (from `BidirectionalIterator` extension). Throws `.atStart` if out of bounds. |
| `peek(ahead Count)` | `Element` | `IterationError` | Read element at `position + ahead`. Throws `.exhausted` if out of bounds. |

`advanceBy` and `retreatBy` are supplied as default extension methods on `Iterator` / `BidirectionalIterator`; they repeatedly call `advance` / `retreat`, so a partial move leaves the iterator at the point where the throw occurred.

### `IterationError` (enum, implements Error)

| Case | Description |
|------|-------------|
| `exhausted` | Iterator would move past the last element |
| `atStart` | Iterator would move before position 0 |

### Example

```maxon
typealias IntIter = ArrayIterator with int

var arr = [1, 2, 3, 4, 5]
var c = try arr.cursor() otherwise panic("empty")
print("{c.current()}\n")        // 1
try c.advance()
print("{c.current()}\n")        // 2
let ahead = try c.peek(2) otherwise 0
print("{ahead}\n")              // 4
```

## Builtin Managed Types

The compiler provides several builtin managed types that wrap OS-level resources (file handles, sockets, directory search handles). These types use RAII via destructors: when the last reference to a managed object goes out of scope, the compiler automatically calls the destructor to release the underlying OS resource.

Managed types are not used directly by application code. Instead, stdlib wrapper types (`File`, `Directory`, `TcpClient`) provide the public API. The managed types are documented here for completeness and for stdlib authors.

### `__ManagedSocket`

Wraps an OS socket file descriptor. Used internally by `TcpClient`. See [Networking (TcpClient)](#networking-tcpclient) for details.

### `__ManagedFile`

Wraps an OS file handle (Windows `HANDLE` or Linux file descriptor). Used internally by `File`.

**Static Methods:**

| Method | Returns | Description |
|--------|---------|-------------|
| `openRead(managed)` | `__ManagedFile` | Open a file for reading. Returns `-1` on failure. |
| `openWrite(managed)` | `__ManagedFile` | Open a file for writing (creates or truncates). Returns `-1` on failure. |
| `exists(managed)` | `int` | Check if a file exists. Returns nonzero if the file exists. |
| `delete(managed)` | `int` | Delete a file. Returns `0` on success. |

**Instance Methods:**

| Method | Returns | Description |
|--------|---------|-------------|
| `size()` | `int` | Get the file size in bytes. |
| `read(managed, size)` | `int` | Read up to `size` bytes into the managed buffer. Returns bytes read. |
| `write(managed)` | `int` | Write the contents of the managed buffer. Returns bytes written, or negative on error. |
| `close()` | -- | Close the file handle. Idempotent; also called automatically by the destructor. |

The `managed` parameters refer to `__ManagedMemory` buffers (the internal backing store of `String` and `ByteArray`).

### `__ManagedDirectory`

Wraps an OS directory search handle (Windows `FindFirstFile`/`FindNextFile` or Linux `opendir`/`readdir`). Used internally by `Directory`.

**Static Methods:**

| Method | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `openSearch(managed)` | `__ManagedDirectory` | `__ManagedDirectoryError` | Open a directory search with a glob pattern. Throws `openSearchFailed` if the path does not exist or access is denied. |
| `exists(managed)` | `bool` | -- | Check if a path exists and is a directory. |
| `create(managed)` | -- | `__ManagedDirectoryError` | Create a directory. Throws `createFailed` on failure. |
| `currentPath()` | `__ManagedMemory` | `__ManagedDirectoryError` | Get the current working directory as a managed string. Throws `currentPathFailed` on OS failure. |

**Instance Methods:**

| Method | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `filename()` | `__ManagedMemory` | -- | Get the filename of the current search result. Panics on a closed iterator. |
| `next()` | `int` | `__ManagedDirectoryError` | Advance to the next search result. Returns non-zero if found, `0` when no more entries. Throws `nextFailed` on OS error. |
| `close()` | -- | -- | Close the search handle. Idempotent; also called automatically by the destructor. |

### `__ManagedMemoryCursor`

Provides a cursor into a `__ManagedMemory` buffer. Increfs the source on creation; decrefs on destruction. Used internally by `ArrayIterator`.

**Instance Methods:**

| Method | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `current()` | `Element` | -- | Load element at current position (no bounds check). |
| `index()` | `int` | -- | Read the current position index. |
| `advance()` | -- | `CursorError` | Move forward by 1 position. |
| `retreat()` | -- | `CursorError` | Move backward by 1 position. |
| `seek(index)` | -- | `CursorError` | Jump to `index`. Throws when out of bounds. |
| `peek(ahead)` | `Element` | `CursorError` | Read element at `position + ahead`. |
