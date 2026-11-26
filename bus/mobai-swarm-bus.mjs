#!/usr/bin/env node
/**
 * MOBAi Swarm Bus — Cross-Tool MCP Server & Zero-Token CLI
 *
 * Bridges Claude Code / Desktop, ChatGPT Codex, Antigravity (Gemini/Opus), and
 * zero-token Watchdog scripts over a shared local WAL-mode SQLite database.
 *
 * IMPORTANT: In MCP stdio mode, stdout is strictly reserved for JSON-RPC 2.0 lines.
 */

import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import readline from 'node:readline';
import { DatabaseSync } from 'node:sqlite';

const SERVER_NAME = 'mobai-swarm-bus';
const SERVER_VERSION = '1.0.0';
const PROTOCOL_VERSION = '2024-11-05';
const DEFAULT_CODEX_THREAD_ID = '01a14504-bd6c-7a50-b715-1a2f2ec76467';
const HEADLESS_RECIPIENTS = new Set(['watchdog', 'claude-opus-worker', 'system']);

function resolveDbPath() {
  if (process.env.MOBADEV_SWARM_BUS_DB && process.env.MOBADEV_SWARM_BUS_DB.trim()) {
    return path.resolve(process.env.MOBADEV_SWARM_BUS_DB.trim());
  }
  const localApp = process.env.LOCALAPPDATA && process.env.LOCALAPPDATA.trim()
    ? process.env.LOCALAPPDATA.trim()
    : path.join(os.homedir(), 'AppData', 'Local');
  return path.join(localApp, 'MOBAdev', 'swarm-bus.db');
}

function openBusDb(dbPath = resolveDbPath()) {
  fs.mkdirSync(path.dirname(dbPath), { recursive: true });
  const db = new DatabaseSync(dbPath);
  db.exec(`
    PRAGMA journal_mode = WAL;
    PRAGMA busy_timeout = 5000;

    CREATE TABLE IF NOT EXISTS messages (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      created_at_utc TEXT NOT NULL,
      sender TEXT NOT NULL,
      recipient TEXT NOT NULL,
      topic TEXT NOT NULL,
      body TEXT NOT NULL,
      pr_number INTEGER,
      issue_number INTEGER,
      priority TEXT NOT NULL DEFAULT 'normal',
      reply_to_id INTEGER,
      status TEXT NOT NULL DEFAULT 'unread',
      acked_by TEXT,
      acked_at_utc TEXT
    );

    CREATE INDEX IF NOT EXISTS idx_messages_recipient_status
      ON messages (recipient, status, id DESC);

    CREATE TABLE IF NOT EXISTS locks (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      agent TEXT NOT NULL,
      path_pattern TEXT NOT NULL UNIQUE,
      pr_number INTEGER,
      claimed_at_utc TEXT NOT NULL,
      expires_at_utc TEXT NOT NULL
    );

    CREATE INDEX IF NOT EXISTS idx_locks_expires
      ON locks (expires_at_utc);
  `);
  return { db, dbPath };
}

function nowUtcIso() {
  return new Date().toISOString();
}

function pruneExpiredLocks(db, nowIso = nowUtcIso()) {
  const stmt = db.prepare('DELETE FROM locks WHERE expires_at_utc <= ?');
  stmt.run(nowIso);
}

function normalizePathPattern(raw) {
  let clean = String(raw ?? '').trim().replace(/\\/g, '/').toLowerCase();
  clean = clean.replace(/^\.\/+/, '').replace(/\/+$/, '');
  if (clean.endsWith('/*')) {
    clean = clean.slice(0, -2);
  }
  return clean;
}

function pathsOverlap(aRaw, bRaw) {
  const a = normalizePathPattern(aRaw);
  const b = normalizePathPattern(bRaw);
  if (!a || !b) return false;
  if (a === b) return true;
  if (a.startsWith(b + '/') || b.startsWith(a + '/')) return true;
  return false;
}

