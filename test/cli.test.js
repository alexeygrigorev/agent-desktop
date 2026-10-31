'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { Readable } = require('node:stream');

const { main, parseArgs, buildRequest, defaultScreenshotPath, prepareOutputPath, UsageError } = require('../src/cli');
const { createMockBackend } = require('../src/backends/mock');
const { selectBackendName } = require('../src/backends');

function sink() {
  const s = { data: '', write(chunk) { s.data += chunk; return true; } };
  return s;
}

async function run(argv, extra = {}) {
  const backend = extra.backend || createMockBackend();
  const stdout = sink();
  const stderr = sink();
  const code = await main(argv, { backend, stdout, stderr, ...extra });
  return { code, stdout: stdout.data, stderr: stderr.data, backend };
}

function tmpDir(t) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'agent-desktop-test-'));
  t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
  return dir;
}

const req = (argv, ctx) => buildRequest(parseArgs(argv), ctx);

// ---- argument parsing ------------------------------------------------------

test('parseArgs splits global options, command, positionals', () => {
  const p = parseArgs(['--json', 'click', '10', '20', '--button', 'right', '--count=2']);
  assert.equal(p.command, 'click');
  assert.deepEqual(p.positionals, ['10', '20']);
  assert.deepEqual(p.options, { button: 'right', count: '2' });
  assert.deepEqual(p.global, { json: true });
});

test('global options may follow the command', () => {
  const p = parseArgs(['windows', '--json', '--backend', 'mock']);
  assert.deepEqual(p.global, { json: true, backend: 'mock' });
});

test('negative numbers are positionals, not options', () => {
  const p = parseArgs(['drag', '-1920', '-5', '100', '-200']);
  assert.deepEqual(p.positionals, ['-1920', '-5', '100', '-200']);
});

test('"--" ends option parsing', () => {
  const p = parseArgs(['type', '--', '--not-an-option', '-x']);
  assert.deepEqual(p.positionals, ['--not-an-option', '-x']);
});

test('short alias -o for screenshot', () => {
  assert.equal(parseArgs(['screenshot', '-o', 'a.png']).options.output, 'a.png');
});

test('parse errors', () => {
  assert.throws(() => parseArgs(['frobnicate']), /unknown command "frobnicate"/);
  assert.throws(() => parseArgs(['click', '--bogus']), /unknown option "--bogus" for "click"/);
  assert.throws(() => parseArgs(['move', '--button', 'left']), /unknown option "--button" for "move"/);
  assert.throws(() => parseArgs(['click', '--button']), /needs a value/);
  assert.throws(() => parseArgs(['screenshot', '--all=yes']), /takes no value/);
  assert.throws(() => parseArgs(['click', '-q']), /unknown option "-q"/);
});

// ---- request building ------------------------------------------------------

test('click: defaults, explicit position, offset', () => {
  assert.deepEqual(req(['click']), { method: 'click', params: { button: 'left', count: 1 } });
  assert.deepEqual(req(['click', '10', '20', '--button', 'Right', '--count', '2']),
    { method: 'click', params: { button: 'right', count: 2, x: 10, y: 20 } });
  assert.deepEqual(req(['click', '2000', '300', '--offset', '-1920,0']).params,
    { button: 'left', count: 1, x: 80, y: 300 });
  assert.throws(() => req(['click', '10']), UsageError);
  assert.throws(() => req(['click', '--button', 'side']), /left, right or middle/);
  assert.throws(() => req(['click', '1', '2', '--count', '0']), /--count must be >= 1/);
  assert.throws(() => req(['click', '1.5', '2']), /X must be an integer/);
  assert.throws(() => req(['click', '--offset', '1,1']), /--offset needs X Y/);
});

test('move and drag', () => {
  assert.deepEqual(req(['move', '-5', '7']), { method: 'move', params: { x: -5, y: 7 } });
  assert.deepEqual(req(['drag', '1', '2', '3', '4']).params,
    { x1: 1, y1: 2, x2: 3, y2: 4, button: 'left', steps: 10, duration: 200 });
  assert.deepEqual(req(['drag', '0', '0', '10', '10', '--offset', '100,50', '--steps', '3', '--duration', '0']).params,
    { x1: 100, y1: 50, x2: 110, y2: 60, button: 'left', steps: 3, duration: 0 });
  assert.throws(() => req(['drag', '1', '2', '3']), /usage: agent-desktop drag/);
});

