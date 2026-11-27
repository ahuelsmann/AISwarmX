#!/usr/bin/env node
/**
 * Claude Code Hook (`SessionStart` / `UserPromptSubmit` / `PreToolUse` / `PostToolUse` / `Stop`)
 * for AISwarmX Swarm Bus.
 *
 * Strictly read-only on the Swarm Bus SQLite DB (`MOBADEV_SWARM_BUS_HOOK_DB` /
 * `MOBADEV_SWARM_BUS_DB` or `%LOCALAPPDATA%\MOBAdev\swarm-bus.db`):
 * - `SessionStart` / `UserPromptSubmit`: injects unread messages + active foreign locks (0 bytes when empty).
 * - `PreToolUse` / `PostToolUse`: drains newly arrived unread messages (`id > last_surfaced_id`, ASC order
 *   so bursts > limit never skip messages) with 0 bytes otherwise.
 * - `Stop`: blocks once (`{ decision: "block", reason }`) on unseen unread messages (`id > last_stop_blocked_id`,
 *   ASC order), guarded strictly by `stop_hook_active === true` and atomic SQLite MAX() watermarks.
 */

import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { DatabaseSync } from 'node:sqlite';

const HOOK_EVENTS = new Set(['SessionStart', 'UserPromptSubmit', 'PreToolUse', 'PostToolUse', 'Stop']);
const PAGE_LIMIT = 8;

function resolveLocalAppDir() {
  const localApp = process.env.LOCALAPPDATA && process.env.LOCALAPPDATA.trim()
    ? process.env.LOCALAPPDATA.trim()
    : path.join(os.homedir(), 'AppData', 'Local');
  return path.join(localApp, 'MOBAdev');
}

function resolveDbPath() {
  for (const envName of ['MOBADEV_SWARM_BUS_HOOK_DB', 'MOBADEV_SWARM_BUS_DB']) {
    const candidate = process.env[envName];
    if (candidate && candidate.trim()) {
      return path.resolve(candidate.trim());
    }
  }
  return path.join(resolveLocalAppDir(), 'swarm-bus.db');
}

function resolveStateDbPath() {
  const envState = process.env.MOBADEV_CLAUDE_HOOK_STATE_FILE;
  if (envState && envState.trim()) {
    const resolved = path.resolve(envState.trim());
    return resolved.endsWith('.db') ? resolved : `${resolved}.db`;
  }
  return path.join(resolveLocalAppDir(), 'claude-hook-state.db');
}

function readHookInput() {
  const fallback = { hookEventName: 'UserPromptSubmit', stopHookActive: false, sessionId: 'default' };
  try {
    if (process.stdin.isTTY) {
      return fallback;
    }
    const raw = fs.readFileSync(0, 'utf8').trim();
    if (!raw) {
      return fallback;
    }
    const parsed = JSON.parse(raw);
    const ev = typeof parsed?.hook_event_name === 'string' && HOOK_EVENTS.has(parsed.hook_event_name)
      ? parsed.hook_event_name
      : 'UserPromptSubmit';
    const sessionId = typeof parsed?.session_id === 'string' && parsed.session_id.trim()
      ? parsed.session_id.trim()
      : 'default';
    return {
      hookEventName: ev,
      stopHookActive: parsed?.stop_hook_active === true,
      sessionId
    };
  } catch {
    return fallback;
  }
}

function isSqliteBusyError(err) {
  const msg = String(err?.message ?? err ?? '');
  return err?.code === 'ERR_SQLITE_ERROR' && /SQLITE_BUSY|database is locked|busy/i.test(msg)
    || /SQLITE_BUSY|database is locked/i.test(msg);
}

function sleepSync(ms) {
  try {
    Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, ms);
  } catch {
    // Ignore if SharedArrayBuffer is unavailable.
  }
}

function withSqliteBusyRetry(fn, maxAttempts = 6) {
  for (let attempt = 1; attempt <= maxAttempts; attempt += 1) {
    try {
      return fn();
    } catch (err) {
      if (!isSqliteBusyError(err) || attempt === maxAttempts) {
        throw err;
      }
      sleepSync(20 * attempt);
    }
  }
}

function openStateDb(stateDbPath) {
  fs.mkdirSync(path.dirname(stateDbPath), { recursive: true });
  return withSqliteBusyRetry(() => {
    const stateDb = new DatabaseSync(stateDbPath);
    try {
      stateDb.exec(`
        PRAGMA busy_timeout = 2000;
        PRAGMA journal_mode = WAL;
        CREATE TABLE IF NOT EXISTS hook_watermarks (
          session_key TEXT PRIMARY KEY,
          last_surfaced_id INTEGER NOT NULL DEFAULT 0,
          last_stop_blocked_id INTEGER NOT NULL DEFAULT 0,
          updated_at_utc TEXT NOT NULL
        );
      `);
      return stateDb;
    } catch (err) {
      try { stateDb.close(); } catch { /* ignore */ }
      throw err;
    }
  });
}

