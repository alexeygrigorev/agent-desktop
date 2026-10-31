'use strict';

const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

const { parseCombo } = require('./keys');
const { parseInteger, parsePoint, imageToScreen } = require('./coords');
const { selectBackendName, createBackend } = require('./backends');
const { diagnose } = require('./doctor');

const VERSION = require('../package.json').version;

const USAGE = `agent-desktop ${VERSION} - OS-level desktop control for agents

Usage: agent-desktop [--json] [--backend windows|mock] <command> [args]

Commands:
  screenshot [--output PATH] [--monitor N | --all | --window W] [--method auto|screen|printwindow]
                                 Capture the whole virtual screen (default), one
                                 monitor or one window to a PNG; parent dirs are
                                 created. Falls back to PrintWindow when the
                                 screen cannot be read (e.g. minimized RDP client).
  click [X Y] [--button left|right|middle] [--count N] [--offset X,Y] [--window W]
                                 Click at X Y (or at the current cursor position).
  move X Y [--offset X,Y] [--window W]
                                 Move the mouse.
  drag X1 Y1 X2 Y2 [--button B] [--steps N] [--duration MS] [--offset X,Y]
                                 Press at X1 Y1, move to X2 Y2, release.
  scroll DY [--dx DX] [--at X,Y] [--window W]
                                 Scroll DY wheel notches (positive = down).
  type TEXT [--delay MS] [--stdin] [--window W [--at X,Y]]
                                 Type text as unicode keystrokes (\\n = Enter).
  key COMBO [COMBO...] [--delay MS] [--window W [--at X,Y]]
                                 Press key combos, e.g. ctrl+shift+t, alt+f4, enter.
  windows [--filter TEXT] [--all]
                                 List top-level windows (Z-order, topmost first).
  focus <TITLE-SUBSTRING|PID|HWND> [--title] [--hwnd]
                                 Restore and bring a window to the foreground.
                                 A bare number is a pid if that process has a
                                 window, else an hwnd.
  monitors                       List monitors and the virtual-screen bounds.
  doctor                         Diagnose the session (RDP/console, foreground,
                                 input desktop, capture and input methods).
  cursor                         Print the current cursor position.
  wait MS                        Sleep MS milliseconds.

Global options:
  --json                         Machine-readable JSON output (one object).
  --backend NAME                 Backend (default: windows on Windows;
                                 env AGENT_DESKTOP_BACKEND).
  -h, --help                     Show this help.
  -v, --version                  Show the version.

Coordinates are physical virtual-screen pixels (DPI-aware, multi-monitor).
A screenshot reports its "origin"; image pixel (px,py) is screen point
(px+origin.x, py+origin.y), which "--offset ORIGIN_X,ORIGIN_Y" applies for you.

--window W targets one window: W is a title substring, a pid or hwnd (bare
number: pid if that process has a window, else hwnd), or explicit "hwnd:N",
"pid:N", "title:TEXT". With --window, X Y are pixels of "screenshot --window W".
--input auto|sendinput|post: auto uses SendInput, and posts window messages
to the --window target when there is no interactive display.
`;

// Options each command accepts: name -> 'bool' | 'value'. Short aliases map
// to long names.
const COMMANDS = {
  screenshot: {
    options: { output: 'value', monitor: 'value', all: 'bool', window: 'value', method: 'value' },
    aliases: { o: 'output' },
  },
  click: { options: { button: 'value', count: 'value', offset: 'value', window: 'value', input: 'value' } },
  move: { options: { offset: 'value', window: 'value', input: 'value' } },
  drag: { options: { button: 'value', steps: 'value', duration: 'value', offset: 'value' } },
  scroll: { options: { dx: 'value', at: 'value', offset: 'value', window: 'value', input: 'value' } },
  type: { options: { delay: 'value', stdin: 'bool', window: 'value', input: 'value', at: 'value' } },
  key: { options: { delay: 'value', window: 'value', input: 'value', at: 'value' } },
  windows: { options: { filter: 'value', all: 'bool' } },
  focus: { options: { title: 'bool', hwnd: 'bool' } },
  monitors: { options: {} },
  cursor: { options: {} },
  doctor: { options: {} },
  wait: { options: {} },
  help: { options: {} },
};

const GLOBAL_OPTIONS = { json: 'bool', backend: 'value', help: 'bool', version: 'bool' };
const GLOBAL_ALIASES = { h: 'help', v: 'version' };

class UsageError extends Error {}

function isNegativeNumber(token) {
  return /^-\d/.test(token);
}

