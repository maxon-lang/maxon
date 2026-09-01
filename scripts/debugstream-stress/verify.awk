# Verify a `maxon monitor --filter=log` trace of ds-race.maxon.
#
# Every decoded event must satisfy the invariant its own payload encodes, and the number of decoded
# events must equal the number the program says it emitted. A torn read — the monitor copying a
# payload the producer had not yet written — violates one of these no matter which bytes it caught:
#
#   * fresh ring memory (all zeros) => cat/lvl are 0, gt is null, arg0 is 0 (out of range), the
#     checksum fails, and a LOG_TEXT tail is not the message its own header claims.
#   * a previous generation's payload at the same ring offset => internally consistent, but its
#     (unit, seq) was already consumed => a DUPLICATE.
#
# Invoke with:
#   -v threads=N -v perThread=M -v texts=T -v seqBase=B -v mul=X -v add=Y -v cat=C -v lvl=L -v padLen=P
#
# Exits 0 iff the trace is clean. Prints one VIOLATION line per bad entry (capped) and a verdict.

BEGIN {
	maxViolationsPrinted = 8
	violations = 0
	duplicates = 0

	decodedEvents = 0
	decodedTexts = 0

	emitted = -1
	emittedTexts = -1
	dropped = -1
	abandoned = 0

	expectedEvents = threads * perThread
	expectedTexts = threads * texts
	maxArg0 = 1 + (threads - 1) * seqBase + (perThread - 1)
}

function violation(what, line) {
	violations++
	if (violations <= maxViolationsPrinted)
		printf("  VIOLATION:%s  <<%s>>\n", what, substr(line, 1, 110))
	else if (violations == maxViolationsPrinted + 1)
		printf("  ... (further violations suppressed)\n")
}

# The program prints these once every producer has finished: the exact numbers of entries that went
# into the ring. Forwarded to the monitor's stdout, so they land in the same stream as the trace.
/^emitted=/      { split($0, kv, "="); emitted      = kv[2] + 0; next }
/^emittedTexts=/ { split($0, kv, "="); emittedTexts = kv[2] + 0; next }

# The monitor's summary, on stderr but merged into this stream by the driver. A DROPPED event never
# reached the ring at all — a different failure from a TORN one, and one that would make the exact
# count check meaningless — so it is surfaced separately rather than lumped in.
/^\[debugstream\]/ {
	if (match($0, /[0-9]+ dropped/))   { s = substr($0, RSTART, RLENGTH); split(s, d, " "); dropped   = d[1] + 0 }
	if (match($0, /[0-9]+ abandoned/)) { s = substr($0, RSTART, RLENGTH); split(s, a, " "); abandoned = a[1] + 0 }
	next
}

# [+0000.012] log_event stress cat=7 lvl=5 gt=0x21d4... P3 unit=4 a0=3000001 a1=9000014
/ log_event / {
	decodedEvents++

	name = ""; c = -1; l = -1; gt = ""; unit = -1; a0 = -1; a1 = -1
	for (i = 1; i <= NF; i++) {
		if ($i == "log_event")  name = $(i + 1)
		else if ($i ~ /^cat=/)  { split($i, t, "="); c    = t[2] + 0 }
		else if ($i ~ /^lvl=/)  { split($i, t, "="); l    = t[2] + 0 }
		else if ($i ~ /^gt=/)   { split($i, t, "="); gt   = t[2] }
		else if ($i ~ /^unit=/) { split($i, t, "="); unit = t[2] + 0 }
		else if ($i ~ /^a0=/)   { split($i, t, "="); a0   = t[2] + 0 }
		else if ($i ~ /^a1=/)   { split($i, t, "="); a1   = t[2] + 0 }
	}

	why = ""
	if (name != "stress")       why = why " name(" name ")"
	if (c != cat)               why = why " cat(" c ")"
	if (l != lvl)               why = why " lvl(" l ")"
	if (gt == "0x0")            why = why " gt(null)"
	if (a0 < 1 || a0 > maxArg0) why = why " a0-range(" a0 ")"
	if (a1 != a0 * mul + add)   why = why " checksum(a1=" a1 " want=" (a0 * mul + add) ")"

	if (a0 >= 1 && a0 <= maxArg0) {
		idx = int((a0 - 1) / seqBase)
		seq = (a0 - 1) % seqBase

		if (seq >= perThread) why = why " seq-range(" seq ")"
		if (unit != idx + 1)  why = why " unit(" unit " want=" (idx + 1) ")"

		if (a0 in seenEvent) { duplicates++; why = why " duplicate-a0(" a0 ")" }
		seenEvent[a0] = 1
	}

	if (why != "") violation(why, $0)
	next
}

