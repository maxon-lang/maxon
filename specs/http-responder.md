---
feature: http-responder
status: experimental
keywords: [http, server, responder, respondLater, linear, continuation, finalizer]
category: network
---

# HttpResponder

## Documentation

A handler that must consult a service before it can answer calls `exchange.respondLater()`, which returns an
`HttpResponder` and ends the handler's duty to answer: the handler returns, and whoever holds the responder answers the
request later. The responder is a [linear value](linear-types.md): `respond(response)`, `respondWith(code, body:,
contentType:)` and `abandon()` are its finalizers, and every path consumes it exactly once. It is held in a local, a
parameter or a message argument, never in a field, so a service that consults another service carries it in each
continuation message.

An answer is a send to the connection's worker that never waits. When the worker cannot take it, `respond` and `respondWith`
throw `HttpResponderError.notSent`, which carries the responder back, and the handler of the failure answers 503 through it
or calls `abandon`, which closes the connection with no answer. `HttpServer.serve` does not wait for a deferred answer: its
worker keeps the connection until the answer arrives or the last handle to the worker is dropped. There is no deadline
for an answer. `respondLater` throws `alreadyResponded` on an exchange already answered or deferred and
`deferralUnavailable` on an exchange `HttpServer.accept` returned.

## Tests

<!-- test: a-handler-asks-an-auth-service-and-answers-through-the-continuation -->
<!-- procs: 1 -->
The handler defers the answer with `respondLater`, sends the responder to the `Auth` service in a message of its own, and returns. `Auth` answers through the responder, so the answer reaches the client from the service, and `respondWith` is the finalizer that consumes it.
```maxon
type Auth
	var checked as Count

	static function create() returns Self
		return Self{checked: 0}
	end 'create'

	export function check(token String, responder HttpResponder)
		self.checked = self.checked + 1

		if token == "/secret" 'allowed'
			try responder.respondWith(StatusCode.ok, body: "welcome", contentType: "text/plain") otherwise (e) 'unsent'
				match e 'why'
					notSent(back) then back.abandon()
					invalidResponse(back) then back.abandon()
				end 'why'
			end 'unsent'

			return
		end 'allowed'

		try responder.respondWith(StatusCode.unauthorized, body: "no", contentType: "text/plain") otherwise (e) 'unsentNo'
			match e 'whyNo'
				notSent(back) then back.abandon()
				invalidResponse(back) then back.abandon()
			end 'whyNo'
		end 'unsentNo'
	end 'check'
end 'Auth'

function refuse(responder HttpResponder)
	try responder.respondWith(StatusCode.serviceUnavailable, body: "busy", contentType: "text/plain") otherwise (e) 'unsent'
		match e 'why'
			notSent(back) then back.abandon()
			invalidResponse(back) then back.abandon()
		end 'why'
	end 'unsent'
end 'refuse'

type AuthHandler implements HttpHandler
	let auth as Auth.handle

	static function create(auth Auth.handle) returns AuthHandler
		return AuthHandler{auth: auth}
	end 'create'

	function handle(exchange mutable HttpExchange) throws HttpServerError
		let responder = try exchange.respondLater()
		let token = exchange.request().url().path

		try self.auth.check(token, responder: responder) otherwise (e) 'busy'
			match e 'why'
				mailboxFull(_, back) then refuse(back)
				serviceStopped(_, back) then refuse(back)
			end 'why'
		end 'busy'
	end 'handle'

	function faulted(fault HttpHandlerFault)
		print("faulted {fault.describe()}\n")
	end 'faulted'
end 'AuthHandler'

function serving(server HttpServer, auth Auth.handle) returns HttpServeEnd
	return server.serve(AuthHandler.create(auth.clone()))
end 'serving'

function exchangeOver(server HttpServer) returns ExitCode
	let port = server.port()
	let auth = spawn Auth.create()
	let peer = async serving(server, auth: auth.clone())

	let allowed = try HttpClient.get("http://127.0.0.1:{port}/secret") otherwise 'cancelled1'
		peer.cancel()
		return 2
	end 'cancelled1'
	let denied = try HttpClient.get("http://127.0.0.1:{port}/other") otherwise 'cancelled2'
		peer.cancel()
		return 3
	end 'cancelled2'

	auth.shutdown()

	let busy = try HttpClient.get("http://127.0.0.1:{port}/secret") otherwise 'cancelled3'
		peer.cancel()
		return 4
	end 'cancelled3'

	peer.cancel()
	server.close()

	print("allowed={allowed.statusCode().number()} {allowed.body()} denied={denied.statusCode().number()} busy={busy.statusCode().number()}\n")

	return 0
end 'exchangeOver'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1

	return exchangeOver(server)
end 'main'
```
```stdout
allowed=200 welcome denied=401 busy=503
```
```exitcode
0
```