// argv (without node and script) -> { command, positionals, options, global }
function parseArgs(argv) {
  const global = {};
  const options = {};
  const positionals = [];
  let command = null;
  let spec = null;
  let endOfOptions = false;

  for (let i = 0; i < argv.length; i++) {
    const token = argv[i];
    if (endOfOptions || token === '-' || isNegativeNumber(token) || !token.startsWith('-')) {
      if (command === null) {
        command = token;
        spec = COMMANDS[command];
        if (!spec) throw new UsageError(`unknown command "${command}" (see --help)`);
      } else {
        positionals.push(token);
      }
      continue;
    }
    if (token === '--') {
      endOfOptions = true;
      continue;
    }
    let name;
    let inlineValue;
    if (token.startsWith('--')) {
      const eq = token.indexOf('=');
      name = eq === -1 ? token.slice(2) : token.slice(2, eq);
      inlineValue = eq === -1 ? undefined : token.slice(eq + 1);
    } else {
      const short = token.slice(1);
      name = (spec && spec.aliases && spec.aliases[short]) || GLOBAL_ALIASES[short];
      if (!name) throw new UsageError(`unknown option "${token}"`);
    }
    let kind;
    let target;
    if (spec && spec.options[name]) {
      kind = spec.options[name];
      target = options;
    } else if (GLOBAL_OPTIONS[name]) {
      kind = GLOBAL_OPTIONS[name];
      target = global;
    } else {
      throw new UsageError(`unknown option "--${name}"${command ? ` for "${command}"` : ''}`);
    }
    if (kind === 'bool') {
      if (inlineValue !== undefined) throw new UsageError(`option "--${name}" takes no value`);
      target[name] = true;
    } else {
      let value = inlineValue;
      if (value === undefined) {
        if (i + 1 >= argv.length) throw new UsageError(`option "--${name}" needs a value`);
        value = argv[++i];
      }
      target[name] = value;
    }
  }
  return { command, positionals, options, global };
}

function expectPositionals(positionals, min, max, usage) {
  if (positionals.length < min || positionals.length > max) {
    throw new UsageError(`usage: agent-desktop ${usage}`);
  }
}

function parseButton(value) {
  const button = (value || 'left').toLowerCase();
  if (!['left', 'right', 'middle'].includes(button)) {
    throw new UsageError(`--button must be left, right or middle, got "${value}"`);
  }
  return button;
}

function parsePositive(value, what, dflt) {
  if (value === undefined) return dflt;
  const n = parseInteger(value, what);
  if (n < 1) throw new UsageError(`${what} must be >= 1, got ${n}`);
  return n;
}

function parseNonNegative(value, what, dflt) {
  if (value === undefined) return dflt;
  const n = parseInteger(value, what);
  if (n < 0) throw new UsageError(`${what} must be >= 0, got ${n}`);
  return n;
}

// --window / focus value -> { hwnd } | { pid } | { title } | { id } (bare
// number: the backend tries it as a pid, then as an hwnd).
function parseWindowTarget(value) {
  const v = String(value);
  let m = /^hwnd:(0x[0-9a-f]+|\d+)$/i.exec(v);
  if (m) return { hwnd: Number(m[1]) };
  m = /^pid:(\d+)$/i.exec(v);
  if (m) return { pid: Number(m[1]) };
  m = /^title:(.*)$/is.exec(v);
  const title = m ? m[1] : v;
  if (!m && /^\d+$/.test(v)) return { id: Number(v) };
  if (title.trim() === '') throw new UsageError('--window needs a title, pid or hwnd');
  return { title };
}

const INPUT_MODES = ['auto', 'sendinput', 'post'];
const CAPTURE_METHODS = ['auto', 'screen', 'printwindow'];

// Shared --window / --input handling for input commands.
function inputTarget(opt) {
  const params = {};
  if (opt.window !== undefined) params.window = parseWindowTarget(opt.window);
  if (opt.input !== undefined) {
    const mode = opt.input.toLowerCase();
    if (!INPUT_MODES.includes(mode)) throw new UsageError(`--input must be one of ${INPUT_MODES.join(', ')}, got "${opt.input}"`);
    if (mode === 'post' && !params.window) throw new UsageError('--input post needs --window');
    params.input = mode;
  }
  return params;
}

// --at X,Y for type/key picks the child window that receives posted keys.
function keyTargetPoint(opt) {
  if (opt.at === undefined) return {};
  if (opt.window === undefined) throw new UsageError('--at needs --window for type/key');
  return parsePoint(opt.at, '--at');
}

