# Known issues

Open defects nobody has landed a fix for yet. `/land` §10 is the procedure: how an entry is recorded,
claimed and drained. An entry is deleted by the commit that fixes it.

`ci-red` entries stay above every `normal` one. Every edit to this file is committed at once, alone:
`git commit -F <msg> -- KNOWN_ISSUES.md`.

```
## <one-line symptom>
- priority: ci-red | normal
- suspects: <sha> <subject>, …          (ci-red only)
- status: open | claimed <session> <ISO date>
- where: file:line
- seen: how it was found, and by which task
- repro: a program that reproduces it, or for ci-red the run URL, lane and failing case
```
