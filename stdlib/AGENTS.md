Work here is compiler work.

@../maxon-bin/AGENTS.md

## Where a file goes

**The `stdlib/` root holds only the major modules: the files a user names.** A root file's `public`
declarations are the library's API, documented in `docs/STDLIB_REFERENCE.md` and checked by `tests/docs`.

**Everything else lives under `stdlib/helpers/<area>/`** (`helpers/tls/`, `helpers/crypto/`, `helpers/http/`,
…): the machinery a major module is built from — protocol state machines, codecs, configs, primitives a
module composes, shared byte helpers, test seams. Helper declarations are `module` (or file-private), not
`public`; stdlib files share them across files freely. A helper type stays `public` only when a public
signature of a root module takes or returns it.

- A new feature adds a root file only for an entry point a user calls; its building blocks go in helpers.
- Spec cases reach a helper through `<!-- tier: stdlib -->`, never by making it `public`.
- Making a helper `public`, or promoting it to the root, is an API decision — ask first.