function point(xs, ys, offset) {
  const p = { x: parseInteger(xs, 'X'), y: parseInteger(ys, 'Y') };
  return offset ? imageToScreen(p, parsePoint(offset, '--offset')) : p;
}

function timestamp(date = new Date()) {
  const pad = (n, w = 2) => String(n).padStart(w, '0');
  return `${date.getFullYear()}${pad(date.getMonth() + 1)}${pad(date.getDate())}-` +
    `${pad(date.getHours())}${pad(date.getMinutes())}${pad(date.getSeconds())}-${pad(date.getMilliseconds(), 3)}`;
}

function defaultScreenshotPath(date) {
  return path.join(os.tmpdir(), 'agent-desktop', `screenshot-${timestamp(date)}.png`);
}

// Resolve against cwd and create parent directories (mkdir -p).
function prepareOutputPath(output, cwd = process.cwd()) {
  const full = path.resolve(cwd, output);
  fs.mkdirSync(path.dirname(full), { recursive: true });
  return full;
}

function readStdin(stream) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    stream.on('data', (c) => chunks.push(Buffer.from(c)));
    stream.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
    stream.on('error', reject);
  });
}

// Turn parsed args into { method, params } for the backend (pure, testable).
function buildRequest(parsed, ctx = {}) {
  const { command, positionals: pos, options: opt } = parsed;
  switch (command) {
    case 'screenshot': {
      expectPositionals(pos, 0, 0, 'screenshot [--output PATH] [--monitor N | --all | --window W]');
      if ([opt.all, opt.monitor !== undefined, opt.window !== undefined].filter(Boolean).length > 1) {
        throw new UsageError('use only one of --monitor, --all, --window');
      }
      const monitor = parsePositive(opt.monitor, '--monitor', undefined);
      const output = opt.output || defaultScreenshotPath(ctx.now);
      const params = { output: path.resolve(ctx.cwd || process.cwd(), output) };
      if (monitor !== undefined) params.monitor = monitor;
      if (opt.window !== undefined) params.window = parseWindowTarget(opt.window);
      if (opt.method !== undefined) {
        const m = opt.method.toLowerCase();
        if (!CAPTURE_METHODS.includes(m)) throw new UsageError(`--method must be one of ${CAPTURE_METHODS.join(', ')}, got "${opt.method}"`);
        params.method = m;
      }
      return { method: 'screenshot', params };
    }
    case 'click': {
      if (pos.length !== 0 && pos.length !== 2) throw new UsageError('usage: agent-desktop click [X Y] [--button B] [--count N]');
      const params = { button: parseButton(opt.button), count: parsePositive(opt.count, '--count', 1) };
      if (pos.length === 2) Object.assign(params, point(pos[0], pos[1], opt.offset));
      else if (opt.offset) throw new UsageError('--offset needs X Y');
      return { method: 'click', params: { ...params, ...inputTarget(opt) } };
    }
    case 'move':
      expectPositionals(pos, 2, 2, 'move X Y');
      return { method: 'move', params: { ...point(pos[0], pos[1], opt.offset), ...inputTarget(opt) } };
    case 'drag': {
      expectPositionals(pos, 4, 4, 'drag X1 Y1 X2 Y2');
      const a = point(pos[0], pos[1], opt.offset);
      const b = point(pos[2], pos[3], opt.offset);
      return {
        method: 'drag',
        params: {
          x1: a.x, y1: a.y, x2: b.x, y2: b.y,
          button: parseButton(opt.button),
          steps: parsePositive(opt.steps, '--steps', 10),
          duration: parseNonNegative(opt.duration, '--duration', 200),
        },
      };
    }
    case 'scroll': {
      expectPositionals(pos, 1, 1, 'scroll DY [--dx DX] [--at X,Y]');
      const params = { dy: parseInteger(pos[0], 'DY'), dx: opt.dx === undefined ? 0 : parseInteger(opt.dx, '--dx') };
      if (opt.at) {
        const at = parsePoint(opt.at, '--at');
        Object.assign(params, opt.offset ? imageToScreen(at, parsePoint(opt.offset, '--offset')) : at);
      } else if (opt.offset) {
        throw new UsageError('--offset needs --at');
      }
      return { method: 'scroll', params: { ...params, ...inputTarget(opt) } };
    }
    case 'type': {
      let text;
      if (opt.stdin) {
        expectPositionals(pos, 0, 0, 'type --stdin');
        text = ctx.stdinText || '';
      } else {
        if (pos.length === 0) throw new UsageError('usage: agent-desktop type TEXT');
        text = pos.join(' ');
      }
      return {
        method: 'type',
        params: { text, delay: parseNonNegative(opt.delay, '--delay', 0), ...inputTarget(opt), ...keyTargetPoint(opt) },
      };
    }
    case 'key': {
      if (pos.length === 0) throw new UsageError('usage: agent-desktop key COMBO [COMBO...]');
      let combos;
      try {
        combos = pos.map(parseCombo);
      } catch (e) {
        throw new UsageError(e.message);
      }
      return {
        method: 'key',
        params: { combos, delay: parseNonNegative(opt.delay, '--delay', 30), ...inputTarget(opt), ...keyTargetPoint(opt) },
      };
    }
    case 'windows':
      expectPositionals(pos, 0, 0, 'windows [--filter TEXT] [--all]');
      return { method: 'windows', params: { all: Boolean(opt.all) } };
    case 'focus': {
      expectPositionals(pos, 1, 1, 'focus <TITLE-SUBSTRING|PID|HWND> [--title] [--hwnd]');
      if (opt.title && opt.hwnd) throw new UsageError('use either --title or --hwnd, not both');
      const target = pos[0];
      if (opt.hwnd) return { method: 'focus', params: { hwnd: parseInteger(target, 'HWND') } };
      if (target.trim() === '') throw new UsageError('window title must not be empty');
      if (opt.title) return { method: 'focus', params: { title: target } };
      return { method: 'focus', params: parseWindowTarget(target) };
    }
    case 'monitors':
      expectPositionals(pos, 0, 0, 'monitors');
      return { method: 'monitors', params: {} };
    case 'cursor':
      expectPositionals(pos, 0, 0, 'cursor');
      return { method: 'cursor', params: {} };
    case 'doctor':
      expectPositionals(pos, 0, 0, 'doctor');
      return { method: 'doctor', params: {} };
    case 'wait': {
      expectPositionals(pos, 1, 1, 'wait MS');
      return { method: null, waitMs: parseNonNegative(pos[0], 'MS', 0) };
    }
    default:
      throw new UsageError(`unknown command "${command}"`);
  }
}

