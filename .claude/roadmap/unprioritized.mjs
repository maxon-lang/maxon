#!/usr/bin/env node
import { execFileSync } from 'node:child_process';

const OWNER = 'maxon-lang';
const PROJECT_NUMBER = 4;
const PRIORITY_FIELD = 'Priority';
const QUERY = `query($endCursor: String) {
  organization(login: "${OWNER}") {
    projectV2(number: ${PROJECT_NUMBER}) {
      items(first: 100, after: $endCursor) {
        pageInfo { hasNextPage endCursor }
        nodes {
          priority: fieldValueByName(name: "${PRIORITY_FIELD}") { ... on ProjectV2ItemFieldSingleSelectValue { name } }
          content { ... on Issue { number title state } }
        }
      }
    }
  }
}`;

const pages = JSON.parse(execFileSync('gh', ['api', 'graphql', '--paginate', '--slurp', '-f', `query=${QUERY}`], { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 }));
const missing = pages
  .flatMap(page => page.data.organization.projectV2.items.nodes)
  .filter(node => node.content?.state === 'OPEN' && !node.priority)
  .map(node => node.content)
  .sort((a, b) => a.number - b.number);

for (const issue of missing) console.log(`#${issue.number} ${issue.title}`);
if (missing.length > 0) {
  console.error(`${missing.length} open Roadmap issue(s) without a ${PRIORITY_FIELD}; choose each with the prioritize skill and set it with status.mjs <n> <status> <P0-P3>`);
  process.exit(1);
}
console.log('every open Roadmap issue has a priority');