function loadWatermark(stateDbPath, sessionHash) {
  try {
    if (!fs.existsSync(stateDbPath)) {
      return { lastSurfacedId: 0, lastStopBlockedId: 0 };
    }
    const stateDb = openStateDb(stateDbPath);
    try {
      const row = stateDb.prepare(`
        SELECT last_surfaced_id, last_stop_blocked_id
        FROM hook_watermarks
        WHERE session_key = ?
      `).get(sessionHash);
      return {
        lastSurfacedId: Number.isFinite(Number(row?.last_surfaced_id)) ? Number(row.last_surfaced_id) : 0,
        lastStopBlockedId: Number.isFinite(Number(row?.last_stop_blocked_id)) ? Number(row.last_stop_blocked_id) : 0
      };
    } finally {
      stateDb.close();
    }
  } catch {
    return { lastSurfacedId: 0, lastStopBlockedId: 0 };
  }
}

function saveWatermarkAtomicMonotonic(stateDbPath, sessionHash, surfacedId, stopBlockedId) {
  try {
    withSqliteBusyRetry(() => {
      const stateDb = openStateDb(stateDbPath);
      try {
        const nowIso = new Date().toISOString();
        stateDb.exec('BEGIN IMMEDIATE;');
        try {
          stateDb.prepare(`
            INSERT INTO hook_watermarks (session_key, last_surfaced_id, last_stop_blocked_id, updated_at_utc)
            VALUES (?, ?, ?, ?)
            ON CONFLICT(session_key) DO UPDATE SET
              last_surfaced_id = MAX(hook_watermarks.last_surfaced_id, excluded.last_surfaced_id),
              last_stop_blocked_id = MAX(hook_watermarks.last_stop_blocked_id, excluded.last_stop_blocked_id),
              updated_at_utc = excluded.updated_at_utc
          `).run(sessionHash, surfacedId ?? 0, stopBlockedId ?? 0, nowIso);
          stateDb.exec('COMMIT;');
        } catch (err) {
          try { stateDb.exec('ROLLBACK;'); } catch { /* ignore */ }
          throw err;
        }
      } finally {
        stateDb.close();
      }
    });
  } catch {
    // Non-fatal: never block hook execution when watermark persistence fails.
  }
}

function truncateText(value, maxLen = 220) {
  const clean = String(value ?? '').replace(/[\r\n\t]+/g, ' ').trim();
  if (clean.length <= maxLen) return clean;
  return clean.slice(0, maxLen - 3) + '...';
}

function formatMessageLines(messages) {
  return messages.map((msg) => {
    const refParts = [];
    if (msg.pr_number) refParts.push(`PR #${msg.pr_number}`);
    if (msg.issue_number) refParts.push(`Issue #${msg.issue_number}`);
    const refText = refParts.length > 0 ? ` (${refParts.join(', ')})` : '';
    return `- #${msg.id} [${msg.topic}]${refText} from ${msg.sender} -> ${msg.recipient}: ${truncateText(msg.body)}`;
  });
}

function queryUnreadAscending(db, minExclusiveId) {
  return db.prepare(`
    SELECT id, sender, recipient, topic, body, pr_number, issue_number, priority
    FROM messages
    WHERE status = 'unread'
      AND id > ?
      AND lower(recipient) IN ('claude-opus', 'claude', 'all')
    ORDER BY id ASC
    LIMIT ?
  `).all(minExclusiveId, PAGE_LIMIT + 1);
}

function queryUnreadRecent(db) {
  return db.prepare(`
    SELECT id, sender, recipient, topic, body, pr_number, issue_number, priority
    FROM messages
    WHERE status = 'unread'
      AND lower(recipient) IN ('claude-opus', 'claude', 'all')
    ORDER BY id DESC
    LIMIT ?
  `).all(PAGE_LIMIT + 1);
}

function queryForeignLocks(db, nowIso) {
  return db.prepare(`
    SELECT agent, path_pattern, pr_number, expires_at_utc
    FROM locks
    WHERE expires_at_utc > ?
      AND lower(agent) NOT IN ('claude-opus', 'claude')
    ORDER BY id ASC
    LIMIT 12
  `).all(nowIso);
}

