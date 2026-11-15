#!/usr/bin/env node
import { execFileSync } from 'node:child_process';

const OWNER = 'maxon-lang';
const REPO = 'maxon-lang/maxon';
const PROJECT_NUMBER = '4';
const STATUS_FIELD = 'Status';

function fail(message) {
  console.error(`status: ${message}`);
  process.exit(1);
}

function gh(args) {
  return execFileSync('gh', args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'inherit'] });
}

const [issue, status] = process.argv.slice(2);
if (!issue || !status) fail('usage: status.mjs <issue number> <status name, e.g. "In progress">');

const project = JSON.parse(gh(['project', 'view', PROJECT_NUMBER, '--owner', OWNER, '--format', 'json']));
const fields = JSON.parse(gh(['project', 'field-list', PROJECT_NUMBER, '--owner', OWNER, '--format', 'json'])).fields;
const field = fields.find(candidate => candidate.name === STATUS_FIELD);
if (!field) fail(`the project has no ${STATUS_FIELD} field`);
const option = field.options.find(candidate => candidate.name === status);
if (!option) fail(`no status "${status}"; the project's are: ${field.options.map(candidate => candidate.name).join(', ')}`);

const url = `https://github.com/${REPO}/issues/${issue}`;
const item = JSON.parse(gh(['project', 'item-add', PROJECT_NUMBER, '--owner', OWNER, '--url', url, '--format', 'json']));
gh(['project', 'item-edit', '--project-id', project.id, '--id', item.id, '--field-id', field.id, '--single-select-option-id', option.id]);
console.log(`#${issue} is ${status}`);
