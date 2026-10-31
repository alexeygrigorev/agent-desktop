'use strict';

// Coordinate helpers. All screen coordinates are physical pixels in the
// virtual-screen space (the union of all monitors; may start at negative
// values when a monitor sits left of or above the primary one).

function parseInteger(value, what) {
  if (typeof value === 'number' && Number.isInteger(value)) return value;
  if (typeof value !== 'string' || !/^[-+]?\d+$/.test(value.trim())) {
    throw new Error(`${what} must be an integer, got "${value}"`);
  }
  return Number(value.trim());
}

// "X,Y" -> { x, y }
function parsePoint(value, what = 'point') {
  const parts = String(value).split(',');
  if (parts.length !== 2) throw new Error(`${what} must be "X,Y", got "${value}"`);
  return { x: parseInteger(parts[0], `${what} x`), y: parseInteger(parts[1], `${what} y`) };
}

// A pixel in a screenshot -> a screen coordinate. `origin` is the
// `origin` reported by `screenshot` (virtual-screen origin for --all,
// monitor top-left for --monitor N). Screenshots are never scaled, so
// this is a pure translation.
function imageToScreen(point, origin = { x: 0, y: 0 }) {
  return { x: point.x + origin.x, y: point.y + origin.y };
}

function screenToImage(point, origin = { x: 0, y: 0 }) {
  return { x: point.x - origin.x, y: point.y - origin.y };
}

// SendInput absolute coordinates with MOUSEEVENTF_VIRTUALDESK are
// normalised to 0..65535 across the virtual screen, and Windows maps a
// normalised value n back to pixel floor(n * width / 65536). Taking the
// ceiling here makes that round trip exact for every pixel. The Windows
// helper uses the same formula (AgentDesktop.cs ToAbsolute) and then pins
// the pixel with SetCursorPos.
function toAbsolute(point, vs) {
  return {
    x: normalise(point.x - vs.x, vs.width),
    y: normalise(point.y - vs.y, vs.height),
  };
}

function normalise(offset, size) {
  if (size <= 1) return 0;
  return clamp(Math.ceil((offset * 65536) / size), 0, 65535);
}

// How Windows maps a normalised coordinate back to a pixel.
function fromAbsolute(abs, vs) {
  return {
    x: vs.x + Math.floor((abs.x * vs.width) / 65536),
    y: vs.y + Math.floor((abs.y * vs.height) / 65536),
  };
}

function clamp(v, lo, hi) {
  return Math.min(hi, Math.max(lo, v));
}

function contains(rect, point) {
  return point.x >= rect.x && point.y >= rect.y &&
    point.x < rect.x + rect.width && point.y < rect.y + rect.height;
}

module.exports = {
  parseInteger,
  parsePoint,
  imageToScreen,
  screenToImage,
  toAbsolute,
  fromAbsolute,
  contains,
};
