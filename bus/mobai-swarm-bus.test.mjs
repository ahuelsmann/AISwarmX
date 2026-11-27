import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import http from 'node:http';
import os from 'node:os';
import path from 'node:path';
import readline from 'node:readline';
import test from 'node:test';
import { DatabaseSync } from 'node:sqlite';

const BUS_SCRIPT = path.resolve(import.meta.dirname, 'mobai-swarm-bus.mjs');
const HOOK_SCRIPT = path.resolve(import.meta.dirname, 'claude-swarm-bus-hook.mjs');
const WATCHDOG_SCRIPT = path.resolve(import.meta.dirname, '..', 'scripts', 'Invoke-SwarmWatchdog.ps1');

function runProcessAsync(command, args, env = process.env) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, { env, stdio: ['ignore', 'pipe', 'pipe'] });
    let stdout = '';
    let stderr = '';
    child.stdout.on('data', (chunk) => { stdout += chunk; });
    child.stderr.on('data', (chunk) => { stderr += chunk; });
    child.on('error', reject);
    child.on('close', (status) => resolve({ status, stdout, stderr }));
  });
}

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

test('bus_send deduplicates recipient-scoped state, preserves distinct recipients, and retries failed wake', async () => {
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

    // Same state to a different recipient must NOT be dropped
    const otherRecipient = handleBusSend(db, {
      from: 'codex',
      to: 'antigravity-gemini',
      topic: 'slice-status',
      pr: 601,
      body: 'Working on checks',
      state: 'running:sha1'
    });
    assert.equal(otherRecipient.ok, true);
    assert.equal(otherRecipient.deduplicated, undefined);
    assert.notEqual(otherRecipient.id, first.id);

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

    // Wake failure recovery: first send fails wake (wake_ok=0), second same-state send with wake retries and marks wake_ok=1
    let seenThreadId = null;
    const failedWake = handleBusSend(db, {
      from: 'watchdog',
      to: 'codex',
      topic: 'HeadIdleWake',
      issue: 382,
      body: 'Wake codex',
      state: 'idle-wake-382',
      wake: true,
      codex_thread_id: 'thread-override-999'
    }, {
      wakeCodex: (_msg, tid) => {
        seenThreadId = tid;
        return { attempted: true, ok: false, target: 'codex', threadId: tid, error: 'transient' };
      }
    });
    assert.equal(failedWake.wakeResults[0].ok, false);
    assert.equal(seenThreadId, 'thread-override-999');
    assert.equal(db.prepare('SELECT wake_ok FROM messages WHERE id = ?').get(failedWake.id).wake_ok, 0);

    const retriedWake = handleBusSend(db, {
      from: 'watchdog',
      to: 'codex',
      topic: 'HeadIdleWake',
      issue: 382,
      body: 'Wake codex',
      state: 'idle-wake-382',
      wake: true,
      codex_thread_id: 'thread-override-999'
    }, {
      wakeCodex: (_msg, tid) => ({ attempted: true, ok: true, target: 'codex', threadId: tid })
    });
    assert.equal(retriedWake.deduplicated, true);
    assert.equal(retriedWake.wake_retried, true);
    assert.equal(retriedWake.existing_id, failedWake.id);
    assert.equal(retriedWake.wakeResults[0].ok, true);
    assert.equal(db.prepare('SELECT wake_ok FROM messages WHERE id = ?').get(failedWake.id).wake_ok, 1);

    // Third same-state send after wake_ok=1 short-circuits without re-waking
    let calledThird = false;
    const shortCircuit = handleBusSend(db, {
      from: 'watchdog',
      to: 'codex',
      topic: 'HeadIdleWake',
      issue: 382,
      body: 'Wake codex',
      state: 'idle-wake-382',
      wake: true
    }, {
      wakeCodex: () => {
        calledThird = true;
        return { attempted: true, ok: true };
      }
    });
    assert.equal(shortCircuit.deduplicated, true);
    assert.equal(shortCircuit.wake_retried, undefined);
    assert.equal(calledThird, false);

    const countRow = db.prepare('SELECT COUNT(*) AS cnt FROM messages').get();
    assert.equal(countRow.cnt, 4);
  } finally {
    db.close();
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('bus_review_claim validates/canonicalizes 40-char hex SHA and supports expiration, release, complete, and list', async () => {
  const { openBusDb, handleBusReviewClaim } = await import('./mobai-swarm-bus.mjs');
  const { dir, dbPath } = createTempDbPath();
  const { db } = openBusDb(dbPath);
  try {
    const shaUpper = 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA';
    const shaLower = shaUpper.toLowerCase();
    const sha2 = '2222222222222222222222222222222222222222';

    assert.throws(() => handleBusReviewClaim(db, { agent: 'codex', pr_number: 55, head_sha: 'abc123', role: 'final' }), /40-character/);
    assert.throws(() => handleBusReviewClaim(db, { agent: 'codex', pr_number: 55, head_sha: 'z'.repeat(40), role: 'final' }), /40-character/);

    const first = handleBusReviewClaim(db, {
      agent: 'codex',
      pr_number: 55,
      head_sha: shaUpper,
      role: 'final',
      ttl_minutes: 60
    });
    assert.equal(first.claimed, true);
    assert.equal(first.head_sha, shaLower);
    assert.equal(first.status, 'claimed');

    // Case-variant duplicate claim is blocked
    const second = handleBusReviewClaim(db, {
      agent: 'antigravity-gemini',
      pr_number: 55,
      head_sha: shaLower,
      role: 'final'
    });
    assert.equal(second.claimed, false);
    assert.equal(second.reason, 'already_claimed');
    assert.equal(second.holder, 'codex');

    // Expired lease allows reclaim by replacement reviewer
    const expiredIso = new Date(Date.now() - 60_000).toISOString();
    db.prepare('UPDATE review_claims SET expires_at_utc = ? WHERE pr_number = 55 AND head_sha = ? AND role = ?').run(expiredIso, shaLower, 'final');
    const reclaimed = handleBusReviewClaim(db, {
      agent: 'antigravity-opus',
      pr_number: 55,
      head_sha: shaUpper,
      role: 'final'
    });
    assert.equal(reclaimed.claimed, true);
    assert.equal(reclaimed.reclaimed, true);
    assert.equal(reclaimed.previous_holder, 'codex');

    // Explicit release allows immediate claim by another reviewer
    const released = handleBusReviewClaim(db, {
      action: 'release',
      pr_number: 55,
      head_sha: shaUpper,
      role: 'final'
    });
    assert.equal(released.released, true);
    assert.equal(released.claim.status, 'released');

    const afterRelease = handleBusReviewClaim(db, {
      agent: 'codex',
      pr_number: 55,
      head_sha: shaLower,
      role: 'final'
    });
    assert.equal(afterRelease.claimed, true);
    assert.equal(afterRelease.reclaimed, true);
    assert.equal(afterRelease.previous_status, 'released');

    // Complete locks head+role permanently and prevents release/reclaim
    const completed = handleBusReviewClaim(db, {
      action: 'complete',
      agent: 'codex',
      pr_number: 55,
      head_sha: shaUpper,
      role: 'final'
    });
    assert.equal(completed.status, 'completed');
    assert.ok(completed.completed_at_utc);

    const blockedCompleted = handleBusReviewClaim(db, {
      agent: 'antigravity-opus',
      pr_number: 55,
      head_sha: shaLower,
      role: 'final'
    });
    assert.equal(blockedCompleted.claimed, false);
    assert.equal(blockedCompleted.reason, 'already_completed');

    const releaseCompleted = handleBusReviewClaim(db, {
      action: 'release',
      pr_number: 55,
      head_sha: shaLower,
      role: 'final'
    });
    assert.equal(releaseCompleted.released, false);
    assert.equal(releaseCompleted.reason, 'already_completed');

    const differentRole = handleBusReviewClaim(db, {
      agent: 'antigravity-gemini',
      pr_number: 55,
      head_sha: shaLower,
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

    const listRes = handleBusReviewClaim(db, { action: 'list', pr_number: 55, head_sha: shaUpper });
    assert.equal(listRes.count, 2);
  } finally {
    db.close();
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('bus_queue supports priority/oldest ordering, resume renewal, expired lease takeover, and effective code_wip >= 2 gate', async () => {
  const { openBusDb, handleBusQueue } = await import('./mobai-swarm-bus.mjs');
  const { dir, dbPath } = createTempDbPath();
  const { db } = openBusDb(dbPath);
  try {
    // Upsert items out of priority order
    handleBusQueue(db, { action: 'upsert', issue_number: 101, title: 'Medium P2 older', priority: 2, risk_class: 'M' });
    handleBusQueue(db, { action: 'upsert', issue_number: 102, title: 'High P1 urgent', priority: 1, risk_class: 'H' });
    handleBusQueue(db, { action: 'upsert', issue_number: 103, title: 'Low P2 newer', priority: 2, risk_class: 'L' });
    handleBusQueue(db, { action: 'upsert', issue_number: 104, title: 'Medium P2 extra', priority: 2, risk_class: 'M' });

    // Re-syncing queued item updates title/priority, and preserves claimed status later
    handleBusQueue(db, { action: 'upsert', issue_number: 102, title: 'High P1 urgent updated', priority: 1, risk_class: 'H' });

    // 1. Lowest priority number (P1=1) is picked first
    const claim1 = handleBusQueue(db, { action: 'next', agent: 'codex' });
    assert.equal(claim1.resumed, false);
    assert.equal(claim1.item.issue_number, 102);
    assert.equal(claim1.item.status, 'claimed');
    assert.equal(claim1.item.title, 'High P1 urgent updated');
    assert.equal(claim1.effective_code_wip, 0);

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

    // 3. When base code_wip=1 + 1 active H/M claim (#102) => effectiveCodeWip=2 >= 2: skips M items and picks L item (#103)
    handleBusQueue(db, { action: 'sync-meta', meta_key: 'code_wip', meta_value: '1' });
    const lOnly = handleBusQueue(db, { action: 'next', agent: 'antigravity-gemini' });
    assert.equal(lOnly.resumed, false);
    assert.equal(lOnly.effective_code_wip, 2);
    assert.equal(lOnly.item.issue_number, 103);

    // Next non-L item (#101/#104 M) is refused while effectiveCodeWip >= 2
    const blocked = handleBusQueue(db, { action: 'next', agent: 'claude-opus' });
    assert.equal(blocked.item, null);
    assert.equal(blocked.effective_code_wip, 2);

    // 4. Lower code_wip to 0 -> effectiveCodeWip=1 < 2, so 101 M is claimed (making active claims = 2), then 104 M is blocked!
    handleBusQueue(db, { action: 'sync-meta', meta_key: 'code_wip', meta_value: '0' });
    const mClaim = handleBusQueue(db, { action: 'next', agent: 'claude-opus' });
    assert.equal(mClaim.item.issue_number, 101);
    assert.equal(mClaim.effective_code_wip, 1);

    const blockedBackToBack = handleBusQueue(db, { action: 'next', agent: 'antigravity-opus' });
    assert.equal(blockedBackToBack.item, null);
    assert.equal(blockedBackToBack.effective_code_wip, 2);

    // 5. Expired lease takeover by another agent (#101 expired while #102 active -> 1 active + 0 code_wip => takeover picks #101)
    const expiredIso = new Date(Date.now() - 60_000).toISOString();
    db.prepare('UPDATE work_queue SET lease_expires_utc = ? WHERE issue_number = 101').run(expiredIso);
    const releaseOther = handleBusQueue(db, { action: 'release', issue_number: 103, agent: 'antigravity-gemini' });
    assert.equal(releaseOther.released, true);
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

test('Concurrent startup, atomic state deduplication, and queue WIP claim race stay consistent across processes', async () => {
  const { dir, dbPath } = createTempDbPath();
  try {
    // Seed legacy pre-state DB missing messages.state, messages.wake_ok, review_claims, work_queue, meta
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
      VALUES ('2026-11-26T00:00:00.000Z', 'codex', 'claude-opus', 'legacy-msg', 'Keep row', 54, 'unread')
    `).run();
    legacyDb.close();

    const workerEnv = { ...process.env, MOBADEV_SWARM_BUS_DB: dbPath };

    // 1. 6 concurrent CLI workers open and upgrade legacy DB simultaneously with zero duplicate column errors
    const startupResults = await Promise.all(
      Array.from({ length: 6 }, () => runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'status'], workerEnv))
    );
    for (const res of startupResults) {
      assert.equal(res.status, 0, `Concurrent startup failed: ${res.stderr}`);
      assert.equal(JSON.parse(res.stdout).recentMessages[0].body, 'Keep row');
    }

    // 2. 4 concurrent sends with identical (sender, recipient, topic, pr, state) insert exactly 1 row
    const sendResults = await Promise.all(
      Array.from({ length: 4 }, () => runProcessAsync(process.execPath, [
        '--no-warnings', BUS_SCRIPT, '--cli', 'send',
        '--from', 'codex', '--to', 'claude-opus', '--topic', 'race-check',
        '--pr', '58', '--body', 'Same state', '--state', 'sha-race-1'
      ], workerEnv))
    );
    for (const res of sendResults) {
      assert.equal(res.status, 0, `Concurrent send failed: ${res.stderr}`);
    }
    const parsedSends = sendResults.map((r) => JSON.parse(r.stdout));
    const insertedSends = parsedSends.filter((p) => !p.deduplicated);
    const dedupSends = parsedSends.filter((p) => p.deduplicated);
    assert.equal(insertedSends.length, 1);
    assert.equal(dedupSends.length, 3);

    // 3. Concurrent queue next with base code_wip=1 and two M items grants strictly 1 claim
    await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'sync-meta', '--meta-key', 'code_wip', '--meta-value', '1'], workerEnv);
    await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'upsert', '--issue-number', '701', '--title', 'Item 701', '--priority', '1', '--risk-class', 'M'], workerEnv);
    await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'upsert', '--issue-number', '702', '--title', 'Item 702', '--priority', '1', '--risk-class', 'M'], workerEnv);

    const raceClaims = await Promise.all([
      runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'next', '--agent', 'codex'], workerEnv),
      runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'next', '--agent', 'claude-opus'], workerEnv)
    ]);
    for (const res of raceClaims) {
      assert.equal(res.status, 0, `Concurrent queue next failed: ${res.stderr}`);
    }
    const claimPayloads = raceClaims.map((r) => JSON.parse(r.stdout));
    const granted = claimPayloads.filter((p) => p.item !== null);
    const denied = claimPayloads.filter((p) => p.item === null);
    assert.equal(granted.length, 1);
    assert.equal(denied.length, 1);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('Invoke-SwarmWatchdog preserves queue on failed GraphQL, paginates Project v2, and forwards CodexThreadId on HeadIdleWake', async () => {
  const pwshCheck = spawnSync('pwsh', ['--version'], { encoding: 'utf8' });
  if (pwshCheck.status !== 0) {
    return;
  }

  const { dir, dbPath } = createTempDbPath();
  const localAppDir = path.join(dir, 'localapp');
  const binDir = path.join(dir, 'bin');
  const logFile = path.join(dir, 'node-calls.jsonl');
  fs.mkdirSync(localAppDir, { recursive: true });
  fs.mkdirSync(binDir, { recursive: true });

  // Mock node.cmd in PATH to log invoked args + MOBADEV_CODEX_THREAD_ID and forward to real node.exe
  const nodeWrapper = path.join(dir, 'node-wrapper.mjs');
  fs.writeFileSync(nodeWrapper, `
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
const args = process.argv.slice(2);
fs.appendFileSync(${JSON.stringify(logFile)}, JSON.stringify({ args, threadEnv: process.env.MOBADEV_CODEX_THREAD_ID ?? null }) + '\\n');
const res = spawnSync(${JSON.stringify(process.execPath)}, args, { stdio: 'inherit', env: process.env });
process.exit(res.status ?? 1);
`, 'utf8');
  fs.writeFileSync(path.join(binDir, 'node.cmd'), `@echo off\r\n"${process.execPath}" "${nodeWrapper}" %*\r\n`, 'utf8');

  const mockGhScript = path.join(dir, 'mock-gh.mjs');
  fs.writeFileSync(mockGhScript, `
const args = process.argv.slice(2);
const mode = process.env.MOCK_GH_MODE || 'fail';
if (args[0] === 'pr' && args[1] === 'list') {
  console.log('[]');
  process.exit(0);
}
if (args[0] === 'api' && args[1] === 'graphql') {
  if (mode === 'fail') {
    process.exit(1);
  }
  const cursorArg = args.find((a) => a.startsWith('cursor='));
  if (!cursorArg) {
    console.log(JSON.stringify({
      data: {
        user: {
          projectV2: {
            fields: { nodes: [{ name: 'Status' }, { name: 'Priority' }, { name: 'Risk class' }] },
            items: {
              pageInfo: { hasNextPage: true, endCursor: 'page2' },
              nodes: [{
                id: 'I1',
                fieldValues: { nodes: [
                  { name: 'Ready', field: { name: 'Status' } },
                  { name: 'P1', field: { name: 'Priority' } },
                  { name: 'M', field: { name: 'Risk class' } }
                ] },
                content: { __typename: 'Issue', number: 801, title: 'Page 1 Issue', state: 'OPEN' }
              }]
            }
          }
        }
      }
    }));
    process.exit(0);
  }
  console.log(JSON.stringify({
    data: {
      user: {
        projectV2: {
          fields: { nodes: [{ name: 'Status' }, { name: 'Priority' }, { name: 'Risk class' }] },
          items: {
            pageInfo: { hasNextPage: false, endCursor: null },
            nodes: [{
              id: 'I2',
              fieldValues: { nodes: [
                { name: 'Ready', field: { name: 'Status' } },
                { name: 'P2', field: { name: 'Priority' } },
                { name: 'L', field: { name: 'Risk class' } }
              ] },
              content: { __typename: 'Issue', number: 802, title: 'Page 2 Issue', state: 'OPEN' }
            }]
          }
        }
      }
    }
  }));
  process.exit(0);
}
console.log('[]');
`, 'utf8');
  fs.writeFileSync(path.join(binDir, 'gh.cmd'), `@echo off\r\n"${process.execPath}" "${mockGhScript}" %*\r\n`, 'utf8');

  const idleTimeIso = new Date(Date.now() - 45 * 60_000).toISOString();
  const server = http.createServer((_req, res) => {
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({
      monitors: [{
        id: 'codex-monitor',
        displayName: 'MOBAi ChatGPT Codex',
        state: 'idle',
        model: 'gpt-6.2-sol',
        lastActivityUtc: idleTimeIso,
        rateLimitResetLabel: null
      }]
    }));
  });

  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  const port = server.address().port;

  try {
    const busEnv = { ...process.env, MOBADEV_SWARM_BUS_DB: dbPath };
    await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'upsert', '--issue-number', '799', '--title', 'Existing Claimed', '--priority', '1', '--risk-class', 'M'], busEnv);
    await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'next', '--agent', 'codex'], busEnv);
    await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'sync-meta', '--meta-key', 'code_wip', '--meta-value', '2'], busEnv);

    const baseWdEnv = {
      ...process.env,
      LOCALAPPDATA: localAppDir,
      MOBADEV_SWARM_BUS_DB: dbPath,
      PATH: `${binDir}${path.delimiter}${process.env.PATH}`
    };

    // Case A: GraphQL failure must preserve existing work_queue and meta.code_wip
    const failRun = await runProcessAsync('pwsh', [
      '-NoProfile', '-File', WATCHDOG_SCRIPT,
      '-SkipHygiene', '-SyncQueue', '-Json',
      '-DashboardApiUrl', `http://127.0.0.1:${port}`
    ], { ...baseWdEnv, MOCK_GH_MODE: 'fail' });
    assert.equal(failRun.status, 0, failRun.stderr);
    const failReport = JSON.parse(failRun.stdout);
    assert.equal(failReport.QueueSync.BoardReadSucceeded, false);
    assert.equal(failReport.QueueSync.RemovedCount, 0);

    const afterFail = JSON.parse((await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'list'], busEnv)).stdout);
    assert.equal(afterFail.code_wip, 2);
    assert.equal(afterFail.items[0].status, 'claimed');
    assert.equal(afterFail.items[0].issue_number, 799);

    // Case B: Paginated 2-page GraphQL sync + HeadIdleWake forwarding custom -CodexThreadId
    const customThread = 'custom-codex-thread-abc';
    const okRun = await runProcessAsync('pwsh', [
      '-NoProfile', '-File', WATCHDOG_SCRIPT,
      '-SkipHygiene', '-DispatchEvents', '-SyncQueue', '-Json',
      '-CodexThreadId', customThread,
      '-DashboardApiUrl', `http://127.0.0.1:${port}`
    ], { ...baseWdEnv, MOCK_GH_MODE: 'paginated' });
    assert.equal(okRun.status, 0, okRun.stderr);
    const okReport = JSON.parse(okRun.stdout);
    assert.equal(okReport.QueueSync.BoardReadSucceeded, true);
    assert.equal(okReport.QueueSync.UpsertedCount, 2);
    assert.equal(okReport.QueueSync.RemovedCount, 0);

    const afterOk = JSON.parse((await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'list'], busEnv)).stdout);
    assert.equal(afterOk.items.find((i) => i.issue_number === 799).status, 'claimed');
    const syncedNums = afterOk.items.filter((i) => i.status === 'queued').map((i) => i.issue_number);
    assert.deepEqual(syncedNums, [801, 802]);

    const loggedCalls = fs.readFileSync(logFile, 'utf8').trim().split('\n').map((l) => JSON.parse(l));
    const idleWakeCall = loggedCalls.find((c) => c.args.includes('HeadIdleWake'));
    assert.ok(idleWakeCall, 'Expected HeadIdleWake bus_send call');
    assert.equal(idleWakeCall.threadEnv, customThread);
    const tidFlagIndex = idleWakeCall.args.indexOf('--codex-thread-id');
    assert.ok(tidFlagIndex !== -1 && idleWakeCall.args[tidFlagIndex + 1] === customThread);
  } finally {
    await new Promise((resolve) => server.close(resolve));
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('Atomic wake reservation prevents overlapping wake dispatches and allows retry on failed or expired pending', async () => {
  const { openBusDb, handleBusSend } = await import('./mobai-swarm-bus.mjs');
  const { dir, dbPath } = createTempDbPath();
  const wakeLogPath = path.join(dir, 'wake-invocations.jsonl');
  const mockCodexMjs = path.join(dir, 'mock-codex.mjs');
  fs.writeFileSync(mockCodexMjs, `
import fs from 'node:fs';
Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 350);
fs.appendFileSync(${JSON.stringify(wakeLogPath)}, JSON.stringify({ argv: process.argv.slice(2), at: Date.now() }) + '\\n');
`, 'utf8');

  const { db } = openBusDb(dbPath);
  try {
    // 1. Overlapping concurrent wake-enabled sends across two processes -> strictly 1 wake dispatch
    const workerEnv = { ...process.env, MOBADEV_SWARM_BUS_DB: dbPath, MOBADEV_CODEX_EXE: mockCodexMjs };
    const p1 = runProcessAsync(process.execPath, [
      '--no-warnings', BUS_SCRIPT, '--cli', 'send',
      '--from', 'watchdog', '--to', 'codex', '--topic', 'HeadIdleWake',
      '--issue', '910', '--body', 'Claim 910', '--state', 'wake-overlap-910', '--wake'
    ], workerEnv);
    await new Promise((resolve) => setTimeout(resolve, 80));
    const p2 = runProcessAsync(process.execPath, [
      '--no-warnings', BUS_SCRIPT, '--cli', 'send',
      '--from', 'watchdog', '--to', 'codex', '--topic', 'HeadIdleWake',
      '--issue', '910', '--body', 'Claim 910', '--state', 'wake-overlap-910', '--wake'
    ], workerEnv);

    const [r1, r2] = await Promise.all([p1, p2]);
    assert.equal(r1.status, 0, r1.stderr);
    assert.equal(r2.status, 0, r2.stderr);
    const o1 = JSON.parse(r1.stdout);
    const o2 = JSON.parse(r2.stdout);
    assert.equal(o1.deduplicated, undefined);
    assert.equal(o2.deduplicated, true);
    assert.equal(o2.wake_in_progress, true);

    const wakeEntries = fs.readFileSync(wakeLogPath, 'utf8').trim().split('\n').filter(Boolean);
    assert.equal(wakeEntries.length, 1);
    assert.equal(db.prepare('SELECT wake_state, wake_ok FROM messages WHERE id = ?').get(o1.id).wake_state, 'ok');

    // 2. Expired 'pending' reservation allows retry
    const expiredIso = new Date(Date.now() - 5000).toISOString();
    const expiredId = handleBusSend(db, {
      from: 'watchdog', to: 'codex', topic: 'HeadIdleWake', issue: 911, body: 'Claim 911', state: 'wake-exp-911', wake: true
    }, { wakeCodex: () => ({ attempted: true, ok: false }) }).id;
    db.prepare("UPDATE messages SET wake_state = 'pending', wake_attempt_expires_at_utc = ? WHERE id = ?").run(expiredIso, expiredId);

    const retriedExpired = handleBusSend(db, {
      from: 'watchdog', to: 'codex', topic: 'HeadIdleWake', issue: 911, body: 'Claim 911', state: 'wake-exp-911', wake: true
    }, { wakeCodex: () => ({ attempted: true, ok: true }) });
    assert.equal(retriedExpired.wake_retried, true);
    assert.equal(retriedExpired.wakeResults[0].ok, true);
    assert.equal(db.prepare('SELECT wake_state FROM messages WHERE id = ?').get(expiredId).wake_state, 'ok');

    // 3. 'failed' wake_state allows retry
    const failedId = handleBusSend(db, {
      from: 'watchdog', to: 'codex', topic: 'HeadIdleWake', issue: 912, body: 'Claim 912', state: 'wake-fail-912', wake: true
    }, { wakeCodex: () => ({ attempted: true, ok: false }) }).id;
    assert.equal(db.prepare('SELECT wake_state FROM messages WHERE id = ?').get(failedId).wake_state, 'failed');

    const retriedFailed = handleBusSend(db, {
      from: 'watchdog', to: 'codex', topic: 'HeadIdleWake', issue: 912, body: 'Claim 912', state: 'wake-fail-912', wake: true
    }, { wakeCodex: () => ({ attempted: true, ok: true }) });
    assert.equal(retriedFailed.wake_retried, true);
    assert.equal(db.prepare('SELECT wake_state FROM messages WHERE id = ?').get(failedId).wake_state, 'ok');
  } finally {
    db.close();
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('Unclassified queue claims count toward WIP reservation and are skipped when effectiveCodeWip >= 2', async () => {
  const { openBusDb, handleBusQueue } = await import('./mobai-swarm-bus.mjs');
  const { dir, dbPath } = createTempDbPath();
  const { db } = openBusDb(dbPath);
  try {
    handleBusQueue(db, { action: 'sync-meta', meta_key: 'code_wip', meta_value: '1' });
    handleBusQueue(db, { action: 'upsert', issue_number: 501, title: 'Unclassified first', priority: 1 });
    handleBusQueue(db, { action: 'upsert', issue_number: 502, title: 'Medium second', priority: 2, risk_class: 'M' });
    handleBusQueue(db, { action: 'upsert', issue_number: 503, title: 'High third', priority: 2, risk_class: 'H' });
    handleBusQueue(db, { action: 'upsert', issue_number: 504, title: 'Unclassified fourth', priority: 2 });
    handleBusQueue(db, { action: 'upsert', issue_number: 505, title: 'Low fifth', priority: 3, risk_class: 'L' });

    const first = handleBusQueue(db, { action: 'next', agent: 'codex' });
    assert.equal(first.item.issue_number, 501);
    assert.equal(first.item.risk_class, null);

    // Now baseCodeWip=1 + active unclassified claim #501 (1) => effective_code_wip = 2 >= 2:
    // H, M, and unclassified items must be skipped; only L (#505) is eligible
    const second = handleBusQueue(db, { action: 'next', agent: 'claude-opus' });
    assert.equal(second.effective_code_wip, 2);
    assert.equal(second.active_claimed_code_count, 1);
    assert.equal(second.item.issue_number, 505);

    const third = handleBusQueue(db, { action: 'next', agent: 'antigravity-opus' });
    assert.equal(third.effective_code_wip, 2);
    assert.equal(third.item, null);
  } finally {
    db.close();
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('Invoke-SwarmWatchdog preserves claimed In progress lease until linked open PR or terminal Closed/Done', async () => {
  const pwshCheck = spawnSync('pwsh', ['--version'], { encoding: 'utf8' });
  if (pwshCheck.status !== 0) {
    return;
  }

  const { dir, dbPath } = createTempDbPath();
  const localAppDir = path.join(dir, 'localapp');
  const binDir = path.join(dir, 'bin');
  fs.mkdirSync(localAppDir, { recursive: true });
  fs.mkdirSync(binDir, { recursive: true });

  const mockGhScript = path.join(dir, 'mock-gh-lifecycle.mjs');
  fs.writeFileSync(mockGhScript, `
const args = process.argv.slice(2);
const phase = process.env.MOCK_PHASE || 'in_progress';
if (args[0] === 'pr' && args[1] === 'list') {
  if (phase === 'linked_pr') {
    console.log(JSON.stringify([{
      number: 901,
      title: 'feat: implement 850',
      url: 'https://github.com/ahuelsmann/MOBAflow/pull/901',
      headRefName: 'codex/850-feature',
      headRefOid: '1234567890abcdef1234567890abcdef12345678',
      isDraft: true,
      mergeStateStatus: 'CLEAN',
      statusCheckRollup: [],
      body: 'Closes #850',
      files: [{ path: 'Backend/Feature.cs' }],
      closingIssuesReferences: [{ number: 850 }]
    }]));
  } else {
    console.log('[]');
  }
  process.exit(0);
}
if (args[0] === 'issue' && args[1] === 'view') {
  const num = Number(args[2]);
  if (phase === 'closed' && num === 851) {
    console.log(JSON.stringify({ state: 'CLOSED', projectItems: [{ status: 'Done' }] }));
  } else {
    console.log(JSON.stringify({ state: 'OPEN', projectItems: [{ status: 'In progress' }] }));
  }
  process.exit(0);
}
if (args[0] === 'api' && args[1] === 'graphql') {
  const nodes = [];
  if (phase === 'in_progress' || phase === 'linked_pr') {
    nodes.push({
      id: 'IP_850',
      fieldValues: { nodes: [
        { name: 'In progress', field: { name: 'Status' } },
        { name: 'P1', field: { name: 'Priority' } },
        { name: 'M', field: { name: 'Risk class' } }
      ] },
      content: { __typename: 'Issue', number: 850, title: 'In Progress Claimed', state: 'OPEN' }
    });
  }
  nodes.push({
    id: 'R_852',
    fieldValues: { nodes: [
      { name: 'Ready', field: { name: 'Status' } },
      { name: 'P2', field: { name: 'Priority' } },
      { name: 'M', field: { name: 'Risk class' } }
    ] },
    content: { __typename: 'Issue', number: 852, title: 'Ready Item', state: 'OPEN' }
  });
  console.log(JSON.stringify({
    data: {
      user: {
        projectV2: {
          fields: { nodes: [{ name: 'Status' }, { name: 'Priority' }, { name: 'Risk class' }] },
          items: { pageInfo: { hasNextPage: false, endCursor: null }, nodes }
        }
      }
    }
  }));
  process.exit(0);
}
console.log('[]');
`, 'utf8');
  fs.writeFileSync(path.join(binDir, 'gh.cmd'), `@echo off\r\n"${process.execPath}" "${mockGhScript}" %*\r\n`, 'utf8');

  try {
    const busEnv = { ...process.env, MOBADEV_SWARM_BUS_DB: dbPath };
    const wdEnv = {
      ...process.env,
      LOCALAPPDATA: localAppDir,
      MOBADEV_SWARM_BUS_DB: dbPath,
      PATH: `${binDir}${path.delimiter}${process.env.PATH}`
    };

    // Seed claimed #850 (In progress on board), queued #853 (dropped from Ready -> removed), and claimed #851 (later closed)
    await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'upsert', '--issue-number', '850', '--title', 'In Progress Claimed', '--priority', '1', '--risk-class', 'M'], busEnv);
    await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'next', '--agent', 'codex'], busEnv);
    await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'upsert', '--issue-number', '851', '--title', 'Closed Claimed', '--priority', '1', '--risk-class', 'M'], busEnv);
    await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'next', '--agent', 'claude-opus'], busEnv);
    await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'upsert', '--issue-number', '853', '--title', 'Stale Queued', '--priority', '2', '--risk-class', 'M'], busEnv);

    // 1. Issue #850 moved to In progress with no linked PR -> stays claimed and reserves WIP; #853 queued -> removed
    const p1Res = await runProcessAsync('pwsh', [
      '-NoProfile', '-File', WATCHDOG_SCRIPT,
      '-SkipHygiene', '-SyncQueue', '-Json', '-DashboardApiUrl', 'http://127.0.0.1:9'
    ], { ...wdEnv, MOCK_PHASE: 'in_progress' });
    assert.equal(p1Res.status, 0, p1Res.stderr);
    const r1 = JSON.parse(p1Res.stdout);
    assert.equal(r1.QueueSync.RemovedCount, 1);
    assert.equal(r1.QueueSync.DoneCount, 0);

    const q1 = JSON.parse((await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'list'], busEnv)).stdout);
    assert.equal(q1.items.find((i) => i.issue_number === 850).status, 'claimed');
    assert.equal(q1.items.find((i) => i.issue_number === 853).status, 'removed');

    // Because #850 and #851 are still claimed (effective_code_wip = 2), #852 (M) cannot be claimed yet
    const blockedNext = JSON.parse((await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'next', '--agent', 'antigravity-opus'], busEnv)).stdout);
    assert.equal(blockedNext.item, null);

    // 2. Linked open code PR #901 appears for #850 -> #850 transitions to done (not double-counted with PR's code_wip=1)
    const p2Res = await runProcessAsync('pwsh', [
      '-NoProfile', '-File', WATCHDOG_SCRIPT,
      '-SkipHygiene', '-SyncQueue', '-Json', '-DashboardApiUrl', 'http://127.0.0.1:9'
    ], { ...wdEnv, MOCK_PHASE: 'linked_pr' });
    assert.equal(p2Res.status, 0, p2Res.stderr);
    const r2 = JSON.parse(p2Res.stdout);
    assert.equal(r2.QueueSync.DoneCount, 1);

    const q2 = JSON.parse((await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'list'], busEnv)).stdout);
    assert.equal(q2.code_wip, 1);
    assert.equal(q2.items.find((i) => i.issue_number === 850).status, 'done');

    // 3. Closed issue #851 -> transitions to done and releases claim
    const p3Res = await runProcessAsync('pwsh', [
      '-NoProfile', '-File', WATCHDOG_SCRIPT,
      '-SkipHygiene', '-SyncQueue', '-Json', '-DashboardApiUrl', 'http://127.0.0.1:9'
    ], { ...wdEnv, MOCK_PHASE: 'closed' });
    assert.equal(p3Res.status, 0, p3Res.stderr);
    const r3 = JSON.parse(p3Res.stdout);
    assert.equal(r3.QueueSync.DoneCount, 1);

    const q3 = JSON.parse((await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'list'], busEnv)).stdout);
    assert.equal(q3.items.find((i) => i.issue_number === 851).status, 'done');
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('Invoke-SwarmWatchdog releases board-present open Done cards and avoids false links from multi-number branches', async () => {
  const pwshCheck = spawnSync('pwsh', ['--version'], { encoding: 'utf8' });
  if (pwshCheck.status !== 0) {
    return;
  }

  const { dir, dbPath } = createTempDbPath();
  const localAppDir = path.join(dir, 'localapp');
  const binDir = path.join(dir, 'bin');
  fs.mkdirSync(localAppDir, { recursive: true });
  fs.mkdirSync(binDir, { recursive: true });

  const mockGhScript = path.join(dir, 'mock-gh-round3.mjs');
  fs.writeFileSync(mockGhScript, `
const args = process.argv.slice(2);
if (args[0] === 'pr' && args[1] === 'list') {
  console.log(JSON.stringify([{
    number: 950,
    title: 'feat: slice 14 for 430',
    url: 'https://github.com/ahuelsmann/MOBAflow/pull/950',
    headRefName: 'codex/430-slice-14',
    headRefOid: 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
    isDraft: true,
    mergeStateStatus: 'CLEAN',
    statusCheckRollup: [],
    body: 'Refs #430',
    files: [{ path: 'Backend/Slice14.cs' }],
    closingIssuesReferences: [{ number: 430 }]
  }]));
  process.exit(0);
}
if (args[0] === 'issue' && args[1] === 'view') {
  console.log(JSON.stringify({ state: 'OPEN', projectItems: [{ status: 'In progress' }] }));
  process.exit(0);
}
if (args[0] === 'api' && args[1] === 'graphql') {
  console.log(JSON.stringify({
    data: {
      user: {
        projectV2: {
          fields: { nodes: [{ name: 'Status' }, { name: 'Priority' }, { name: 'Risk class' }] },
          items: {
            pageInfo: { hasNextPage: false, endCursor: null },
            nodes: [
              {
                id: 'OPEN_DONE_860',
                fieldValues: { nodes: [
                  { name: 'Done', field: { name: 'Status' } },
                  { name: 'P1', field: { name: 'Priority' } },
                  { name: 'M', field: { name: 'Risk class' } }
                ] },
                content: { __typename: 'Issue', number: 860, title: 'Open Done Card', state: 'OPEN' }
              },
              {
                id: 'IP_14',
                fieldValues: { nodes: [
                  { name: 'In progress', field: { name: 'Status' } },
                  { name: 'P1', field: { name: 'Priority' } },
                  { name: 'M', field: { name: 'Risk class' } }
                ] },
                content: { __typename: 'Issue', number: 14, title: 'Unrelated Issue 14', state: 'OPEN' }
              }
            ]
          }
        }
      }
    }
  }));
  process.exit(0);
}
console.log('[]');
`, 'utf8');
  fs.writeFileSync(path.join(binDir, 'gh.cmd'), `@echo off\r\n"${process.execPath}" "${mockGhScript}" %*\r\n`, 'utf8');

  try {
    const busEnv = { ...process.env, MOBADEV_SWARM_BUS_DB: dbPath };
    const wdEnv = {
      ...process.env,
      LOCALAPPDATA: localAppDir,
      MOBADEV_SWARM_BUS_DB: dbPath,
      PATH: `${binDir}${path.delimiter}${process.env.PATH}`
    };

    // Seed claimed #860 (board-present Done while GitHub state OPEN), #14 (In progress, must NOT match codex/430-slice-14), and #430 (linked to PR #950)
    await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'upsert', '--issue-number', '860', '--title', 'Open Done Card', '--priority', '1', '--risk-class', 'M'], busEnv);
    await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'next', '--agent', 'codex'], busEnv);
    await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'upsert', '--issue-number', '14', '--title', 'Unrelated Issue 14', '--priority', '1', '--risk-class', 'M'], busEnv);
    await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'next', '--agent', 'claude-opus'], busEnv);
    await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'upsert', '--issue-number', '430', '--title', 'Issue 430', '--priority', '1', '--risk-class', 'M'], busEnv);
    const seedDb = new DatabaseSync(dbPath);
    const futureLease = new Date(Date.now() + 3600_000).toISOString();
    seedDb.prepare("UPDATE work_queue SET status = 'claimed', claimed_by = 'antigravity-opus', claimed_at_utc = ?, lease_expires_utc = ? WHERE issue_number = 430").run(new Date().toISOString(), futureLease);
    seedDb.close();

    const res = await runProcessAsync('pwsh', [
      '-NoProfile', '-File', WATCHDOG_SCRIPT,
      '-SkipHygiene', '-SyncQueue', '-Json', '-DashboardApiUrl', 'http://127.0.0.1:9'
    ], wdEnv);
    assert.equal(res.status, 0, res.stderr);
    const report = JSON.parse(res.stdout);
    assert.equal(report.QueueSync.BoardReadSucceeded, true);
    assert.equal(report.QueueSync.DoneCount, 2);

    const list = JSON.parse((await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'list'], busEnv)).stdout);
    assert.equal(list.items.find((i) => i.issue_number === 860).status, 'done');
    assert.equal(list.items.find((i) => i.issue_number === 430).status, 'done');
    assert.equal(list.items.find((i) => i.issue_number === 14).status, 'claimed');
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('Invoke-SwarmWatchdog supports PR-disabled repos, computes code_wip from active claims, skips PR events, and detects landed commits/Done issues', async () => {
  const pwshCheck = spawnSync('pwsh', ['--version'], { encoding: 'utf8' });
  if (pwshCheck.status !== 0) {
    return;
  }

  const { dir, dbPath } = createTempDbPath();
  const localAppDir = path.join(dir, 'localapp');
  const binDir = path.join(dir, 'bin');
  fs.mkdirSync(localAppDir, { recursive: true });
  fs.mkdirSync(binDir, { recursive: true });

  const mockGhScript = path.join(dir, 'mock-gh-pr-free.mjs');
  fs.writeFileSync(mockGhScript, `
const args = process.argv.slice(2);
const mode = process.env.MOCK_PR_FREE_MODE || 'repo_flag';
const nowIso = new Date().toISOString();

if (args[0] === 'api' && args[1] === 'repos/ahuelsmann/MOBAflow') {
  if (mode === 'repo_flag') {
    console.log(JSON.stringify({ has_pull_requests: false }));
  } else {
    console.log(JSON.stringify({ has_pull_requests: true }));
  }
  process.exit(0);
}

if (args[0] === 'pr' && args[1] === 'list') {
  console.error('GraphQL: Pull requests are disabled for this repository');
  process.exit(1);
}

if (args[0] === 'api' && args.some((a) => a.startsWith('repos/ahuelsmann/MOBAflow/commits'))) {
  if (mode === 'landed_commit') {
    console.log(JSON.stringify([{
      sha: 'deadbeef1234567890abcdef1234567890abcdef',
      html_url: 'https://github.com/ahuelsmann/MOBAflow/commit/deadbeef',
      commit: {
        message: 'feat: finish undo stack (Closes #960)',
        committer: { date: nowIso }
      }
    }]));
  } else {
    console.log('[]');
  }
  process.exit(0);
}

if (args[0] === 'issue' && args[1] === 'view') {
  const num = Number(args[2]);
  if ((mode === 'landed_done' && num === 961) || (mode === 'landed_commit' && num === 960)) {
    console.log(JSON.stringify({ state: 'CLOSED', projectItems: [{ status: 'Done' }] }));
  } else {
    console.log(JSON.stringify({ state: 'OPEN', projectItems: [{ status: 'In progress' }] }));
  }
  process.exit(0);
}

if (args[0] === 'api' && args[1] === 'graphql') {
  const payload = {
    data: {
      user: {
        projectV2: {
          fields: { nodes: [{ name: 'Status' }, { name: 'Priority' }, { name: 'Risk class' }] },
          items: {
            pageInfo: { hasNextPage: false, endCursor: null },
            nodes: [{
              id: 'READY_962',
              fieldValues: { nodes: [
                { name: 'Ready', field: { name: 'Status' } },
                { name: 'P1', field: { name: 'Priority' } },
                { name: 'M', field: { name: 'Risk class' } }
              ] },
              content: { __typename: 'Issue', number: 962, title: 'Next Ready Issue', state: 'OPEN' }
            }]
          }
        }
      }
    }
  };
  if (mode === 'graphql_pr_disabled') {
    payload.errors = [{ message: 'Pull requests are disabled' }];
  }
  console.log(JSON.stringify(payload));
  process.exit(mode === 'graphql_pr_disabled' ? 1 : 0);
}
console.log('[]');
`, 'utf8');
  fs.writeFileSync(path.join(binDir, 'gh.cmd'), `@echo off\r\n"${process.execPath}" "${mockGhScript}" %*\r\n`, 'utf8');

  try {
    const busEnv = { ...process.env, MOBADEV_SWARM_BUS_DB: dbPath };
    const wdEnv = {
      ...process.env,
      LOCALAPPDATA: localAppDir,
      MOBADEV_SWARM_BUS_DB: dbPath,
      PATH: `${binDir}${path.delimiter}${process.env.PATH}`
    };

    // Seed two active claims (#960 M, #961 unclassified)
    await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'upsert', '--issue-number', '960', '--title', 'Claimed 960', '--priority', '1', '--risk-class', 'M'], busEnv);
    await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'next', '--agent', 'codex'], busEnv);
    await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'upsert', '--issue-number', '961', '--title', 'Claimed 961', '--priority', '1'], busEnv);
    await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'next', '--agent', 'claude-opus'], busEnv);

    // (a) + (b) GraphQL "Pull requests are disabled" / has_pull_requests: false runs reconciliation, computes code_wip from active claims, and emits 0 PR events
    const res1 = await runProcessAsync('pwsh', [
      '-NoProfile', '-File', WATCHDOG_SCRIPT,
      '-SkipHygiene', '-DispatchEvents', '-SyncQueue', '-Json', '-DashboardApiUrl', 'http://127.0.0.1:9'
    ], { ...wdEnv, MOCK_PR_FREE_MODE: 'graphql_pr_disabled' });
    assert.equal(res1.status, 0, res1.stderr);
    const rep1 = JSON.parse(res1.stdout);
    assert.equal(rep1.PullRequestsDisabled, true);
    assert.equal(rep1.QueueSync.BoardReadSucceeded, true);
    assert.equal(rep1.OpenPrCount, 0);
    assert.equal(rep1.OpenCodePrCount, 0);
    assert.equal(rep1.ActiveClaimedCodeCount, 2);
    assert.equal(rep1.EffectiveCodeWip, 2);
    assert.equal(rep1.QueueSync.CodeWip, 2);
    assert.equal(rep1.NewEventsDispatched.filter((e) => e.EventType === 'CiOrSonarFailed' || e.EventType === 'AllChecksGreenReady').length, 0);

    // meta.code_wip stays 0 so bus_queue next computes effective_code_wip = 0 + 2 = 2 (no double counting) and refuses #962 (M)
    const blockedNext = JSON.parse((await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'next', '--agent', 'antigravity-opus'], busEnv)).stdout);
    assert.equal(blockedNext.effective_code_wip, 2);
    assert.equal(blockedNext.item, null);

    // (c1) Closes #960 commit on main releases #960 lease and emits NextWorkAvailable
    const res2 = await runProcessAsync('pwsh', [
      '-NoProfile', '-File', WATCHDOG_SCRIPT,
      '-SkipHygiene', '-DispatchEvents', '-SyncQueue', '-Json', '-DashboardApiUrl', 'http://127.0.0.1:9'
    ], { ...wdEnv, MOCK_PR_FREE_MODE: 'landed_commit' });
    assert.equal(res2.status, 0, res2.stderr);
    const rep2 = JSON.parse(res2.stdout);
    assert.equal(rep2.PullRequestsDisabled, true);
    assert.equal(rep2.QueueSync.DoneCount, 1);
    assert.equal(rep2.ActiveClaimedCodeCount, 1);
    const commitNextEv = rep2.NewEventsDispatched.find((e) => e.EventType === 'NextWorkAvailable');
    assert.ok(commitNextEv, 'Expected NextWorkAvailable after Closes #960 commit on main');
    assert.equal(commitNextEv.IssueNumber, 962);

    // (c2) Closed/Done issue #961 releases #961 lease and emits NextWorkAvailable
    const res3 = await runProcessAsync('pwsh', [
      '-NoProfile', '-File', WATCHDOG_SCRIPT,
      '-SkipHygiene', '-DispatchEvents', '-SyncQueue', '-Json', '-DashboardApiUrl', 'http://127.0.0.1:9'
    ], { ...wdEnv, MOCK_PR_FREE_MODE: 'landed_done' });
    assert.equal(res3.status, 0, res3.stderr);
    const rep3 = JSON.parse(res3.stdout);
    assert.equal(rep3.QueueSync.DoneCount, 1);
    assert.equal(rep3.ActiveClaimedCodeCount, 0);
    const doneNextEv = rep3.NewEventsDispatched.find((e) => e.EventType === 'NextWorkAvailable');
    assert.ok(doneNextEv, 'Expected NextWorkAvailable after #961 closed/Done');

    const inboxClaude = JSON.parse((await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'inbox', '--agent', 'claude-opus'], busEnv)).stdout);
    assert.ok(inboxClaude.messages.some((m) => m.topic === 'NextWorkAvailable'));
    const inboxCodex = JSON.parse((await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'inbox', '--agent', 'codex'], busEnv)).stdout);
    assert.equal(inboxCodex.messages.some((m) => m.topic === 'NextWorkAvailable'), false);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('Invoke-SwarmWatchdog guards reopened/reclaimed commits, ambiguous 404 PR errors, and COO/Acting-COO intake routing', async () => {
  const pwshCheck = spawnSync('pwsh', ['--version'], { encoding: 'utf8' });
  if (pwshCheck.status !== 0) {
    return;
  }

  const { dir, dbPath } = createTempDbPath();
  const localAppDir = path.join(dir, 'localapp');
  const binDir = path.join(dir, 'bin');
  fs.mkdirSync(localAppDir, { recursive: true });
  fs.mkdirSync(binDir, { recursive: true });

  const olderCommitIso = new Date(Date.now() - 10 * 60 * 1000).toISOString();
  const staleIso = new Date(Date.now() - 45 * 60 * 1000).toISOString();
  let mockClaudeState = 'online';

  const server = http.createServer((req, res) => {
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({
      monitors: [
        { id: 'claude-monitor', displayName: 'MOBAi Claude Opus', state: mockClaudeState, model: 'Opus 5.5', lastActivityUtc: new Date().toISOString() },
        { id: 'antigravity-monitor', displayName: 'MOBAi Antigravity Gemini', state: 'online', model: 'Gemini 4 Carbon', lastActivityUtc: new Date().toISOString() },
        { id: 'codex-monitor', displayName: 'MOBAi ChatGPT Codex', state: 'idle', model: 'GPT-6.2 Sol', lastActivityUtc: staleIso }
      ]
    }));
  });
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  const port = server.address().port;

  const mockGhScript = path.join(dir, 'mock-gh-guards.mjs');
  fs.writeFileSync(mockGhScript, `
const args = process.argv.slice(2);
const mode = process.env.MOCK_GUARD_MODE || 'reopened_claim';

if (args[0] === 'api' && args[1] === 'repos/ahuelsmann/MOBAflow') {
  console.log(JSON.stringify({ has_pull_requests: mode !== 'reopened_claim' && mode !== 'acting_coo_intake' }));
  process.exit(0);
}

if (args[0] === 'pr' && args[1] === 'list') {
  if (mode === 'ambiguous_404') {
    console.error('HTTP 404: Not Found (/repos/ahuelsmann/MOBAflow/pulls)');
  } else {
    console.error('GraphQL: Pull requests are disabled for this repository');
  }
  process.exit(1);
}

if (args[0] === 'api' && args.some((a) => a.startsWith('repos/ahuelsmann/MOBAflow/commits'))) {
  if (mode === 'reopened_claim' || mode === 'acting_coo_intake') {
    console.log(JSON.stringify([{
      sha: mode === 'acting_coo_intake' ? 'cafe1234567890abcdef1234567890abcdef1234' : 'beef1234567890abcdef1234567890abcdef1234',
      html_url: 'https://github.com/ahuelsmann/MOBAflow/commit/beef1234',
      commit: {
        message: mode === 'acting_coo_intake' ? 'feat: ship item (Closes #969)' : 'feat: first pass (Closes #965)',
        committer: { date: ${JSON.stringify(olderCommitIso)} }
      }
    }]));
  } else {
    console.log('[]');
  }
  process.exit(0);
}

if (args[0] === 'issue' && args[1] === 'view') {
  const num = Number(args[2]);
  if (mode === 'acting_coo_intake' && num === 969) {
    console.log(JSON.stringify({ state: 'CLOSED', projectItems: [{ status: 'Done' }] }));
  } else {
    console.log(JSON.stringify({ state: 'OPEN', projectItems: [{ status: 'In progress' }] }));
  }
  process.exit(0);
}

if (args[0] === 'api' && args[1] === 'graphql') {
  const nodes = mode === 'reopened_claim'
    ? [
        {
          id: 'INPROGRESS_965',
          fieldValues: { nodes: [
            { name: 'In progress', field: { name: 'Status' } },
            { name: 'P1', field: { name: 'Priority' } },
            { name: 'M', field: { name: 'Risk class' } }
          ] },
          content: { __typename: 'Issue', number: 965, title: 'Reopened Reclaimed 965', state: 'OPEN' }
        },
        {
          id: 'READY_971',
          fieldValues: { nodes: [
            { name: 'Ready', field: { name: 'Status' } },
            { name: 'P1', field: { name: 'Priority' } },
            { name: 'M', field: { name: 'Risk class' } }
          ] },
          content: { __typename: 'Issue', number: 971, title: 'Fresh Ready Item', state: 'OPEN' }
        }
      ]
    : [
        {
          id: 'READY_971',
          fieldValues: { nodes: [
            { name: 'Ready', field: { name: 'Status' } },
            { name: 'P1', field: { name: 'Priority' } },
            { name: 'M', field: { name: 'Risk class' } }
          ] },
          content: { __typename: 'Issue', number: 971, title: 'Fresh Ready Item', state: 'OPEN' }
        }
      ];

  console.log(JSON.stringify({
    data: {
      user: {
        projectV2: {
          fields: { nodes: [{ name: 'Status' }, { name: 'Priority' }, { name: 'Risk class' }] },
          items: { pageInfo: { hasNextPage: false, endCursor: null }, nodes }
        }
      }
    }
  }));
  process.exit(0);
}
console.log('[]');
`, 'utf8');
  fs.writeFileSync(path.join(binDir, 'gh.cmd'), `@echo off\r\n"${process.execPath}" "${mockGhScript}" %*\r\n`, 'utf8');

  try {
    const busEnv = { ...process.env, MOBADEV_SWARM_BUS_DB: dbPath };
    const wdEnv = {
      ...process.env,
      LOCALAPPDATA: localAppDir,
      MOBADEV_SWARM_BUS_DB: dbPath,
      PATH: `${binDir}${path.delimiter}${process.env.PATH}`
    };

    // Seed #965 claimed AFTER olderCommitIso (T1 > T0) by antigravity-opus (so codex holds NO active claim)
    await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'upsert', '--issue-number', '965', '--title', 'Reopened Reclaimed 965', '--priority', '1', '--risk-class', 'M'], busEnv);
    await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'next', '--agent', 'antigravity-opus'], busEnv);
    await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'sync-meta', '--meta-key', 'code_wip', '--meta-value', '2'], busEnv);

    // 1. Ambiguous HTTP 404 when has_pull_requests: true MUST NOT trigger PR-disabled mode and MUST preserve code_wip & claimed leases
    const res404 = await runProcessAsync('pwsh', [
      '-NoProfile', '-File', WATCHDOG_SCRIPT,
      '-SkipHygiene', '-SyncQueue', '-Json', '-DashboardApiUrl', `http://127.0.0.1:${port}`
    ], { ...wdEnv, MOCK_GUARD_MODE: 'ambiguous_404' });
    assert.equal(res404.status, 0, res404.stderr);
    const rep404 = JSON.parse(res404.stdout);
    assert.equal(rep404.PullRequestsDisabled, false);
    assert.equal(rep404.QueueSync.OpenPrsReadSucceeded, false);
    assert.equal(rep404.QueueSync.SchemaValid, false);
    assert.equal(rep404.QueueSync.RemovedCount, 0);
    assert.equal(rep404.QueueSync.DoneCount, 0);
    const listAfter404 = JSON.parse((await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'list'], busEnv)).stdout);
    assert.equal(listAfter404.code_wip, 2);
    assert.equal(listAfter404.items.find((i) => i.issue_number === 965).status, 'claimed');

    // 2. Older T0 Closes #965 commit in 2h window MUST NOT close reopened/reclaimed #965 (T1 > T0, OPEN In progress);
    //    and HeadIdleWake with no Codex claim routes new-issue intake to COO (claude-opus), NOT codex.
    const resReopen = await runProcessAsync('pwsh', [
      '-NoProfile', '-File', WATCHDOG_SCRIPT,
      '-SkipHygiene', '-DispatchEvents', '-SyncQueue', '-Json', '-DashboardApiUrl', `http://127.0.0.1:${port}`
    ], { ...wdEnv, MOCK_GUARD_MODE: 'reopened_claim' });
    assert.equal(resReopen.status, 0, resReopen.stderr);
    const repReopen = JSON.parse(resReopen.stdout);
    assert.equal(repReopen.PullRequestsDisabled, true);
    assert.equal(repReopen.QueueSync.DoneCount, 0);
    const listAfterReopen = JSON.parse((await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'queue', '--action', 'list'], busEnv)).stdout);
    assert.equal(listAfterReopen.items.find((i) => i.issue_number === 965).status, 'claimed');

    const idleWakeCoo = repReopen.NewEventsDispatched.find((e) => e.EventType === 'HeadIdleWake');
    assert.ok(idleWakeCoo, 'Expected HeadIdleWake intake notification to active COO');
    assert.equal(idleWakeCoo.BusRecipient, 'claude-opus');
    assert.equal(idleWakeCoo.WakeViaBus, false);

    // 3. During Claude quota pause (rate_limited), NextWorkAvailable & new-issue HeadIdleWake route strictly to Acting COO (antigravity-gemini), never waking codex to self-claim
    mockClaudeState = 'rate_limited';
    const resFailover = await runProcessAsync('pwsh', [
      '-NoProfile', '-File', WATCHDOG_SCRIPT,
      '-SkipHygiene', '-DispatchEvents', '-SyncQueue', '-ResetEventState', '-Json', '-DashboardApiUrl', `http://127.0.0.1:${port}`
    ], { ...wdEnv, MOCK_GUARD_MODE: 'acting_coo_intake' });
    assert.equal(resFailover.status, 0, resFailover.stderr);
    const repFailover = JSON.parse(resFailover.stdout);
    const actingNext = repFailover.NewEventsDispatched.find((e) => e.EventType === 'NextWorkAvailable');
    const actingIdle = repFailover.NewEventsDispatched.find((e) => e.EventType === 'HeadIdleWake');
    assert.ok(actingNext && actingNext.BusRecipient === 'antigravity-gemini');
    assert.ok(actingIdle && actingIdle.BusRecipient === 'antigravity-gemini' && actingIdle.WakeViaBus === false);
    const codexInbox = JSON.parse((await runProcessAsync(process.execPath, ['--no-warnings', BUS_SCRIPT, '--cli', 'inbox', '--agent', 'codex'], busEnv)).stdout);
    assert.equal(codexInbox.messages.some((m) => m.topic === 'HeadIdleWake' || m.topic === 'NextWorkAvailable'), false);
  } finally {
    await new Promise((resolve) => server.close(resolve));
    fs.rmSync(dir, { recursive: true, force: true });
  }
});