function tryWakeCodex(messageText) {
  const codexExe = process.env.MOBADEV_CODEX_EXE && process.env.MOBADEV_CODEX_EXE.trim()
    ? process.env.MOBADEV_CODEX_EXE.trim()
    : path.join(os.homedir(), '.codex', '.sandbox-bin', 'codex.exe');

  const threadId = process.env.MOBADEV_CODEX_THREAD_ID && process.env.MOBADEV_CODEX_THREAD_ID.trim()
    ? process.env.MOBADEV_CODEX_THREAD_ID.trim()
    : DEFAULT_CODEX_THREAD_ID;

  if (!fs.existsSync(codexExe)) {
    return { attempted: false, target: 'codex', reason: 'codex.exe not found' };
  }

  try {
    execFileSync(codexExe, ['queue', '--thread', threadId, '--message', messageText], {
      stdio: 'ignore',
      timeout: 6000,
      windowsHide: true
    });
    return { attempted: true, ok: true, target: 'codex', threadId };
  } catch (err) {
    return {
      attempted: true,
      ok: false,
      target: 'codex',
      threadId,
      error: err instanceof Error ? err.message : String(err)
    };
  }
}

export function handleBusSend(db, args) {
  const sender = String(args.from ?? args.sender ?? '').trim();
  const recipient = String(args.to ?? args.recipient ?? '').trim();
  const topic = String(args.topic ?? '').trim();
  const body = String(args.body ?? '').trim();
  if (!sender || !recipient || !topic || !body) {
    throw new Error('bus_send requires non-empty "from", "to", "topic", and "body".');
  }

  const prNumber = args.pr !== undefined && args.pr !== null && args.pr !== '' ? Number(args.pr) : null;
  const issueNumber = args.issue !== undefined && args.issue !== null && args.issue !== '' ? Number(args.issue) : null;
  const replyToId = args.reply_to !== undefined && args.reply_to !== null && args.reply_to !== '' ? Number(args.reply_to) : null;
  const priority = ['low', 'normal', 'high', 'urgent'].includes(String(args.priority ?? 'normal').toLowerCase())
    ? String(args.priority ?? 'normal').toLowerCase()
    : 'normal';

  const createdAt = nowUtcIso();
  if (sender.toLowerCase() === 'watchdog' && Number.isFinite(prNumber)) {
    db.prepare(`
      UPDATE messages
      SET status = 'acked', acked_by = 'auto-superseded', acked_at_utc = ?
      WHERE lower(sender) = 'watchdog' AND pr_number = ? AND status = 'unread'
    `).run(createdAt, prNumber);
  }

  const isHeadlessRecipient = HEADLESS_RECIPIENTS.has(recipient.toLowerCase());
  const initialStatus = isHeadlessRecipient ? 'acked' : 'unread';
  const initialAckedBy = isHeadlessRecipient ? 'auto-headless' : null;
  const initialAckedAt = isHeadlessRecipient ? createdAt : null;

  const stmt = db.prepare(`
    INSERT INTO messages (created_at_utc, sender, recipient, topic, body, pr_number, issue_number, priority, reply_to_id, status, acked_by, acked_at_utc)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
  `);
  const info = stmt.run(
    createdAt,
    sender,
    recipient,
    topic,
    body,
    Number.isFinite(prNumber) ? prNumber : null,
    Number.isFinite(issueNumber) ? issueNumber : null,
    priority,
    Number.isFinite(replyToId) ? replyToId : null,
    initialStatus,
    initialAckedBy,
    initialAckedAt
  );

  const wakeResults = [];
  if (args.wake === true || args.wake === 'true') {
    const recLower = recipient.toLowerCase();
    if (recLower.includes('codex') || recLower === 'all') {
      const prTag = Number.isFinite(prNumber) ? ` (PR #${prNumber})` : '';
      const wakeMsg = `[SwarmBus #${info.lastInsertRowid}] ${sender} -> ${recipient}${prTag} [${topic}]: ${body}`;
      wakeResults.push(tryWakeCodex(wakeMsg));
    }
  }

  return {
    ok: true,
    id: Number(info.lastInsertRowid),
    createdAtUtc: createdAt,
    from: sender,
    to: recipient,
    topic,
    prNumber: Number.isFinite(prNumber) ? prNumber : null,
    issueNumber: Number.isFinite(issueNumber) ? issueNumber : null,
    priority,
    wakeResults
  };
}

