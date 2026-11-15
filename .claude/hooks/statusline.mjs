#!/usr/bin/env node
import { readFileSync, writeFileSync, renameSync, mkdirSync } from 'node:fs';
import { homedir } from 'node:os';
import { join } from 'node:path';

const USAGE_FILE = 'plan-usage.json';
const WINDOW_LABELS = { five_hour: '5h', seven_day: '7d' };

const input = JSON.parse(readFileSync(0, 'utf8'));
const limits = input.rate_limits;

if (limits) {
  const dir = process.env.CLAUDE_CONFIG_DIR || join(homedir(), '.claude');
  const path = join(dir, USAGE_FILE);
  mkdirSync(dir, { recursive: true });
  writeFileSync(`${path}.${process.pid}`, JSON.stringify({ at: Math.round(Date.now() / 1000), model: input.model?.id ?? null, rate_limits: limits }));
  renameSync(`${path}.${process.pid}`, path);
}

const parts = [input.model?.display_name ?? 'Claude'];

for (const [key, label] of Object.entries(WINDOW_LABELS)) {
  const window = limits?.[key];
  if (window) parts.push(`${label} ${Math.round(window.used_percentage)}%`);
}

console.log(parts.join(' · '));
