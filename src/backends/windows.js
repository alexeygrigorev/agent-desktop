'use strict';

// Windows backend: one short-lived Windows PowerShell 5.1 process per
// command, running windows/helper.ps1, which compiles (and caches)
// windows/AgentDesktop.cs and calls user32/gdi32 directly. No admin rights,
// no native Node modules.

const { execFile } = require('node:child_process');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

const HELPER = path.join(__dirname, 'windows', 'helper.ps1');
const RESULT_PREFIX = 'AGENT_DESKTOP_RESULT:';
// Keep well under the 32K Windows command-line limit.
const MAX_INLINE_REQUEST = 8000;
const DEFAULT_HELPER_TIMEOUT_MS = 60000;
const HELPER_TIMEOUT_BUFFER_MS = 30000;
const MAX_HELPER_TIMEOUT_MS = 0x7fffffff;

function powershellPath() {
  if (process.env.AGENT_DESKTOP_POWERSHELL) return process.env.AGENT_DESKTOP_POWERSHELL;
  const root = process.env.SystemRoot || 'C:\\Windows';
  const full = path.join(root, 'System32', 'WindowsPowerShell', 'v1.0', 'powershell.exe');
  return fs.existsSync(full) ? full : 'powershell.exe';
}

function parseHelperOutput(stdout) {
  const line = String(stdout)
    .split(/\r?\n/)
    .find((l) => l.startsWith(RESULT_PREFIX));
  if (!line) return null;
  const json = Buffer.from(line.slice(RESULT_PREFIX.length).trim(), 'base64').toString('utf8');
  return JSON.parse(json);
}

function encodeRequest(request) {
  return Buffer.from(JSON.stringify(request), 'utf8').toString('base64');
}

function timeoutForRequest(request, minimum = DEFAULT_HELPER_TIMEOUT_MS) {
  let requestedMs = 0;
  if (request.command === 'drag') {
    requestedMs = Math.max(0, Number(request.duration) || 0);
  } else if (request.command === 'type') {
    const chars = String(request.text || '').length;
    const delay = Math.max(0, Number(request.delay) || 0);
    requestedMs = chars * Math.max(delay, 2);
  } else if (request.command === 'key') {
    const combos = Array.isArray(request.combos) ? request.combos.length : 0;
    requestedMs = Math.max(0, Number(request.delay) || 0) * Math.max(0, combos - 1);
  }
  return Math.min(MAX_HELPER_TIMEOUT_MS, Math.max(minimum, requestedMs + HELPER_TIMEOUT_BUFFER_MS));
}

function runHelper(request, { timeoutMs } = {}) {
  const effectiveTimeout = timeoutMs === undefined ? timeoutForRequest(request) : timeoutMs;
  const encoded = encodeRequest(request);
  const args = ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', HELPER];
  let tmpDir = null;
  if (encoded.length > MAX_INLINE_REQUEST) {
    tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'agent-desktop-'));
    const file = path.join(tmpDir, 'request.b64');
    fs.writeFileSync(file, encoded);
    args.push('-RequestFile', file);
  } else {
    args.push('-Request', encoded);
  }
  return new Promise((resolve, reject) => {
    execFile(
      powershellPath(),
      args,
      { windowsHide: true, timeout: effectiveTimeout, maxBuffer: 64 * 1024 * 1024 },
      (err, stdout, stderr) => {
        if (tmpDir) fs.rmSync(tmpDir, { recursive: true, force: true });
        let result = null;
        try {
          result = parseHelperOutput(stdout);
        } catch (parseErr) {
          return reject(new Error(`could not parse helper output: ${parseErr.message}`));
        }
        if (!result) {
          const detail = String(stderr || '').trim() || (err && err.message) || 'no output';
          return reject(new Error(`windows helper failed: ${detail}`));
        }
        if (!result.ok) return reject(new Error(result.error || 'windows helper reported an error'));
        delete result.ok;
        resolve(result);
      },
    );
  });
}

function createWindowsBackend(options = {}) {
  const call = (command, params) => runHelper({ command, ...params }, options);
  return {
    name: 'windows',
    screenshot: (p) => call('screenshot', p),
    click: (p) => call('click', p),
    move: (p) => call('move', p),
    drag: (p) => call('drag', p),
    scroll: (p) => call('scroll', p),
    type: (p) => call('type', p),
    key: (p) => call('key', p),
    windows: (p) => call('windows', p),
    focus: (p) => call('focus', p),
    monitors: () => call('monitors', {}),
    cursor: () => call('cursor', {}),
    doctor: () => call('doctor', {}),
  };
}

module.exports = { createWindowsBackend, parseHelperOutput, encodeRequest, timeoutForRequest, HELPER };
