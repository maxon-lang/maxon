import contextlib
import json
import subprocess
import threading
import time
from pathlib import Path

import bench_common

ROOT = Path(bench_common.REPO)
MAXON_EXE = Path(bench_common.SLOT_COMPILER)
DRAIN_JOIN_SECONDS = 5
SHUTDOWN_REQUEST_ID = 99
EXIT_WAIT_SECONDS = 5
CODE_ACTION_REQUEST_ID = 2
CODE_ACTION_WAIT_SECONDS = 10


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


def await_response(proc, request_id, seconds):
  deadline = time.time() + seconds
  while time.time() < deadline:
    msg = read_message(proc, deadline)
    if msg is None:
      return None
    if msg.get("id") == request_id:
      return msg
  return None


def shut_down(proc):
  send(proc, {"jsonrpc": "2.0", "id": SHUTDOWN_REQUEST_ID, "method": "shutdown"})
  send(proc, {"jsonrpc": "2.0", "method": "exit"})
  try:
    proc.wait(timeout=EXIT_WAIT_SECONDS)
  except subprocess.TimeoutExpired:
    proc.terminate()


def drain(stream):
  while True:
    if not stream.readline():
      return


@contextlib.contextmanager
def lsp_server(exe, cwd):
  with subprocess.Popen(
    [str(exe), "lsp-server"],
    stdin=subprocess.PIPE,
    stdout=subprocess.PIPE,
    stderr=subprocess.PIPE,
    cwd=str(cwd),
  ) as proc:
    drainer = threading.Thread(target=drain, args=(proc.stderr,), daemon=True)
    drainer.start()
    try:
      yield proc
    finally:
      if proc.poll() is None:
        proc.kill()
      proc.wait()
      drainer.join(DRAIN_JOIN_SECONDS)
