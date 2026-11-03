'use strict';

// Platform-independent checks of the Windows backend plumbing.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const { parseHelperOutput, encodeRequest, timeoutForRequest, HELPER } = require('../src/backends/windows');

const CS = path.join(__dirname, '..', 'src', 'backends', 'windows', 'AgentDesktop.cs');

test('request encoding round-trips unicode', () => {
  const req = { command: 'type', text: 'Привет, 世界 ✓' };
  const decoded = JSON.parse(Buffer.from(encodeRequest(req), 'base64').toString('utf8'));
  assert.deepEqual(decoded, req);
});

test('helper output is found among other lines and decoded as UTF-8', () => {
  const payload = { ok: true, windows: [{ title: 'Ünïcode – Notepad' }] };
  const line = `AGENT_DESKTOP_RESULT:${Buffer.from(JSON.stringify(payload)).toString('base64')}`;
  assert.deepEqual(parseHelperOutput(`warning: something\r\n${line}\r\n`), payload);
  assert.equal(parseHelperOutput('nothing here'), null);
});

test('helper files exist next to the backend', () => {
  assert.ok(fs.existsSync(HELPER));
  assert.ok(fs.existsSync(CS));
});

test('helper timeout covers requested long input durations', () => {
  assert.equal(timeoutForRequest({ command: 'type', text: 'x'.repeat(1000), delay: 100 }), 130000);
  assert.equal(timeoutForRequest({ command: 'drag', duration: 90000 }), 120000);
  assert.equal(timeoutForRequest({ command: 'key', combos: [[], [], []], delay: 20000 }), 70000);
  assert.equal(timeoutForRequest({ command: 'doctor' }), 60000);
});

test('C# helper stays compatible with Windows PowerShell 5.1 Add-Type (C# 5)', () => {
  const src = fs.readFileSync(CS, 'utf8')
    .replace(/\/\/.*$/gm, '')
    .replace(/"(?:[^"\\]|\\.)*"/g, '""');
  assert.doesNotMatch(src, /\$"/, 'string interpolation');
  assert.doesNotMatch(src, /\?\./, 'null-conditional operator');
  assert.doesNotMatch(src, /\bout var\b/, 'out var');
  assert.doesNotMatch(src, /\)\s*=>/, 'expression-bodied member or lambda');
  assert.doesNotMatch(src, /\bnameof\(/, 'nameof');
});

test('C# helper sets per-monitor-v2 DPI awareness and uses the virtual desktop', () => {
  const src = fs.readFileSync(CS, 'utf8');
  assert.match(src, /SetProcessDpiAwarenessContext\(new IntPtr\(-4\)\)/);
  assert.match(src, /MOUSEEVENTF_VIRTUALDESK/);
  assert.match(src, /SM_XVIRTUALSCREEN/);
  // Same normalisation formula as src/coords.js.
  assert.match(src, /Math\.Ceiling\(offset \* 65536\.0 \/ size\)/);
});

test('C# helper has the no-display fallbacks', () => {
  const src = fs.readFileSync(CS, 'utf8');
  assert.match(src, /PrintWindow\(hWnd, hdc, PW_RENDERFULLCONTENT\)/);
  assert.match(src, /const uint PW_RENDERFULLCONTENT = 2;/);
  assert.match(src, /"printwindow-composite"/);
  assert.match(src, /Post\(child, WM_CHAR/);
  assert.match(src, /GetForegroundWindow\(\) == IntPtr\.Zero/);
  assert.match(src, /case "doctor": return Doctor\(\);/);
});

test('C# helper reads JSON arrays as IList, never ArrayList (live bug: key always failed)', () => {
  // JavaScriptSerializer.DeserializeObject returns object[] for JSON arrays,
  // so "as ArrayList" is always null and "(ArrayList)" throws.
  const src = fs.readFileSync(CS, 'utf8').replace(/\/\/.*$/gm, '');
  assert.doesNotMatch(src, /ArrayList/);
  assert.match(src, /IList combos = Get\(req, "combos"\) as IList;/);
});

test('window-targeted SendInput clicks foreground the target before clicking', () => {
  const src = fs.readFileSync(CS, 'utf8');
  assert.match(src, /static Dictionary<string, object> ClickSend[\s\S]*?FocusForInput\(target\);/);
  assert.match(src, /static void FocusForInput\(WindowData target\)/);
});
