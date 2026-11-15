#!/usr/bin/env node
import { execFileSync, spawnSync } from 'node:child_process';
import { readFileSync, existsSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { homedir, hostname } from 'node:os';
import { join } from 'node:path';

const TARGET_PERCENT = 98;
const WINDOW_HOURS = { five_hour: 5, seven_day: 168 };
const LIVE_HEARTBEAT_MINUTES = 30;
const RATE_SPAN_HOURS = 2;
const MIN_RATE_SPAN_MINUTES = 20;
const HISTORY_HOURS = 12;
const HISTORY_SPACING_SECONDS = 240;
const SAME_WINDOW_TOLERANCE_SECONDS = 120;
const DEFAULT_WAIT_SECONDS = 90;
const WAIT_TIMEOUT_EXIT = 3;
const ACCOUNT_LABEL_LENGTH = 8;
const ORCHESTRATOR_ID = 'orchestrator';
const STATES = ['working', 'idle', 'paused'];
const LABELS = { mailbox: 'mailbox', task: 'task', fleet: 'fleet' };
const FLEET_ISSUE_TITLE = 'fleet';
const USAGE_FILE = 'plan-usage.json';
const READ_REACTION = '+1';

function fail(message) {
  console.error(`fleet: ${message}`);
  process.exit(1);
}

function repo() {
  const name = process.env.MAXON_FLEET_REPO;
  if (!name) fail('MAXON_FLEET_REPO is not set; name the private fleet repository as owner/name');
  return name;
}

function gh(args, input) {
  return execFileSync('gh', args, {
    encoding: 'utf8',
    input,
    stdio: [input === undefined ? 'ignore' : 'pipe', 'pipe', 'inherit'],
    maxBuffer: 64 * 1024 * 1024,
  });
}

function api(method, path, body) {
  const args = ['api', '-X', method, path];
  if (body !== undefined) args.push('--input', '-');
  const out = gh(args, body === undefined ? undefined : JSON.stringify(body));
  return out.trim() ? JSON.parse(out) : null;
}

function apiAll(path) {
  return JSON.parse(gh(['api', '--paginate', '--slurp', path])).flat();
}

function parseArgs(argv) {
  const options = {};
  const positional = [];

  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];

    if (arg.startsWith('--')) {
      const value = argv[i + 1];
      if (value === undefined || value.startsWith('--')) fail(`${arg} needs a value`);
      options[arg.slice(2)] = value;
      i++;
    } else {
      positional.push(arg);
    }
  }

  return { options, positional };
}

function required(options, name) {
  const value = options[name];
  if (value === undefined) fail(`--${name} is required`);
  return value;
}

function seconds(options, name, fallback) {
  const raw = options[name];
  if (raw === undefined) return fallback;
  const value = Number(raw);
  if (!Number.isFinite(value) || value <= 0) fail(`--${name} must be a positive number of seconds`);
  return value;
}

function nowSeconds() {
  return Date.now() / 1000;
}

function openIssues(label) {
  return apiAll(`repos/${repo()}/issues?labels=${encodeURIComponent(label)}&state=open&per_page=100`);
}

function matchingIssues(label, title) {
  return openIssues(label).filter(issue => issue.title === title).sort((a, b) => a.number - b.number);
}

function findIssue(label, title) {
  return matchingIssues(label, title)[0];
}

function ensureIssue(label, title) {
  return findIssue(label, title) ?? api('POST', `repos/${repo()}/issues`, { title, labels: [label] });
}

function comments(issue) {
  return apiAll(`repos/${repo()}/issues/${issue.number}/comments?per_page=100`);
}

function mailboxTitle(id) {
  return `mailbox: ${id}`;
}

function unread(id) {
  return matchingIssues(LABELS.mailbox, mailboxTitle(id))
    .flatMap(comments)
    .filter(comment => (comment.reactions?.[READ_REACTION] ?? 0) === 0);
}

function marker(kind, key) {
  return `<!-- fleet-${kind}:${key} -->`;
}

function jsonBody(kind, key, data) {
  return `${marker(kind, key)}\n\`\`\`json\n${JSON.stringify(data, null, 2)}\n\`\`\``;
}

function parseJsonBody(body) {
  const match = /```json\n([\s\S]*?)\n```/.exec(body);
  if (!match) throw new Error('a fleet comment has no json block');
  return JSON.parse(match[1]);
}

function upsertComment(issue, existing, kind, key, data) {
  const body = jsonBody(kind, key, data);
  if (existing) return api('PATCH', `repos/${repo()}/issues/comments/${existing.id}`, { body });
  return api('POST', `repos/${repo()}/issues/${issue.number}/comments`, { body });
}

function fleetIssue() {
  const issue = findIssue(LABELS.fleet, FLEET_ISSUE_TITLE);
  if (!issue) fail('no fleet issue; run `fleet.mjs init` once');
  return issue;
}

