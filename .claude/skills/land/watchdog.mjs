import { execFileSync } from "node:child_process";
import { createHash } from "node:crypto";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";

const MS_PER_SECOND = 1000;
const SECONDS_PER_MINUTE = 60;
const MS_PER_MINUTE = MS_PER_SECOND * SECONDS_PER_MINUTE;
const WINDOWS_TICKS_PER_SECOND = 10_000_000;
const SECONDS_PER_DAY = 86_400;
const MAX_CLOCK_FIELDS = 3;

const DEFAULTS = Object.freeze({
  stallMinutes: 20,
  artifactMinutes: 60,
  cpuMinutes: 20,
  contextTokens: 150_000,
  calls: 60,
  pollSeconds: 60,
});

const EXIT = Object.freeze({ success: 0, usage: 2, trip: 3 });

const TRIP = Object.freeze({
  stalled: "stalled",
  noProgress: "no-progress",
  cpuFlat: "cpu-flat",
  limit: "limit",
});

const TRANSCRIPT_EXTENSION = ".jsonl";
const META_EXTENSION = ".meta.json";
const AGENT_FILE_PREFIX = "agent-";
const SUBAGENTS_DIRECTORY = "subagents";
const PROJECTS_DIRECTORY = path.join(os.homedir(), ".claude", "projects");
const ASSIGNMENT_SEPARATOR = "=";
const LIST_SEPARATOR = ",";
const MISSING_SIGNATURE = "missing";

const NUMERIC_FLAGS = Object.freeze({
  "--stall-minutes": "stallMinutes",
  "--artifact-minutes": "artifactMinutes",
  "--cpu-minutes": "cpuMinutes",
  "--context-tokens": "contextTokens",
  "--calls": "calls",
  "--poll-seconds": "pollSeconds",
});

const HELP = `Usage: node watchdog.mjs --agent <name>=<agent id or transcript path> [--agent ...]
                        [--artifacts <name>=<path>,<path>...] [--pid <name>=<pid>] [--unbounded <name>]
                        [--stall-minutes <m>] [--artifact-minutes <m>] [--cpu-minutes <m>]
                        [--context-tokens <n>] [--calls <n>] [--poll-seconds <s>]

Polls each agent's transcript, artifacts and processes without any model call, and exits only on a trip
(exit ${EXIT.trip}), printing one line per trip: TRIP <agent> <trip> <detail>.

  ${TRIP.stalled}      the transcript has not grown for --stall-minutes (default ${DEFAULTS.stallMinutes})
  ${TRIP.noProgress}  the artifacts have not changed for --artifact-minutes (default ${DEFAULTS.artifactMinutes})
               while the transcript grew
  ${TRIP.cpuFlat}     a process's CPU time has not moved for --cpu-minutes (default ${DEFAULTS.cpuMinutes})
  ${TRIP.limit}        the latest call's context (input + cache read + cache creation) reached
               --context-tokens (default ${DEFAULTS.contextTokens}), or the distinct assistant messages
               with usage reached --calls (default ${DEFAULTS.calls}); never for an --unbounded agent

An agent whose transcript's ${META_EXTENSION} records it finished is no longer watched; when none is left the
watchdog prints IDLE and exits ${EXIT.success}. Polls every --poll-seconds (default ${DEFAULTS.pollSeconds}).
An agent id is resolved under ${path.join(PROJECTS_DIRECTORY, "*", "*", SUBAGENTS_DIRECTORY)}.`;

class UsageError extends Error {}

function splitAssignment(flag, value) {
  const at = value === undefined ? -1 : value.indexOf(ASSIGNMENT_SEPARATOR);

  if (at <= 0 || at === value.length - 1) {
    throw new UsageError(`${flag} takes <name>${ASSIGNMENT_SEPARATOR}<value>, got ${value ?? "nothing"}`);
  }

  return [value.slice(0, at), value.slice(at + 1)];
}

function parsePositiveNumber(flag, value) {
  const number = Number(value);

  if (value === undefined || !Number.isFinite(number) || number <= 0) {
    throw new UsageError(`${flag} takes a positive number, got ${value ?? "nothing"}`);
  }

  return number;
}

