"""Smoke test for the LSP E3010 quick fix in a multi-file project.

Creates a temp project directory containing build.maxon and two .maxon files,
each with multiple unneeded casts. Opens one of them in the LSP and verifies
that:
  - per-diagnostic fixes are produced for the diagnostics in scope,
  - "Remove all unneeded casts in file" combines just the active file's edits,
  - "Remove all unneeded casts in project" combines edits across both files.

Exits 0 on success, non-zero on any mismatch."""

import json
import os
import shutil
import subprocess
import sys
import threading
import time
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
MAXON_EXE = ROOT / "bin" / "maxon.exe"

# A build.maxon counts as a project root only when it declares an exported
# build() function (see ProjectManager.HasExportedBuildFunction). Without one,
# each .maxon file is treated as its own single-file project.
BUILD_MAXON = """export function build()
end 'build'
"""

# Both files define their own helper so neither has to import the other —
# this isolates fix-all-in-project from cross-file resolution issues.
FILE_A = """typealias ByteA = int(0 to u8.max)

function helperA() returns ByteA
\tlet a = 1 as ByteA
\tlet b = a as ByteA
\tlet c = b as ByteA
\treturn c
end 'helperA'
"""

FILE_B = """typealias ByteB = int(0 to u8.max)

function main() returns ExitCode
\tlet a = 1 as ByteB
\tlet b = a as ByteB
\tlet c = b as ByteB
\treturn c
end 'main'
"""


def send(proc, payload):
  body = json.dumps(payload).encode("utf-8")
  header = f"Content-Length: {len(body)}\r\n\r\n".encode("ascii")
  proc.stdin.write(header + body)
  proc.stdin.flush()


def read_message(proc, deadline):
  header = b""
  while True:
    if time.time() > deadline:
      return None
    ch = proc.stdout.read(1)
    if not ch:
      return None
    header += ch
    if header.endswith(b"\r\n\r\n"):
      break
  length = 0
  for line in header.decode("ascii").split("\r\n"):
    if line.lower().startswith("content-length:"):
      length = int(line.split(":", 1)[1].strip())
  body = b""
  while len(body) < length:
    chunk = proc.stdout.read(length - len(body))
    if not chunk:
      return None
    body += chunk
  return json.loads(body.decode("utf-8"))


