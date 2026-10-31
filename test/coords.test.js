'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const c = require('../src/coords');

test('parseInteger accepts signed integers only', () => {
  assert.equal(c.parseInteger('42', 'X'), 42);
  assert.equal(c.parseInteger('-1920', 'X'), -1920);
  assert.equal(c.parseInteger(' +7 ', 'X'), 7);
  assert.throws(() => c.parseInteger('1.5', 'X'), /X must be an integer/);
  assert.throws(() => c.parseInteger('abc', 'Y'), /Y must be an integer/);
  assert.throws(() => c.parseInteger('', 'Y'), /integer/);
});

test('parsePoint', () => {
  assert.deepEqual(c.parsePoint('-1920,0'), { x: -1920, y: 0 });
  assert.throws(() => c.parsePoint('1,2,3', '--offset'), /--offset must be "X,Y"/);
  assert.throws(() => c.parsePoint('1'), /"X,Y"/);
});

test('image pixel <-> screen point is a translation by origin', () => {
  const origin = { x: -1920, y: -200 };
  const screen = c.imageToScreen({ x: 2000, y: 300 }, origin);
  assert.deepEqual(screen, { x: 80, y: 100 });
  assert.deepEqual(c.screenToImage(screen, origin), { x: 2000, y: 300 });
  assert.deepEqual(c.imageToScreen({ x: 5, y: 6 }), { x: 5, y: 6 });
});

test('SendInput normalisation round-trips every pixel exactly', () => {
  const layouts = [
    { x: 0, y: 0, width: 1920, height: 1080 },
    { x: -1920, y: 0, width: 4480, height: 1440 },
    { x: -2560, y: -400, width: 6400, height: 2160 },
    { x: 0, y: 0, width: 1366, height: 768 },
    { x: 0, y: 0, width: 7680, height: 4320 },
  ];
  for (const vs of layouts) {
    for (let x = vs.x; x < vs.x + vs.width; x++) {
      const y = vs.y + ((x - vs.x) % vs.height);
      const back = c.fromAbsolute(c.toAbsolute({ x, y }, vs), vs);
      if (back.x !== x || back.y !== y) {
        assert.fail(`(${x},${y}) -> (${back.x},${back.y}) for ${JSON.stringify(vs)}`);
      }
    }
  }
});

test('normalised coordinates stay in 0..65535', () => {
  const vs = { x: -1920, y: 0, width: 4480, height: 1440 };
  assert.deepEqual(c.toAbsolute({ x: -1920, y: 0 }, vs), { x: 0, y: 0 });
  const br = c.toAbsolute({ x: 2559, y: 1439 }, vs);
  assert.ok(br.x <= 65535 && br.y <= 65535);
  assert.deepEqual(c.toAbsolute({ x: 99999, y: -99999 }, vs), { x: 65535, y: 0 });
});

test('contains', () => {
  const r = { x: -1920, y: 0, width: 1920, height: 1080 };
  assert.equal(c.contains(r, { x: -1, y: 0 }), true);
  assert.equal(c.contains(r, { x: 0, y: 0 }), false);
});
