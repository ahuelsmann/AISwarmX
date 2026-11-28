import test from 'node:test';
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawn, spawnSync } from 'node:child_process';
import { DatabaseSync } from 'node:sqlite';

const HOOK_SCRIPT = path.resolve(import.meta.dirname, 'claude-swarm-bus-hook.mjs');

function setupTempEnv() {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'aiswarmx-hook-test-'));
  const dbPath = path.join(dir, 'swarm-bus.db');
  const statePath = path.join(dir, 'claude-hook-state.db');
  const db = new DatabaseSync(dbPath);
  db.exec(`
    CREATE TABLE messages (
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
    CREATE TABLE locks (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      agent TEXT NOT NULL,
      path_pattern TEXT NOT NULL UNIQUE,
      pr_number INTEGER,
      claimed_at_utc TEXT NOT NULL,
      expires_at_utc TEXT NOT NULL
    );
  `);
  return { dir, dbPath, statePath, db };
}

function runHook(dbPath, statePath, payload, envOverride = {}) {
  return spawnSync(process.execPath, ['--no-warnings', HOOK_SCRIPT], {
    env: {
      ...process.env,
      MOBADEV_SWARM_BUS_DB: dbPath,
      MOBADEV_CLAUDE_HOOK_STATE_FILE: statePath,
      ...envOverride
    },
    input: payload === null ? '' : JSON.stringify(payload),
    encoding: 'utf8'
  });
}

function runHookAsync(dbPath, statePath, payload) {
  return new Promise((resolve, reject) => {
    const child = spawn(process.execPath, ['--no-warnings', HOOK_SCRIPT], {
      env: {
        ...process.env,
        MOBADEV_SWARM_BUS_DB: dbPath,
        MOBADEV_CLAUDE_HOOK_STATE_FILE: statePath
      },
      stdio: ['pipe', 'pipe', 'pipe']
    });
    let stdout = '';
    child.stdout.on('data', (chunk) => { stdout += chunk; });
    child.on('error', reject);
    child.on('close', (status) => resolve({ status, stdout }));
    child.stdin.end(JSON.stringify(payload));
  });
}

