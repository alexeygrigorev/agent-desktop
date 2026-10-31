'use strict';

// Real Windows backend smoke test. Skipped on other platforms. Needs an
// interactive desktop session (GitHub's windows-latest runners have one).

const test = require('node:test');
const assert = require('node:assert/strict');
const { execFileSync } = require('node:child_process');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

const BIN = path.join(__dirname, '..', 'bin', 'agent-desktop.js');
const skip = process.platform !== 'win32' && 'Windows only';

function cli(...args) {
  const out = execFileSync(process.execPath, [BIN, '--json', ...args], { encoding: 'utf8', timeout: 120000 });
  const res = JSON.parse(out);
  assert.equal(res.ok, true, out);
  return res;
}

function pngSize(file) {
  const buf = fs.readFileSync(file);
  assert.equal(buf.subarray(0, 8).toString('hex'), '89504e470d0a1a0a', 'PNG signature');
  return { width: buf.readUInt32BE(16), height: buf.readUInt32BE(20) };
}

test('monitors reports a virtual screen and DPI awareness', { skip }, () => {
  const res = cli('monitors');
  assert.ok(res.monitors.length >= 1);
  assert.ok(res.virtualScreen.width > 0 && res.virtualScreen.height > 0);
  assert.equal(res.monitors.filter((m) => m.primary).length, 1);
  assert.match(res.dpiAwareness, /per-monitor/);
  console.log(JSON.stringify(res));
});

test('screenshot --all writes a PNG of the virtual screen into new dirs', { skip }, (t) => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'agent-desktop-smoke-'));
  t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
  const out = path.join(dir, 'nested', 'deeper', 'all.png');
  const res = cli('screenshot', '--all', '--output', out);
  const size = pngSize(out);
  assert.deepEqual(size, { width: res.width, height: res.height });
  assert.deepEqual(size, { width: res.virtualScreen.width, height: res.virtualScreen.height });
  assert.deepEqual(res.origin, { x: res.virtualScreen.x, y: res.virtualScreen.y });
  console.log(JSON.stringify(res));
});

test('screenshot --monitor 1 captures the primary monitor', { skip }, (t) => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'agent-desktop-smoke-'));
  t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
  const primary = cli('monitors').monitors[0];
  const res = cli('screenshot', '--monitor', '1', '-o', path.join(dir, 'm1.png'));
  assert.deepEqual(pngSize(res.path), { width: primary.width, height: primary.height });
  assert.deepEqual(res.origin, { x: primary.x, y: primary.y });
});

test('windows lists top-level windows as JSON', { skip }, () => {
  const res = cli('windows');
  assert.ok(Array.isArray(res.windows));
  for (const w of res.windows) {
    assert.equal(typeof w.hwnd, 'number');
    assert.equal(typeof w.title, 'string');
    assert.ok(w.rect && typeof w.rect.width === 'number');
  }
  console.log(`${res.windows.length} windows: ${res.windows.map((w) => w.title).slice(0, 10).join(' | ')}`);
});

test('move then cursor returns the exact pixel', { skip }, () => {
  const vs = cli('monitors').virtualScreen;
  const target = { x: vs.x + Math.floor(vs.width / 3), y: vs.y + Math.floor(vs.height / 3) };
  const moved = cli('move', String(target.x), String(target.y));
  assert.deepEqual(moved.cursor, target);
  assert.deepEqual(cli('cursor').cursor, target);
});

test('doctor reports an interactive session on the CI desktop', { skip }, () => {
  const res = cli('doctor');
  console.log(JSON.stringify(res));
  assert.ok(['ok', 'degraded'].includes(res.status));
  assert.equal(typeof res.sessionId, 'number');
  assert.equal(res.capture.printWindow.ok, true);
});

test('screenshot --method printwindow composites the virtual screen', { skip }, (t) => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'agent-desktop-smoke-'));
  t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
  const res = cli('screenshot', '--method', 'printwindow', '-o', path.join(dir, 'pw.png'));
  assert.equal(res.method, 'printwindow-composite');
  assert.ok(res.composited >= 1);
  assert.deepEqual(pngSize(res.path), { width: res.virtualScreen.width, height: res.virtualScreen.height });
});

test('screenshot --window and posted input use the same window-relative pixels', { skip }, (t) => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'agent-desktop-smoke-'));
  t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
  const win = cli('windows').windows.find((w) => !w.minimized && w.rect.width > 50 && w.rect.height > 50);
  assert.ok(win, 'a visible window to test with');
  const shot = cli('screenshot', '--window', `hwnd:${win.hwnd}`, '-o', path.join(dir, 'win.png'));
  assert.equal(shot.method, 'printwindow');
  assert.deepEqual(shot.origin, { x: win.rect.x, y: win.rect.y });
  assert.deepEqual(pngSize(shot.path), { width: win.rect.width, height: win.rect.height });
  const moved = cli('move', '20', '30', '--window', `hwnd:${win.hwnd}`, '--input', 'post');
  assert.equal(moved.input, 'postmessage');
  assert.deepEqual(moved.point, { x: win.rect.x + 20, y: win.rect.y + 30 });
  console.log(JSON.stringify(moved));
});

test('key works end to end through the helper (regression: "no key combos given")', { skip }, () => {
  const res = cli('key', 'shift', 'shift+f24');
  assert.equal(res.combos, 2);
  assert.equal(res.input, 'sendinput');
});

test('focus with a bare number accepts a pid and an hwnd', { skip }, () => {
  const win = cli('windows').windows.find((w) => !w.minimized && w.rect.width > 50);
  assert.ok(win, 'a visible window to test with');
  assert.equal(cli('focus', String(win.hwnd)).window.hwnd, win.hwnd);
  assert.equal(cli('focus', String(win.pid)).window.pid, win.pid);
});