function parsePid(flag, value) {
  const pid = Number(value);

  if (!Number.isInteger(pid) || pid <= 0) {
    throw new UsageError(`${flag} takes a process id, got ${value}`);
  }

  return pid;
}

function parseArguments(argv) {
  const settings = { ...DEFAULTS };
  const agents = new Map();
  const artifacts = new Map();
  const pids = new Map();
  const unbounded = new Set();

  for (let index = 0; index < argv.length; index += 1) {
    const flag = argv[index];

    if (flag === "--help") {
      return { help: true };
    }

    const value = argv[index + 1];
    index += 1;

    if (flag in NUMERIC_FLAGS) {
      settings[NUMERIC_FLAGS[flag]] = parsePositiveNumber(flag, value);
    } else if (flag === "--agent") {
      const [name, source] = splitAssignment(flag, value);

      if (agents.has(name)) {
        throw new UsageError(`--agent ${name} is given twice`);
      }

      agents.set(name, source);
    } else if (flag === "--artifacts") {
      const [name, list] = splitAssignment(flag, value);
      const paths = list.split(LIST_SEPARATOR).filter((entry) => entry.length > 0);
      artifacts.set(name, [...(artifacts.get(name) ?? []), ...paths]);
    } else if (flag === "--pid") {
      const [name, pid] = splitAssignment(flag, value);
      pids.set(name, [...(pids.get(name) ?? []), parsePid(flag, pid)]);
    } else if (flag === "--unbounded") {
      if (value === undefined) {
        throw new UsageError(`${flag} takes an agent name`);
      }

      unbounded.add(value);
    } else {
      throw new UsageError(`unknown argument ${flag}`);
    }
  }

  if (agents.size === 0) {
    throw new UsageError("at least one --agent is required");
  }

  for (const name of [...artifacts.keys(), ...pids.keys(), ...unbounded]) {
    if (!agents.has(name)) {
      throw new UsageError(`${name} has artifacts, a pid or --unbounded but no --agent`);
    }
  }

  return { help: false, settings, agents, artifacts, pids, unbounded };
}

function isTranscriptPath(source) {
  return source.endsWith(TRANSCRIPT_EXTENSION) || source.includes("/") || source.includes("\\");
}

function readDirectoryNames(directory) {
  try {
    return fs.readdirSync(directory, { withFileTypes: true }).filter((entry) => entry.isDirectory()).map((entry) => entry.name);
  } catch (error) {
    if (error.code === "ENOENT") {
      return [];
    }

    throw error;
  }
}

function findTranscriptById(agentId) {
  const fileName = (agentId.startsWith(AGENT_FILE_PREFIX) ? agentId : AGENT_FILE_PREFIX + agentId) + TRANSCRIPT_EXTENSION;

  for (const project of readDirectoryNames(PROJECTS_DIRECTORY)) {
    for (const session of readDirectoryNames(path.join(PROJECTS_DIRECTORY, project))) {
      const candidate = path.join(PROJECTS_DIRECTORY, project, session, SUBAGENTS_DIRECTORY, fileName);

      if (fs.existsSync(candidate)) {
        return candidate;
      }
    }
  }

  return null;
}

function resolveTranscript(source) {
  if (isTranscriptPath(source)) {
    return fs.existsSync(source) ? path.resolve(source) : null;
  }

  return findTranscriptById(source);
}

function isFinished(transcriptPath) {
  const metaPath = transcriptPath.slice(0, -TRANSCRIPT_EXTENSION.length) + META_EXTENSION;

  try {
    return JSON.parse(fs.readFileSync(metaPath, "utf8")).finished === true;
  } catch (error) {
    if (error.code === "ENOENT" || error instanceof SyntaxError) {
      return false;
    }

    throw error;
  }
}

function contextOf(usage) {
  return (usage.input_tokens ?? 0) + (usage.cache_read_input_tokens ?? 0) + (usage.cache_creation_input_tokens ?? 0);
}

function absorbLine(agent, line) {
  if (line.trim().length === 0) {
    return;
  }

  let entry;

  try {
    entry = JSON.parse(line);
  } catch (error) {
    if (error instanceof SyntaxError) {
      return;
    }

    throw error;
  }

  const usage = entry?.message?.usage;

  if (entry.type !== "assistant" || usage === undefined || usage === null) {
    return;
  }

  agent.callIds.add(entry.message.id ?? entry.requestId ?? entry.uuid);
  agent.context = contextOf(usage);
}

