'use strict';

// Key-combo parsing: "ctrl+shift+t" -> [{ name, vk, extended }, ...].
// Virtual-key codes are the Windows VK_* values; other backends can map
// from the canonical `name`.

const MODIFIERS = {
  ctrl: { vk: 0x11 },
  shift: { vk: 0x10 },
  alt: { vk: 0x12 },
  win: { vk: 0x5b, extended: true },
};

const MODIFIER_ALIASES = {
  ctrl: 'ctrl', control: 'ctrl', ctl: 'ctrl',
  shift: 'shift',
  alt: 'alt', option: 'alt', menu: 'alt',
  win: 'win', windows: 'win', meta: 'win', super: 'win', cmd: 'win', command: 'win',
};

const NAMED_KEYS = {
  enter: { vk: 0x0d }, return: { vk: 0x0d },
  esc: { vk: 0x1b }, escape: { vk: 0x1b },
  tab: { vk: 0x09 },
  space: { vk: 0x20 },
  backspace: { vk: 0x08 }, bksp: { vk: 0x08 },
  delete: { vk: 0x2e, extended: true }, del: { vk: 0x2e, extended: true },
  insert: { vk: 0x2d, extended: true }, ins: { vk: 0x2d, extended: true },
  home: { vk: 0x24, extended: true },
  end: { vk: 0x23, extended: true },
  pageup: { vk: 0x21, extended: true }, pgup: { vk: 0x21, extended: true },
  pagedown: { vk: 0x22, extended: true }, pgdn: { vk: 0x22, extended: true },
  left: { vk: 0x25, extended: true },
  up: { vk: 0x26, extended: true },
  right: { vk: 0x27, extended: true },
  down: { vk: 0x28, extended: true },
  capslock: { vk: 0x14 },
  numlock: { vk: 0x90, extended: true },
  scrolllock: { vk: 0x91 },
  printscreen: { vk: 0x2c, extended: true }, prtsc: { vk: 0x2c, extended: true },
  pause: { vk: 0x13 },
  apps: { vk: 0x5d, extended: true }, contextmenu: { vk: 0x5d, extended: true },
  volumemute: { vk: 0xad, extended: true },
  volumedown: { vk: 0xae, extended: true },
  volumeup: { vk: 0xaf, extended: true },
  medianext: { vk: 0xb0, extended: true },
  mediaprev: { vk: 0xb1, extended: true },
  mediastop: { vk: 0xb2, extended: true },
  mediaplaypause: { vk: 0xb3, extended: true },
  plus: { vk: 0xbb }, equal: { vk: 0xbb }, '=': { vk: 0xbb },
  minus: { vk: 0xbd }, '-': { vk: 0xbd },
  comma: { vk: 0xbc }, ',': { vk: 0xbc },
  period: { vk: 0xbe }, '.': { vk: 0xbe },
  slash: { vk: 0xbf }, '/': { vk: 0xbf },
  semicolon: { vk: 0xba }, ';': { vk: 0xba },
  backquote: { vk: 0xc0 }, backtick: { vk: 0xc0 }, '`': { vk: 0xc0 },
  bracketleft: { vk: 0xdb }, '[': { vk: 0xdb },
  backslash: { vk: 0xdc }, '\\': { vk: 0xdc },
  bracketright: { vk: 0xdd }, ']': { vk: 0xdd },
  quote: { vk: 0xde }, "'": { vk: 0xde },
  multiply: { vk: 0x6a }, add: { vk: 0x6b }, subtract: { vk: 0x6d },
  decimal: { vk: 0x6e }, divide: { vk: 0x6f, extended: true },
};

function lookupKey(token) {
  const t = token.toLowerCase();
  if (MODIFIER_ALIASES[t]) {
    const name = MODIFIER_ALIASES[t];
    return { name, ...MODIFIERS[name], modifier: true };
  }
  if (NAMED_KEYS[t]) return { name: t, ...NAMED_KEYS[t] };
  if (/^[a-z]$/.test(t)) return { name: t, vk: t.toUpperCase().charCodeAt(0) };
  if (/^[0-9]$/.test(t)) return { name: t, vk: t.charCodeAt(0) };
  let m = /^f([1-9]|1[0-9]|2[0-4])$/.exec(t);
  if (m) return { name: t, vk: 0x6f + Number(m[1]) };
  m = /^(?:numpad|num)([0-9])$/.exec(t);
  if (m) return { name: `numpad${m[1]}`, vk: 0x60 + Number(m[1]) };
  return null;
}

// Parse one combo such as "ctrl+shift+t", "alt+f4", "enter" or "ctrl++".
// Returns keys in press order (modifiers first); release is the reverse.
function parseCombo(combo) {
  if (typeof combo !== 'string' || combo.length === 0) {
    throw new Error('empty key combo');
  }
  const tokens = [];
  // A literal "+" key is written as "plus" or as a trailing "+" ("ctrl++").
  let rest = combo;
  if (rest === '+') rest = 'plus';
  else if (rest.endsWith('++')) rest = rest.slice(0, -1) + 'plus';
  for (const part of rest.split('+')) {
    if (part === '') throw new Error(`invalid key combo "${combo}"`);
    tokens.push(part);
  }
  const keys = tokens.map((tok) => {
    const k = lookupKey(tok);
    if (!k) throw new Error(`unknown key "${tok}" in combo "${combo}"`);
    return k;
  });
  const seen = new Set();
  for (const k of keys) {
    if (seen.has(k.vk)) throw new Error(`duplicate key "${k.name}" in combo "${combo}"`);
    seen.add(k.vk);
  }
  const mods = keys.filter((k) => k.modifier);
  const main = keys.filter((k) => !k.modifier);
  if (main.length > 1) {
    throw new Error(`combo "${combo}" has more than one non-modifier key`);
  }
  return [...mods, ...main].map(({ name, vk, extended }) => ({
    name,
    vk,
    extended: Boolean(extended),
  }));
}

module.exports = { parseCombo, lookupKey };
