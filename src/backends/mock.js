'use strict';

// In-memory backend for tests and dry runs (`--backend mock`). Records every
// call and simulates a two-monitor desktop with a secondary monitor left of
// the primary, so the virtual screen starts at a negative x.
// createMockBackend({ interactive: false }) simulates a session with no
// interactive display (e.g. a minimized RDP client): screen capture fails and
// input needs --window.

const fs = require('node:fs');

const MONITORS = [
  { index: 1, device: '\\\\.\\DISPLAY1', primary: true, x: 0, y: 0, width: 2560, height: 1440,
    workArea: { x: 0, y: 0, width: 2560, height: 1392 }, dpi: 144, scale: 1.5 },
  { index: 2, device: '\\\\.\\DISPLAY2', primary: false, x: -1920, y: 0, width: 1920, height: 1080,
    workArea: { x: -1920, y: 0, width: 1920, height: 1040 }, dpi: 96, scale: 1 },
];
const VIRTUAL_SCREEN = { x: -1920, y: 0, width: 4480, height: 1440 };

// Smallest valid PNG (1x1, transparent).
const PNG_1X1 = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==',
  'base64',
);

function createMockBackend({ interactive = true } = {}) {
  const calls = [];
  let cursor = { x: 100, y: 100 };
  let windows = [
    { hwnd: 1001, pid: 4242, process: 'chrome', title: 'Extensions - Google Chrome', class: 'Chrome_WidgetWin_1',
      rect: { x: 0, y: 0, width: 1600, height: 1000 }, minimized: false, maximized: false, foreground: true },
    { hwnd: 1002, pid: 777, process: 'notepad', title: 'Untitled - Notepad', class: 'Notepad',
      rect: { x: -1800, y: 100, width: 800, height: 600 }, minimized: true, maximized: false, foreground: false },
  ];
  const record = (command, params) => calls.push({ command, params: { ...params } });

  function findWindow(target) {
    let matches;
    if (target.hwnd !== undefined) matches = windows.filter((w) => w.hwnd === target.hwnd);
    else if (target.pid !== undefined) matches = windows.filter((w) => w.pid === target.pid);
    else if (target.id !== undefined) {
      matches = windows.filter((w) => w.hwnd === target.id);
      if (matches.length === 0) matches = windows.filter((w) => w.pid === target.id);
    } else matches = windows.filter((w) => w.title.toLowerCase().includes(target.title.toLowerCase()));
    if (matches.length === 0) throw new Error('no visible window matches');
    return { window: matches[0], matches: matches.length };
  }

  // Mirrors the Windows helper's RunInput: SendInput when interactive,
  // otherwise PostMessage to the --window target.
  function input(p, sendResult) {
    if (!p.window) {
      if (p.input === 'post') throw new Error('--input post needs --window');
      return { ...sendResult(p), input: 'sendinput' };
    }
    const { window: w } = findWindow(p.window);
    const rel = p.x !== undefined ? { x: p.x, y: p.y } : { x: Math.floor(w.rect.width / 2), y: Math.floor(w.rect.height / 2) };
    const screen = { x: w.rect.x + rel.x, y: w.rect.y + rel.y };
    if (p.input === 'sendinput' || (p.input !== 'post' && interactive)) {
      return { ...sendResult({ ...p, ...(p.x !== undefined ? screen : {}) }), input: 'sendinput', window: w };
    }
    const res = { point: screen, client: { x: rel.x, y: rel.y - 80 }, targetHwnd: w.hwnd + 1, targetClass: 'Chrome_RenderWidgetHostHWND',
      input: 'postmessage', window: w };
    if (p.input !== 'post') res.fallbackReason = 'no interactive display (no foreground window or input desktop)';
    return res;
  }

  return {
    name: 'mock',
    calls,
    async screenshot(p) {
      record('screenshot', p);
      let area = VIRTUAL_SCREEN;
      let method = 'screen';
      let win;
      const warnings = [];
      if (p.window) {
        win = findWindow(p.window).window;
        area = win.rect;
        method = p.method === 'screen' ? 'screen' : 'printwindow';
        if (method === 'screen' && !interactive) throw new Error('screen capture failed: The handle is invalid');
      } else {
        if (p.monitor) {
          const m = MONITORS[p.monitor - 1];
          if (!m) throw new Error(`monitor ${p.monitor} not found (have ${MONITORS.length})`);
          area = m;
        }
        if (p.method === 'printwindow' || !interactive) {
          if (p.method === 'screen') throw new Error('screen capture failed: The handle is invalid');
          method = 'printwindow-composite';
          if (!interactive) warnings.push('screen capture unavailable (The handle is invalid); composited 1 windows with PrintWindow.');
        }
      }
      fs.writeFileSync(p.output, PNG_1X1);
      const res = {
        path: p.output, width: area.width, height: area.height,
        origin: { x: area.x, y: area.y }, monitor: p.monitor || null, method, blank: false, warnings,
        virtualScreen: VIRTUAL_SCREEN,
      };
      if (win) res.window = win;
      return res;
    },
    async click(p) {
      record('click', p);
      return { ...input(p, (q) => {
        if (q.x !== undefined) cursor = { x: q.x, y: q.y };
        return { cursor };
      }), button: p.button, count: p.count };
    },
    async move(p) {
      record('move', p);
      return input(p, (q) => {
        cursor = { x: q.x, y: q.y };
        return { cursor };
      });
    },
    async drag(p) {
      record('drag', p);
      cursor = { x: p.x2, y: p.y2 };
      return { cursor };
    },
    async scroll(p) {
      record('scroll', p);
      return { ...input(p, (q) => {
        if (q.x !== undefined) cursor = { x: q.x, y: q.y };
        return { cursor };
      }), dy: p.dy, dx: p.dx };
    },
    async type(p) {
      record('type', p);
      return { ...input(p, () => ({})), typed: p.text.length };
    },
    async key(p) {
      record('key', p);
      return { ...input(p, () => ({})), combos: p.combos.length };
    },
    async windows(p) {
      record('windows', p);
      return { windows };
    },
    async focus(p) {
      record('focus', p);
      const { window: target, matches: count } = findWindow(p);
      windows = windows.map((w) => ({ ...w, foreground: w.hwnd === target.hwnd,
        minimized: w.hwnd === target.hwnd ? false : w.minimized }));
      return { window: windows.find((w) => w.hwnd === target.hwnd), focused: true, matches: count };
    },
    async monitors() {
      record('monitors', {});
      return { monitors: MONITORS, virtualScreen: VIRTUAL_SCREEN };
    },
    async cursor() {
      record('cursor', {});
      return { cursor };
    },
    async doctor() {
      record('doctor', {});
      return {
        sessionId: 2, consoleSessionId: 1, sessionName: 'RDP-Tcp#0', connectState: 'active', remote: true,
        foregroundHwnd: interactive ? 1001 : 0, foregroundTitle: interactive ? 'Extensions - Google Chrome' : undefined,
        inputDesktop: 'Default', virtualScreen: VIRTUAL_SCREEN,
        capture: {
          screen: interactive ? { ok: true } : { ok: false, error: 'The handle is invalid' },
          printWindow: { ok: true, blank: false, hwnd: 1001, title: 'Extensions - Google Chrome' },
        },
        sendInput: { ok: true }, interactive, dpiAwareness: 'per-monitor-v2',
      };
    },
  };
}

module.exports = { createMockBackend, MONITORS, VIRTUAL_SCREEN };