export function handleBusInbox(db, args) {
  const agent = String(args.agent ?? 'all').trim();
  const unreadOnly = args.unread_only === undefined ? true : Boolean(args.unread_only);
  const limit = Math.max(1, Math.min(100, Number(args.limit ?? 20) || 20));
  const includeAllRecipients = agent.toLowerCase() === 'all' || agent === '*';

  const sql = includeAllRecipients
    ? (unreadOnly
        ? `SELECT id, created_at_utc, sender, recipient, topic, body, pr_number, issue_number, priority, reply_to_id, status, acked_by, acked_at_utc
           FROM messages
           WHERE status = 'unread'
           ORDER BY id DESC LIMIT ?`
        : `SELECT id, created_at_utc, sender, recipient, topic, body, pr_number, issue_number, priority, reply_to_id, status, acked_by, acked_at_utc
           FROM messages
           ORDER BY id DESC LIMIT ?`)
    : (unreadOnly
        ? `SELECT id, created_at_utc, sender, recipient, topic, body, pr_number, issue_number, priority, reply_to_id, status, acked_by, acked_at_utc
           FROM messages
           WHERE (lower(recipient) = lower(?) OR lower(recipient) = 'all') AND status = 'unread'
           ORDER BY id DESC LIMIT ?`
        : `SELECT id, created_at_utc, sender, recipient, topic, body, pr_number, issue_number, priority, reply_to_id, status, acked_by, acked_at_utc
           FROM messages
           WHERE (lower(recipient) = lower(?) OR lower(recipient) = 'all')
           ORDER BY id DESC LIMIT ?`);

  const rows = includeAllRecipients ? db.prepare(sql).all(limit) : db.prepare(sql).all(agent, limit);
  return {
    agent,
    unreadOnly,
    count: rows.length,
    messages: rows
  };
}

export function handleBusAck(db, args) {
  const messageId = Number(args.message_id ?? args.id);
  const agent = String(args.agent ?? '').trim();
  const reply = args.reply !== undefined && args.reply !== null ? String(args.reply).trim() : '';
  if (!Number.isFinite(messageId) || !agent) {
    throw new Error('bus_ack requires numeric "message_id" and non-empty "agent".');
  }

  const existing = db.prepare('SELECT id, sender, topic, pr_number, issue_number FROM messages WHERE id = ?').get(messageId);
  if (!existing) {
    throw new Error(`Message #${messageId} not found.`);
  }

  const ackedAt = nowUtcIso();
  db.prepare(`
    UPDATE messages SET status = 'acked', acked_by = ?, acked_at_utc = ? WHERE id = ?
  `).run(agent, ackedAt, messageId);

  let replyResult = null;
  if (reply.length > 0) {
    replyResult = handleBusSend(db, {
      from: agent,
      to: existing.sender,
      topic: `re:${existing.topic}`,
      body: reply,
      pr: existing.pr_number,
      issue: existing.issue_number,
      reply_to: messageId
    });
  }

  return {
    ok: true,
    ackedMessageId: messageId,
    ackedBy: agent,
    ackedAtUtc: ackedAt,
    replyMessage: replyResult
  };
}