function readTranscript(agent) {
  const { size, mtimeMs } = fs.statSync(agent.transcriptPath);

  if (size < agent.offset) {
    agent.offset = 0;
    agent.pending = "";
    agent.callIds.clear();
    agent.context = 0;
  }

  if (size === agent.offset) {
    return;
  }

  const length = size - agent.offset;
  const buffer = Buffer.alloc(length);
  const descriptor = fs.openSync(agent.transcriptPath, "r");

  try {
    fs.readSync(descriptor, buffer, 0, length, agent.offset);
  } finally {
    fs.closeSync(descriptor);
  }

  agent.offset = size;
  agent.transcriptGrewAt = mtimeMs;
  const lines = (agent.pending + buffer.toString("utf8")).split("\n");
  agent.pending = lines.pop();

  for (const line of lines) {
    absorbLine(agent, line);
  }
}

function signatureOf(paths) {
  const hash = createHash("sha256");
  let newest = 0;

  const visit = (target) => {
    let stat;

    try {
      stat = fs.lstatSync(target);
    } catch (error) {
      if (error.code === "ENOENT") {
        hash.update(`${target}\0${MISSING_SIGNATURE}\n`);
        return;
      }

      throw error;
    }

    if (stat.isDirectory()) {
      hash.update(`${target}\0dir\n`);

      for (const name of fs.readdirSync(target).sort()) {
        visit(path.join(target, name));
      }
    } else {
      hash.update(`${target}\0${stat.size}\0${stat.mtimeMs}\n`);
    }

    newest = Math.max(newest, stat.mtimeMs);
  };

  for (const target of paths) {
    visit(target);
  }

  return { digest: hash.digest("hex"), newest };
}

function parsePosixCpuTime(text) {
  const [days, clock] = text.includes("-") ? text.split("-") : ["0", text];
  const parts = clock.split(":").map(Number);

  if (parts.length > MAX_CLOCK_FIELDS || !Number.isFinite(Number(days)) || parts.some((part) => !Number.isFinite(part))) {
    throw new Error(`unreadable CPU time ${text}`);
  }

  return Number(days) * SECONDS_PER_DAY + parts.reduce((seconds, part) => seconds * SECONDS_PER_MINUTE + part, 0);
}

function runSampler(command, args) {
  try {
    return execFileSync(command, args, { encoding: "utf8", stdio: ["ignore", "pipe", "ignore"], windowsHide: true });
  } catch (error) {
    if (typeof error.stdout === "string") {
      return error.stdout;
    }

    throw error;
  }
}

