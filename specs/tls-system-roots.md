---
feature: tls-system-roots
status: experimental
keywords: [tls, TlsClient, system roots, trust store, HttpClient, service, message]
category: network
---

# The system roots are settled without waiting

## Documentation

A client that trusts the system roots reads them from the operating system the first time a connection needs them, and keeps the result for the rest of the process: a read that failed is kept too, and every later connection that trusts the system roots fails with `trustStoreUnavailable` without reading again. The read is made by the connection that needs it, on its own thread, and no connection waits for another's: connections that start together while nothing is kept each read the source, one result is kept, and a later connection uses it. A message body, which may not wait on another party, can therefore make a TLS connection and an `HttpClient` request.

## Tests

<!-- test: tls-system-roots.the-system-roots-are-read-once-for-every-connection -->
<!-- tier: stdlib/helpers/tls -->
Two connections made with default options, which trust the system roots, read the roots from the operating system once between them: the second connection verifies against the roots the first one loaded. The case settles the program-wide roots from a source that answers the case's root and counts its reads.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	let bytes = ByteArray.create(0)
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function readFault(error StreamError) returns String
	return match error 'error'
		secure(reason) gives describe(reason)
		failed(reason) gives "failed({reason})"
		timedOut or
		closed gives error.name
	end 'error'
end 'readFault'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	let options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let keyP256Hex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"

typealias ReadCount = int(0 to 1000000)
typealias ReadCountCell = SharedValue with ReadCount

type CountingTrust implements TrustSource
	let reads as ReadCountCell

	static function create(reads ReadCountCell) returns CountingTrust
		return Self{reads: reads}
	end 'create'

	function read() returns RootCertificates throws TrustStoreError
		reads.publish(reads.current() + 1)

		return RootCertificates from [fromHex(rootHex)]
	end 'read'
end 'CountingTrust'

function serveTwice(listener TlsListener) returns String
	var report = ""

	for attempt in 1 to 2 'eachAttempt'
		let stream = try listener.accept() otherwise (error) 'refused'
			report.append("[accept {attempt}: {describe(error)}]")
			continue
		end 'refused'

		let heardBytes = try stream.readSome(1024) otherwise (error) 'unheard'
			report.append("[recv {attempt}: {readFault(error)}]")
			continue
		end 'unheard'
		let heard = String.from(heardBytes)

		report.append("[served {attempt}: {heard}]")
		stream.close()
	end 'eachAttempt'

	return report
end 'serveTwice'

function dial(port NetworkPort) returns String
	let options = TlsClientOptions.create()

	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	let stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	_ = try stream.send("hello") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	stream.close()

	return "connected"
end 'dial'

function main() returns ExitCode
	let reads = ReadCountCell.create(0 as ReadCount)
	try SystemRoots.settle(CountingTrust.create(reads.clone())) otherwise return 3

	let listener = try TlsListener.bind("127.0.0.1", port: 0, options: trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))) otherwise return 1
	let listenerPort = listener.port()
	let peer = async serveTwice(listener)

	print("first: {dial(listenerPort)}\n")
	print("second: {dial(listenerPort)}\n")
	print("server: {await peer}\n")
	print("reads: {reads.current()}\n")

	return 0
