#!/usr/bin/env node
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const REPO = 'maxon-lang/maxon';
const PRIORITIES = ['P0', 'P1', 'P2', 'P3'];
const ROADMAP_LABELS = ['bug', 'design'];
const ACCEPTED_LABEL = 'accepted';

function fail(message) {
  console.error(`file: ${message}`);
  process.exit(1);
}

function parse(argv) {
  const options = { label: [] };
  for (let i = 0; i < argv.length; i += 2) {
    const name = argv[i]?.replace(/^--/, '');
    const value = argv[i + 1];
    if (!name || value === undefined) fail(`${argv[i]} needs a value`);
    if (name === 'label') options.label.push(value);
    else options[name] = value;
  }
  return options;
}

const options = parse(process.argv.slice(2));
const usage = 'usage: file.mjs --title <t> --body-file <path> --label <l> [--label <l> …] --priority <P0-P3> [--status Ready|"In progress"]';
if (!options.title || !options['body-file'] || options.label.length === 0) fail(usage);

const accepted = options.label.includes(ACCEPTED_LABEL);
const onRoadmap = accepted && options.label.some(label => ROADMAP_LABELS.includes(label));
if (onRoadmap && !PRIORITIES.includes(options.priority)) fail(`an accepted ${options.label.filter(label => ROADMAP_LABELS.includes(label)).join('/')} goes on the Roadmap and needs --priority (${PRIORITIES.join(', ')}); follow the file-issue skill`);
if (!onRoadmap && options.priority) fail('only an accepted bug or design goes on the Roadmap with a priority; other issues land on the Backlog, and moving one to the Roadmap is the maintainer\'s decision');

const args = ['issue', 'create', '-R', REPO, '--title', options.title, '--body-file', options['body-file']];
for (const label of options.label) args.push('--label', label);
const url = execFileSync('gh', args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'inherit'] }).trim();
const number = url.split('/').pop();

if (onRoadmap) {
  const status = join(dirname(fileURLToPath(import.meta.url)), 'status.mjs');
  execFileSync('node', [status, number, options.status ?? 'Ready', options.priority], { stdio: 'inherit' });
} else {
  console.log(`#${number} filed; it lands on the Backlog`);
}
console.log(url);
