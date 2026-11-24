import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import readline from 'node:readline';
import test from 'node:test';

const BUS_SCRIPT = path.resolve(import.meta.dirname, 'mobai-swarm-bus.mjs');

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
    assert.deepEqual(names, ['bus_send', 'bus_inbox', 'bus_ack', 'bus_locks', 'bus_status']);

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
