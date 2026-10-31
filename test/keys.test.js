'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { parseCombo } = require('../src/keys');

const vks = (combo) => parseCombo(combo).map((k) => k.vk);

test('single keys', () => {
  assert.deepEqual(vks('enter'), [0x0d]);
  assert.deepEqual(vks('Return'), [0x0d]);
  assert.deepEqual(vks('esc'), [0x1b]);
  assert.deepEqual(vks('a'), [0x41]);
  assert.deepEqual(vks('Z'), [0x5a]);
  assert.deepEqual(vks('7'), [0x37]);
  assert.deepEqual(vks('f5'), [0x74]);
  assert.deepEqual(vks('f24'), [0x87]);
  assert.deepEqual(vks('numpad3'), [0x63]);
});

test('modifier combos press modifiers first', () => {
  assert.deepEqual(vks('ctrl+shift+t'), [0x11, 0x10, 0x54]);
  assert.deepEqual(vks('alt+f4'), [0x12, 0x73]);
  assert.deepEqual(vks('t+ctrl'), [0x11, 0x54]);
  assert.deepEqual(vks('Control+Alt+Delete'), [0x11, 0x12, 0x2e]);
  assert.deepEqual(vks('win+r'), [0x5b, 0x52]);
  assert.deepEqual(vks('cmd+l'), [0x5b, 0x4c]);
});

test('modifier on its own is a valid combo', () => {
  assert.deepEqual(vks('shift'), [0x10]);
  assert.deepEqual(vks('ctrl+shift'), [0x11, 0x10]);
});

test('extended flag for navigation keys', () => {
  const [k] = parseCombo('left');
  assert.equal(k.extended, true);
  assert.equal(parseCombo('a')[0].extended, false);
  assert.deepEqual(parseCombo('ctrl+end').map((k2) => k2.extended), [false, true]);
});

test('punctuation and the plus key', () => {
  assert.deepEqual(vks('ctrl+plus'), [0x11, 0xbb]);
  assert.deepEqual(vks('ctrl++'), [0x11, 0xbb]);
  assert.deepEqual(vks('+'), [0xbb]);
  assert.deepEqual(vks('ctrl+-'), [0x11, 0xbd]);
  assert.deepEqual(vks('ctrl+/'), [0x11, 0xbf]);
  assert.deepEqual(vks('ctrl+['), [0x11, 0xdb]);
});

test('names are canonical', () => {
  assert.deepEqual(parseCombo('Control+PgDn').map((k) => k.name), ['ctrl', 'pgdn']);
});

test('invalid combos throw', () => {
  assert.throws(() => parseCombo(''), /empty/);
  assert.throws(() => parseCombo('ctrl+'), /invalid|unknown/);
  assert.throws(() => parseCombo('ctrl+nosuchkey'), /unknown key "nosuchkey"/);
  assert.throws(() => parseCombo('ctrl+a+b'), /more than one/);
  assert.throws(() => parseCombo('ctrl+control+a'), /duplicate/);
  assert.throws(() => parseCombo('f25'), /unknown key/);
});