# [+0000.012] log_text cat=7 lvl=5 gt=0x21d4... P3 unit=4 t3s17-AAAA...
# The tail is the whole point: it is written by a byte-copy loop AFTER the entry became visible, so
# it is the field a torn read mangles. Its content names the thread and its own sequence, so it can
# be checked against the entry's own `unit` with no reference to any other entry.
/ log_text / {
	decodedTexts++

	c = -1; l = -1; gt = ""; unit = -1; tail = ""
	for (i = 1; i <= NF; i++) {
		if ($i ~ /^cat=/)       { split($i, t, "="); c    = t[2] + 0 }
		else if ($i ~ /^lvl=/)  { split($i, t, "="); l    = t[2] + 0 }
		else if ($i ~ /^gt=/)   { split($i, t, "="); gt   = t[2] }
		else if ($i ~ /^unit=/) { split($i, t, "="); unit = t[2] + 0; tail = $(i + 1) }
	}

	why = ""
	if (c != cat)    why = why " cat(" c ")"
	if (l != lvl)    why = why " lvl(" l ")"
	if (gt == "0x0") why = why " gt(null)"

	# The exact shape the producer wrote: t<idx>s<seq>- followed by padLen 'A's, and nothing else.
	# Anything the monitor saw instead of that is bytes the producer had not written yet.
	if (tail !~ /^t[0-9]+s[0-9]+-A+$/) {
		why = why " torn-tail(len=" length(tail) ")"
	} else {
		dash = index(tail, "-")
		if (length(tail) - dash != padLen) why = why " tail-len(" (length(tail) - dash) " want=" padLen ")"

		body = substr(tail, 2, dash - 2)          # "<idx>s<seq>"
		split(body, part, "s")
		idx = part[1] + 0
		seq = part[2] + 0

		if (idx < 0 || idx >= threads) why = why " text-idx(" idx ")"
		if (seq < 0 || seq >= texts)   why = why " text-seq(" seq ")"
		if (unit != idx + 1)           why = why " unit(" unit " want=" (idx + 1) ")"

		key = idx "/" seq
		if (key in seenText) { duplicates++; why = why " duplicate-text(" key ")" }
		seenText[key] = 1
	}

	if (why != "") violation(why, $0)
	next
}

# THE TWO VERDICTS ARE SEPARATE, and keeping them apart is what lets the harness sweep a wide band
# of producer pacings instead of one lucky number.
#
#   INTEGRITY (exit 1) — every decoded entry satisfied its own payload invariant. This is THE race
#   gate, and it is meaningful whatever else happened: a torn payload is a torn payload even in a
#   run whose ring overflowed.
#
#   COMPLETENESS (exit 2) — nothing was lost or duplicated, and the decoded count equals the emitted
#   count. This one is only ANSWERABLE when the ring did not overflow: a DROPPED event never reached
#   the ring at all, which is a capacity fact about the test, not a correctness fact about the
#   compiler. A run that drops is INCONCLUSIVE on counts, not failing — and the driver requires that
#   at least one pacing in the sweep be conclusive.
END {
	printf("\n")
	printf("  log_event : decoded %d, emitted %s, expected %d\n",
		decodedEvents, (emitted < 0 ? "MISSING" : emitted), expectedEvents)
	printf("  log_text  : decoded %d, emitted %s, expected %d\n",
		decodedTexts, (emittedTexts < 0 ? "MISSING" : emittedTexts), expectedTexts)
	printf("  dropped   : %s\n", (dropped < 0 ? "MISSING (no [debugstream] summary)" : dropped))
	printf("  abandoned : %d\n", abandoned)
	printf("  VIOLATIONS: %d  (of which duplicates: %d)\n", violations, duplicates)

	if (violations > 0) {
		printf("  FAIL: %d decoded entries did not satisfy their own payload invariant — TORN READS\n", violations)
		exit 1
	}
	if (abandoned != 0) {
		printf("  FAIL: %d entries abandoned, but no producer was killed in this run\n", abandoned)
		exit 1
	}
	if (dropped < 0) {
		printf("  FAIL: no [debugstream] summary line — the monitor did not finish\n")
		exit 1
	}
	if (emitted < 0 || emittedTexts < 0) {
		printf("  FAIL: the program did not print its emitted counts\n")
		exit 1
	}
	if (emitted != expectedEvents || emittedTexts != expectedTexts) {
		printf("  FAIL: the program emitted %d/%d, expected %d/%d — the test is misconfigured\n",
			emitted, emittedTexts, expectedEvents, expectedTexts)
		exit 1
	}

	if (dropped != 0) {
		printf("  INTEGRITY OK (no torn payloads); COUNTS INCONCLUSIVE: %d events dropped (ring overflowed)\n", dropped)
		exit 2
	}

	if (decodedEvents != emitted) {
		printf("  FAIL: decoded %d log_events but %d were emitted (lost or duplicated)\n", decodedEvents, emitted)
		exit 1
	}
	if (decodedTexts != emittedTexts) {
		printf("  FAIL: decoded %d log_texts but %d were emitted (lost or duplicated)\n", decodedTexts, emittedTexts)
		exit 1
	}

	printf("  PASS: %d events + %d texts, every payload self-consistent, none lost, none duplicated\n",
		decodedEvents, decodedTexts)
	exit 0
}