<!-- test: a-failed-send-hands-the-responder-back-and-the-handler-answers-503 -->
<!-- procs: 1 -->
The `Auth` service is shut down, so the handler's send fails `serviceStopped` and carries the responder back. The handler's `otherwise` arm answers 503 through it.
```maxon
type Auth
	var checked as Count

	static function create() returns Self
		return Self{checked: 0}
	end 'create'

	export function check(token String, responder HttpResponder)
		self.checked = self.checked + 1

		try responder.respondWith(StatusCode.ok, body: token, contentType: "text/plain") otherwise (e) 'unsent'
			match e 'why'
				notSent(back) then back.abandon()
				invalidResponse(back) then back.abandon()
			end 'why'
		end 'unsent'
	end 'check'
end 'Auth'

function refuse(responder HttpResponder)
	try responder.respondWith(StatusCode.serviceUnavailable, body: "busy", contentType: "text/plain") otherwise (e) 'unsent'
		match e 'why'
			notSent(back) then back.abandon()
			invalidResponse(back) then back.abandon()
		end 'why'
	end 'unsent'
end 'refuse'

type AuthHandler implements HttpHandler
	let auth as Auth.handle

	static function create(auth Auth.handle) returns AuthHandler
		return AuthHandler{auth: auth}
	end 'create'

	function handle(exchange mutable HttpExchange) throws HttpServerError
		let responder = try exchange.respondLater()
		let token = exchange.request().url().path

		try self.auth.check(token, responder: responder) otherwise (e) 'busy'
			match e 'why'
				mailboxFull(_, back) then refuse(back)
				serviceStopped(_, back) then refuse(back)
			end 'why'
		end 'busy'
	end 'handle'

	function faulted(fault HttpHandlerFault)
		print("faulted {fault.describe()}\n")
	end 'faulted'
end 'AuthHandler'

function serving(server HttpServer, auth Auth.handle) returns HttpServeEnd
	return server.serve(AuthHandler.create(auth.clone()))
end 'serving'

function exchangeOver(server HttpServer) returns ExitCode
	let port = server.port()
	let auth = spawn Auth.create()
	auth.shutdown()

	let peer = async serving(server, auth: auth.clone())

	let busy = try HttpClient.get("http://127.0.0.1:{port}/anything") otherwise 'cancelled'
		peer.cancel()
		return 2
	end 'cancelled'

	peer.cancel()
	server.close()

	print("busy={busy.statusCode().number()} {busy.body()}\n")

	return 0
end 'exchangeOver'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1

	return exchangeOver(server)
end 'main'
```
```stdout
busy=503 busy
```
```exitcode
0
```

