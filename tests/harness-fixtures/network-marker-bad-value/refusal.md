---
feature: harness-refusal-network-marker-bad-value
---
# A network marker whose value nothing recognizes

`SpecParser.parseNetworkValue` refuses it. The failure it prevents is silent and fails OPEN: an
unrecognized value read as "no restriction" would put a case that reaches a real external host straight
back into the default gate, and the suite would go red on somebody else's outage with nothing saying why.

<!-- expect-refusal: is the only value this marker takes -->

## Tests

<!-- test: network-marker-bad-value -->
<!-- network: loopback -->

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

```exitcode
0
```