def main():
  if not MAXON_EXE.exists():
    print(f"missing {MAXON_EXE}", file=sys.stderr)
    return 1

  tmp_dir = Path(os.environ.get("TEMP", "/tmp")) / "maxon-lsp-codefix-project-test"
  if tmp_dir.exists():
    shutil.rmtree(tmp_dir)
  tmp_dir.mkdir(parents=True)

  (tmp_dir / "build.maxon").write_text(BUILD_MAXON, encoding="utf-8")
  file_a = tmp_dir / "a.maxon"
  file_b = tmp_dir / "b.maxon"
  file_a.write_text(FILE_A, encoding="utf-8")
  file_b.write_text(FILE_B, encoding="utf-8")
  uri_a = file_a.as_uri()
  uri_b = file_b.as_uri()

  proc = subprocess.Popen(
    [str(MAXON_EXE), "lsp-server"],
    stdin=subprocess.PIPE,
    stdout=subprocess.PIPE,
    stderr=subprocess.PIPE,
    cwd=str(ROOT),
  )

  def drain_stderr():
    while True:
      line = proc.stderr.readline()
      if not line:
        return

  threading.Thread(target=drain_stderr, daemon=True).start()

  send(proc, {
    "jsonrpc": "2.0",
    "id": 1,
    "method": "initialize",
    "params": {
      "processId": os.getpid(),
      "rootUri": tmp_dir.as_uri(),
      "capabilities": {},
    },
  })

  diagnostics_a = []
  diagnostics_b = []
  initialized = False
  deadline = time.time() + 30
  # Need diagnostics from BOTH files before requesting code actions, so the
  # project's stored diagnostic set includes b.maxon.
  while time.time() < deadline:
    msg = read_message(proc, deadline)
    if msg is None:
      break
    method = msg.get("method")
    if msg.get("id") == 1 and "result" in msg:
      send(proc, {"jsonrpc": "2.0", "method": "initialized", "params": {}})
      send(proc, {
        "jsonrpc": "2.0",
        "method": "textDocument/didOpen",
        "params": {
          "textDocument": {
            "uri": uri_a,
            "languageId": "maxon",
            "version": 1,
            "text": FILE_A,
          }
        },
      })
      send(proc, {
        "jsonrpc": "2.0",
        "method": "textDocument/didOpen",
        "params": {
          "textDocument": {
            "uri": uri_b,
            "languageId": "maxon",
            "version": 1,
            "text": FILE_B,
          }
        },
      })
      initialized = True
    elif method == "textDocument/publishDiagnostics":
      params = msg.get("params", {})
      diag_uri = params.get("uri", "")
      diags = [d for d in params.get("diagnostics", []) if d.get("code") == "E3010"]
      if diag_uri.lower() == uri_a.lower() and diags:
        diagnostics_a = diags
        uri_a = diag_uri  # server's canonical spelling
      elif diag_uri.lower() == uri_b.lower() and diags:
        diagnostics_b = diags
        uri_b = diag_uri
      if diagnostics_a and diagnostics_b:
        break

  if not initialized:
    print("LSP never initialized", file=sys.stderr)
    proc.terminate()
    return 1
  if not diagnostics_a or not diagnostics_b:
    print(f"missing diagnostics: a={len(diagnostics_a)} b={len(diagnostics_b)}", file=sys.stderr)
    proc.terminate()
    return 1

  print(f"a.maxon: {len(diagnostics_a)} E3010(s), b.maxon: {len(diagnostics_b)} E3010(s)", flush=True)

  # Request code actions on a.maxon — the project-wide fix should include
  # b.maxon edits too.
  send(proc, {
    "jsonrpc": "2.0",
    "id": 2,
    "method": "textDocument/codeAction",
    "params": {
      "textDocument": {"uri": uri_a},
      "range": {
        "start": {"line": 0, "character": 0},
        "end": {"line": len(FILE_A.split("\n")), "character": 0},
      },
      "context": {"diagnostics": diagnostics_a},
    },
  })

  response = None
  deadline = time.time() + 10
  while time.time() < deadline:
    msg = read_message(proc, deadline)
    if msg is None:
      break
    if msg.get("id") == 2:
      response = msg
      break

  send(proc, {"jsonrpc": "2.0", "id": 99, "method": "shutdown"})
  send(proc, {"jsonrpc": "2.0", "method": "exit"})
  try:
    proc.wait(timeout=5)
  except subprocess.TimeoutExpired:
    proc.terminate()

  if response is None:
    print("no codeAction response", file=sys.stderr)
    return 1

  actions = response.get("result") or []
  print(f"got {len(actions)} action(s):")
  for a in actions:
    file_edits = a.get("edit", {}).get("changes", {})
    edit_count = sum(len(v) for v in file_edits.values())
    print(f"  - {a.get('title')!r} (kind={a.get('kind')}, edits={edit_count} across {len(file_edits)} file(s))")

  fix_all_file = [a for a in actions if a.get("kind") == "quickfix" and "in file" in a.get("title", "")]
  fix_all_project = [a for a in actions if a.get("kind") == "quickfix" and "in project" in a.get("title", "")]

  if len(fix_all_file) != 1:
    print(f"FAIL: expected 1 fix-all-in-file action, got {len(fix_all_file)}", file=sys.stderr)
    return 1
  file_edits = fix_all_file[0].get("edit", {}).get("changes", {})
  if len(file_edits) != 1 or uri_a not in file_edits:
    print(f"FAIL: fix-all-in-file should touch only a.maxon, got keys {list(file_edits.keys())}", file=sys.stderr)
    return 1
  if len(file_edits[uri_a]) != len(diagnostics_a):
    print(f"FAIL: fix-all-in-file edit count mismatch: {len(file_edits[uri_a])} vs {len(diagnostics_a)}", file=sys.stderr)
    return 1

  if len(fix_all_project) != 1:
    print(f"FAIL: expected 1 fix-all-in-project action, got {len(fix_all_project)}", file=sys.stderr)
    return 1
  project_edits = fix_all_project[0].get("edit", {}).get("changes", {})
  if len(project_edits) != 2:
    print(f"FAIL: fix-all-in-project should touch 2 files, got {len(project_edits)}: {list(project_edits.keys())}", file=sys.stderr)
    return 1
  total_project_edits = sum(len(v) for v in project_edits.values())
  expected_total = len(diagnostics_a) + len(diagnostics_b)
  if total_project_edits != expected_total:
    print(f"FAIL: fix-all-in-project edit count mismatch: {total_project_edits} vs {expected_total}", file=sys.stderr)
    return 1

  print("OK")
  return 0


if __name__ == "__main__":
  sys.exit(main())