export function handleBusLocks(db, args) {
  const nowIso = nowUtcIso();
  pruneExpiredLocks(db, nowIso);

  const action = String(args.action ?? 'list').trim().toLowerCase();
  if (action === 'list') {
    const rows = db.prepare('SELECT id, agent, path_pattern, pr_number, claimed_at_utc, expires_at_utc FROM locks ORDER BY id ASC').all();
    return { ok: true, action: 'list', locks: rows };
  }

  const agent = String(args.agent ?? '').trim();
  const rawPaths = Array.isArray(args.paths)
    ? args.paths.map((p) => String(p).trim()).filter(Boolean)
    : String(args.paths ?? '').split(',').map((p) => p.trim()).filter(Boolean);

  if (action === 'release') {
    if (!agent && rawPaths.length === 0) {
      throw new Error('bus_locks release requires "agent" or "paths".');
    }
    let removed = 0;
    if (rawPaths.length > 0) {
      const del = agent
        ? db.prepare('DELETE FROM locks WHERE lower(agent) = lower(?) AND path_pattern = ?')
        : db.prepare('DELETE FROM locks WHERE path_pattern = ?');
      for (const p of rawPaths) {
        const res = agent ? del.run(agent, p) : del.run(p);
        removed += res.changes;
      }
    } else {
      const res = db.prepare('DELETE FROM locks WHERE lower(agent) = lower(?)').run(agent);
      removed = res.changes;
    }
    const remaining = db.prepare('SELECT id, agent, path_pattern, pr_number, claimed_at_utc, expires_at_utc FROM locks ORDER BY id ASC').all();
    return { ok: true, action: 'release', removedCount: removed, locks: remaining };
  }

  if (action === 'claim') {
    if (!agent || rawPaths.length === 0) {
      throw new Error('bus_locks claim requires "agent" and non-empty "paths".');
    }
    const ttlMin = Math.max(5, Math.min(1440, Number(args.ttl_minutes ?? 120) || 120));
    const expiresIso = new Date(Date.now() + ttlMin * 60_000).toISOString();
    const prNumber = args.pr !== undefined && args.pr !== null && args.pr !== '' ? Number(args.pr) : null;

    const existingLocks = db.prepare('SELECT id, agent, path_pattern, pr_number, claimed_at_utc, expires_at_utc FROM locks').all();
    const conflicts = [];
    for (const reqPath of rawPaths) {
      for (const ex of existingLocks) {
        if (ex.agent.toLowerCase() !== agent.toLowerCase() && pathsOverlap(reqPath, ex.path_pattern)) {
          conflicts.push({
            requestedPath: reqPath,
            conflictingAgent: ex.agent,
            conflictingPath: ex.path_pattern,
            conflictingPr: ex.pr_number,
            expiresAtUtc: ex.expires_at_utc
          });
        }
      }
    }

    if (conflicts.length > 0) {
      return { ok: false, action: 'claim', conflicts };
    }

    const upsert = db.prepare(`
      INSERT INTO locks (agent, path_pattern, pr_number, claimed_at_utc, expires_at_utc)
      VALUES (?, ?, ?, ?, ?)
      ON CONFLICT(path_pattern) DO UPDATE SET
        agent = excluded.agent,
        pr_number = excluded.pr_number,
        claimed_at_utc = excluded.claimed_at_utc,
        expires_at_utc = excluded.expires_at_utc
    `);

    for (const reqPath of rawPaths) {
      upsert.run(agent, reqPath, Number.isFinite(prNumber) ? prNumber : null, nowIso, expiresIso);
    }

    const locks = db.prepare('SELECT id, agent, path_pattern, pr_number, claimed_at_utc, expires_at_utc FROM locks ORDER BY id ASC').all();
    return { ok: true, action: 'claim', claimedPaths: rawPaths, locks };
  }

  throw new Error(`Unsupported bus_locks action '${action}'. Use 'list', 'claim', or 'release'.`);
}

export function handleBusStatus(db, args = {}) {
  const nowIso = nowUtcIso();
  pruneExpiredLocks(db, nowIso);
  const limit = Math.max(1, Math.min(50, Number(args.limit ?? 15) || 15));

  const unreadByRecipient = db.prepare(`
    SELECT recipient, COUNT(*) AS unread_count
    FROM messages
    WHERE status = 'unread'
    GROUP BY recipient
    ORDER BY unread_count DESC, recipient ASC
  `).all();

  const totalUnreadRow = db.prepare(`SELECT COUNT(*) AS cnt FROM messages WHERE status = 'unread'`).get();
  const locks = db.prepare('SELECT id, agent, path_pattern, pr_number, claimed_at_utc, expires_at_utc FROM locks ORDER BY id ASC').all();
  const recentMessages = db.prepare(`
    SELECT id, created_at_utc, sender, recipient, topic, body, pr_number, issue_number, priority, reply_to_id, status, acked_by, acked_at_utc
    FROM messages
    ORDER BY id DESC
    LIMIT ?
  `).all(limit);

  return {
    generatedAtUtc: nowIso,
    totalUnread: Number(totalUnreadRow?.cnt ?? 0),
    unreadByRecipient,
    activeLocks: locks,
    recentMessages
  };
}

