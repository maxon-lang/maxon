#!/usr/bin/env node
import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { homedir } from 'node:os';
import { basename, dirname, join } from 'node:path';

const JSONL_SUFFIX = '.jsonl';
const META_SUFFIX = '.meta.json';
const SUBAGENTS_DIR = 'subagents';
const SUBAGENT_FILE_PREFIX = 'agent-';
const PROJECTS_DIR = ['.claude', 'projects'];
const JSON_FLAG = '--json';
const MAIN_AGENT_KEY = 'main';
const MAIN_AGENT_LABEL = 'main session';
const SYNTHETIC_MODEL = '<synthetic>';
const LABEL_WIDTH = 52;
const TOTAL_LABEL = 'total';
const SHARE_DECIMALS = 1;
const PERCENT = 100;
const COLUMN_GAP = '  ';
const NUMBER_LOCALE = 'en-US';
const COLUMNS = [
  { header: 'agent', align: 'left' },
  { header: 'model', align: 'left' },
  { header: 'calls', align: 'right' },
  { header: 'first ctx', align: 'right' },
  { header: 'peak ctx', align: 'right' },
  { header: 'cache read', align: 'right' },
  { header: 'cache create', align: 'right' },
  { header: 'output', align: 'right' },
  { header: 'read share', align: 'right' },
];

function fail(message) {
  console.error(`usage-table: ${message}`);
  process.exit(1);
}

function projectsRoot() {
  return join(homedir(), ...PROJECTS_DIR);
}

function resolveTranscript(argument) {
  if (existsSync(argument)) return argument;

  const root = projectsRoot();
  const wanted = argument.endsWith(JSONL_SUFFIX) ? argument : `${argument}${JSONL_SUFFIX}`;
  const matches = readdirSync(root, { withFileTypes: true })
    .filter((entry) => entry.isDirectory())
    .map((entry) => join(root, entry.name, wanted))
    .filter((candidate) => existsSync(candidate));

  if (matches.length === 0) fail(`no transcript '${argument}' (not a file, and no ${wanted} under ${root})`);
  if (matches.length > 1) fail(`session '${argument}' is ambiguous: ${matches.join(', ')}`);

  return matches[0];
}

function readEntries(file) {
  return readFileSync(file, 'utf8')
    .split('\n')
    .map((line, index) => ({ line, number: index + 1 }))
    .filter(({ line }) => line.trim() !== '')
    .map(({ line, number }) => {
      try {
        return JSON.parse(line);
      } catch (error) {
        return fail(`${file}:${number}: ${error.message}`);
      }
    });
}

function readMeta(subagentsDir, agentId) {
  const metaFile = join(subagentsDir, `${SUBAGENT_FILE_PREFIX}${agentId}${META_SUFFIX}`);

  return existsSync(metaFile) ? JSON.parse(readFileSync(metaFile, 'utf8')) : {};
}

function transcriptFiles(mainFile) {
  const subagentsDir = join(dirname(mainFile), basename(mainFile, JSONL_SUFFIX), SUBAGENTS_DIR);
  const subagentFiles = existsSync(subagentsDir)
    ? readdirSync(subagentsDir)
        .filter((name) => name.startsWith(SUBAGENT_FILE_PREFIX) && name.endsWith(JSONL_SUFFIX))
        .sort()
        .map((name) => join(subagentsDir, name))
    : [];

  return { subagentsDir, files: [mainFile, ...subagentFiles] };
}

function newAgent(key, label) {
  return { key, label, models: new Set(), calls: 0, firstContext: null, peakContext: 0, cacheRead: 0, cacheCreation: 0, output: 0, firstTimestamp: null };
}

function describeAgent(key, meta, mainFile, mainTitle) {
  if (key === MAIN_AGENT_KEY) return mainTitle ? `${MAIN_AGENT_LABEL}: ${mainTitle}` : `${MAIN_AGENT_LABEL}: ${basename(mainFile)}`;

  const description = meta.description ?? `${SUBAGENT_FILE_PREFIX}${key}`;

  return meta.agentType ? `${description} (${meta.agentType})` : description;
}