<!-- test: the-responder-travels-through-two-services-in-continuation-messages -->
<!-- procs: 1 -->
`Auth` passes the responder on to `Directory` in another message, and `Directory` answers. No message waits for the answer.
```maxon
type Directory
	var looked as Count

	static function create() returns Self
		return Self{looked: 0}
	end 'create'

	export function lookup(name String, responder HttpResponder)
		self.looked = self.looked + 1

		try responder.respondWith(StatusCode.ok, body: "found {name}", contentType: "text/plain") otherwise (e) 'unsent'
			match e 'why'
				notSent(back) then back.abandon()
				invalidResponse(back) then back.abandon()
			end 'why'
		end 'unsent'
	end 'lookup'

	export function lookedUp() returns Count
		return self.looked
	end 'lookedUp'
end 'Directory'

function refuse(responder HttpResponder)
	try responder.respondWith(StatusCode.serviceUnavailable, body: "busy", contentType: "text/plain") otherwise (e) 'unsent'
		match e 'why'
			notSent(back) then back.abandon()
			invalidResponse(back) then back.abandon()
		end 'why'
	end 'unsent'
end 'refuse'

type Auth
	let directory as Directory.handle

	static function create(directory Directory.handle) returns Self
		return Self{directory: directory}
	end 'create'

	export function check(token String, responder HttpResponder)
		try self.directory.lookup(token, responder: responder) otherwise (e) 'busy'
			match e 'why'
				mailboxFull(_, back) then refuse(back)
				serviceStopped(_, back) then refuse(back)
			end 'why'
		end 'busy'
	end 'check'
end 'Auth'

type AuthHandler implements HttpHandler
	let auth as Auth.handle

	static function create(auth Auth.handle) returns AuthHandler
		return AuthHandler{auth: auth}
	end 'create'

	function handle(exchange mutable HttpExchange) throws HttpServerError
		let responder = try exchange.respondLater()
		let token = exchange.request().url().path

		try self.auth.check(token, responder: responder) otherwise (e) 'busy'
			match e 'why'
				mailboxFull(_, back) then refuse(back)
				serviceStopped(_, back) then refuse(back)
			end 'why'
		end 'busy'
	end 'handle'

	function faulted(fault HttpHandlerFault)
		print("faulted {fault.describe()}\n")
	end 'faulted'
end 'AuthHandler'

function serving(server HttpServer, auth Auth.handle) returns HttpServeEnd
	return server.serve(AuthHandler.create(auth.clone()))
end 'serving'

function exchangeOver(server HttpServer) returns ExitCode
	let port = server.port()
	let directory = spawn Directory.create()
	let auth = spawn Auth.create(directory.clone())
	let peer = async serving(server, auth: auth.clone())

	let first = try HttpClient.get("http://127.0.0.1:{port}/alice") otherwise 'cancelled1'
		peer.cancel()
		return 2
	end 'cancelled1'
	let second = try HttpClient.get("http://127.0.0.1:{port}/bob") otherwise 'cancelled2'
		peer.cancel()
		return 3
	end 'cancelled2'

	peer.cancel()
	server.close()

	let looked = try await directory.lookedUp() otherwise return 4

	print("{first.statusCode().number()} {first.body()} {second.statusCode().number()} {second.body()} looked={looked}\n")

	return 0
end 'exchangeOver'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1

	return exchangeOver(server)
end 'main'
```
```stdout
200 found /alice 200 found /bob looked=2
```
```exitcode
0
```

<!-- test: abandon-closes-the-connection-without-an-answer -->
<!-- procs: 1 -->
`abandon` is the finalizer for a responder that cannot answer: it ends the responder and the worker closes the connection, so the client sees no response.
```maxon
type GivingUpHandler implements HttpHandler
	let label as String

	static function create(label String) returns GivingUpHandler
		return GivingUpHandler{label: label}
	end 'create'

	function handle(exchange mutable HttpExchange) throws HttpServerError
		let responder = try exchange.respondLater()
		responder.abandon()
	end 'handle'

	function faulted(fault HttpHandlerFault)
		print("faulted {fault.describe()} {label}\n")
	end 'faulted'
end 'GivingUpHandler'

function serving(server HttpServer) returns HttpServeEnd
	return server.serve(GivingUpHandler.create("gave up"))
end 'serving'

function exchangeOver(server HttpServer) returns ExitCode
	let port = server.port()
	let peer = async serving(server)

	let reached = try HttpClient.get("http://127.0.0.1:{port}/nothing") otherwise (e) 'noAnswer'
		peer.cancel()
		server.close()
		print("no answer: {e.name}\n")

		return 0
	end 'noAnswer'

	peer.cancel()
	server.close()
	print("answered {reached.statusCode().number()}\n")

	return 1
end 'exchangeOver'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1

	return exchangeOver(server)
end 'main'
```
```stdout
no answer: invalidResponse
```
```exitcode
0
```

