# Changelog

Generated from git history by `scripts/changelog.sh`. **Do not edit by hand** — a
correction goes in `docs/changelog-overrides.txt`, and `--check` fails a release whose committed
file does not match what history says.

## 0.1.0 — 2026-09-08

- release.yml: the bootstrap case skips, it does not go red ([69d9705](https://github.com/maxon-lang/maxon/commit/69d9705d992cf06c4976fdef4b4c56b1d07db993))
- docs: a runtime change takes two self-compiles, and the tar bullet is current ([4a2f8e5](https://github.com/maxon-lang/maxon/commit/4a2f8e565cbe3a3e01b1a9c36e0077992ebfbbbe))
- release: probe tar for --mode instead of assuming GNU, and verify the shipped bit ([a8d9edd](https://github.com/maxon-lang/maxon/commit/a8d9edd93e3a6341bb1161e51d07ba73f7a959d5))
- added hello world example ([94c7a24](https://github.com/maxon-lang/maxon/commit/94c7a24aaac203fc805dd4312a021e4108ac029f))
- docs: stop naming platforms the compiler has outgrown ([a7bc537](https://github.com/maxon-lang/maxon/commit/a7bc537a7736ce429915fae19901ce488d9ec493))
- build: delete scripts/build.sh — the compiler builds this tree itself ([46f8466](https://github.com/maxon-lang/maxon/commit/46f8466aea463edbd52090000ac73a8bd2e9049a))
- compiler: create the output directory, and say so when it cannot ([e58246f](https://github.com/maxon-lang/maxon/commit/e58246f6a8e1995dff8a8eb81583db15eb6a2463))
- subprocessLastErrorMessage() renders `os error <n>`, not `win32 error <n>` ([5085d78](https://github.com/maxon-lang/maxon/commit/5085d789bd4ee319594c5deed179b728c5018836))
- InputSource.delayed feeds one second after the child's first stdout byte, not after the spawn ([13d20bf](https://github.com/maxon-lang/maxon/commit/13d20bf10b6b688aa3b0ac699663e974c73cde92))
- docs: how a release is built and published ([9bdce77](https://github.com/maxon-lang/maxon/commit/9bdce77dba78023c4a678730da7d87c20cc15520))
- release: keep the debug sidecar out of the archives ([faf3de4](https://github.com/maxon-lang/maxon/commit/faf3de4b9b54bdc7b07ba8fbe305ed07d646d8c4))
- installer: offer the VS Code extension when VS Code is present ([d9ac7d4](https://github.com/maxon-lang/maxon/commit/d9ac7d450e48ca109ac35b0d80a19854957fc445))
- release: write default notes, and ship the binary executable ([c23b096](https://github.com/maxon-lang/maxon/commit/c23b0967f11cef729701f3e9699dab7256f6b9f9))
- macOS installs with Homebrew, and the tap updates itself from the release ([f684bbd](https://github.com/maxon-lang/maxon/commit/f684bbd9962ec22dc3ea0431ae3d49186a2c949b))
- The extension is published: display name `Maxon`, because the marketplace refused the old one ([ed78f89](https://github.com/maxon-lang/maxon/commit/ed78f890def9c4235d91ecc35f3e5d7b7b4bc640))
- The extension has a publisher, and a workflow that publishes it to both registries ([ebcebd5](https://github.com/maxon-lang/maxon/commit/ebcebd5d9acd5e70893484dd6db5b6d7d2806da0))
- C6: the extension can find a compiler it did not build, and offers to get one when it cannot ([947ecce](https://github.com/maxon-lang/maxon/commit/947eccef9380240d428728f8e3c6052a16b8a41f))
- C5: CI on all four architectures, issue and PR templates, and a security policy ([11edbfe](https://github.com/maxon-lang/maxon/commit/11edbfeb5b6f417f7d0d6da229120d182fc92667))
- C3: a Windows installer built from the published archive, and the refusal an installed compiler makes reachable ([6d6d0b8](https://github.com/maxon-lang/maxon/commit/6d6d0b80b9b3ffe6b134696c63feffd840bec276))
- The build manifest is a task, not a program — `build`, not `main` — and the build system is documented ([b46c9bc](https://github.com/maxon-lang/maxon/commit/b46c9bcc4323f4abee13028f1585bb8098c3f810))
- `maxon build` at the root builds this compiler: the manifest is a PROGRAM, and a compiler may replace its own running image ([b64016a](https://github.com/maxon-lang/maxon/commit/b64016af1b98f7e7b061ad46b39709084d031a78))
- C2: one archive per target, each built and tested on its own architecture ([aa7896a](https://github.com/maxon-lang/maxon/commit/aa7896a6282f56d5c953f0d9da22deeb779a222c))
- `maxon --version` answers, and the seed comes from the release page rather than a script ([2c8d1b7](https://github.com/maxon-lang/maxon/commit/2c8d1b7df2359ea1861f342d807f84f5e182202c))
- `bootstrap.sh` needs curl and nothing else — building this project should not start with installing a CLI ([c6a5f3b](https://github.com/maxon-lang/maxon/commit/c6a5f3bf9c12f6eba19bae717e7ad3b63704dd09))
