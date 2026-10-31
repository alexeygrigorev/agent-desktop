'use strict';

// Turns the raw session facts from the backend's `doctor` call into a
// status and human hints. Pure, so it is unit-tested with canned facts.

const SUPPRESS_HINT =
  'On the machine running the Remote Desktop client, stop it from pausing the session while minimized: ' +
  'reg add "HKCU\\Software\\Microsoft\\Terminal Server Client" /v RemoteDesktop_SuppressWhenMinimized /t REG_DWORD /d 2 /f ' +
  '(then reconnect), or keep the RDP window restored (not minimized).';

const WINDOW_HINT =
  'Until then, use window-level fallbacks: `screenshot --window <title|pid|hwnd>` (PrintWindow) and ' +
  '`click/move/scroll/type/key --window <title|pid|hwnd>` (input posted to the window; coordinates are pixels of the --window screenshot).';

function diagnose(facts) {
  const problems = [];
  const hints = [];
  const screenOk = Boolean(facts.capture && facts.capture.screen && facts.capture.screen.ok);
  const printOk = Boolean(facts.capture && facts.capture.printWindow && facts.capture.printWindow.ok);
  const sendOk = Boolean(facts.sendInput && facts.sendInput.ok);
  const noForeground = !facts.foregroundHwnd;
  const desktop = facts.inputDesktop;
  const disconnected = facts.connectState === 'disconnected';

  if (disconnected) {
    problems.push('the session is disconnected');
    hints.push('Reconnect the Remote Desktop client. (Moving the session to the console with `tscon <id> /dest:console` needs admin rights.)');
  } else if (facts.remote && (noForeground || !screenOk)) {
    problems.push('Remote Desktop session is not rendering (RDP client minimized or not drawing)');
    hints.push(SUPPRESS_HINT);
  }
  if (desktop === null || desktop === undefined) {
    problems.push(`input desktop is not accessible${facts.inputDesktopError ? ` (${facts.inputDesktopError})` : ''}`);
    hints.push('The session may be locked or showing a secure desktop (UAC, lock screen): unlock it.');
  } else if (desktop.toLowerCase() !== 'default') {
    problems.push(`input desktop is "${desktop}", not "Default"`);
    hints.push('A lock screen or UAC prompt has the input desktop: unlock the session or answer the prompt.');
  }
  if (!facts.remote && noForeground && !disconnected) {
    problems.push('no foreground window');
    hints.push('Click any window on the desktop, or run `agent-desktop focus <title>`.');
  }
  if (!screenOk) problems.push(`screen capture (BitBlt) fails${facts.capture && facts.capture.screen && facts.capture.screen.error ? `: ${facts.capture.screen.error}` : ''}`);
  if (!sendOk) problems.push('SendInput is blocked');
  if (!printOk) problems.push('PrintWindow capture is not working');

  let status;
  if (screenOk && sendOk && !noForeground && desktop && desktop.toLowerCase() === 'default') {
    status = 'ok';
    hints.push('Interactive desktop: screenshots use BitBlt and input uses SendInput.');
  } else if (printOk) {
    status = 'degraded';
    hints.push(WINDOW_HINT);
  } else {
    status = 'unusable';
  }
  return { status, problems, hints };
}

module.exports = { diagnose, SUPPRESS_HINT };