function readFleet() {
  const issue = fleetIssue();
  const all = comments(issue);
  const pick = kind => all
    .filter(comment => comment.body.startsWith(`<!-- fleet-${kind}:`))
    .map(comment => ({ comment, key: /^<!-- fleet-\w+:(.*?) -->/.exec(comment.body)[1], data: parseJsonBody(comment.body) }));
  return { issue, heartbeats: pick('heartbeat'), histories: pick('history') };
}

function configDir() {
  return process.env.CLAUDE_CONFIG_DIR || join(homedir(), '.claude');
}

function account() {
  const result = spawnSync('claude', ['auth', 'status'], { encoding: 'utf8', shell: true });
  if (result.status !== 0) fail(`claude auth status exited ${result.status}`);
  const status = JSON.parse(result.stdout);
  if (!status.orgId) fail('claude auth status names no organisation; is this session logged in to claude.ai?');
  const label = createHash('sha256').update(status.orgId).digest('hex').slice(0, ACCOUNT_LABEL_LENGTH);
  return { label, planTier: status.planTier ?? status.subscriptionType ?? 'unknown' };
}

function localUsage() {
  const path = join(configDir(), USAGE_FILE);
  if (!existsSync(path)) return null;
  return JSON.parse(readFileSync(path, 'utf8'));
}

function commandInit() {
  for (const name of Object.values(LABELS)) {
    try {
      api('POST', `repos/${repo()}/labels`, { name });
    } catch {
      if (!apiAll(`repos/${repo()}/labels?per_page=100`).some(label => label.name === name)) fail(`could not create label ${name}`);
    }
  }

  const issue = ensureIssue(LABELS.fleet, FLEET_ISSUE_TITLE);
  console.log(`fleet issue #${issue.number} ready in ${repo()}`);
}

function commandSend(options) {
  const from = required(options, 'as');
  const to = required(options, 'to');
  const text = readFileSync(required(options, 'file'), 'utf8').trim();
  const box = ensureIssue(LABELS.mailbox, mailboxTitle(to));
  const posted = api('POST', `repos/${repo()}/issues/${box.number}/comments`, { body: `FROM ${from}\n\n${text}` });
  console.log(`sent ${posted.id} to ${to}`);
}

function commandInbox(options) {
  const messages = unread(required(options, 'as'));

  if (messages.length === 0) {
    console.log('no messages');
    return;
  }

  for (const message of messages) console.log(`=== ${message.id} ${message.created_at}\n${message.body}\n`);
}

function commandAck(options, ids) {
  required(options, 'as');
  if (ids.length === 0) fail('ack needs the ids of the messages read');
  for (const id of ids) api('POST', `repos/${repo()}/issues/comments/${id}/reactions`, { content: READ_REACTION });
  console.log(`acknowledged ${ids.length}`);
}

function commandWait(options) {
  const id = required(options, 'as');
  const interval = seconds(options, 'interval', DEFAULT_WAIT_SECONDS);
  const max = seconds(options, 'max', Infinity);
  const start = nowSeconds();

  for (;;) {
    const count = unread(id).length;

    if (count > 0) {
      console.log(`${count} message(s) for ${id}`);
      return;
    }

    if (nowSeconds() - start + interval > max) {
      console.log(`no messages for ${id} within ${max} s`);
      process.exit(WAIT_TIMEOUT_EXIT);
    }

    Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, interval * 1000);
  }
}

function commandHeartbeat(options) {
  const id = required(options, 'as');
  const state = required(options, 'state');
  if (!STATES.includes(state)) fail(`--state must be one of ${STATES.join(', ')}`);
  const { label, planTier } = account();
  const usage = localUsage();
  const data = {
    id,
    account: label,
    planTier,
    model: options.model ?? usage?.model ?? required(options, 'model'),
    machine: hostname(),
    state,
    task: options.task ?? null,
    at: Math.round(nowSeconds()),
    usage,
  };
  const { issue, heartbeats } = readFleet();
  upsertComment(issue, heartbeats.find(entry => entry.key === id)?.comment, 'heartbeat', id, data);
  console.log(`heartbeat ${id} ${state} on account ${label}${usage ? '' : ' (no usage sample yet)'}`);
}

function ageMinutes(at) {
  return (nowSeconds() - at) / 60;
}

function commandStatus() {
  const { heartbeats } = readFleet();
  const rows = heartbeats
    .map(entry => entry.data)
    .sort((a, b) => a.account.localeCompare(b.account) || a.id.localeCompare(b.id));

  for (const row of rows) {
    const live = ageMinutes(row.at) < LIVE_HEARTBEAT_MINUTES ? 'live' : 'stale';
    console.log(`${row.account}  ${row.id.padEnd(24)} ${row.state.padEnd(8)} ${String(row.model).padEnd(18)} ${row.machine.padEnd(16)} ${live} ${Math.round(ageMinutes(row.at))}m  ${row.task ?? ''}`);
  }
}

