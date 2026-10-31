'use strict';

const BACKENDS = {
  windows: () => require('./windows').createWindowsBackend(),
  mock: () => require('./mock').createMockBackend(),
};

// --backend flag > AGENT_DESKTOP_BACKEND env > platform default.
function selectBackendName(requested, env = process.env, platform = process.platform) {
  const name = requested || env.AGENT_DESKTOP_BACKEND || (platform === 'win32' ? 'windows' : null);
  if (!name) {
    throw new Error(
      `no desktop backend for platform "${platform}" (supported: Windows). ` +
        'Use --backend mock for a dry run.',
    );
  }
  if (!BACKENDS[name]) {
    throw new Error(`unknown backend "${name}" (available: ${Object.keys(BACKENDS).join(', ')})`);
  }
  return name;
}

function createBackend(name) {
  return BACKENDS[name]();
}

module.exports = { selectBackendName, createBackend, BACKENDS };