function fmtPoint(p) {
  return `(${p.x},${p.y})`;
}

function fmtRect(r) {
  return `${r.width}x${r.height} at ${fmtPoint(r)}`;
}

function where(result) {
  return result.cursor || result.point || { x: '?', y: '?' };
}

function via(result) {
  if (result.input !== 'postmessage') return '';
  return ` (posted to ${result.targetHwnd} ${result.targetClass})`;
}

function formatDoctor(r) {
  const cap = r.capture || {};
  const ok = (c) => (c && c.ok ? 'ok' : `FAILED${c && c.error ? ` (${c.error})` : ''}`);
  const lines = [
    `status: ${r.status}`,
    `session: id=${r.sessionId} name=${r.sessionName || '?'} state=${r.connectState || '?'} ` +
      `${r.remote ? 'remote (RDP)' : 'local/console'}; console session id=${r.consoleSessionId}`,
    `foreground window: ${r.foregroundHwnd ? `${r.foregroundHwnd} "${r.foregroundTitle || ''}"` : 'none (0)'}`,
    `input desktop: ${r.inputDesktop || `not accessible${r.inputDesktopError ? ` (${r.inputDesktopError})` : ''}`}`,
    `screen capture (BitBlt): ${ok(cap.screen)}`,
    `window capture (PrintWindow): ${ok(cap.printWindow)}` +
      (cap.printWindow && cap.printWindow.hwnd ? ` tested on "${cap.printWindow.title}"` : '') +
      (cap.printWindow && cap.printWindow.blank ? ' [image blank]' : ''),
    `SendInput: ${ok(r.sendInput)}`,
    `DPI awareness: ${r.dpiAwareness || '?'}`,
  ];
  if (r.problems.length) lines.push('problems:', ...r.problems.map((p) => `  - ${p}`));
  lines.push('hints:', ...r.hints.map((h) => `  - ${h}`));
  return lines.join('\n');
}

