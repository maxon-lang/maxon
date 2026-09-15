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

Verify that a file probe and an HTTP connect, parked on different kinds of I/O at the same time, resume
interleaved, and that the request's failure still reaches `try await`.

<!-- test: http-client.async-trace-interleave -->
<!-- AsyncTrace -->
⭐⭐ **A TRACE CASE AND A LIVE HOST ARE INCOMPATIBLE.** A trace pins an INTERLEAVING, and a live host makes
that interleaving a function of somebody else's latency. So the peer is a listener this program binds on
`127.0.0.1`, at a port the kernel picks, and the case needs no `--network`. `async-tcp.md` keeps the same
separation: its `AsyncTrace` cases carry no `network: live`, and its `network: live` case carries no trace.

⚠ **THE LISTENER NEVER ACCEPTS, BECAUSE A PARKED ACCEPT RACES THE DIAL.** One loopback handshake readies
both the accept and the connect, and the poller reports the two in either order. Unaccepted, the connection
completes into the backlog, so the dial and the request's send still complete, and closing the listener
resets it, which ends the request with an error.

⚠ **THE `sleep` IS THE HANDOVER, AND IT IS A MARGIN, NOT A SYNCHRONIZATION.** It parks `main` on a timer
rather than requeueing it, so the HTTP task runs alone until it is waiting for a response. Nothing orders the
close after the request's send except `settleMs`: an HTTP task stalled for longer than that would meet the
reset before it waits for a response, and pin a different trace.

Both addresses are literals, which wait on no resolver, so the bind does not park and the HTTP connect
parks once, for the dial.
```maxon
// Far longer than a loopback connect and send, which is all the HTTP task does before it waits.
let settleMs = 200

function doHttp(port NetworkPort) returns ExitCode throws HttpError
	let response = try HttpClient.get("http://127.0.0.1:{port}/get")
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
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let fileTask = async doFileIo()
	let httpTask = async doHttp(listener.port())
	let fileResult = await fileTask

	sleep(settleMs)
	listener.close()

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
io_yield #1 [file_exists]
io_yield #2 [net_connect]
io_resume #1 [file_exists]
await #1 [yield]
sleep_yield #0
io_resume #2 [net_connect]
io_yield #2 [net_send]
io_resume #2 [net_send]
io_yield #2 [net_recv]
io_resume #2 [net_recv]
sleep_resume #0
io_yield #0 [net_close]
io_resume #0 [net_close]
io_yield #2 [net_close]
io_resume #2 [net_close]
try_await #2 [yield]
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