<!-- test: an-exchange-from-accept-has-no-worker-to-defer-to -->
<!-- procs: 1 -->
`HttpServer.accept` hands out an exchange no worker serves, so `respondLater` throws `deferralUnavailable` and the handler answers directly.
```maxon
function fetch(port NetworkPort) returns Count
	let response = try HttpClient.get("http://127.0.0.1:{port}/accepted") otherwise return 0

	return response.statusCode().number() as Count
end 'fetch'

function serveOne(server HttpServer) returns ExitCode throws HttpServerError
	let exchange = try server.accept()

	let responder = try exchange.respondLater() otherwise (e) 'refused'
		print("deferral refused: {e.name}\n")
		try exchange.respondWith(StatusCode.ok, body: "answered directly", contentType: "text/plain")

		return 0
	end 'refused'

	responder.abandon()

	return 1
end 'serveOne'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	let port = server.port()
	let client = async fetch(port)

	let served = try serveOne(server) otherwise 'failed'
		client.cancel()
		return 2
	end 'failed'

	let status = await client
	print("status {status} exit {served}\n")

	return 0
end 'main'
```
```stdout
deferral refused: deferralUnavailable
status 200 exit 0
```
```exitcode
0
```

<!-- test: a-responder-the-handler-never-answers-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->

```maxon
type ForgetfulHandler implements HttpHandler
	let label as String

	static function create(label String) returns ForgetfulHandler
		return ForgetfulHandler{label: label}
	end 'create'

	function handle(exchange mutable HttpExchange) throws HttpServerError
		let responder = try exchange.respondLater()
		print("{label} {exchange.request().url().path}\n")
	end 'handle'

	function faulted(fault HttpHandlerFault)
		print("faulted {fault.describe()}\n")
	end 'faulted'
end 'ForgetfulHandler'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	_ = server.serve(ForgetfulHandler.create("forgot"))

	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:9:11: this drops 'responder', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
error E3012: <fragment>:10:7: unused variable: 'responder'
```

<!-- test: a-responder-left-by-an-early-return-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->

```maxon
type HastyHandler implements HttpHandler
	let label as String

	static function create(label String) returns HastyHandler
		return HastyHandler{label: label}
	end 'create'

	function handle(exchange mutable HttpExchange) throws HttpServerError
		let responder = try exchange.respondLater()

		if exchange.request().url().path == "/skip" 'skip'
			return
		end 'skip'

		try responder.respondWith(StatusCode.ok, body: label, contentType: "text/plain") otherwise (e) 'unsent'
			match e 'why'
				notSent(back) then back.abandon()
				invalidResponse(back) then back.abandon()
			end 'why'
		end 'unsent'
	end 'handle'

	function faulted(fault HttpHandlerFault)
		print("faulted {fault.describe()}\n")
	end 'faulted'
end 'HastyHandler'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	_ = server.serve(HastyHandler.create("hasty"))

	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:13:4: this drops 'responder', a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: ignoring-a-failed-answer-drops-the-responder -->
<!-- unsupported-targets: wasm32-wasi -->

```maxon
type CarelessHandler implements HttpHandler
	let label as String

	static function create(label String) returns CarelessHandler
		return CarelessHandler{label: label}
	end 'create'

	function handle(exchange mutable HttpExchange) throws HttpServerError
		let responder = try exchange.respondLater()

		try responder.respondWith(StatusCode.ok, body: label, contentType: "text/plain") otherwise ignore
	end 'handle'

	function faulted(fault HttpHandlerFault)
		print("faulted {fault.describe()}\n")
	end 'faulted'
end 'CarelessHandler'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	_ = server.serve(CarelessHandler.create("careless"))

	return 0
end 'main'
```
```maxoncstderr
error E3223: <fragment>:12:3: this drops a linear value that was never consumed: call one of its finalizers (a `consuming self` method) on it, or move it into a call, a send or a `return`, on every path out of here, `throw`s and early `return`s included
```

<!-- test: answering-twice-is-refused -->

```maxon
function answerTwice(responder HttpResponder)
	try responder.respondWith(StatusCode.ok, body: "first", contentType: "text/plain") otherwise (e) 'unsent'
		match e 'why'
			notSent(back) then back.abandon()
			invalidResponse(back) then back.abandon()
		end 'why'
	end 'unsent'

	try responder.respondWith(StatusCode.ok, body: "second", contentType: "text/plain") otherwise (e) 'unsentAgain'
		match e 'again'
			notSent(back) then back.abandon()
			invalidResponse(back) then back.abandon()
		end 'again'
	end 'unsentAgain'