function formatText(command, result) {
  switch (command) {
    case 'screenshot': {
      let scope = result.monitor ? `monitor ${result.monitor}` : 'all monitors';
      if (result.window) scope = `window ${result.window.hwnd} "${result.window.title}"`;
      return `${result.path}\n${result.width}x${result.height}, origin ${fmtPoint(result.origin)}, ${scope}` +
        (result.method ? `, via ${result.method}` : '');
    }
    case 'click':
      return `clicked ${result.button}${result.count > 1 ? ` x${result.count}` : ''} at ${fmtPoint(where(result))}${via(result)}`;
    case 'move':
      return result.cursor ? `cursor ${fmtPoint(result.cursor)}${via(result)}` : `moved to ${fmtPoint(where(result))}${via(result)}`;
    case 'drag':
    case 'cursor':
      return `cursor ${fmtPoint(result.cursor)}`;
    case 'scroll':
      return `scrolled dy=${result.dy} dx=${result.dx} at ${fmtPoint(where(result))}${via(result)}`;
    case 'type':
      return `typed ${result.typed} chars${via(result)}`;
    case 'key':
      return `pressed ${result.combos} combo${result.combos === 1 ? '' : 's'}${via(result)}`;
    case 'doctor':
      return formatDoctor(result);
    case 'windows':
      if (result.windows.length === 0) return '(no windows)';
      return result.windows
        .map((w) => {
          const flags = [w.foreground && 'fg', w.minimized && 'min', w.maximized && 'max'].filter(Boolean).join(',');
          return `${w.hwnd}\tpid=${w.pid}\t${w.process || '?'}\t${fmtRect(w.rect)}${flags ? `\t[${flags}]` : ''}\t${w.title}`;
        })
        .join('\n');
    case 'focus':
      return `focused ${result.window.hwnd} pid=${result.window.pid} "${result.window.title}"` +
        (result.matches > 1 ? ` (${result.matches} matches, took the topmost)` : '');
    case 'monitors':
      return [
        ...result.monitors.map((m) => `${m.index}${m.primary ? '*' : ' '} ${m.device}\t${fmtRect(m)}\tdpi=${m.dpi} scale=${m.scale}`),
        `virtual screen ${fmtRect(result.virtualScreen)}`,
      ].join('\n');
    case 'wait':
      return `waited ${result.waitedMs} ms`;
    default:
      return JSON.stringify(result);
  }
}

function filterWindows(result, filter) {
  if (!filter) return result;
  const f = filter.toLowerCase();
  return {
    ...result,
    windows: result.windows.filter((w) =>
      (w.title || '').toLowerCase().includes(f) || (w.process || '').toLowerCase().includes(f)),
  };
}

// Returns the process exit code. `io` lets tests inject streams and a backend.
async function main(argv, io = {}) {
  const stdout = io.stdout || process.stdout;
  const stderr = io.stderr || process.stderr;
  let json = argv.includes('--json');
  try {
    const parsed = parseArgs(argv);
    json = Boolean(parsed.global.json);
    if (parsed.global.version) {
      stdout.write(json ? `${JSON.stringify({ ok: true, version: VERSION })}\n` : `${VERSION}\n`);
      return 0;
    }
    if (parsed.global.help || parsed.command === null || parsed.command === 'help') {
      stdout.write(USAGE);
      return parsed.command === null && !parsed.global.help ? 2 : 0;
    }

    const ctx = { cwd: io.cwd || process.cwd(), now: io.now };
    if (parsed.command === 'type' && parsed.options.stdin) {
      ctx.stdinText = await readStdin(io.stdin || process.stdin);
    }
    const request = buildRequest(parsed, ctx);

    let result;
    if (request.method === null) {
      await new Promise((r) => setTimeout(r, request.waitMs));
      result = { waitedMs: request.waitMs };
    } else {
      if (request.method === 'screenshot') {
        request.params.output = prepareOutputPath(request.params.output, ctx.cwd);
      }
      const backend = io.backend ||
        (io.createBackend || createBackend)(selectBackendName(parsed.global.backend));
      result = await backend[request.method](request.params);
      if (parsed.command === 'windows') result = filterWindows(result, parsed.options.filter);
      if (parsed.command === 'doctor') result = { ...result, ...diagnose(result) };
    }

    if (json) stdout.write(`${JSON.stringify({ ok: true, command: parsed.command, ...result })}\n`);
    else {
      stdout.write(`${formatText(parsed.command, result)}\n`);
      for (const w of result.warnings || []) stderr.write(`agent-desktop: warning: ${w}\n`);
    }
    return 0;
  } catch (err) {
    const usage = err instanceof UsageError;
    if (json) stdout.write(`${JSON.stringify({ ok: false, error: err.message })}\n`);
    else stderr.write(`agent-desktop: ${err.message}\n`);
    return usage ? 2 : 1;
  }
}

module.exports = {
  main,
  parseArgs,
  buildRequest,
  parseWindowTarget,
  formatText,
  prepareOutputPath,
  defaultScreenshotPath,
  UsageError,
  USAGE,
  VERSION,
};