test('scroll', () => {
  assert.deepEqual(req(['scroll', '3']).params, { dy: 3, dx: 0 });
  assert.deepEqual(req(['scroll', '-2', '--dx', '1', '--at', '50,60']).params, { dy: -2, dx: 1, x: 50, y: 60 });
  assert.deepEqual(req(['scroll', '1', '--at', '10,10', '--offset', '-1920,0']).params, { dy: 1, dx: 0, x: -1910, y: 10 });
  assert.throws(() => req(['scroll', '1', '--offset', '1,1']), /--offset needs --at/);
});

test('type joins positionals and supports --stdin', () => {
  assert.deepEqual(req(['type', 'hello', 'world']).params, { text: 'hello world', delay: 0 });
  assert.deepEqual(req(['type', '--stdin'], { stdinText: 'Grüße\n' }).params, { text: 'Grüße\n', delay: 0 });
  assert.throws(() => req(['type']), /usage: agent-desktop type/);
});

test('key parses every combo up front', () => {
  const r = req(['key', 'ctrl+shift+t', 'enter']);
  assert.equal(r.method, 'key');
  assert.deepEqual(r.params.combos.map((c) => c.map((k) => k.vk)), [[0x11, 0x10, 0x54], [0x0d]]);
  assert.throws(() => req(['key', 'ctrl+nope']), UsageError);
});

test('focus target: pid, title, hwnd', () => {
  assert.deepEqual(req(['focus', '4242']).params, { pid: 4242 });
  assert.deepEqual(req(['focus', 'Google Chrome']).params, { title: 'Google Chrome' });
  assert.deepEqual(req(['focus', '2048', '--title']).params, { title: '2048' });
  assert.deepEqual(req(['focus', '1001', '--hwnd']).params, { hwnd: 1001 });
});

test('screenshot request: monitor/all and default path', () => {
  const cwd = path.resolve('/work');
  assert.deepEqual(req(['screenshot', '-o', 'shots/a.png'], { cwd }).params, { output: path.join(cwd, 'shots', 'a.png') });
  assert.equal(req(['screenshot', '--monitor', '2', '-o', 'a.png'], { cwd }).params.monitor, 2);
  assert.throws(() => req(['screenshot', '--monitor', '1', '--all']), /either --monitor or --all/);
  assert.throws(() => req(['screenshot', '--monitor', '0']), /--monitor must be >= 1/);
  const now = new Date(2026, 9, 31, 14, 5, 9, 7);
  assert.equal(defaultScreenshotPath(now), path.join(os.tmpdir(), 'agent-desktop', 'screenshot-20261031-140509-007.png'));
});

test('backend selection', () => {
  assert.equal(selectBackendName(undefined, {}, 'win32'), 'windows');
  assert.equal(selectBackendName('mock', {}, 'linux'), 'mock');
  assert.equal(selectBackendName(undefined, { AGENT_DESKTOP_BACKEND: 'mock' }, 'linux'), 'mock');
  assert.throws(() => selectBackendName(undefined, {}, 'linux'), /no desktop backend for platform "linux"/);
  assert.throws(() => selectBackendName('x11', {}, 'linux'), /unknown backend "x11"/);
});

// ---- end to end with the mock backend --------------------------------------

test('screenshot creates missing parent directories (mkdir -p)', async (t) => {
  const dir = tmpDir(t);
  const out = path.join(dir, 'a', 'b', 'c', 'shot.png');
  const { code, stdout, backend } = await run(['--json', 'screenshot', '--output', out]);
  assert.equal(code, 0);
  const res = JSON.parse(stdout);
  assert.equal(res.ok, true);
  assert.equal(res.path, out);
  assert.deepEqual(res.origin, { x: -1920, y: 0 });
  assert.equal(fs.readFileSync(out).subarray(1, 4).toString(), 'PNG');
  assert.equal(backend.calls[0].params.output, out);
});

test('prepareOutputPath resolves relative paths and creates dirs', (t) => {
  const dir = tmpDir(t);
  const full = prepareOutputPath(path.join('x', 'y', 'z.png'), dir);
  assert.equal(full, path.join(dir, 'x', 'y', 'z.png'));
  assert.ok(fs.statSync(path.join(dir, 'x', 'y')).isDirectory());
});

