'use strict';

// Platform-independent checks of the Windows backend plumbing.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const { parseHelperOutput, encodeRequest, HELPER } = require('../src/backends/windows');

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