function sampleCpuSeconds(pids) {
  const seconds = new Map();

  if (pids.length === 0) {
    return seconds;
  }

  if (process.platform === "win32") {
    const script = `Get-Process -Id ${pids.join(LIST_SEPARATOR)} -ErrorAction SilentlyContinue | ForEach-Object { "$($_.Id) $($_.TotalProcessorTime.Ticks)" }`;

    for (const line of runSampler("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script]).split(/\r?\n/)) {
      const [pid, ticks] = line.trim().split(/\s+/);

      if (pid !== undefined && ticks !== undefined) {
        seconds.set(Number(pid), Number(ticks) / WINDOWS_TICKS_PER_SECOND);
      }
    }

    return seconds;
  }

  for (const line of runSampler("ps", ["-o", "pid=,time=", "-p", pids.join(LIST_SEPARATOR)]).split("\n")) {
    const [pid, time] = line.trim().split(/\s+/);

    if (pid !== undefined && time !== undefined) {
      seconds.set(Number(pid), parsePosixCpuTime(time));
    }
  }

  return seconds;
}

function minutesSince(now, then) {
  return (now - then) / MS_PER_MINUTE;
}

function formatMinutes(minutes) {
  return `${minutes.toFixed(1)} min`;
}

function createAgent(name, source, bounded, artifactPaths, pids, now) {
  const artifacts = artifactPaths.length > 0 ? signatureOf(artifactPaths) : null;

  return {
    name,
    source,
    bounded,
    transcriptPath: null,
    offset: 0,
    pending: "",
    callIds: new Set(),
    context: 0,
    transcriptGrewAt: now,
    artifactPaths,
    artifactSignature: artifacts?.digest ?? null,
    artifactChangedAt: artifacts === null || artifacts.newest === 0 ? now : artifacts.newest,
    processes: pids.map((pid) => ({ pid, cpuSeconds: null, cpuChangedAt: now })),
  };
}

function checkAgent(agent, settings, cpuSeconds, now) {
  const trips = [];

  if (agent.transcriptPath !== null) {
    readTranscript(agent);

    if (agent.bounded && agent.context >= settings.contextTokens) {
      trips.push([TRIP.limit, `context ${agent.context} tokens >= ${settings.contextTokens}`]);
    }

    if (agent.bounded && agent.callIds.size >= settings.calls) {
      trips.push([TRIP.limit, `${agent.callIds.size} model calls >= ${settings.calls}`]);
    }
  }

  const quiet = minutesSince(now, agent.transcriptGrewAt);

  if (quiet >= settings.stallMinutes) {
    const subject = agent.transcriptPath ?? `transcript for ${agent.source} not found`;
    trips.push([TRIP.stalled, `${subject} has not grown for ${formatMinutes(quiet)}`]);
  }

  if (agent.artifactSignature !== null) {
    const { digest } = signatureOf(agent.artifactPaths);

    if (digest !== agent.artifactSignature) {
      agent.artifactSignature = digest;
      agent.artifactChangedAt = now;
    }

    const unchanged = minutesSince(now, agent.artifactChangedAt);

    if (unchanged >= settings.artifactMinutes && agent.transcriptGrewAt > agent.artifactChangedAt) {
      trips.push([TRIP.noProgress, `artifacts unchanged for ${formatMinutes(unchanged)} while the transcript grew`]);
    }
  }

  agent.processes = agent.processes.filter((entry) => cpuSeconds.has(entry.pid));

  for (const entry of agent.processes) {
    const sample = cpuSeconds.get(entry.pid);

    if (entry.cpuSeconds !== sample) {
      entry.cpuSeconds = sample;
      entry.cpuChangedAt = now;
    }

    const flat = minutesSince(now, entry.cpuChangedAt);

    if (flat >= settings.cpuMinutes) {
      trips.push([TRIP.cpuFlat, `process ${entry.pid} CPU flat at ${sample.toFixed(2)} s for ${formatMinutes(flat)}`]);
    }
  }

  return trips;
}

function poll(state) {
  const now = Date.now();

  for (const agent of state.agents) {
    if (agent.transcriptPath === null) {
      agent.transcriptPath = resolveTranscript(agent.source);
    }
  }

  state.agents = state.agents.filter((agent) => agent.transcriptPath === null || !isFinished(agent.transcriptPath));

  if (state.agents.length === 0) {
    console.log("IDLE every watched agent has finished");
    process.exit(EXIT.success);
  }

  const cpuSeconds = sampleCpuSeconds(state.agents.flatMap((agent) => agent.processes.map((entry) => entry.pid)));
  const trips = state.agents.flatMap((agent) =>
    checkAgent(agent, state.settings, cpuSeconds, now).map(([trip, detail]) => `TRIP ${agent.name} ${trip} ${detail}`),
  );

  if (trips.length > 0) {
    console.log(trips.join("\n"));
    process.exit(EXIT.trip);
  }

  setTimeout(() => poll(state), state.settings.pollSeconds * MS_PER_SECOND);
}

function main() {
  let parsed;

  try {
    parsed = parseArguments(process.argv.slice(2));
  } catch (error) {
    if (error instanceof UsageError) {
      console.error(`watchdog: ${error.message}\n\n${HELP}`);
      process.exit(EXIT.usage);
    }

    throw error;
  }

  if (parsed.help) {
    console.log(HELP);
    process.exit(EXIT.success);
  }

  const now = Date.now();
  const agents = [...parsed.agents].map(([name, source]) =>
    createAgent(name, source, !parsed.unbounded.has(name), parsed.artifacts.get(name) ?? [], parsed.pids.get(name) ?? [], now),
  );

  poll({ settings: parsed.settings, agents });
}

main();
