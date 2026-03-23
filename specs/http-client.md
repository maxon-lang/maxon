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
- `HttpError` — error union for HTTP operations

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
  var request = try HttpRequest.create(HttpMethod.get, url: "http://example.com/path?q=1") otherwise 'err'
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
```maxon
function doGet() returns ExitCode throws HttpError
  let response = try HttpClient.get("http://httpbin.org/get")
  if response.statusCode() == 200 'ok'
    return 0
  end 'ok'
  return 1
end 'doGet'

function main() returns ExitCode
  var p = async doGet()
  let result = try await p otherwise 99
  return result
end 'main'
```
```exitcode
0
```

### HTTP POST

<!-- test: http-client.post -->
```maxon
function doPost() returns ExitCode throws HttpError
  let response = try HttpClient.post("http://httpbin.org/post", body: "hello=world")
  if response.statusCode() == 200 'ok'
    return 0
  end 'ok'
  return 1
end 'doPost'

function main() returns ExitCode
  var p = async doPost()
  let result = try await p otherwise 99
  return result
end 'main'
```
```exitcode
0
```

### Status 404

<!-- test: http-client.status-404 -->
```maxon
function doGet() returns ExitCode throws HttpError
  let response = try HttpClient.get("http://httpbin.org/status/404")
  if response.statusCode() == 404 'notFound'
    return 0
  end 'notFound'
  return 1
end 'doGet'

function main() returns ExitCode
  var p = async doGet()
  let result = try await p otherwise 99
  return result
end 'main'
```
```exitcode
0
```

### Response Body Contains Expected Content

<!-- test: http-client.response-body -->
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
  var p = async doGet()
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
```maxon
function doHttp() returns ExitCode throws HttpError
  let response = try HttpClient.get("http://httpbin.org/get")
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
  var httpTask = async doHttp()
  sleep(100)
  var fileTask = async doFileIo()
  var fileResult = await fileTask
  var httpResult = try await httpTask otherwise 99
  return httpResult + fileResult
end 'main'
```
```exitcode
0
```
```stderr
spawn #1
sleep_yield #0
io_yield #1 [net_connect]
sleep_resume #0
spawn #2
io_yield #2 [file_exists]
io_resume #2 [file_exists]
io_resume #1 [net_connect]
io_yield #1 [net_connect]
await #2 [yield]
io_resume #1 [net_connect]
io_yield #1 [net_send]
io_resume #1 [net_send]
sleep_yield #1
sleep_resume #1
io_yield #1 [net_recv]
io_resume #1 [net_recv]
io_yield #1 [net_recv]
io_resume #1 [net_recv]
io_yield #1 [net_close]
io_resume #1 [net_close]
try_await #1 [yield]
```

### Response Headers

<!-- test: http-client.response-headers -->
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
  var p = async doGet()
  let result = try await p otherwise 99
  return result
end 'main'
```
```exitcode
0
```