test('Missing DB, unknown event fallback, and SessionStart foreign locks injection work safely read-only', () => {
  const { dir, dbPath, statePath, db } = setupTempEnv();
  try {
    const missing = runHook(path.join(dir, 'nonexistent.db'), statePath, { hook_event_name: 'PostToolUse' });
    assert.equal(missing.status, 0);
    assert.equal(missing.stdout, '');

    const nowIso = new Date().toISOString();
    const futureIso = new Date(Date.now() + 3600_000).toISOString();
    db.prepare(`
      INSERT INTO locks (agent, path_pattern, pr_number, claimed_at_utc, expires_at_utc)
      VALUES ('codex', 'dashboard/src', 56, ?, ?)
    `).run(nowIso, futureIso);

    const unknownEv = runHook(dbPath, statePath, { hook_event_name: 'SomeFutureEvent' });
    assert.equal(unknownEv.status, 0);
    const out = JSON.parse(unknownEv.stdout.trim());
    assert.equal(out.hookSpecificOutput.hookEventName, 'UserPromptSubmit');
    assert.match(out.hookSpecificOutput.additionalContext, /dashboard\/src/);

    const countRow = db.prepare('SELECT COUNT(*) AS cnt FROM messages').get();
    assert.equal(countRow.cnt, 0);
  } finally {
    db.close();
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('Cursor draining reads forward across a burst larger than 8 without stranding messages', () => {
  const { dir, dbPath, statePath, db } = setupTempEnv();
  try {
    const nowIso = new Date().toISOString();
    const insert = db.prepare(`
      INSERT INTO messages (created_at_utc, sender, recipient, topic, body, status)
      VALUES (?, 'codex', 'claude-opus', ?, ?, 'unread')
    `);
    for (let i = 1; i <= 11; i += 1) {
      insert.run(nowIso, `burst-${i}`, `Body ${i}`);
    }

    const batch1 = runHook(dbPath, statePath, { hook_event_name: 'PostToolUse', session_id: 'burst-sess' });
    assert.equal(batch1.status, 0);
    const ctx1 = JSON.parse(batch1.stdout.trim()).hookSpecificOutput.additionalContext;
    for (let i = 1; i <= 8; i += 1) {
      assert.match(ctx1, new RegExp(`#` + i + ` \\[burst-` + i + `\\]`));
    }
    assert.doesNotMatch(ctx1, /burst-9/);

    const batch2 = runHook(dbPath, statePath, { hook_event_name: 'PostToolUse', session_id: 'burst-sess' });
    assert.equal(batch2.status, 0);
    const ctx2 = JSON.parse(batch2.stdout.trim()).hookSpecificOutput.additionalContext;
    for (let i = 9; i <= 11; i += 1) {
      assert.match(ctx2, new RegExp(`#` + i + ` \\[burst-` + i + `\\]`));
    }

    const batch3 = runHook(dbPath, statePath, { hook_event_name: 'PostToolUse', session_id: 'burst-sess' });
    assert.equal(batch3.status, 0);
    assert.equal(batch3.stdout, '');
  } finally {
    db.close();
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('Competing cold-start and same-session hooks preserve all watermarks and atomic MAX bounds', async () => {
  const { dir, dbPath, statePath, db } = setupTempEnv();
  try {
    const nowIso = new Date().toISOString();
    const insert = db.prepare(`
      INSERT INTO messages (created_at_utc, sender, recipient, topic, body, status)
      VALUES (?, 'codex', 'claude-opus', ?, ?, 'unread')
    `);
    insert.run(nowIso, 'msg-1', 'First');

    // Cold-start 4 distinct hooks concurrently on a brand-new state DB (exercises WAL creation + busy_timeout + retry)
    const coldSessions = ['cold-1', 'cold-2', 'cold-3', 'cold-4'];
    const coldResults = await Promise.all([
      runHookAsync(dbPath, statePath, { hook_event_name: 'PreToolUse', session_id: coldSessions[0] }),
      runHookAsync(dbPath, statePath, { hook_event_name: 'PostToolUse', session_id: coldSessions[1] }),
      runHookAsync(dbPath, statePath, { hook_event_name: 'UserPromptSubmit', session_id: coldSessions[2] }),
      runHookAsync(dbPath, statePath, { hook_event_name: 'Stop', session_id: coldSessions[3], stop_hook_active: false })
    ]);
    for (const res of coldResults) {
      assert.equal(res.status, 0);
    }

    const sdb1 = new DatabaseSync(statePath, { readOnly: true });
    try {
      for (const [idx, sid] of coldSessions.entries()) {
        const key = crypto.createHash('sha256').update(`${dbPath}::*::${sid}`).digest('hex').slice(0, 24);
        const row = sdb1.prepare('SELECT last_surfaced_id, last_stop_blocked_id FROM hook_watermarks WHERE session_key = ?').get(key);
        assert.ok(row, `Expected watermark row for ${sid}`);
        assert.equal(row.last_surfaced_id, 1);
        assert.equal(row.last_stop_blocked_id, idx === 3 ? 1 : 0);
      }
    } finally {
      sdb1.close();
    }

    // Now test same-session non-regression: cold-4 already has (last_surfaced_id=1, last_stop_blocked_id=1).
    // Insert #2 and run PreToolUse (sets surfaced=2, passes stopBlockedId=0) -> MAX preserves last_stop_blocked_id=1.
    insert.run(nowIso, 'msg-2', 'Second');
    const preAfterStop = runHook(dbPath, statePath, { hook_event_name: 'PreToolUse', session_id: 'cold-4' });
    assert.equal(preAfterStop.status, 0);
    assert.match(JSON.parse(preAfterStop.stdout.trim()).hookSpecificOutput.additionalContext, /msg-2/);

    const sdb2 = new DatabaseSync(statePath, { readOnly: true });
    try {
      const key4 = crypto.createHash('sha256').update(`${dbPath}::*::cold-4`).digest('hex').slice(0, 24);
      const row4 = sdb2.prepare('SELECT last_surfaced_id, last_stop_blocked_id FROM hook_watermarks WHERE session_key = ?').get(key4);
      assert.equal(row4.last_surfaced_id, 2);
      assert.equal(row4.last_stop_blocked_id, 1);
    } finally {
      sdb2.close();
    }

    // Re-running PreToolUse must stay silent, while Stop blocks only on #2 and reaches (2, 2)
    assert.equal(runHook(dbPath, statePath, { hook_event_name: 'PostToolUse', session_id: 'cold-4' }).stdout, '');
    const stop2 = runHook(dbPath, statePath, { hook_event_name: 'Stop', session_id: 'cold-4', stop_hook_active: false });
    assert.equal(JSON.parse(stop2.stdout.trim()).decision, 'block');
    assert.equal(runHook(dbPath, statePath, { hook_event_name: 'Stop', session_id: 'cold-4', stop_hook_active: false }).stdout, '');
  } finally {
    db.close();
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('Per-session isolation, Stop loop guard, and MOBADEV_SWARM_BUS_HOOK_DB override work cleanly', async () => {
  const { dir, dbPath, statePath, db } = setupTempEnv();
  try {
    const nowIso = new Date().toISOString();
    db.prepare(`
      INSERT INTO messages (created_at_utc, sender, recipient, topic, body, status)
      VALUES (?, 'codex', 'claude-opus', 'concurrent-note', 'Review delta', 'unread')
    `).run(nowIso);

    const [r1, r2] = await Promise.all([
      runHookAsync(dbPath, statePath, { hook_event_name: 'PostToolUse', session_id: 'sess-A' }),
      runHookAsync(dbPath, statePath, { hook_event_name: 'PostToolUse', session_id: 'sess-B' })
    ]);
    assert.equal(r1.status, 0);
    assert.equal(r2.status, 0);
    assert.match(JSON.parse(r1.stdout.trim()).hookSpecificOutput.additionalContext, /concurrent-note/);
    assert.match(JSON.parse(r2.stdout.trim()).hookSpecificOutput.additionalContext, /concurrent-note/);

    // stop_hook_active: true bypasses Stop block even when unread messages exist
    db.prepare(`
      INSERT INTO messages (created_at_utc, sender, recipient, topic, body, status)
      VALUES (?, 'codex', 'claude-opus', 'follow-up', 'Extra note', 'unread')
    `).run(nowIso);
    const activeGuard = runHook(dbPath, statePath, { hook_event_name: 'Stop', session_id: 'sess-A', stop_hook_active: true });
    assert.equal(activeGuard.stdout, '');

    // MOBADEV_SWARM_BUS_HOOK_DB overrides MOBADEV_SWARM_BUS_DB cleanly
    const bogusDb = path.join(dir, 'unused.db');
    const viaHookDb = runHook(bogusDb, statePath, { hook_event_name: 'PostToolUse', session_id: 'sess-hook-override' }, {
      MOBADEV_SWARM_BUS_HOOK_DB: dbPath
    });
    assert.equal(viaHookDb.status, 0);
    assert.match(JSON.parse(viaHookDb.stdout.trim()).hookSpecificOutput.additionalContext, /follow-up/);
  } finally {
    db.close();
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('N-world v2 database filters unread messages and locks by cwd workspace_id including generic git repos', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'aiswarmx-hook-nworld-'));
  const dbPath = path.join(dir, 'swarm-bus-v2.db');
  const statePath = path.join(dir, 'claude-hook-state.db');
  const customRepoDir = path.join(dir, 'NewWorldRepo');
  fs.mkdirSync(path.join(customRepoDir, '.git'), { recursive: true });

  const db = new DatabaseSync(dbPath);
  try {
    db.exec(`
      CREATE TABLE messages (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        workspace_id TEXT NOT NULL DEFAULT 'default',
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
      CREATE TABLE locks (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        workspace_id TEXT NOT NULL DEFAULT 'default',
        agent TEXT NOT NULL,
        path_pattern TEXT NOT NULL,
        pr_number INTEGER,
        claimed_at_utc TEXT NOT NULL,
        expires_at_utc TEXT NOT NULL
      );
    `);

    const nowIso = new Date().toISOString();
    const futureIso = new Date(Date.now() + 3600_000).toISOString();
    const insertMsg = db.prepare(`
      INSERT INTO messages (workspace_id, created_at_utc, sender, recipient, topic, body, status)
      VALUES (?, ?, 'codex', 'claude-opus', ?, ?, 'unread')
    `);
    insertMsg.run('mobaflow', nowIso, 'mobaflow-topic', 'MOBAflow note');
    insertMsg.run('sentix', nowIso, 'sentix-topic', 'SentiX note');
    insertMsg.run('newworldrepo', nowIso, 'nworld-topic', 'NewWorld note');
    insertMsg.run('default', nowIso, 'shared-topic', 'Shared default note');

    db.prepare(`
      INSERT INTO locks (workspace_id, agent, path_pattern, pr_number, claimed_at_utc, expires_at_utc)
      VALUES ('sentix', 'codex', 'src/dotnet/SentiX.Risk', 10, ?, ?)
    `).run(nowIso, futureIso);

    // MOBAflow session only sees mobaflow + default (NOT sentix or newworldrepo, and NO sentix lock).
    const mfOut = runHook(dbPath, statePath, {
      hook_event_name: 'SessionStart',
      session_id: 'mf-sess',
      cwd: 'C:\\Repo\\ahuelsmann\\MOBAflow'
    }, { MOBADEV_SWARM_BUS_V2_DB: dbPath });
    assert.equal(mfOut.status, 0);
    const mfCtx = JSON.parse(mfOut.stdout.trim()).hookSpecificOutput.additionalContext;
    assert.match(mfCtx, /mobaflow-topic/);
    assert.match(mfCtx, /shared-topic/);
    assert.doesNotMatch(mfCtx, /sentix-topic/);
    assert.doesNotMatch(mfCtx, /nworld-topic/);
    assert.doesNotMatch(mfCtx, /SentiX\.Risk/);

    // Arbitrary Nth git repo infers leaf folder ('newworldrepo') automatically.
    const nwOut = runHook(dbPath, statePath, {
      hook_event_name: 'UserPromptSubmit',
      session_id: 'nw-sess',
      cwd: customRepoDir
    }, { MOBADEV_SWARM_BUS_V2_DB: dbPath });
    assert.equal(nwOut.status, 0);
    const nwCtx = JSON.parse(nwOut.stdout.trim()).hookSpecificOutput.additionalContext;
    assert.match(nwCtx, /nworld-topic/);
    assert.doesNotMatch(nwCtx, /mobaflow-topic/);
  } finally {
    db.close();
    fs.rmSync(dir, { recursive: true, force: true });
  }
});