function windowState(sample, name, now) {
  const window = sample?.rate_limits?.[name];
  if (!window || window.resets_at <= now) return null;
  return { used: window.used_percentage, resetsAt: window.resets_at };
}

function perConsumerRate(history, name, current, now) {
  const points = history.filter(point =>
    point[name] &&
    Math.abs(point[name].resetsAt - current.resetsAt) < SAME_WINDOW_TOLERANCE_SECONDS &&
    point.at >= now - RATE_SPAN_HOURS * 3600);
  if (points.length < 2) return null;
  const first = points[0];
  const last = points[points.length - 1];
  const spanHours = (last.at - first.at) / 3600;
  if (spanHours * 60 < MIN_RATE_SPAN_MINUTES) return null;
  const meanConsumers = points.reduce((sum, point) => sum + point.consumers, 0) / points.length;
  if (meanConsumers === 0) return null;
  return (last[name].used - first[name].used) / spanHours / meanConsumers;
}

function allowedConsumers(current, rate, now) {
  if (!current) return { limit: Infinity, why: 'no such window on this plan, or it has reset' };
  const headroom = TARGET_PERCENT - current.used;
  if (headroom <= 0) return { limit: 0, why: `at ${current.used}%` };
  if (rate === null) return { limit: Infinity, why: 'burn rate not yet measured' };
  if (rate <= 0) return { limit: Infinity, why: 'no measurable burn' };
  const hoursLeft = (current.resetsAt - now) / 3600;
  return { limit: headroom / (rate * hoursLeft), why: `${rate.toFixed(2)}%/h per consumer, ${hoursLeft.toFixed(1)} h left` };
}

function commandBudget() {
  const { issue, heartbeats, histories } = readFleet();
  const now = nowSeconds();
  const accounts = [...new Set(heartbeats.map(entry => entry.data.account))].sort();
  const plan = [];

  for (const acct of accounts) {
    const live = heartbeats.map(entry => entry.data).filter(row => row.account === acct && ageMinutes(row.at) < LIVE_HEARTBEAT_MINUTES);
    const orchestrator = live.some(row => row.id === ORCHESTRATOR_ID) ? 1 : 0;
    const workers = live.filter(row => row.id !== ORCHESTRATOR_ID);
    const working = workers.filter(row => row.state === 'working').length;
    const available = workers.length;
    const sample = live.map(row => row.usage).filter(Boolean).sort((a, b) => b.at - a.at)[0] ?? null;

    const stored = histories.find(entry => entry.key === acct);
    let history = stored?.data ?? [];

    if (sample && (history.length === 0 || sample.at - history[history.length - 1].at >= HISTORY_SPACING_SECONDS)) {
      const point = { at: sample.at, consumers: working + orchestrator };
      for (const name of Object.keys(WINDOW_HOURS)) point[name] = windowState(sample, name, now);
      history.push(point);
    }

    history = history.filter(point => point.at >= now - HISTORY_HOURS * 3600);
    upsertComment(issue, stored?.comment, 'history', acct, history);

    const windows = Object.keys(WINDOW_HOURS).map(name => {
      const current = windowState(sample, name, now);
      const allowed = allowedConsumers(current, current ? perConsumerRate(history, name, current, now) : null, now);
      return { name, used: current?.used ?? null, ...allowed, limit: allowed.limit === Infinity ? 'unbounded' : Number(allowed.limit.toFixed(2)) };
    });

    const limit = Math.min(...windows.map(window => window.limit));
    const ceiling = limit === Infinity ? working + 1 : Math.floor(limit) - orchestrator;
    const target = sample ? Math.max(0, Math.min(ceiling, working + 1, available)) : working;
    const action = !sample ? 'hold (no usage sample)'
      : target > working ? `start ${target - working}`
        : target < working ? `pause ${working - target}`
          : 'hold';

    plan.push({ account: acct, working, available, orchestrator: orchestrator === 1, target, action, sampleAgeMinutes: sample ? Math.round(ageMinutes(sample.at)) : null, windows });
  }

  console.log(JSON.stringify(plan, null, 2));
}

const COMMANDS = {
  init: commandInit,
  send: commandSend,
  inbox: commandInbox,
  ack: commandAck,
  wait: commandWait,
  heartbeat: commandHeartbeat,
  status: commandStatus,
  budget: commandBudget,
};

const [name, ...rest] = process.argv.slice(2);
const command = COMMANDS[name];
if (!command) fail(`usage: fleet.mjs <${Object.keys(COMMANDS).join('|')}> [--as <id>] …`);
const { options, positional } = parseArgs(rest);
command(options, positional);
