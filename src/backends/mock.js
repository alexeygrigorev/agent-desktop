'use strict';

// In-memory backend for tests and dry runs (`--backend mock`). Records every
// call and simulates a two-monitor desktop with a secondary monitor left of
// the primary, so the virtual screen starts at a negative x.

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

function createMockBackend() {
  const calls = [];
  let cursor = { x: 100, y: 100 };
  let windows = [
    { hwnd: 1001, pid: 4242, process: 'chrome', title: 'Extensions - Google Chrome', class: 'Chrome_WidgetWin_1',
      rect: { x: 0, y: 0, width: 1600, height: 1000 }, minimized: false, maximized: false, foreground: true },
    { hwnd: 1002, pid: 777, process: 'notepad', title: 'Untitled - Notepad', class: 'Notepad',
      rect: { x: -1800, y: 100, width: 800, height: 600 }, minimized: true, maximized: false, foreground: false },
  ];
  const record = (command, params) => calls.push({ command, params: { ...params } });

  return {
    name: 'mock',
    calls,
    async screenshot(p) {
      record('screenshot', p);
      let area = VIRTUAL_SCREEN;
      if (p.monitor) {
        const m = MONITORS[p.monitor - 1];
        if (!m) throw new Error(`monitor ${p.monitor} not found (have ${MONITORS.length})`);
        area = m;
      }
      fs.writeFileSync(p.output, PNG_1X1);
      return {
        path: p.output, width: area.width, height: area.height,
        origin: { x: area.x, y: area.y }, monitor: p.monitor || null, virtualScreen: VIRTUAL_SCREEN,
      };
    },
    async click(p) {
      record('click', p);
      if (p.x !== undefined) cursor = { x: p.x, y: p.y };
      return { cursor, button: p.button, count: p.count };
    },
    async move(p) {
      record('move', p);
      cursor = { x: p.x, y: p.y };
      return { cursor };
    },
    async drag(p) {
      record('drag', p);
      cursor = { x: p.x2, y: p.y2 };
      return { cursor };
    },
    async scroll(p) {
      record('scroll', p);
      if (p.x !== undefined) cursor = { x: p.x, y: p.y };
      return { cursor, dy: p.dy, dx: p.dx };
    },
    async type(p) {
      record('type', p);
      return { typed: p.text.length };
    },
    async key(p) {
      record('key', p);
      return { combos: p.combos.length };
    },
    async windows(p) {
      record('windows', p);
      return { windows };
    },
    async focus(p) {
      record('focus', p);
      let matches;
      if (p.hwnd !== undefined) matches = windows.filter((w) => w.hwnd === p.hwnd);
      else if (p.pid !== undefined) matches = windows.filter((w) => w.pid === p.pid);
      else matches = windows.filter((w) => w.title.toLowerCase().includes(p.title.toLowerCase()));
      if (matches.length === 0) throw new Error('no visible window matches');
      const target = matches[0];
      windows = windows.map((w) => ({ ...w, foreground: w.hwnd === target.hwnd,
        minimized: w.hwnd === target.hwnd ? false : w.minimized }));
      return { window: windows.find((w) => w.hwnd === target.hwnd), focused: true, matches: matches.length };
    },
    async monitors() {
      record('monitors', {});
      return { monitors: MONITORS, virtualScreen: VIRTUAL_SCREEN };
    },
    async cursor() {
      record('cursor', {});
      return { cursor };
    },
  };
}

module.exports = { createMockBackend, MONITORS, VIRTUAL_SCREEN };
