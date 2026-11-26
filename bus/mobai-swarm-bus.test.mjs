import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import readline from 'node:readline';
import test from 'node:test';
import { DatabaseSync } from 'node:sqlite';

const BUS_SCRIPT = path.resolve(import.meta.dirname, 'mobai-swarm-bus.mjs');
const HOOK_SCRIPT = path.resolve(import.meta.dirname, 'claude-swarm-bus-hook.mjs');

function createTempDbPath() {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'mobai-bus-test-'));
  return { dir, dbPath: path.join(dir, 'swarm-bus.db') };
}

test('MOBAi Swarm Bus handles full MCP JSON-RPC stdio round-trip', async () => {
  const { dir, dbPath } = createTempDbPath();
  const child = spawn(process.execPath, ['--no-warnings', BUS_SCRIPT], {
    env: { ...process.env, MOBADEV_SWARM_BUS_DB: dbPath },
    stdio: ['pipe', 'pipe', 'pipe']
  });

  const rl = readline.createInterface({ input: child.stdout });
  const pending = new Map();
  rl.on('line', (line) => {
    const parsed = JSON.parse(line);
    const resolve = pending.get(parsed.id);
    if (resolve) {
      pending.delete(parsed.id);
      resolve(parsed);
    }
  });

  let nextId = 1;
  function rpc(method, params = {}) {
    const id = nextId++;
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error(`Timeout waiting for ${method}`)), 5000);
      pending.set(id, (res) => {
        clearTimeout(timer);
        resolve(res);
      });
      child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n');
    });
  }

  try {
    const init = await rpc('initialize', {});
    assert.equal(init.result.serverInfo.name, 'mobai-swarm-bus');

    const tools = await rpc('tools/list', {});
    const names = tools.result.tools.map((t) => t.name);
    assert.deepEqual(names, ['bus_send', 'bus_inbox', 'bus_ack', 'bus_locks', 'bus_status', 'bus_review_claim', 'bus_queue']);

    const sendRes = await rpc('tools/call', {
      name: 'bus_send',
      arguments: {
        from: 'antigravity-gemini',
        to: 'codex',
        topic: 'review-pass',
        body: 'PR #541 P3 UI/UX PASS',
        pr: 541,
        priority: 'high'
      }
    });
    const sendPayload = JSON.parse(sendRes.result.content[0].text);
    assert.equal(sendPayload.ok, true);
    assert.equal(sendPayload.id, 1);

    const inboxRes = await rpc('tools/call', {
      name: 'bus_inbox',
      arguments: { agent: 'codex', unread_only: true }
    });
    const inboxPayload = JSON.parse(inboxRes.result.content[0].text);
    assert.equal(inboxPayload.count, 1);
    assert.equal(inboxPayload.messages[0].topic, 'review-pass');

    const ackRes = await rpc('tools/call', {
      name: 'bus_ack',
      arguments: { message_id: 1, agent: 'codex', reply: 'Fixing polish notes now.' }
    });
    const ackPayload = JSON.parse(ackRes.result.content[0].text);
    assert.equal(ackPayload.ok, true);
    assert.equal(ackPayload.replyMessage.to, 'antigravity-gemini');

    const claim1 = await rpc('tools/call', {
      name: 'bus_locks',
      arguments: { action: 'claim', agent: 'claude-opus', paths: ['.github/workflows'], pr: 537 }
    });
    assert.equal(JSON.parse(claim1.result.content[0].text).ok, true);

    const claimConflict = await rpc('tools/call', {
      name: 'bus_locks',
      arguments: { action: 'claim', agent: 'antigravity-gemini', paths: ['.github/workflows/quality.yml'], pr: 539 }
    });
    const conflictPayload = JSON.parse(claimConflict.result.content[0].text);
    assert.equal(conflictPayload.ok, false);
    assert.equal(conflictPayload.conflicts.length, 1);
    assert.equal(conflictPayload.conflicts[0].conflictingAgent, 'claude-opus');

    const release = await rpc('tools/call', {
      name: 'bus_locks',
      arguments: { action: 'release', agent: 'claude-opus' }
    });
    assert.equal(JSON.parse(release.result.content[0].text).locks.length, 0);

    const status = await rpc('tools/call', {
      name: 'bus_status',
      arguments: { limit: 10 }
    });
    const statusPayload = JSON.parse(status.result.content[0].text);
    assert.equal(statusPayload.totalUnread, 1);
    assert.equal(statusPayload.recentMessages.length, 2);
  } finally {
    rl.close();
    child.stdin.end();
    await new Promise((resolve) => {
      const timer = setTimeout(() => {
        child.kill();
        resolve();
      }, 1500);
      child.once('exit', () => {
        clearTimeout(timer);
        resolve();
      });
    });
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('Claude Swarm Bus hook emits nothing when empty and injects unread messages + foreign locks when present', () => {
  const { dir, dbPath } = createTempDbPath();
  try {
    const emptyRes = spawnSync(process.execPath, ['--no-warnings', HOOK_SCRIPT], {
      env: { ...process.env, MOBADEV_SWARM_BUS_DB: dbPath },
      input: JSON.stringify({ hook_event_name: 'UserPromptSubmit' }),
      encoding: 'utf8'
    });
    assert.equal(emptyRes.status, 0);
    assert.equal(emptyRes.stdout.trim(), '');

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
    const nowIso = new Date().toISOString();
    const futureIso = new Date(Date.now() + 3600_000).toISOString();
    db.prepare(`
      INSERT INTO messages (created_at_utc, sender, recipient, topic, body, pr_number, priority, status)
      VALUES (?, 'antigravity-gemini', 'claude-opus', 'sonar-finding', 'Fix githubactions:S6505 at quality.yml:765', 537, 'high', 'unread')
    `).run(nowIso);
    db.prepare(`
      INSERT INTO locks (agent, path_pattern, pr_number, claimed_at_utc, expires_at_utc)
      VALUES ('codex', 'MOBAdemo/app/src/shell', 541, ?, ?)
    `).run(nowIso, futureIso);
    db.close();

    const populatedRes = spawnSync(process.execPath, ['--no-warnings', HOOK_SCRIPT], {
      env: { ...process.env, MOBADEV_SWARM_BUS_DB: dbPath },
      input: JSON.stringify({ hook_event_name: 'UserPromptSubmit' }),
      encoding: 'utf8'
    });
    assert.equal(populatedRes.status, 0);
    const parsed = JSON.parse(populatedRes.stdout.trim());
    assert.equal(parsed.hookSpecificOutput.hookEventName, 'UserPromptSubmit');
    assert.match(parsed.hookSpecificOutput.additionalContext, /sonar-finding/);
    assert.match(parsed.hookSpecificOutput.additionalContext, /PR #537/);
    assert.match(parsed.hookSpecificOutput.additionalContext, /MOBAdemo\/app\/src\/shell/);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('Swarm Bus auto-acks headless recipients, supersedes stale watchdog PR alerts, and aggregates inbox for all', async () => {
  const { dir, dbPath } = createTempDbPath();
  const child = spawn(process.execPath, ['--no-warnings', BUS_SCRIPT], {
    env: { ...process.env, MOBADEV_SWARM_BUS_DB: dbPath },
    stdio: ['pipe', 'pipe', 'pipe']
  });

  const rl = readline.createInterface({ input: child.stdout });
  const pending = new Map();
  rl.on('line', (line) => {
    const parsed = JSON.parse(line);
    const resolve = pending.get(parsed.id);
    if (resolve) {
      pending.delete(parsed.id);
      resolve(parsed);
    }
  });

  let nextId = 1;
  function rpc(method, params = {}) {
    const id = nextId++;
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error(`Timeout waiting for ${method}`)), 5000);
      pending.set(id, (res) => {
        clearTimeout(timer);
        resolve(res);
      });
      child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n');
    });
  }

  try {
    await rpc('initialize', {});

    // 1. Watchdog emits first alert on PR #545, then a newer alert on the same PR -> older alert auto-superseded.
    await rpc('tools/call', {
      name: 'bus_send',
      arguments: { from: 'watchdog', to: 'codex', topic: 'CiOrSonarFailed', body: 'first', pr: 545 }
    });
    await rpc('tools/call', {
      name: 'bus_send',
      arguments: { from: 'watchdog', to: 'codex', topic: 'AllChecksGreenReady', body: 'second', pr: 545 }
    });

    // 2. Ack with reply back to headless 'watchdog' -> reply is auto-marked acked (never creates orphan unread).
    await rpc('tools/call', {
      name: 'bus_ack',
      arguments: { message_id: 2, agent: 'codex', reply: 'Received green alert.' }
    });

    // 3. Message to headless 'claude-opus-worker' is also auto-acked immediately.
    await rpc('tools/call', {
      name: 'bus_send',
      arguments: { from: 'antigravity-gemini', to: 'claude-opus-worker', topic: 're:p1-review-pass', body: 'Noted.', pr: 544 }
    });

    // 4. Send one real unread message to codex and query agent: 'all'.
    await rpc('tools/call', {
      name: 'bus_send',
      arguments: { from: 'antigravity-gemini', to: 'codex', topic: 'review-pass', body: 'PR #545 PASS', pr: 545 }
    });

    const allInbox = JSON.parse((await rpc('tools/call', {
      name: 'bus_inbox',
      arguments: { agent: 'all', unread_only: true }
    })).result.content[0].text);
    assert.equal(allInbox.count, 1);
    assert.equal(allInbox.messages[0].recipient, 'codex');
    assert.equal(allInbox.messages[0].topic, 'review-pass');

    const status = JSON.parse((await rpc('tools/call', {
      name: 'bus_status',
      arguments: { limit: 10 }
    })).result.content[0].text);
    assert.equal(status.totalUnread, 1);
  } finally {
    rl.close();
    child.stdin.end();
    await new Promise((resolve) => {
      const timer = setTimeout(() => {
        child.kill();
        resolve();
      }, 1500);
      child.once('exit', () => {
        clearTimeout(timer);
        resolve();
      });
    });
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('bus_send deduplicates unchanged state and inserts when state changes', async () => {
  const { openBusDb, handleBusSend } = await import('./mobai-swarm-bus.mjs');
  const { dir, dbPath } = createTempDbPath();
  const { db } = openBusDb(dbPath);
  try {
    const first = handleBusSend(db, {
      from: 'codex',
      to: 'claude-opus',
      topic: 'slice-status',
      pr: 601,
      body: 'Working on checks',
      state: 'running:sha1'
    });
    assert.equal(first.ok, true);
    assert.equal(first.deduplicated, undefined);

    const dup = handleBusSend(db, {
      from: 'codex',
      to: 'claude-opus',
      topic: 'slice-status',
      pr: 601,
      body: 'Still running checks',
      state: 'running:sha1'
    });
    assert.equal(dup.ok, true);
    assert.equal(dup.deduplicated, true);
    assert.equal(dup.existing_id, first.id);

    const changed = handleBusSend(db, {
      from: 'codex',
      to: 'claude-opus',
      topic: 'slice-status',
      pr: 601,
      body: 'Checks green now',
      state: 'green:sha1'
    });
    assert.equal(changed.ok, true);
    assert.equal(changed.deduplicated, undefined);
    assert.notEqual(changed.id, first.id);

    const countRow = db.prepare('SELECT COUNT(*) AS cnt FROM messages').get();
    assert.equal(countRow.cnt, 2);
  } finally {
    db.close();
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('bus_review_claim enforces single holder per (pr_number, head_sha, role)', async () => {
  const { openBusDb, handleBusReviewClaim } = await import('./mobai-swarm-bus.mjs');
  const { dir, dbPath } = createTempDbPath();
  const { db } = openBusDb(dbPath);
  try {
    const sha1 = '1111111111111111111111111111111111111111';
    const sha2 = '2222222222222222222222222222222222222222';

    const first = handleBusReviewClaim(db, {
      agent: 'codex',
      pr_number: 55,
      head_sha: sha1,
      role: 'final'
    });
    assert.equal(first.claimed, true);

    const second = handleBusReviewClaim(db, {
      agent: 'antigravity-gemini',
      pr_number: 55,
      head_sha: sha1,
      role: 'final'
    });
    assert.equal(second.claimed, false);
    assert.equal(second.holder, 'codex');
    assert.ok(second.claimed_at_utc);

    const differentRole = handleBusReviewClaim(db, {
      agent: 'antigravity-gemini',
      pr_number: 55,
      head_sha: sha1,
      role: 'p3-verification'
    });
    assert.equal(differentRole.claimed, true);

    const newHead = handleBusReviewClaim(db, {
      agent: 'antigravity-opus',
      pr_number: 55,
      head_sha: sha2,
      role: 'final'
    });
    assert.equal(newHead.claimed, true);
  } finally {
    db.close();
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('bus_queue supports priority/oldest ordering, resume renewal, expired lease takeover, and code_wip L-only gate', async () => {
  const { openBusDb, handleBusQueue } = await import('./mobai-swarm-bus.mjs');
  const { dir, dbPath } = createTempDbPath();
  const { db } = openBusDb(dbPath);
  try {
    // Upsert items out of priority order
    handleBusQueue(db, { action: 'upsert', issue_number: 101, title: 'Medium P2 older', priority: 2, risk_class: 'M' });
    handleBusQueue(db, { action: 'upsert', issue_number: 102, title: 'High P1 urgent', priority: 1, risk_class: 'H' });
    handleBusQueue(db, { action: 'upsert', issue_number: 103, title: 'Low P2 newer', priority: 2, risk_class: 'L' });

    // Re-syncing queued item updates title/priority, and preserves claimed status later
    handleBusQueue(db, { action: 'upsert', issue_number: 102, title: 'High P1 urgent updated', priority: 1, risk_class: 'H' });

    // 1. Lowest priority number (P1=1) is picked first
    const claim1 = handleBusQueue(db, { action: 'next', agent: 'codex' });
    assert.equal(claim1.resumed, false);
    assert.equal(claim1.item.issue_number, 102);
    assert.equal(claim1.item.status, 'claimed');
    assert.equal(claim1.item.title, 'High P1 urgent updated');

    // Re-syncing an actively claimed item keeps status='claimed'
    const resynced = handleBusQueue(db, { action: 'upsert', issue_number: 102, title: 'High P1 still Ready', priority: 1, risk_class: 'H' });
    assert.equal(resynced.item.status, 'claimed');
    assert.equal(resynced.item.claimed_by, 'codex');

    // 2. Calling next as same agent resumes own active claim and renews lease
    const pastLease = new Date(Date.now() + 60_000).toISOString();
    db.prepare('UPDATE work_queue SET lease_expires_utc = ? WHERE issue_number = 102').run(pastLease);
    const resumed = handleBusQueue(db, { action: 'next', agent: 'codex' });
    assert.equal(resumed.resumed, true);
    assert.equal(resumed.item.issue_number, 102);
    assert.ok(resumed.item.lease_expires_utc > pastLease);

    // 3. When code_wip >= 3, non-L items (101 M) are skipped and only L item (103 L) is eligible
    handleBusQueue(db, { action: 'sync-meta', meta_key: 'code_wip', meta_value: '3' });
    const lOnly = handleBusQueue(db, { action: 'next', agent: 'antigravity-gemini' });
    assert.equal(lOnly.resumed, false);
    assert.equal(lOnly.item.issue_number, 103);

    // Next non-L item is refused while code_wip >= 3
    const blocked = handleBusQueue(db, { action: 'next', agent: 'claude-opus' });
    assert.equal(blocked.item, null);

    // 4. Lower code_wip back to 1 -> 101 M becomes eligible
    handleBusQueue(db, { action: 'sync-meta', meta_key: 'code_wip', meta_value: '1' });
    const mClaim = handleBusQueue(db, { action: 'next', agent: 'claude-opus' });
    assert.equal(mClaim.item.issue_number, 101);

    // 5. Expired lease takeover by another agent
    const expiredIso = new Date(Date.now() - 60_000).toISOString();
    db.prepare('UPDATE work_queue SET lease_expires_utc = ? WHERE issue_number = 101').run(expiredIso);
    const releaseOther = handleBusQueue(db, { action: 'release', issue_number: 103, agent: 'antigravity-gemini' });
    assert.equal(releaseOther.released, true);
    // 101 (P2, id=1) is older than 103 (P2, id=3) and has an expired lease, so takeover picks 101
    const takeover = handleBusQueue(db, { action: 'next', agent: 'antigravity-opus' });
    assert.equal(takeover.resumed, false);
    assert.equal(takeover.item.issue_number, 101);
    assert.equal(takeover.item.claimed_by, 'antigravity-opus');

    // 6. Done marks item completed
    const doneRes = handleBusQueue(db, { action: 'done', issue_number: 101 });
    assert.equal(doneRes.updated, true);
    assert.equal(doneRes.item.status, 'done');
  } finally {
    db.close();
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('Additive schema migration upgrades legacy swarm-bus.db without losing existing rows', () => {
  const { dir, dbPath } = createTempDbPath();
  try {
    // Create legacy schema missing messages.state, review_claims, work_queue, and meta
    const legacyDb = new DatabaseSync(dbPath);
    legacyDb.exec(`
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
    legacyDb.prepare(`
      INSERT INTO messages (created_at_utc, sender, recipient, topic, body, pr_number, status)
      VALUES ('2026-11-26T00:00:00.000Z', 'codex', 'claude-opus', 'legacy-msg', 'Preserve me', 54, 'unread')
    `).run();
    legacyDb.prepare(`
      INSERT INTO locks (agent, path_pattern, pr_number, claimed_at_utc, expires_at_utc)
      VALUES ('codex', 'MOBAsmart', 55, '2026-11-26T00:00:00.000Z', '2099-01-01T00:00:00.000Z')
    `).run();
    legacyDb.close();

    // Run CLI command against legacy DB to trigger openBusDb additive migration
    const cliStatus = spawnSync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'status'], {
      env: { ...process.env, MOBADEV_SWARM_BUS_DB: dbPath },
      encoding: 'utf8'
    });
    assert.equal(cliStatus.status, 0);
    const parsedStatus = JSON.parse(cliStatus.stdout);
    assert.equal(parsedStatus.totalUnread, 1);
    assert.equal(parsedStatus.recentMessages[0].body, 'Preserve me');
    assert.equal(parsedStatus.recentMessages[0].state, null);
    assert.equal(parsedStatus.activeLocks.length, 1);
    assert.equal(parsedStatus.activeLocks[0].path_pattern, 'MOBAsmart');

    // Verify new CLI handlers work on migrated DB
    const cliClaim = spawnSync(process.execPath, [
      '--no-warnings', BUS_SCRIPT, '--cli', 'review-claim',
      '--agent', 'codex', '--pr', '55', '--head-sha', 'abcdef1234567890abcdef1234567890abcdef12', '--role', 'final'
    ], {
      env: { ...process.env, MOBADEV_SWARM_BUS_DB: dbPath },
      encoding: 'utf8'
    });
    assert.equal(cliClaim.status, 0);
    assert.equal(JSON.parse(cliClaim.stdout).claimed, true);

    const cliUpsert = spawnSync(process.execPath, [
      '--no-warnings', BUS_SCRIPT, '--cli', 'queue',
      '--action', 'upsert', '--issue-number', '382', '--title', 'RF-18', '--priority', '2', '--risk-class', 'M'
    ], {
      env: { ...process.env, MOBADEV_SWARM_BUS_DB: dbPath },
      encoding: 'utf8'
    });
    assert.equal(cliUpsert.status, 0);
    assert.equal(JSON.parse(cliUpsert.stdout).item.issue_number, 382);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});
