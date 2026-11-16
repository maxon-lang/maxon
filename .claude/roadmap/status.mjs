#!/usr/bin/env node
import { execFileSync } from 'node:child_process';

const OWNER = 'maxon-lang';
const REPO = 'maxon-lang/maxon';
const PROJECT_NUMBER = '4';
const BACKLOG_PROJECT_NUMBER = 3;
const STATUS_FIELD = 'Status';
const PRIORITY_FIELD = 'Priority';
const ITEMS_PER_ISSUE = 20;

function fail(message) {
  console.error(`status: ${message}`);
  process.exit(1);
}

function gh(args) {
  return execFileSync('gh', args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'inherit'] });
}

const [issue, status, priority] = process.argv.slice(2);
if (!issue || !status) fail('usage: status.mjs <issue number> <status name, e.g. "In progress"> [priority, e.g. P1]');

const [repoOwner, repoName] = REPO.split('/');
const query = `query($owner: String!, $name: String!, $number: Int!) {
  repository(owner: $owner, name: $name) {
    issue(number: $number) {
      projectItems(first: ${ITEMS_PER_ISSUE}) {
        nodes {
          id
          project { number }
          priority: fieldValueByName(name: "${PRIORITY_FIELD}") { ... on ProjectV2ItemFieldSingleSelectValue { name } }
        }
      }
    }
  }
}`;
const boards = JSON.parse(gh(['api', 'graphql', '-f', `query=${query}`, '-f', `owner=${repoOwner}`, '-f', `name=${repoName}`, '-F', `number=${issue}`]));
const nodes = boards.data.repository.issue.projectItems.nodes;
const roadmapItem = nodes.find(node => String(node.project.number) === PROJECT_NUMBER);
const currentPriority = roadmapItem?.priority?.name ?? null;
if (!priority && !currentPriority) {
  fail(`#${issue} has no ${PRIORITY_FIELD} on the Roadmap; give one (P0-P3) as the third argument`);
}

const project = JSON.parse(gh(['project', 'view', PROJECT_NUMBER, '--owner', OWNER, '--format', 'json']));
const fields = JSON.parse(gh(['project', 'field-list', PROJECT_NUMBER, '--owner', OWNER, '--format', 'json'])).fields;

function optionOf(fieldName, value) {
  const field = fields.find(candidate => candidate.name === fieldName);
  if (!field) fail(`the project has no ${fieldName} field`);
  const option = field.options.find(candidate => candidate.name === value);
  if (!option) fail(`no ${fieldName} "${value}"; the project's are: ${field.options.map(candidate => candidate.name).join(', ')}`);
  return { field, option };
}

const choices = [optionOf(STATUS_FIELD, status), ...(priority ? [optionOf(PRIORITY_FIELD, priority)] : [])];

const url = `https://github.com/${REPO}/issues/${issue}`;
const item = JSON.parse(gh(['project', 'item-add', PROJECT_NUMBER, '--owner', OWNER, '--url', url, '--format', 'json']));
for (const { field, option } of choices) {
  gh(['project', 'item-edit', '--project-id', project.id, '--id', item.id, '--field-id', field.id, '--single-select-option-id', option.id]);
}

const backlogItems = nodes.filter(node => node.project.number === BACKLOG_PROJECT_NUMBER);
for (const backlogItem of backlogItems) {
  gh(['project', 'item-delete', String(BACKLOG_PROJECT_NUMBER), '--owner', OWNER, '--id', backlogItem.id]);
}

console.log(`#${issue} is ${status}, ${priority ?? currentPriority}${backlogItems.length > 0 ? ', and off the Backlog' : ''}`);
