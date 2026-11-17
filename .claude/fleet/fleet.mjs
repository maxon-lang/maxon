#!/usr/bin/env node
import { execFileSync, spawnSync } from 'node:child_process';
import { readFileSync, existsSync, readdirSync, statSync, openSync, readSync, closeSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { homedir, hostname } from 'node:os';
import { join } from 'node:path';
import { unprioritizedIssues } from '../roadmap/unprioritized.mjs';

const TARGET_PERCENT = 95;
const STATUS_LINE_LIMIT_KINDS = { five_hour: 'session', seven_day: 'weekly_all' };
const USAGE_COMMAND = '/usage';
const LIVE_HEARTBEAT_MINUTES = 30;
const SILENT_HEARTBEAT_MINUTES = 60;
const RATE_SPAN_HOURS = 2;
const MIN_RATE_SPAN_MINUTES = 20;
const HISTORY_HOURS = 12;
const HISTORY_SPACING_SECONDS = 240;
const SAME_WINDOW_TOLERANCE_SECONDS = 120;
const DEFAULT_POLL_SECONDS = 30;
const DEFAULT_WAKE_MAX_SECONDS = 3000;
const WAKE_HEARTBEAT_SECONDS = 600;
const WAIT_TIMEOUT_EXIT = 3;
const ACCOUNT_LABEL_LENGTH = 8;
const ORCHESTRATOR_ID = 'orchestrator';
const STATE = { working: 'working', idle: 'idle', paused: 'paused' };
const STATES = Object.values(STATE);
const LIVENESS = { waiting: 'waiting', live: 'live', stale: 'stale', silent: 'silent' };
const LABELS = { mailbox: 'mailbox', task: 'task', fleet: 'fleet' };
const FLEET_ISSUE_TITLE = 'fleet';
const USAGE_FILE = 'plan-usage.json';
const READ_REACTION = '+1';
const WATERMARK_KIND = 'watermark';
const WATERMARK_PATTERN = new RegExp(`<!-- fleet-${WATERMARK_KIND}:(.*?) -->`);
const ACK_SEPARATOR = ',';
const GH_MAX_BUFFER = 64 * 1024 * 1024;
const HTTP_NOT_MODIFIED = 304;
const SESSION_ENV = 'CLAUDE_CODE_SESSION_ID';
const TRANSCRIPTS_DIR = 'projects';
const TRANSCRIPT_TAIL_BYTES = 1024 * 1024;
const TOKENS_PER_THOUSAND = 1000;
const UNKNOWN_FIGURE = '-';
const PUBLIC_REPO = 'maxon-lang/maxon';
const CI_WORKFLOW = 'ci';
const RUN_COMPLETED = 'completed';
const RUN_FAILED = 'failure';
const RUN_PASSED = 'success';
const RUN_VERDICTS = [RUN_PASSED, RUN_FAILED];
const MAIN_RUNS_SCANNED = 20;

function fail(message) {
  console.error(`fleet: ${message}`);
  process.exit(1);
}

const FLEET_REPO_CONFIG_KEY = 'maxon.fleetRepo';

let fleetRepo = null;

function repo() {
  if (fleetRepo) return fleetRepo;
  const result = spawnSync('git', ['config', '--global', '--get', FLEET_REPO_CONFIG_KEY], { encoding: 'utf8' });
  const name = result.status === 0 ? result.stdout.trim() : '';
  if (!name) fail(`git config --global ${FLEET_REPO_CONFIG_KEY} is not set; set it to the private fleet repository, owner/name`);
  fleetRepo = name;
  return fleetRepo;
}

function gh(args, input) {
  return execFileSync('gh', args, {
    encoding: 'utf8',
    input,
    stdio: [input === undefined ? 'ignore' : 'pipe', 'pipe', 'inherit'],
    maxBuffer: GH_MAX_BUFFER,
  });
}

function api(method, path, body) {
  const args = ['api', '-X', method, path];
  if (body !== undefined) args.push('--input', '-');
  const out = gh(args, body === undefined ? undefined : JSON.stringify(body));
  return out.trim() ? JSON.parse(out) : null;
}

const responses = new Map();

function header(head, name) {
  return new RegExp(`^${name}:\\s*(.*)$`, 'im').exec(head)?.[1].trim() ?? null;
}

function get(path) {
  const cached = responses.get(path);
  const args = ['api', '-i', path];
  if (cached) args.push('-H', `If-None-Match: ${cached.etag}`);
  const result = spawnSync('gh', args, { encoding: 'utf8', maxBuffer: GH_MAX_BUFFER });
  const separator = /\r?\n\r?\n/.exec(result.stdout);
  const head = separator ? result.stdout.slice(0, separator.index) : result.stdout;
  const status = Number(/^HTTP\/\S+ (\d{3})/.exec(head)?.[1]);

  if (cached && status === HTTP_NOT_MODIFIED) return { data: JSON.parse(cached.text), next: cached.next };
  if (result.status !== 0 || !separator) fail(`gh api ${path}: ${result.stderr.trim() || `HTTP ${status}`}`);

  const text = result.stdout.slice(separator.index + separator[0].length);
  const next = /rel="next"/.test(header(head, 'Link') ?? '');
  const etag = header(head, 'ETag');
  if (etag) responses.set(path, { etag, text, next });
  return { data: JSON.parse(text), next };
}

function apiAll(path) {
  const first = get(path);
  if (!first.next) return first.data;
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

function sleep(secondsToSleep) {
  Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, secondsToSleep * 1000);
}

async function watch(interval, max, evaluate) {
  const start = nowSeconds();

  for (;;) {
    const result = await evaluate();
    if (result) return result;
    if (nowSeconds() - start + interval > max) return null;
    sleep(interval);
  }
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

function comments(issue, since = null) {
  const after = since ? `&since=${encodeURIComponent(since)}` : '';
  return apiAll(`repos/${repo()}/issues/${issue.number}/comments?per_page=100${after}`);
}

function mailboxTitle(id) {
  return `mailbox: ${id}`;
}

function mailboxes(id) {
  return matchingIssues(LABELS.mailbox, mailboxTitle(id));
}

function watermark(box) {
  return WATERMARK_PATTERN.exec(box.body ?? '')?.[1] ?? null;
}

function mailboxComments(box) {
  return comments(box, watermark(box));
}

function isUnread(comment) {
  return (comment.reactions?.[READ_REACTION] ?? 0) === 0;
}

function unread(id) {
  return mailboxes(id).flatMap(box => mailboxComments(box).filter(isUnread));
}

function advanceWatermark(box, acked) {
  const seen = mailboxComments(box);
  const pending = seen.filter(comment => isUnread(comment) && !acked.has(String(comment.id)));
  const times = (pending.length > 0 ? pending : seen).map(comment => comment.created_at).sort();
  if (times.length === 0) return;

  const next = pending.length > 0 ? times[0] : times[times.length - 1];
  if (next === watermark(box)) return;
  api('PATCH', `repos/${repo()}/issues/${box.number}`, { body: marker(WATERMARK_KIND, next) });
}

function formatMessage(message) {
  return `=== ${message.id} ${message.created_at}\n${message.body}\n`;
}

function printMessages(messages) {
  if (messages.length === 0) {
    console.log('no messages');
    return;
  }

  for (const message of messages) console.log(formatMessage(message));
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

function transcriptPath() {
  const session = process.env[SESSION_ENV];
  const projects = join(configDir(), TRANSCRIPTS_DIR);
  if (!session || !existsSync(projects)) return null;
  return readdirSync(projects).map(dir => join(projects, dir, `${session}.jsonl`)).find(existsSync) ?? null;
}

function promptTokens(line) {
  const entry = JSON.parse(line);
  const usage = entry.message?.usage;
  if (entry.type !== 'assistant' || entry.isSidechain || !usage) return null;
  return usage.input_tokens + (usage.cache_creation_input_tokens ?? 0) + (usage.cache_read_input_tokens ?? 0);
}

function contextTokens() {
  const path = transcriptPath();
  if (!path) return null;
  const size = statSync(path).size;
  const fd = openSync(path, 'r');

  try {
    for (let span = TRANSCRIPT_TAIL_BYTES; ; span *= 2) {
      const start = Math.max(0, size - span);
      const buffer = Buffer.alloc(size - start);
      readSync(fd, buffer, 0, buffer.length, start);
      const lines = buffer.toString('utf8').split('\n').slice(start > 0 ? 1 : 0, -1).reverse();

      for (const line of lines) {
        if (!line.includes('"type":"assistant"')) continue;
        const tokens = promptTokens(line);
        if (tokens !== null) return tokens;
      }

      if (start === 0) return null;
    }
  } finally {
    closeSync(fd);
  }
}

const ACCOUNT_LABEL_PATTERN = new RegExp(`^[0-9a-f]{${ACCOUNT_LABEL_LENGTH}}$`);
const ACCOUNT_ENV = 'MAXON_FLEET_ACCOUNT';
const ACCOUNT_OPTION_HINT = `pass --account <label> or set ${ACCOUNT_ENV}, the account's label as \`fleet.mjs status\` shows it`;

function account(options) {
  const given = options.account ?? process.env[ACCOUNT_ENV];
  if (given !== undefined && !ACCOUNT_LABEL_PATTERN.test(given)) fail(`the account label must be ${ACCOUNT_LABEL_LENGTH} lowercase hex digits; ${ACCOUNT_OPTION_HINT}`);

  const result = spawnSync('claude auth status', { encoding: 'utf8', shell: true });
  const status = result.status === 0 ? JSON.parse(result.stdout) : {};
  const planTier = status.planTier ?? status.subscriptionType ?? 'unknown';

  if (!status.orgId) {
    if (given !== undefined) return { label: given, planTier };
    if (result.status !== 0) fail(`claude auth status exited ${result.status}; ${ACCOUNT_OPTION_HINT}`);
    fail(`claude auth status names no organisation, as in a cloud session; ${ACCOUNT_OPTION_HINT}`);
  }

  const label = createHash('sha256').update(status.orgId).digest('hex').slice(0, ACCOUNT_LABEL_LENGTH);
  if (given !== undefined && given !== label) fail(`account label ${given} is not this session's account, ${label}`);
  return { label, planTier };
}

function limitKey(limit) {
  const model = limit.scope?.model?.display_name;
  return model ? `${limit.kind}:${model}` : limit.kind;
}

async function sdkUsage() {
  const { query } = await import('@anthropic-ai/claude-agent-sdk');

  for await (const message of query({ prompt: USAGE_COMMAND, options: { maxTurns: 1, settingSources: [] } })) {
    const limits = message.usage_report?.rate_limits?.limits;
    if (!limits) continue;
    return {
      at: Math.round(nowSeconds()),
      source: 'sdk',
      limits: limits
        .filter(limit => limit.resets_at)
        .map(limit => ({ key: limitKey(limit), used: limit.percent, resetsAt: Math.round(Date.parse(limit.resets_at) / 1000), active: limit.is_active })),
    };
  }

  throw new Error(`${USAGE_COMMAND} returned no usage report`);
}

function statusLineUsage() {
  const path = join(configDir(), USAGE_FILE);
  if (!existsSync(path)) return null;
  const sample = JSON.parse(readFileSync(path, 'utf8'));
  return {
    at: sample.at,
    source: 'status-line',
    limits: Object.entries(sample.rate_limits ?? {})
      .filter(([name]) => STATUS_LINE_LIMIT_KINDS[name])
      .map(([name, window]) => ({ key: STATUS_LINE_LIMIT_KINDS[name], used: window.used_percentage, resetsAt: window.resets_at })),
  };
}

async function usageSample() {
  try {
    return await sdkUsage();
  } catch (error) {
    console.error(`fleet: the SDK usage report failed (${error.message}); using the status line's sample. If the SDK is missing, run \`npm ci --prefix .claude/fleet\` once in this checkout`);
    return statusLineUsage();
  }
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

const ASSIGN_KIND = 'ASSIGN';
const OVER_BUDGET_OPTION = 'over-budget';
const OVER_BUDGET_CONSENT = 'yes';
const PAUSE_ACTION = 'pause';

function recipientAccount(to, options) {
  if (options.account !== undefined) {
    if (!ACCOUNT_LABEL_PATTERN.test(options.account)) fail(`the account label must be ${ACCOUNT_LABEL_LENGTH} lowercase hex digits`);
    return options.account;
  }
  const beat = readFleet().heartbeats.find(entry => entry.key === to);
  if (!beat) fail(`${to} has no heartbeat, so its account's budget is unknown; pass --account <label> for the account it will run on`);
  return beat.data.account;
}

function requireBudgetRoom(to, options) {
  const acct = recipientAccount(to, options);
  const entry = budgetPlan().find(row => row.account === acct);
  if (!entry || !entry.action.startsWith(PAUSE_ACTION)) return;
  if (options[OVER_BUDGET_OPTION] === OVER_BUDGET_CONSENT) {
    console.error(`fleet: account ${acct} reads "${entry.action}"; sending the ASSIGN to ${to} anyway (--${OVER_BUDGET_OPTION} ${OVER_BUDGET_CONSENT})`);
    return;
  }
  const windows = entry.windows.map(window => `${window.key} ${window.used}% (${window.why})`).join('; ');
  fail(`refusing ASSIGN to ${to}: account ${acct} reads "${entry.action}" (${windows}). A paused account takes no new task; reply PAUSE instead. Only on the user's explicit word, pass --${OVER_BUDGET_OPTION} ${OVER_BUDGET_CONSENT}`);
}

function commandSend(options) {
  const from = required(options, 'as');
  const to = required(options, 'to');
  const text = readFileSync(required(options, 'file'), 'utf8').trim();
  if (text.split(/\s/, 1)[0] === ASSIGN_KIND) requireBudgetRoom(to, options);
  const box = ensureIssue(LABELS.mailbox, mailboxTitle(to));
  const posted = api('POST', `repos/${repo()}/issues/${box.number}/comments`, { body: `FROM ${from}\n\n${text}` });
  console.log(`sent ${posted.id} to ${to}`);
}

function commandInbox(options) {
  printMessages(unread(required(options, 'as')));
}

function commandAck(options, given) {
  const id = required(options, 'as');
  const ids = given.flatMap(arg => String(arg).split(ACK_SEPARATOR)).filter(Boolean);
  if (ids.length === 0) fail('ack needs the ids of the messages read');
  for (const comment of ids) api('POST', `repos/${repo()}/issues/comments/${comment}/reactions`, { content: READ_REACTION });
  const acked = new Set(ids.map(String));
  for (const box of mailboxes(id)) advanceWatermark(box, acked);
  console.log(`acknowledged ${ids.length}`);
}

async function commandWait(options) {
  const id = required(options, 'as');
  const max = seconds(options, 'max', Infinity);
  const messages = await watch(seconds(options, 'interval', DEFAULT_POLL_SECONDS), max, () => {
    const found = unread(id);
    return found.length > 0 ? found : null;
  });

  if (!messages) {
    console.log(`no messages for ${id} within ${max} s`);
    process.exit(WAIT_TIMEOUT_EXIT);
  }

  printMessages(messages);
}

async function commandStep(options) {
  if (options.ack !== undefined) commandAck(options, [options.ack]);
  await commandHeartbeat(options);
  commandInbox(options);
}

async function commandHeartbeat(options) {
  const id = required(options, 'as');
  const state = required(options, 'state');
  if (!STATES.includes(state)) fail(`--state must be one of ${STATES.join(', ')}`);
  if (state === STATE.paused && options.task === undefined) fail('--state paused needs --task: a session paused with no task is idle (--state idle)');
  const model = required(options, 'model');
  const { label, planTier } = account(options);
  const usage = await usageSample();
  const context = contextTokens();
  const data = {
    id,
    account: label,
    planTier,
    model,
    machine: hostname(),
    state,
    task: options.task ?? null,
    at: Math.round(nowSeconds()),
    contextTokens: context,
    usage,
  };
  const { issue, heartbeats } = readFleet();
  upsertComment(issue, heartbeats.find(entry => entry.key === id)?.comment, 'heartbeat', id, data);
  console.log(`heartbeat ${id} ${state} on account ${label}${usage ? '' : ' (no usage sample yet)'}${context === null ? ' (no context size)' : ''}`);
}

function ageMinutes(at) {
  return (nowSeconds() - at) / 60;
}

function heartbeatRows(fleet) {
  return fleet.heartbeats
    .map(entry => entry.data)
    .sort((a, b) => a.account.localeCompare(b.account) || a.id.localeCompare(b.id));
}

function liveness(row) {
  const age = ageMinutes(row.at);
  if (row.task != null && age >= SILENT_HEARTBEAT_MINUTES) return LIVENESS.silent;
  if (row.state !== STATE.working) return LIVENESS.waiting;
  return age < LIVE_HEARTBEAT_MINUTES ? LIVENESS.live : LIVENESS.stale;
}

function contextFigure(row) {
  return row.contextTokens == null ? UNKNOWN_FIGURE : `${Math.round(row.contextTokens / TOKENS_PER_THOUSAND)}k`;
}

function commandStatus() {
  for (const row of heartbeatRows(readFleet())) {
    console.log(`${row.account}  ${row.id.padEnd(24)} ${row.state.padEnd(8)} ${String(row.model).padEnd(18)} ${row.machine.padEnd(16)} ${liveness(row)} ${Math.round(ageMinutes(row.at))}m  ctx ${contextFigure(row).padEnd(6)} ${row.task ?? ''}`);
  }
}

function openLimits(sample, now) {
  return Object.fromEntries((sample?.limits ?? [])
    .filter(limit => limit.resetsAt > now && limit.active !== false)
    .map(limit => [limit.key, { used: limit.used, resetsAt: limit.resetsAt }]));
}

function perConsumerRate(history, key, current, now) {
  const points = history.filter(point =>
    point.limits?.[key] &&
    Math.abs(point.limits[key].resetsAt - current.resetsAt) < SAME_WINDOW_TOLERANCE_SECONDS &&
    point.at >= now - RATE_SPAN_HOURS * 3600);
  if (points.length < 2) return null;
  const first = points[0];
  const last = points[points.length - 1];
  const spanHours = (last.at - first.at) / 3600;
  if (spanHours * 60 < MIN_RATE_SPAN_MINUTES) return null;
  const meanConsumers = points.reduce((sum, point) => sum + point.consumers, 0) / points.length;
  if (meanConsumers === 0) return null;
  return (last.limits[key].used - first.limits[key].used) / spanHours / meanConsumers;
}

function allowedConsumers(current, rate, now) {
  const headroom = TARGET_PERCENT - current.used;
  if (headroom <= 0) return { limit: 0, why: `at ${current.used}%` };
  if (rate === null) return { limit: Infinity, why: 'burn rate not yet measured' };
  if (rate <= 0) return { limit: Infinity, why: 'no measurable burn' };
  const hoursLeft = (current.resetsAt - now) / 3600;
  return { limit: headroom / (rate * hoursLeft), why: `${rate.toFixed(2)}%/h per consumer, ${hoursLeft.toFixed(1)} h left` };
}

function budgetPlan(fleet = readFleet()) {
  const { issue, heartbeats, histories } = fleet;
  const now = nowSeconds();
  const accounts = [...new Set(heartbeats.map(entry => entry.data.account))].sort();
  const plan = [];

  for (const acct of accounts) {
    const live = heartbeats.map(entry => entry.data).filter(row => row.account === acct && ageMinutes(row.at) < LIVE_HEARTBEAT_MINUTES);
    const workers = live.filter(row => row.id !== ORCHESTRATOR_ID);
    const working = workers.filter(row => row.state === STATE.working).length;
    const available = workers.length;
    const liveSample = live.map(row => row.usage).filter(Boolean).sort((a, b) => b.at - a.at)[0] ?? null;

    const stored = histories.find(entry => entry.key === acct);
    const recorded = JSON.stringify(stored?.data ?? []);
    let history = stored?.data ?? [];
    const lastPoint = history[history.length - 1] ?? null;

    const sample = liveSample ?? (lastPoint ? { at: lastPoint.at, limits: Object.entries(lastPoint.limits).map(([key, limit]) => ({ key, ...limit })) } : null);
    const limits = openLimits(sample, now);

    if (liveSample && (history.length === 0 || liveSample.at - history[history.length - 1].at >= HISTORY_SPACING_SECONDS)) {
      history.push({ at: liveSample.at, consumers: working, limits });
    }

    const newest = history[history.length - 1];
    history = history.filter(point => point === newest || point.at >= now - HISTORY_HOURS * 3600);
    if (JSON.stringify(history) !== recorded) upsertComment(issue, stored?.comment, 'history', acct, history);

    const windows = Object.entries(limits).map(([key, current]) => {
      const allowed = allowedConsumers(current, perConsumerRate(history, key, current, now), now);
      return { key, used: current.used, resetsAt: new Date(current.resetsAt * 1000).toISOString(), why: allowed.why, limit: allowed.limit };
    });

    const limit = Math.min(Infinity, ...windows.map(window => window.limit));
    for (const window of windows) window.limit = window.limit === Infinity ? 'unbounded' : Number(window.limit.toFixed(2));
    const ceiling = limit === Infinity ? working + 1 : Math.floor(limit);
    const target = sample ? Math.max(0, Math.min(ceiling, working + 1, available)) : working;
    const paced = !sample ? 'hold (no usage sample)'
      : target > working ? `start ${target - working}`
        : target < working ? `pause ${working - target}`
          : 'hold';
    const sampleAgeMinutes = sample ? Math.round(ageMinutes(sample.at)) : null;
    const action = sample && !liveSample ? `${paced} (last sample ${sampleAgeMinutes} min old)` : paced;

    plan.push({ account: acct, working, available, target, pace: paced, action, hasLiveSample: Boolean(liveSample), sampleAgeMinutes, windows });
  }

  return plan;
}

function commandBudget() {
  console.log(JSON.stringify(budgetPlan(), null, 2));
}

const MAIN_REMOTE = 'origin';
const MAIN_BRANCH = 'main';
const FINISHED_KIND = 'finished';

function git(args) {
  const result = spawnSync('git', args, { encoding: 'utf8' });
  if (result.status !== 0) fail(`git ${args.join(' ')}: ${(result.stderr || result.stdout).trim().split('\n')[0]}`);
  return result.stdout.trim();
}

function commandLand(options) {
  const task = required(options, 'task');
  const branch = required(options, 'branch');
  const main = `${MAIN_REMOTE}/${MAIN_BRANCH}`;
  const tip = `${MAIN_REMOTE}/${branch}`;
  git(['fetch', '--quiet', MAIN_REMOTE, MAIN_BRANCH, branch]);

  const ahead = Number(git(['rev-list', '--count', `${main}..${tip}`]));
  if (ahead !== 1) fail(`${branch} holds ${ahead} commits on ${main}; the worker squashes it to one`);
  if (spawnSync('git', ['merge-base', '--is-ancestor', main, tip]).status !== 0) fail(`${branch} does not fast-forward ${main}; the worker rebases it`);
  if (git(['rev-list', '--merges', `${main}..${tip}`])) fail(`${branch} holds a merge commit; the worker rebases it`);

  const sha = git(['rev-parse', '--short=9', tip]);
  git(['push', '--quiet', MAIN_REMOTE, `${tip}:refs/heads/${MAIN_BRANCH}`]);
  git(['push', '--quiet', MAIN_REMOTE, '--delete', branch]);
  api('POST', `repos/${repo()}/issues/${task}/comments`, { body: `Landed as ${sha} (${branch} fast-forwarded to ${MAIN_BRANCH} and deleted).` });
  api('PATCH', `repos/${repo()}/issues/${task}`, { state: 'closed' });
  console.log(`landed ${sha}; ${branch} deleted; task ${task} closed`);
  releaseWaiters(task);
}

const WAITS_ON_PATTERN = /^Waits on:\s*(.+)$/im;

function waitsOn(issue) {
  const match = WAITS_ON_PATTERN.exec(issue.body ?? '');
  return match ? [...match[1].matchAll(/#(\d+)/g)].map(found => Number(found[1])) : [];
}

function ownerOf(issue) {
  const colon = issue.title.indexOf(': ');
  return colon > 0 && !issue.title.slice(0, colon).includes(' ') ? issue.title.slice(0, colon) : null;
}

function isOpen(number) {
  return get(`repos/${repo()}/issues/${number}`).data.state === 'open';
}

function parkedTasks(closed = null) {
  return openIssues(LABELS.task)
    .filter(issue => waitsOn(issue).length > 0 && (closed === null || waitsOn(issue).includes(closed)))
    .map(issue => ({ issue, owner: ownerOf(issue), pending: waitsOn(issue).filter(number => number !== closed && isOpen(number)) }));
}

function taskNumbers(numbers) {
  return numbers.map(number => `#${number}`).join(', ');
}

function releaseWaiters(closedTask) {
  const closed = Number(closedTask);
  for (const { issue: waiter, owner, pending } of parkedTasks(closed)) {
    if (pending.length > 0) {
      console.log(`task ${waiter.number} (${owner}) still waits on ${taskNumbers(pending)}`);
      continue;
    }
    const body = `GO ${waiter.number}\n\nEvery task it waited on is closed (the last was task ${closed}). Rebase onto origin/main and carry on.`;
    if (owner) {
      const box = ensureIssue(LABELS.mailbox, mailboxTitle(owner));
      api('POST', `repos/${repo()}/issues/${box.number}/comments`, { body });
    }
    api('POST', `repos/${repo()}/issues/${waiter.number}/comments`, { body: `Released: task ${closed} closed, nothing left to wait on.` });
    console.log(`task ${waiter.number}: GO sent to ${owner ?? '(no owner in title; tell it yourself)'}`);
  }
}

function commandRelease(options) {
  releaseWaiters(required(options, 'task'));
}

function parkedLine({ issue, owner, pending }) {
  const state = pending.length === 0 ? 'READY TO RELEASE (nothing open left): run release' : `waits on ${taskNumbers(pending)}`;
  return `task ${issue.number} (${owner ?? '?'}): ${state}`;
}

function commandWaiting() {
  const parked = parkedTasks();
  if (parked.length === 0) {
    console.log('no task is waiting on another');
    return;
  }
  for (const task of parked) console.log(parkedLine(task));
}

function redMain() {
  const runs = JSON.parse(gh(['run', 'list', '-R', PUBLIC_REPO, '--branch', MAIN_BRANCH, '--workflow', CI_WORKFLOW, '--limit', String(MAIN_RUNS_SCANNED), '--json', 'conclusion,headSha,status,url']));
  const latestVerdict = runs.find(run => run.status === RUN_COMPLETED && RUN_VERDICTS.includes(run.conclusion));
  return latestVerdict?.conclusion === RUN_FAILED ? [latestVerdict] : [];
}

const WAKE_CHECKS = [
  {
    name: 'message',
    always: true,
    findings: ({ id }) => unread(id).map(message => ({ key: String(message.id), line: formatMessage(message) })),
  },
  {
    name: 'main-red',
    findings: () => redMain().map(run => ({ key: run.headSha, line: `${CI_WORKFLOW} failed on ${MAIN_BRANCH} at ${run.headSha}: ${run.url}` })),
  },
  {
    name: 'silent',
    findings: ({ fleet }) => heartbeatRows(fleet)
      .filter(row => liveness(row) === LIVENESS.silent)
      .map(row => ({ key: `${row.id}@${row.at}`, line: `${row.id} (task ${row.task}, ${row.state}) last heartbeat ${Math.round(ageMinutes(row.at))} min ago` })),
  },
  {
    name: 'budget',
    findings: ({ fleet }) => budgetPlan(fleet).map(entry => ({ key: `${entry.account} ${entry.pace}`, line: `account ${entry.account}: ${entry.action}` })),
  },
  {
    name: 'unprioritized',
    findings: () => unprioritizedIssues().map(issue => ({ key: String(issue.number), line: `#${issue.number} ${issue.title}` })),
  },
  {
    name: 'release',
    findings: () => parkedTasks()
      .filter(task => task.pending.length === 0)
      .map(task => ({ key: String(task.issue.number), line: parkedLine(task) })),
  },
];

function evaluateChecks(id) {
  const context = { id, fleet: readFleet() };
  return WAKE_CHECKS.map(check => ({ check, findings: check.findings(context) }));
}

function printFindings(title, results) {
  const shown = results.filter(result => result.findings.length > 0);
  if (shown.length === 0) return;
  console.log(`--- ${title}`);
  for (const { check, findings } of shown) {
    for (const finding of findings) console.log(`${check.name}: ${finding.line}`);
  }
}

async function commandWake(options) {
  const id = required(options, 'as');
  const max = seconds(options, 'max', DEFAULT_WAKE_MAX_SECONDS);
  const beat = { ...options, state: STATE.working };
  let lastBeat = nowSeconds();
  await commandHeartbeat(beat);

  let baseline = null;
  let standing = [];

  const fired = await watch(seconds(options, 'interval', DEFAULT_POLL_SECONDS), max, async () => {
    if (nowSeconds() - lastBeat >= WAKE_HEARTBEAT_SECONDS) {
      lastBeat = nowSeconds();
      await commandHeartbeat(beat);
    }

    const results = evaluateChecks(id);
    baseline ??= new Map(results.map(({ check, findings }) => [check, new Set(findings.map(finding => finding.key))]));
    const select = isNew => results.map(({ check, findings }) => ({
      check,
      findings: findings.filter(finding => (check.always || !baseline.get(check).has(finding.key)) === isNew),
    }));
    standing = select(false);
    const news = select(true);
    return news.some(result => result.findings.length > 0) ? news : null;
  });

  if (!fired) {
    await commandHeartbeat(beat);
    console.log(`wake: no check fired within ${max} s`);
    printFindings('standing', standing);
    process.exit(WAIT_TIMEOUT_EXIT);
  }

  console.log(`wake: ${fired.filter(result => result.findings.length > 0).map(result => result.check.name).join(', ')}`);
  printFindings('fired', fired);
  printFindings('standing', standing);
}

function commandRetire(options) {
  const id = required(options, 'id');
  const archived = options.archived === 'yes';
  const { issue, heartbeats } = readFleet();
  const beat = heartbeats.find(entry => entry.key === id);

  if (!archived) {
    const data = {
      id,
      account: beat?.data.account ?? options.account ?? null,
      task: options.task ?? beat?.data.task ?? null,
      place: beat?.data.machine ?? options.place ?? null,
      at: Math.round(nowSeconds()),
    };
    api('POST', `repos/${repo()}/issues/${issue.number}/comments`, { body: jsonBody(FINISHED_KIND, id, data) });
  }

  if (beat) api('DELETE', `repos/${repo()}/issues/comments/${beat.comment.id}`);
  for (const mailbox of mailboxes(id)) {
    api('PATCH', `repos/${repo()}/issues/${mailbox.number}`, { state: 'closed' });
  }
  console.log(`retired ${id}${archived ? ' (archived itself)' : '; finished marker posted for the panel'}`);
}

const COMMANDS = {
  land: commandLand,
  release: commandRelease,
  waiting: commandWaiting,
  retire: commandRetire,
  init: commandInit,
  send: commandSend,
  inbox: commandInbox,
  ack: commandAck,
  wait: commandWait,
  step: commandStep,
  wake: commandWake,
  heartbeat: commandHeartbeat,
  status: commandStatus,
  budget: commandBudget,
};

const [name, ...rest] = process.argv.slice(2);
const command = COMMANDS[name];
if (!command) fail(`usage: fleet.mjs <${Object.keys(COMMANDS).join('|')}> [--as <id>] …`);
const { options, positional } = parseArgs(rest);
await command(options, positional);