const TOOL_DEFINITIONS = [
  {
    name: 'bus_send',
    description: 'Send a cross-tool message via MOBAi Swarm Bus to another standing agent or @all (Claude, Codex, Antigravity, Watchdog).',
    inputSchema: {
      type: 'object',
      properties: {
        from: { type: 'string', description: 'Sender identifier, e.g. claude-opus, codex, antigravity-gemini, antigravity-opus.' },
        to: { type: 'string', description: 'Recipient identifier (claude-opus, codex, antigravity-gemini, antigravity-opus, or all).' },
        topic: { type: 'string', description: 'Short topic tag, e.g. design-go, review-pass, sonar-finding, lock-handoff.' },
        body: { type: 'string', description: 'Concise message body with exact SHA/files/decision.' },
        pr: { type: 'integer', description: 'Optional MOBAflow/MOBAdev PR number.' },
        issue: { type: 'integer', description: 'Optional GitHub issue number.' },
        priority: { type: 'string', enum: ['low', 'normal', 'high', 'urgent'], default: 'normal' },
        reply_to: { type: 'integer', description: 'Optional parent bus message id.' },
        wake: { type: 'boolean', description: 'If true and recipient includes codex or all, also pushes into codex.exe queue.', default: false }
      },
      required: ['from', 'to', 'topic', 'body']
    }
  },
  {
    name: 'bus_inbox',
    description: 'Read unread (or recent) cross-tool messages addressed to the specified agent or all.',
    inputSchema: {
      type: 'object',
      properties: {
        agent: { type: 'string', description: 'Recipient agent id, e.g. claude-opus, codex, antigravity-gemini.' },
        unread_only: { type: 'boolean', default: true },
        limit: { type: 'integer', default: 20 }
      },
      required: ['agent']
    }
  },
  {
    name: 'bus_ack',
    description: 'Acknowledge a Swarm Bus message as processed and optionally send a threaded reply to the sender.',
    inputSchema: {
      type: 'object',
      properties: {
        message_id: { type: 'integer', description: 'ID of the message to acknowledge.' },
        agent: { type: 'string', description: 'Agent acknowledging the message.' },
        reply: { type: 'string', description: 'Optional reply body sent back to original sender.' }
      },
      required: ['message_id', 'agent']
    }
  },
  {
    name: 'bus_locks',
    description: 'Claim, release, or list active cross-tool path locks to prevent overlapping edits across Claude, Codex, and Antigravity.',
    inputSchema: {
      type: 'object',
      properties: {
        action: { type: 'string', enum: ['list', 'claim', 'release'], default: 'list' },
        agent: { type: 'string', description: 'Owning agent identifier.' },
        paths: {
          type: 'array',
          items: { type: 'string' },
          description: 'Repo-relative locked area paths/prefixes, e.g. [".github/workflows", "MOBAdemo/app/src/shell"].'
        },
        pr: { type: 'integer', description: 'Optional associated PR number.' },
        ttl_minutes: { type: 'integer', default: 120 }
      },
      required: ['action']
    }
  },
  {
    name: 'bus_status',
    description: 'Inspect live Swarm Bus summary: unread counts, active cross-tool path locks, and recent messages.',
    inputSchema: {
      type: 'object',
      properties: {
        limit: { type: 'integer', default: 15 }
      }
    }
  }
];

function executeTool(db, name, args = {}) {
  switch (name) {
    case 'bus_send':
      return handleBusSend(db, args);
    case 'bus_inbox':
      return handleBusInbox(db, args);
    case 'bus_ack':
      return handleBusAck(db, args);
    case 'bus_locks':
      return handleBusLocks(db, args);
    case 'bus_status':
      return handleBusStatus(db, args);
    default:
      throw new Error(`Unknown tool: ${name}`);
  }
}

function writeJsonRpc(msg) {
  process.stdout.write(JSON.stringify(msg) + '\n');
}

