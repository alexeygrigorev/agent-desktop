'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { diagnose } = require('../src/doctor');

const healthy = {
  sessionId: 1, consoleSessionId: 1, sessionName: 'Console', connectState: 'active', remote: false,
  foregroundHwnd: 1234, inputDesktop: 'Default',
  capture: { screen: { ok: true }, printWindow: { ok: true } }, sendInput: { ok: true },
};

test('healthy console session', () => {
  const d = diagnose(healthy);
  assert.equal(d.status, 'ok');
  assert.deepEqual(d.problems, []);
  assert.match(d.hints[0], /BitBlt.*SendInput/);
});

test('minimized RDP client (live case: no foreground, BitBlt "handle is invalid")', () => {
  const d = diagnose({
    ...healthy, sessionId: 2, sessionName: 'RDP-Tcp#0', remote: true, foregroundHwnd: 0,
    capture: { screen: { ok: false, error: 'The handle is invalid' }, printWindow: { ok: true } },
  });
  assert.equal(d.status, 'degraded');
  assert.ok(d.problems.some((p) => /not rendering/.test(p)));
  assert.ok(d.problems.some((p) => /BitBlt\) fails: The handle is invalid/.test(p)));
  assert.ok(d.hints.some((h) => /RemoteDesktop_SuppressWhenMinimized \/t REG_DWORD \/d 2/.test(h)));
  assert.ok(d.hints.some((h) => /--window/.test(h)));
  assert.ok(d.hints.some((h) => /Chrome .*ignore posted input.*interactive display/.test(h)));
});

test('disconnected session', () => {
  const d = diagnose({ ...healthy, remote: true, connectState: 'disconnected', foregroundHwnd: 0,
    capture: { screen: { ok: false }, printWindow: { ok: false } } });
  assert.equal(d.status, 'unusable');
  assert.ok(d.hints.some((h) => /Reconnect/.test(h)));
});

test('locked workstation / secure desktop', () => {
  const locked = diagnose({ ...healthy, inputDesktop: null, inputDesktopError: 'OpenInputDesktop failed (error 5)' });
  assert.notEqual(locked.status, 'ok');
  assert.ok(locked.problems.some((p) => /not accessible \(OpenInputDesktop failed/.test(p)));
  const uac = diagnose({ ...healthy, inputDesktop: 'Winlogon' });
  assert.ok(uac.problems.some((p) => /"Winlogon"/.test(p)));
});

test('blocked SendInput is reported', () => {
  const d = diagnose({ ...healthy, sendInput: { ok: false, error: 'blocked' } });
  assert.equal(d.status, 'degraded');
  assert.ok(d.problems.includes('SendInput is blocked'));
});
