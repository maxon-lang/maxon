Work here is compiler work.

@../maxon-bin/AGENTS.md

## Names

No single leading underscore for private members in `stdlib/` filenames, fields or functions: privacy is
the unmarked default, or `module`. Names starting `__` are compiler and runtime intrinsics, a separate
convention.

## Where a file goes

**The `stdlib/` root holds only the major modules: the files a user names.** A root file's `public`
declarations are the library's API, documented in `docs/STDLIB_REFERENCE.md` and checked by `tests/docs`.

**Everything else lives under `stdlib/helpers/<area>/`** (`helpers/tls/`, `helpers/crypto/`, `helpers/http/`,
…): the machinery a major module is built from — protocol state machines, codecs, configs, primitives a
module composes, shared byte helpers, test seams.

- A helper a root module uses is `public`: `module` is visible only in its declaring directory's subtree, so
  a `module` declaration under `helpers/` cannot be read from the root. A helper used only inside its own
  `helpers/<area>/` directory is `module` or file-private.
- A `public` helper is reachable by user programs, but it is not the library's documented API: it has no
  `STDLIB_REFERENCE.md` section and `tests/docs` does not scan `helpers/`.
- A new feature adds a root file only for an entry point a user calls; its building blocks go in helpers.
- Promoting a helper to the root is an API decision — ask first.
- A spec case that reaches a helper's `module` declarations carries `<!-- tier: stdlib/helpers/<area> -->`: it is staged in that
  directory, because a case at the `stdlib/` root cannot see them.