function runMcpServer() {
  const { db } = openBusDb();
  const rl = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });

  rl.on('close', () => {
    try {
      db.close();
    } catch {
      // Ignore close error on shutdown.
    }
  });

  rl.on('line', (line) => {
    const trimmed = line.trim();
    if (!trimmed) return;

    let req;
    try {
      req = JSON.parse(trimmed);
    } catch {
      writeJsonRpc({ jsonrpc: '2.0', id: null, error: { code: -32700, message: 'Parse error' } });
      return;
    }

    const id = Object.prototype.hasOwnProperty.call(req, 'id') ? req.id : undefined;

    if (req.method === 'notifications/initialized' || id === undefined) {
      return;
    }

    try {
      if (req.method === 'initialize') {
        writeJsonRpc({
          jsonrpc: '2.0',
          id,
          result: {
            protocolVersion: PROTOCOL_VERSION,
            capabilities: { tools: { listChanged: false } },
            serverInfo: { name: SERVER_NAME, version: SERVER_VERSION }
          }
        });
      } else if (req.method === 'ping') {
        writeJsonRpc({ jsonrpc: '2.0', id, result: {} });
      } else if (req.method === 'tools/list') {
        writeJsonRpc({ jsonrpc: '2.0', id, result: { tools: TOOL_DEFINITIONS } });
      } else if (req.method === 'tools/call') {
        const toolName = req.params?.name;
        const toolArgs = req.params?.arguments ?? {};
        const payload = executeTool(db, toolName, toolArgs);
        writeJsonRpc({
          jsonrpc: '2.0',
          id,
          result: {
            content: [{ type: 'text', text: JSON.stringify(payload, null, 2) }],
            isError: payload?.ok === false
          }
        });
      } else {
        writeJsonRpc({ jsonrpc: '2.0', id, error: { code: -32601, message: `Method not found: ${req.method}` } });
      }
    } catch (err) {
      writeJsonRpc({
        jsonrpc: '2.0',
        id,
        result: {
          content: [{ type: 'text', text: JSON.stringify({ ok: false, error: err instanceof Error ? err.message : String(err) }) }],
          isError: true
        }
      });
    }
  });
}

function parseCliFlags(argv) {
  const flags = {};
  for (let i = 0; i < argv.length; i++) {
    const token = argv[i];
    if (!token.startsWith('--')) continue;
    const key = token.slice(2);
    const next = argv[i + 1];
    if (next === undefined || next.startsWith('--')) {
      flags[key] = true;
    } else {
      flags[key] = next;
      i++;
    }
  }
  return flags;
}

function runCli(args) {
  const sub = String(args[0] ?? 'status').toLowerCase();
  const flags = parseCliFlags(args.slice(1));
  const { db } = openBusDb();

  let result;
  if (sub === 'send') {
    result = handleBusSend(db, {
      from: flags.from,
      to: flags.to,
      topic: flags.topic,
      body: flags.body,
      pr: flags.pr,
      issue: flags.issue,
      priority: flags.priority,
      reply_to: flags['reply-to'],
      wake: flags.wake === true || flags.wake === 'true'
    });
  } else if (sub === 'inbox') {
    result = handleBusInbox(db, {
      agent: flags.agent ?? 'all',
      unread_only: flags.all ? false : true,
      limit: flags.limit
    });
  } else if (sub === 'ack') {
    result = handleBusAck(db, {
      message_id: flags['message-id'] ?? flags.id,
      agent: flags.agent,
      reply: flags.reply
    });
  } else if (sub === 'locks') {
    result = handleBusLocks(db, {
      action: flags.action ?? 'list',
      agent: flags.agent,
      paths: flags.paths,
      pr: flags.pr,
      ttl_minutes: flags['ttl-minutes']
    });
  } else if (sub === 'status') {
    result = handleBusStatus(db, { limit: flags.limit });
  } else {
    throw new Error(`Unknown CLI command '${sub}'. Expected: send, inbox, ack, locks, status.`);
  }

  process.stdout.write(JSON.stringify(result, null, 2) + '\n');
}

const isEntryPoint = process.argv[1] && path.resolve(process.argv[1]) === path.resolve(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1'));
if (isEntryPoint) {
  const cliIndex = process.argv.indexOf('--cli');
  if (cliIndex !== -1) {
    runCli(process.argv.slice(cliIndex + 1));
  } else {
    runMcpServer();
  }
}
