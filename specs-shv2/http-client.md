---
feature: http-client
status: experimental
keywords: [http, client, request, response, network, url]
category: network
---

# HttpClient

## Documentation

HTTP/1.1 client for making HTTP requests over TCP connections.

**Types:**
- `HttpClient` — stateless HTTP client with static methods for making requests
- `HttpRequest` — represents an HTTP request with method, URL, headers, body
- `HttpResponse` — represents an HTTP response with status code, headers, body
- `HttpHeaders` — case-insensitive header map
- `HttpMethod` — enum of HTTP methods (get, post, put, delete, head, patch)
- `HttpError` — error enum for HTTP operations

**Quick usage:**

```text
function fetchData() returns ExitCode throws HttpError
  let response = try HttpClient.get("http://httpbin.org/get")
  print(response.body())
  return 0
end 'fetchData'
```

**Building requests manually:**

```text
function postData() returns ExitCode throws HttpError
  var request = try HttpRequest.create(HttpMethod.post, url: "http://httpbin.org/post")
  request.setHeader("content-type", value: "application/json")
  request.setBody("hello=world")
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

## Tests

### Invalid URL

<!-- test: http-client.invalid-url -->
```maxon
function main() returns ExitCode
	if let response = try HttpClient.get("not a url") 'ok'
		return 1
	end 'ok' else 'err'
		return 0
	end 'err'
end 'main'
```
```exitcode
0
```

### Request Building

<!-- test: http-client.build-request -->
```maxon
function main() returns ExitCode
	let request = try HttpRequest.create(HttpMethod.get, url: "http://example.com/path?q=1") otherwise 'err'
		return 1
	end 'err'
	let url = request.url()
	let host = try url.host() otherwise ""
	if host != "example.com" 'badHost'
		return 2
	end 'badHost'
	let path = url.path()
	if path != "/path" 'badPath'
		return 3
	end 'badPath'
	return 0
end 'main'
```
```exitcode
0
```

### HTTP GET

<!-- test: http-client.get -->
<!-- network: live -->
```maxon
function doGet() returns ExitCode throws HttpError
	let response = try HttpClient.get("http://httpbin.org/get")
	if response.statusCode() == 200 'ok'
		return 0
	end 'ok'
	return 1
end 'doGet'

function main() returns ExitCode
	let p = async doGet()
	let result = try await p otherwise 99
	return result
end 'main'
```
```exitcode
0
```

### HTTP POST

<!-- test: http-client.post -->
<!-- network: live -->
```maxon
function doPost() returns ExitCode throws HttpError
	let response = try HttpClient.post("http://httpbin.org/post", body: "hello=world")
	if response.statusCode() == 200 'ok'
		return 0
	end 'ok'
	return 1
end 'doPost'

function main() returns ExitCode
	let p = async doPost()
	let result = try await p otherwise 99
	return result
end 'main'
```
```exitcode
0
```

### Status 404

<!-- test: http-client.status-404 -->
<!-- network: live -->
```maxon
function doGet() returns ExitCode throws HttpError
	let response = try HttpClient.get("http://httpbin.org/status/404")
	if response.statusCode() == 404 'notFound'
		return 0
	end 'notFound'
	return 1
end 'doGet'

function main() returns ExitCode
	let p = async doGet()
	let result = try await p otherwise 99
	return result
end 'main'
```
```exitcode
0
```

### Response Body Contains Expected Content

<!-- test: http-client.response-body -->
<!-- network: live -->
```maxon
function doGet() returns ExitCode throws HttpError
	let response = try HttpClient.get("http://httpbin.org/get")
	let body = response.body()
	if body.contains("httpbin.org") 'hasContent'
		return 0
	end 'hasContent'
	return 1
end 'doGet'

function main() returns ExitCode
	let p = async doGet()
	let result = try await p otherwise 99
	return result
end 'main'
```
```exitcode
0
```

### Async HTTP with Concurrent File I/O

Verify that file I/O on fiber #2 interleaves with HTTP networking on fiber #1.
With runnext scheduling, the later-spawned file I/O fiber (#2) runs first,
completing file_exists before the HTTP fiber (#1) begins net_connect.

<!-- test: http-client.async-trace-interleave -->
<!-- AsyncTrace -->
⭐⭐ **A TRACE CASE AND A LIVE HOST ARE INCOMPATIBLE, AND THIS FILE'S SIBLING ALREADY SAYS SO.** A trace
pins an INTERLEAVING; a live host makes that interleaving a function of somebody else's latency. In
`async-tcp.md` the two `AsyncTrace` cases carry no `network: live` and the `network: live` case carries no
trace — the separation is the design, and this case was the only one trying to do both.

⚠ **MEASURED: the previous pin was a LATENCY RACE that had already gone stale.** It expected a `sleep(100)`
in `main` to expire mid-request — an early `sleep_resume #0`, and a `sleep_yield #1` retry inside the HTTP
task. Against `httpbin.org` today the request finishes first, so the run ends `try_await #1 [immediate]`
rather than `[yield]` and the trace is 20 lines against the 22 pinned. Three runs agreed with each other and
none agreed with the file: **stable on the day, and a hostage to the link.**

⇒ The address is `192.0.2.1` — TEST-NET-1, reserved by RFC 5737 and guaranteed unroutable — which is what
`async-tcp.trace-mixed-io` already uses to trace a network path without one. The `sleep` is gone with it,
because the timer was only there to force the second spawn to land mid-request. **The trace is stronger for
it**: both tasks are now parked on DIFFERENT kinds of I/O at the same time and resume interleaved, where the
live version had the HTTP task nearly finish before the file task was even spawned. Measured identical
across 15 runs at ~35 ms each, and it needs no `--network`.
```maxon
function doHttp() returns ExitCode throws HttpError
	let response = try HttpClient.get("http://192.0.2.1/get")
	if response.statusCode() == StatusCode.ok 'ok'
		return 0
	end 'ok'
	return 1
end 'doHttp'

function doFileIo() returns ExitCode
	let exists = File.exists(FilePath from "no_such_file.txt")
	if exists 'found'
		return 1
	end 'found'
	return 0
end 'doFileIo'

function main() returns ExitCode
	let httpTask = async doHttp()
	let fileTask = async doFileIo()
	let fileResult = await fileTask
	let httpResult = try await httpTask otherwise 99
	return httpResult + fileResult
end 'main'
```
```exitcode
99
```
```stderr
spawn #1
spawn #2
io_yield #1 [net_connect]
io_yield #2 [file_exists]
io_resume #1 [net_connect]
io_resume #2 [file_exists]
await #2 [yield]
try_await #1 [immediate]
```

### Response Headers

<!-- test: http-client.response-headers -->
<!-- network: live -->
```maxon
function doGet() returns ExitCode throws HttpError
	let response = try HttpClient.get("http://httpbin.org/get")
	let contentType = try response.header("content-type")
	if contentType.contains("application/json") 'ok'
		return 0
	end 'ok'
	return 1
end 'doGet'

function main() returns ExitCode
	let p = async doGet()
	let result = try await p otherwise 99
	return result
end 'main'
```
```exitcode
0
```