function collectAgents(mainFile) {
  const { subagentsDir, files } = transcriptFiles(mainFile);
  const agents = new Map();
  const seenCalls = new Set();
  let mainTitle = null;

  for (const file of files) {
    for (const entry of readEntries(file)) {
      if (entry.type === 'custom-title' && !entry.agentId && file === mainFile) mainTitle = entry.customTitle;
      if (entry.type !== 'assistant' || !entry.message?.usage || entry.message.model === SYNTHETIC_MODEL) continue;

      const callId = entry.message.id ?? entry.requestId;
      if (callId === undefined) fail(`${file}: assistant entry ${entry.uuid} has neither message.id nor requestId`);
      if (seenCalls.has(callId)) continue;
      seenCalls.add(callId);

      const key = entry.agentId ?? MAIN_AGENT_KEY;
      if (!agents.has(key)) agents.set(key, newAgent(key, describeAgent(key, readMeta(subagentsDir, key), mainFile, null)));

      const agent = agents.get(key);
      const usage = entry.message.usage;
      const context = usage.input_tokens + usage.cache_read_input_tokens + usage.cache_creation_input_tokens;

      agent.models.add(entry.message.model);
      agent.calls += 1;
      agent.firstContext ??= context;
      agent.peakContext = Math.max(agent.peakContext, context);
      agent.cacheRead += usage.cache_read_input_tokens;
      agent.cacheCreation += usage.cache_creation_input_tokens;
      agent.output += usage.output_tokens;
      agent.firstTimestamp ??= entry.timestamp;
    }
  }

  const main = agents.get(MAIN_AGENT_KEY);
  if (main) main.label = describeAgent(MAIN_AGENT_KEY, {}, mainFile, mainTitle);

  return [...agents.values()].sort((a, b) => (a.key === MAIN_AGENT_KEY ? -1 : b.key === MAIN_AGENT_KEY ? 1 : a.firstTimestamp.localeCompare(b.firstTimestamp)));
}

function summarize(agents) {
  const totalCacheRead = agents.reduce((sum, agent) => sum + agent.cacheRead, 0);
  const share = (value) => (totalCacheRead === 0 ? 0 : (value / totalCacheRead) * PERCENT);
  const rows = agents.map((agent) => ({
    agent: agent.label,
    models: [...agent.models].sort(),
    calls: agent.calls,
    firstContext: agent.firstContext,
    peakContext: agent.peakContext,
    cacheRead: agent.cacheRead,
    cacheCreation: agent.cacheCreation,
    output: agent.output,
    cacheReadShare: share(agent.cacheRead),
  }));
  const total = {
    agent: TOTAL_LABEL,
    models: [...new Set(rows.flatMap((row) => row.models))].sort(),
    calls: rows.reduce((sum, row) => sum + row.calls, 0),
    firstContext: null,
    peakContext: Math.max(0, ...rows.map((row) => row.peakContext)),
    cacheRead: totalCacheRead,
    cacheCreation: rows.reduce((sum, row) => sum + row.cacheCreation, 0),
    output: rows.reduce((sum, row) => sum + row.output, 0),
    cacheReadShare: rows.length === 0 ? 0 : PERCENT,
  };

  return { rows, total };
}

function formatNumber(value) {
  return value === null ? '' : value.toLocaleString(NUMBER_LOCALE);
}

function truncate(text, width) {
  return text.length <= width ? text : `${text.slice(0, width - 1)}…`;
}

function renderTable({ rows, total }) {
  const cells = [...rows, total].map((row) => [
    truncate(row.agent, LABEL_WIDTH),
    row.models.join(','),
    formatNumber(row.calls),
    formatNumber(row.firstContext),
    formatNumber(row.peakContext),
    formatNumber(row.cacheRead),
    formatNumber(row.cacheCreation),
    formatNumber(row.output),
    `${row.cacheReadShare.toFixed(SHARE_DECIMALS)}%`,
  ]);
  const widths = COLUMNS.map((column, index) => Math.max(column.header.length, ...cells.map((row) => row[index].length)));
  const pad = (text, index) => (COLUMNS[index].align === 'left' ? text.padEnd(widths[index]) : text.padStart(widths[index]));
  const line = (row) => row.map(pad).join(COLUMN_GAP).trimEnd();
  const rule = widths.map((width) => '-'.repeat(width)).join(COLUMN_GAP);
  const body = cells.slice(0, -1).map(line);

  return [line(COLUMNS.map((column) => column.header)), rule, ...body, rule, line(cells.at(-1))].join('\n');
}

function main() {
  const args = process.argv.slice(2);
  const json = args.includes(JSON_FLAG);
  const positional = args.filter((arg) => arg !== JSON_FLAG);

  if (positional.length !== 1) fail(`usage: usage-table.mjs <transcript.jsonl | session id> [${JSON_FLAG}]`);

  const agents = collectAgents(resolveTranscript(positional[0]));
  if (agents.length === 0) fail('the transcript records no model calls');

  const summary = summarize(agents);

  console.log(json ? JSON.stringify(summary, null, 2) : renderTable(summary));
}

main();