end 'main'
```
```stdout
first: connected
second: connected
server: [served 1: hello][served 2: hello]
reads: 1
```
```exitcode
0
```


<!-- test: tls-system-roots.connections-that-start-together-each-settle-the-system-roots-without-waiting -->
<!-- tier: stdlib/helpers/tls -->
Four connections that start together settle the system roots at once, while the roots source is still answering its first read. None waits for another: each reads the source itself, one result is kept, and all four settle. A later connection verifies against the kept roots without reading the source again.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	let bytes = ByteArray.create(0)
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function readFault(error StreamError) returns String
	return match error 'error'
		secure(reason) gives describe(reason)
		failed(reason) gives "failed({reason})"
		timedOut or
		closed gives error.name
	end 'error'
end 'readFault'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	let options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let keyP256Hex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"

typealias ReadCount = int(0 to 1000000)
typealias ReadCountCell = SharedValue with ReadCount

type SlowTrust implements TrustSource
	let reads as ReadCountCell

	static function create(reads ReadCountCell) returns SlowTrust
		return Self{reads: reads}
	end 'create'

	function read() returns RootCertificates throws TrustStoreError
		reads.publish(reads.current() + 1)
		sleep(150)

		return RootCertificates from [fromHex(rootHex)]
	end 'read'
end 'SlowTrust'

function serveOne(listener TlsListener) returns String
	let stream = try listener.accept() otherwise (error) 'refused'
		return "[accept: {describe(error)}]"
	end 'refused'

	let heardBytes = try stream.readSome(1024) otherwise (error) 'unheard'
		return "[recv: {readFault(error)}]"
	end 'unheard'
	let heard = String.from(heardBytes)

	stream.close()

	return "[served {heard}]"
end 'serveOne'

function dial(port NetworkPort) returns String
	let options = TlsClientOptions.create()

	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	let stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	_ = try stream.send("hello") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	stream.close()

	return "connected"
end 'dial'

function settled(reads ReadCountCell) returns String
	try SystemRoots.settle(SlowTrust.create(reads)) otherwise return "failed"

	return "settled"
end 'settled'

function main() returns ExitCode
	let reads = ReadCountCell.create(0 as ReadCount)
	let first = async settled(reads.clone())
	let second = async settled(reads.clone())
	let third = async settled(reads.clone())
	let fourth = async settled(reads.clone())

	print("settlers: {await first} {await second} {await third} {await fourth}\n")

	let readCount = reads.current()
	print("read at least once and at most once each: {readCount >= 1 and readCount <= 4}\n")

	let listener = try TlsListener.bind("127.0.0.1", port: 0, options: trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))) otherwise return 1
	let port = listener.port()
	let peer = async serveOne(listener)

	print("client: {dial(port)}\n")
	print("server: {await peer}\n")
	print("reads kept: {reads.current() == readCount}\n")

	return 0
end 'main'
```
```stdout
settlers: settled settled settled settled
read at least once and at most once each: true
client: connected
server: [served hello]
reads kept: true
```
```exitcode
0
```

<!-- test: tls-system-roots.a-failed-read-of-the-system-roots-is-kept -->
<!-- tier: stdlib/helpers/tls -->
A roots source that cannot be read fails the connection that asked with `trustStoreUnavailable`, and the failure is kept: the next connection fails the same way without the source being read again.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	let bytes = ByteArray.create(0)
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	let options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let keyP256Hex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"

typealias ReadCount = int(0 to 1000000)
typealias ReadCountCell = SharedValue with ReadCount

type MissingTrust implements TrustSource
	let reads as ReadCountCell

	static function create(reads ReadCountCell) returns MissingTrust
		return Self{reads: reads}
	end 'create'

	function read() returns RootCertificates throws TrustStoreError
		reads.publish(reads.current() + 1)

		throw TrustStoreError.unavailable
	end 'read'
end 'MissingTrust'

function dial(port NetworkPort) returns String
	let options = TlsClientOptions.create()

	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	_ = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	return "connected"
end 'dial'

function main() returns ExitCode
	let reads = ReadCountCell.create(0 as ReadCount)
	try SystemRoots.settle(MissingTrust.create(reads.clone())) otherwise ignore

	let listener = try TlsListener.bind("127.0.0.1", port: 0, options: trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))) otherwise return 1

	print("first: {dial(listener.port())}\n")
	print("second: {dial(listener.port())}\n")
	print("reads: {reads.current()}\n")

	listener.close()

	return 0
end 'main'
```
```stdout
first: connect: trustStoreUnavailable
second: connect: trustStoreUnavailable
reads: 1
```
```exitcode
0
```

