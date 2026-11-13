---
feature: cown-torture
status: stable
keywords: [cown, acquire, torture, services, green-threads, concurrency, mutual-exclusion]
category: concurrency
---

# The multi-cown acquire under load

## Documentation

`CownTorture.violations(cownCount, workers:, rounds:)` drives the runtime's acquire through the door an
`acquire` block uses (`__cown_acquire`). It starts `cownCount` services and `workers` green threads; each
round a worker draws a service or a pair of services (a draw may repeat one) and either sends the first a
message or acquires the pair, holds it across a yield, and releases it. One worker stops a service half way
through, so later acquires of it answer stopped and later sends to it fail. The answer is how many times a
held service was entered again, plus one when the per-service entry counts do not add up to the messages
delivered and the services acquired, plus one for every acquire of the stopped service that did not answer
stopped; 0 means mutual exclusion and the stopped answer held. `cownCount` must be a power of two.

A parked acquirer is a green thread parked on the group's grant, so the cases below run at a scale where
most acquires wait for a grant.

## Tests

<!-- test: cown-torture.eight-services-eight-workers -->
Eight services, eight workers and two thousand rounds each: acquires wait for grants, messages interleave
with them on the same services, and one service is stopped mid-run. No held service is entered twice and the
counts add up.
```maxon
function main() returns ExitCode
	let violations = CownTorture.violations(8, workers: 8, rounds: 2000)

	if violations > 0 'aViolation'
		print("violations={violations}\n")

		return 1
	end 'aViolation'

	print("held\n")

	return 0
end 'main'
```
```stdout
held
```
```exitcode
0
```

<!-- test: cown-torture.two-services-contended -->
Two services and six workers: every acquire contends with another for one of the two, so most park.
```maxon
function main() returns ExitCode
	let violations = CownTorture.violations(2, workers: 6, rounds: 3000)

	if violations > 0 'aViolation'
		print("violations={violations}\n")

		return 1
	end 'aViolation'

	print("held\n")

	return 0
end 'main'
```
```stdout
held
```
```exitcode
0
```

<!-- test: cown-torture.one-service-is-both-halves-of-every-pair -->
One service: every pair names it twice, so the duplicate collapses to one slot and the service is both the
first and the last name of the stopped probe.
```maxon
function main() returns ExitCode
	let violations = CownTorture.violations(1, workers: 4, rounds: 1500)

	if violations > 0 'aViolation'
		print("violations={violations}\n")

		return 1
	end 'aViolation'

	print("held\n")

	return 0
end 'main'
```
```stdout
held
```
```exitcode
0
```

<!-- test: cown-torture.a-count-that-is-not-a-power-of-two-aborts -->
Three services is not a power of two, so the process aborts instead of drawing from a skewed range.
```maxon
function main() returns ExitCode
	let violations = CownTorture.violations(3, workers: 1, rounds: 1)

	print("{violations}\n")

	return 0
end 'main'
```
```exitcode
98
```
```stderr
fatal error: runtime abort 98 (serviceSendProtocolBroken)
```