end 'answerTwice'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:10:6: use of moved value 'responder': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-responder-cannot-be-kept-in-a-field -->

```maxon
type Parked
	var waiting as HttpResponder

	static function create(responder HttpResponder) returns Self
		return Self{waiting: responder}
	end 'create'
end 'Parked'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3224: <fragment>:3:6: the field 'waiting' holds a linear value, and a linear value lives only in locals, parameters and message arguments: a field would keep it past the code that must consume it
```

<!-- test: an-invalid-response-hands-the-responder-back-and-the-service-answers-again -->
<!-- procs: 1 -->
`Auth` answers a `204` that carries a body, which no connection can send. `respondWith` validates the response before it sends, so it throws `invalidResponse` carrying the responder back, and `Auth` answers a valid `500` through it.
```maxon
function answerValid(responder HttpResponder)
	try responder.respondWith(StatusCode.internalServerError, body: "fallback", contentType: "text/plain") otherwise (e) 'unsent'
		match e 'why'
			notSent(back) then back.abandon()
			invalidResponse(back) then back.abandon()
		end 'why'
	end 'unsent'
end 'answerValid'

type Auth
	var checked as Count

	static function create() returns Self
		return Self{checked: 0}
	end 'create'

	export function check(responder HttpResponder)
		self.checked = self.checked + 1

		try responder.respondWith(StatusCode.noContent, body: "unsendable", contentType: "text/plain") otherwise (e) 'rejected'
			match e 'why'
				notSent(back) then back.abandon()
				invalidResponse(back) then answerValid(back)
			end 'why'
		end 'rejected'
	end 'check'
end 'Auth'

type CheckHandler implements HttpHandler
	let auth as Auth.handle

	static function create(auth Auth.handle) returns CheckHandler
		return CheckHandler{auth: auth}
	end 'create'

	function handle(exchange mutable HttpExchange) throws HttpServerError
		let responder = try exchange.respondLater()

		try self.auth.check(responder) otherwise (e) 'busy'
			match e 'why'
				mailboxFull(back) then answerValid(back)
				serviceStopped(back) then answerValid(back)
			end 'why'
		end 'busy'
	end 'handle'

	function faulted(fault HttpHandlerFault)
		print("faulted {fault.describe()}\n")
	end 'faulted'
end 'CheckHandler'

function serving(server HttpServer, auth Auth.handle) returns HttpServeEnd
	return server.serve(CheckHandler.create(auth.clone()))
end 'serving'

function exchangeOver(server HttpServer) returns ExitCode
	let port = server.port()
	let auth = spawn Auth.create()
	let peer = async serving(server, auth: auth.clone())

	let answer = try HttpClient.get("http://127.0.0.1:{port}/anything") otherwise 'cancelled'
		peer.cancel()
		return 2
	end 'cancelled'

	peer.cancel()
	server.close()

	print("status={answer.statusCode().number()} {answer.body()}\n")

	return 0
end 'exchangeOver'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1

	return exchangeOver(server)
end 'main'
```
```stdout
status=500 fallback
```
```exitcode
0
```

<!-- test: a-handler-cannot-keep-its-exchange-in-a-container -->
A handler borrows its exchange for one call, and the exchange holds the connection, so it cannot be pushed anywhere that outlives the call. `request()`, `security()` and `respond()` can therefore never meet an exchange the worker has taken.
```maxon
typealias ExchangeArray = Array with HttpExchange

type KeepingHandler implements HttpHandler
	var count as Count

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	function handle(exchange mutable HttpExchange) throws HttpServerError
		let kept = ExchangeArray.create(0)
		kept.push(exchange)
	end 'handle'

	function faulted(fault HttpHandlerFault)
		print("faulted {fault.describe()}\n")
	end 'faulted'
end 'KeepingHandler'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	_ = server.serve(freeze KeepingHandler.create())

	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:13:13: cannot store into 'an element of the container push writes' a unique value read out of a field or an element: the field or element goes on owning it, and a unique value has one name; read what is needed through the field without binding it, or move it out with `pop` or `remove`
```