test('screenshot of one monitor reports that monitor origin', async (t) => {
  const dir = tmpDir(t);
  const { stdout } = await run(['screenshot', '--monitor', '2', '-o', path.join(dir, 's.png'), '--json']);
  const res = JSON.parse(stdout);
  assert.equal(res.monitor, 2);
  assert.deepEqual(res.origin, { x: -1920, y: 0 });
  assert.equal(res.width, 1920);
});

test('pixel found in a screenshot maps to the right click point via --offset', async (t) => {
  const dir = tmpDir(t);
  const shot = JSON.parse((await run(['--json', 'screenshot', '-o', path.join(dir, 's.png')])).stdout);
  const pixel = { x: 2100, y: 640 }; // e.g. a button found in the image
  const backend = createMockBackend();
  const { code } = await run(['click', String(pixel.x), String(pixel.y), '--offset', `${shot.origin.x},${shot.origin.y}`], { backend });
  assert.equal(code, 0);
  assert.deepEqual(backend.calls[0].params, { button: 'left', count: 1, x: 180, y: 640 });
});

test('text output for click, windows, monitors, focus', async () => {
  assert.match((await run(['click', '5', '6', '--count', '2'])).stdout, /clicked left x2 at \(5,6\)/);
  const w = (await run(['windows'])).stdout;
  assert.match(w, /1001\tpid=4242\tchrome\t1600x1000 at \(0,0\)\t\[fg\]\tExtensions - Google Chrome/);
  assert.match((await run(['monitors'])).stdout, /virtual screen 4480x1440 at \(-1920,0\)/);
  assert.match((await run(['focus', 'notepad'])).stdout, /focused 1002 pid=777 "Untitled - Notepad"/);
});

test('windows --filter matches title or process name', async () => {
  const res = JSON.parse((await run(['windows', '--json', '--filter', 'NOTEPAD'])).stdout);
  assert.deepEqual(res.windows.map((w) => w.hwnd), [1002]);
});

test('type --stdin reads text from stdin', async () => {
  const backend = createMockBackend();
  const { code } = await run(['type', '--stdin'], { backend, stdin: Readable.from([Buffer.from('héllo')]) });
  assert.equal(code, 0);
  assert.equal(backend.calls[0].params.text, 'héllo');
});

test('wait sleeps without touching the backend', async () => {
  const backend = createMockBackend();
  const t0 = Date.now();
  const { stdout } = await run(['--json', 'wait', '30'], { backend });
  assert.ok(Date.now() - t0 >= 25);
  assert.deepEqual(JSON.parse(stdout), { ok: true, command: 'wait', waitedMs: 30 });
  assert.equal(backend.calls.length, 0);
});

test('errors: usage exit 2, runtime exit 1, JSON error object', async () => {
  const usage = await run(['click', '1']);
  assert.equal(usage.code, 2);
  assert.match(usage.stderr, /^agent-desktop: usage: agent-desktop click/);
  const runtime = await run(['--json', 'focus', 'no such window']);
  assert.equal(runtime.code, 1);
  assert.deepEqual(JSON.parse(runtime.stdout), { ok: false, error: 'no visible window matches' });
});

test('help and version', async () => {
  const help = await run(['--help']);
  assert.equal(help.code, 0);
  assert.match(help.stdout, /Usage: agent-desktop/);
  for (const cmd of ['screenshot', 'click', 'move', 'drag', 'scroll', 'type', 'key', 'windows', 'focus', 'monitors', 'cursor', 'wait']) {
    assert.match(help.stdout, new RegExp(`\\n  ${cmd}\\b`), `help lists ${cmd}`);
  }
  assert.equal((await run([])).code, 2);
  assert.match((await run(['--version'])).stdout, /^\d+\.\d+\.\d+\n$/);
});

test('bin script runs end to end with the mock backend', async () => {
  const { execFileSync } = require('node:child_process');
  const out = execFileSync(process.execPath, [path.join(__dirname, '..', 'bin', 'agent-desktop.js'), '--backend', 'mock', '--json', 'cursor']);
  assert.deepEqual(JSON.parse(out), { ok: true, command: 'cursor', cursor: { x: 100, y: 100 } });
});
