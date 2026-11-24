#!/usr/bin/env node
/**
 * Claude Code Hook (`SessionStart` / `UserPromptSubmit`) for MOBAi Swarm Bus.
 *
 * Strictly read-only: checks %LOCALAPPDATA%\MOBAdev\swarm-bus.db for unread
 * messages addressed to claude-opus / claude / all and active foreign locks.
 * Emits nothing (0 context tokens) when nothing is pending.
 */

import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { DatabaseSync } from 'node:sqlite';

function resolveDbPath() {
  if (process.env.MOBADEV_SWARM_BUS_DB && process.env.MOBADEV_SWARM_BUS_DB.trim()) {
    return path.resolve(process.env.MOBADEV_SWARM_BUS_DB.trim());
  }
  const localApp = process.env.LOCALAPPDATA && process.env.LOCALAPPDATA.trim()
    ? process.env.LOCALAPPDATA.trim()
    : path.join(os.homedir(), 'AppData', 'Local');
  return path.join(localApp, 'MOBAdev', 'swarm-bus.db');
}

function readHookEventName() {
  try {
    if (process.stdin.isTTY) {
      return 'UserPromptSubmit';
    }
    const raw = fs.readFileSync(0, 'utf8').trim();
    if (!raw) {
      return 'UserPromptSubmit';
    }
    const parsed = JSON.parse(raw);
    const ev = parsed?.hook_event_name;
    return ev === 'SessionStart' || ev === 'UserPromptSubmit' ? ev : 'UserPromptSubmit';
  } catch {
    return 'UserPromptSubmit';
  }
}

function truncateText(value, maxLen = 220) {
  const clean = String(value ?? '').replace(/[\r\n\t]+/g, ' ').trim();
  if (clean.length <= maxLen) return clean;
  return clean.slice(0, maxLen - 3) + '...';
}

function main() {
  const dbPath = resolveDbPath();
  if (!fs.existsSync(dbPath)) {
    return;
  }

  const hookEventName = readHookEventName();
  const nowIso = new Date().toISOString();

  const db = new DatabaseSync(dbPath, { readOnly: true });
  try {
    db.exec('PRAGMA busy_timeout = 1000;');

    const unreadMessages = db.prepare(`
      SELECT id, sender, recipient, topic, body, pr_number, issue_number, priority
      FROM messages
      WHERE status = 'unread'
        AND lower(recipient) IN ('claude-opus', 'claude', 'all')
      ORDER BY id DESC
      LIMIT 8
    `).all();

    const foreignLocks = db.prepare(`
      SELECT agent, path_pattern, pr_number, expires_at_utc
      FROM locks
      WHERE expires_at_utc > ?
        AND lower(agent) NOT IN ('claude-opus', 'claude')
      ORDER BY id ASC
      LIMIT 12
    `).all(nowIso);

    if (unreadMessages.length === 0 && foreignLocks.length === 0) {
      return;
    }

    const lines = ['[MOBAi Swarm Bus — Live Cross-Tool Context]'];

    if (unreadMessages.length > 0) {
      lines.push(`Unread messages for Claude (${unreadMessages.length}):`);
      for (const msg of unreadMessages) {
        const refParts = [];
        if (msg.pr_number) refParts.push(`PR #${msg.pr_number}`);
        if (msg.issue_number) refParts.push(`Issue #${msg.issue_number}`);
        const refText = refParts.length > 0 ? ` (${refParts.join(', ')})` : '';
        lines.push(`- #${msg.id} [${msg.topic}]${refText} from ${msg.sender} -> ${msg.recipient}: ${truncateText(msg.body)}`);
      }
    }

    if (foreignLocks.length > 0) {
      lines.push(`Active foreign path locks (do not edit without coordination):`);
      for (const lk of foreignLocks) {
        const prTag = lk.pr_number ? ` (PR #${lk.pr_number})` : '';
        lines.push(`- ${lk.agent}${prTag}: ${lk.path_pattern}`);
      }
    }

    lines.push('Tip: use MCP `mobai-swarm-bus` (`bus_ack`, `bus_send`, `bus_locks`) to reply or coordinate.');

    const output = {
      hookSpecificOutput: {
        hookEventName,
        additionalContext: lines.join('\n')
      }
    };

    process.stdout.write(JSON.stringify(output) + '\n');
  } finally {
    db.close();
  }
}

try {
  main();
} catch {
  // Never block or fail a Claude Code turn on hook error.
  process.exit(0);
}
