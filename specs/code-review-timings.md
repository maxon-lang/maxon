# Code Review Timings

Wall-clock measurements taken at the end of each `/code-review` run. Helps
spot regressions in build / test time as the codebase grows.

`C# tests` is the C# bootstrap compiler's spec-test wall-clock; `SH x64` /
`SH wasm` are the self-hosted compiler's spec-test wall-clocks for the
x64-windows and wasm32-wasi targets respectively. `C# n` / `SH n` record
how many tests ran in each suite, and `W` is the `--workers=N` value the
self-hosted runner used (default in [MaxonArgs.maxon](../maxon-selfhosted/Compiler/MaxonArgs.maxon)).
Pre-2026-05-19 rows predate this columnization — `?` marks unrecorded values.

| Date | C# build | C# tests | C# n | Self-hosted build | SH x64 | SH wasm | SH n | W |
|------|---------:|---------:|-----:|------------------:|-------:|--------:|-----:|---:|
| 2026-05-12 |    9.3s |   15.2s |    ? |             14.0s |  10.0s |    5.0s |    ? |  ? |
| 2026-05-13 |   10.0s |   13.0s |    ? |             12.0s |   9.0s |    5.0s |    ? |  ? |
| 2026-05-12 |   10.4s |   14.4s |    ? |             11.4s |   9.8s |    6.1s |    ? |  ? |
| 2026-05-12 |    2.3s |   15.7s |    ? |             14.5s |  13.0s |    8.9s |    ? |  ? |
| 2026-05-12 |   10.0s |   14.1s |    ? |             13.1s |   9.9s |    5.7s |    ? |  ? |
| 2026-05-12 |   12.2s |   16.0s |    ? |             15.9s |  11.5s |    6.5s |    ? |  ? |
| 2026-05-12 |    1.9s |   14.2s |    ? |             12.3s |   9.9s |    5.9s |    ? |  ? |
| 2026-05-12 |   10.0s |   16.0s |    ? |             15.0s |  11.0s |    7.0s |    ? |  ? |
| 2026-05-14 |   10.3s |   13.3s |    ? |             17.0s |  16.7s |    9.6s |    ? |  ? |
| 2026-05-14 |   10.0s |   14.0s |    ? |             13.0s |  14.0s |    9.0s |    ? |  ? |
| 2026-05-14 |    7.1s |   14.1s |    ? |             12.7s |  16.5s |   11.7s |    ? |  ? |
| 2026-05-18 |    1.9s |   13.2s |    ? |             13.1s |  16.7s |   12.3s |    ? |  ? |
| 2026-05-18 |   10.8s |   14.1s |    ? |             13.4s |  14.9s |   11.7s |    ? |  ? |
| 2026-05-19 |    1.8s |   16.6s | 2742 |             15.2s |  24.9s |   15.1s | 1300 |  8 |
| 2026-05-19 |   11.7s |   18.5s | 2742 |             14.0s |  21.2s |   18.1s | 1300 |  8 |
| 2026-05-19 |    n/a |   16.0s | 2742 |             13.7s |  17.6s |   15.4s | 1300 |  8 |
| 2026-05-19 |    n/a |   16.0s | 2742 |             12.9s |  15.6s |   13.3s | 1300 |  8 |
| 2026-05-19 |    n/a |   15.3s | 2742 |             12.9s |  14.8s |   10.5s | 1300 | 12 |
| 2026-05-19 |    6.6s |   16.9s | 2742 |             17.6s |  17.4s |   15.7s | 1300 | 12 |
| 2026-05-19 |    1.9s |   18.1s | 2753 |             15.4s |  24.1s |   17.9s | 1355 | 12 |
| 2026-05-20 |    2.2s |   17.3s | 2753 |             13.5s |  18.1s |   16.8s | 1355 | 12 |
| 2026-05-20 |   12.6s |   17.9s | 2762 |             16.5s |  19.8s |   15.0s | 1364 | 12 |
| 2026-05-20 |    1.9s |   15.7s | 2753 |             14.4s |  21.7s |   14.6s | 1379 | 12 |
| 2026-05-20 |   10.1s |   17.9s | 2767 |             18.8s |  23.2s |   15.2s | 1393 | 12 |
| 2026-05-21 |    9.6s |   17.9s | 2767 |             15.3s |  20.1s |   13.6s | 1450 | 12 |
| 2026-05-21 |    9.0s |   19.8s | 2769 |             16.7s |  23.5s |   16.4s | 1452 | 12 |
| 2026-05-21 |   12.7s |   19.6s | 2769 |             19.9s |  24.3s |   20.0s | 1492 | 12 |
| 2026-05-22 |   10.0s |   17.0s | 2762 |             15.9s |  35.6s |   19.3s | 1586 | 12 |