function main() {
  const dbPath = resolveDbPath();
  if (!fs.existsSync(dbPath)) {
    return;
  }

  const { hookEventName, stopHookActive, sessionId } = readHookInput();
  if (hookEventName === 'Stop' && stopHookActive) {
    return;
  }

  const sessionHash = crypto.createHash('sha256').update(`${dbPath}::${sessionId}`).digest('hex').slice(0, 24);
  const stateDbPath = resolveStateDbPath();
  const watermark = loadWatermark(stateDbPath, sessionHash);
  const nowIso = new Date().toISOString();

  const db = new DatabaseSync(dbPath, { readOnly: true });
  try {
    db.exec('PRAGMA busy_timeout = 1000;');

    if (hookEventName === 'Stop') {
      const stopFloor = Math.max(watermark.lastStopBlockedId, 0);
      const rows = queryUnreadAscending(db, stopFloor);
      if (rows.length === 0) {
        return;
      }

      const hasMore = rows.length > PAGE_LIMIT;
      const batch = hasMore ? rows.slice(0, PAGE_LIMIT) : rows;
      const maxBatchId = Math.max(...batch.map((m) => Number(m.id)));
      saveWatermarkAtomicMonotonic(stateDbPath, sessionHash, maxBatchId, maxBatchId);

      const reasonLines = [
        `[AISwarmX Swarm Bus — Live Drain] ${batch.length} unread message(s)${hasMore ? ' (more pending)' : ''} require review:`,
        ...formatMessageLines(batch),
        'Review and acknowledge via MCP `mobai-swarm-bus` (`bus_ack`, `bus_send`) before ending your turn.'
      ];

      process.stdout.write(JSON.stringify({ decision: 'block', reason: reasonLines.join('\n') }) + '\n');
      return;
    }

    if (hookEventName === 'PreToolUse' || hookEventName === 'PostToolUse') {
      const rows = queryUnreadAscending(db, watermark.lastSurfacedId);
      if (rows.length === 0) {
        return;
      }

      const hasMore = rows.length > PAGE_LIMIT;
      const batch = hasMore ? rows.slice(0, PAGE_LIMIT) : rows;
      const maxBatchId = Math.max(...batch.map((m) => Number(m.id)));
      saveWatermarkAtomicMonotonic(stateDbPath, sessionHash, maxBatchId, 0);

      const lines = [
        '[AISwarmX Swarm Bus — Live Mid-Turn Message Drain]',
        `New unread messages for Claude (${batch.length}${hasMore ? '+' : ''}):`,
        ...formatMessageLines(batch),
        'Tip: use MCP `mobai-swarm-bus` (`bus_ack`, `bus_send`, `bus_locks`) to coordinate.'
      ];

      process.stdout.write(JSON.stringify({
        hookSpecificOutput: {
          hookEventName,
          additionalContext: lines.join('\n')
        }
      }) + '\n');
      return;
    }

    // SessionStart / UserPromptSubmit: surface newest unread + active foreign locks.
    const rows = queryUnreadRecent(db);
    const hasMore = rows.length > PAGE_LIMIT;
    const unreadMessages = hasMore ? rows.slice(0, PAGE_LIMIT) : rows;
    const foreignLocks = queryForeignLocks(db, nowIso);
    if (unreadMessages.length === 0 && foreignLocks.length === 0) {
      return;
    }

    if (unreadMessages.length > 0 && !hasMore) {
      const maxId = Math.max(...unreadMessages.map((m) => Number(m.id)));
      saveWatermarkAtomicMonotonic(stateDbPath, sessionHash, maxId, 0);
    }

    const lines = ['[AISwarmX Swarm Bus — Live Cross-Tool Context]'];
    if (unreadMessages.length > 0) {
      lines.push(`Unread messages for Claude (${unreadMessages.length}${hasMore ? '+' : ''}):`);
      lines.push(...formatMessageLines(unreadMessages));
    }

    if (foreignLocks.length > 0) {
      lines.push('Active foreign path locks (do not edit without coordination):');
      for (const lk of foreignLocks) {
        const prTag = lk.pr_number ? ` (PR #${lk.pr_number})` : '';
        lines.push(`- ${lk.agent}${prTag}: ${lk.path_pattern}`);
      }
    }

    lines.push('Tip: use MCP `mobai-swarm-bus` (`bus_ack`, `bus_send`, `bus_locks`) to reply or coordinate.');

    process.stdout.write(JSON.stringify({
      hookSpecificOutput: {
        hookEventName,
        additionalContext: lines.join('\n')
      }
    }) + '\n');
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
