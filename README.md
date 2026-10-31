# agent-desktop

OS-level desktop control for AI agents and scripts: full-screen screenshots,
mouse clicks, moves and drags, keyboard input and hotkeys, and listing and
focusing windows. It covers what browser automation cannot reach:
`chrome://` pages, native dialogs, other apps.

- One Node.js CLI, `bin/agent-desktop.js`. Node >= 22, no dependencies,
  no `npm install`.
- Windows backend: one Windows PowerShell 5.1 helper that calls `user32` and
  `gdi32` through `Add-Type` P/Invoke. It needs no admin rights, no UAC
  prompt and no installer.
- DPI-aware (per-monitor v2) and multi-monitor. A pixel in a screenshot maps
  to a click with no scaling.
- Every command accepts `--json` and prints a single JSON object for agents
  to read.

The backend is pluggable (`src/backends/`). Windows is the only real one so
far. A `mock` backend handles tests and dry runs on any OS.

## Install

```powershell
git clone https://github.com/alexeygrigorev/agent-desktop.git
cd agent-desktop
node bin/agent-desktop.js monitors
```

That's all you need. To get a shorter command, pick one of these (neither
needs admin):

```powershell
# Option A: npm link (puts agent-desktop on PATH via %APPDATA%\npm)
npm link
agent-desktop monitors

# Option B: a PowerShell function in your profile
Add-Content $PROFILE 'function agent-desktop { node "C:\path\to\agent-desktop\bin\agent-desktop.js" @args }'
. $PROFILE
agent-desktop monitors
```

The first command compiles the C# helper, which takes a second or two. The
compiled helper is cached in `%LOCALAPPDATA%\agent-desktop\cache`, and each
later command runs as one short PowerShell call.

## Coordinates

All coordinates are **physical pixels in the virtual screen**, the union of
all monitors. The helper turns on per-monitor-v2 DPI awareness before doing
anything else, so a 150% scale setting does not shift anything.

The virtual screen can start at negative coordinates when a monitor sits to
the left of or above the primary one. `monitors` shows the layout:

```text
> agent-desktop monitors
1* \\.\DISPLAY1   2560x1440 at (0,0)      dpi=144 scale=1.5
2  \\.\DISPLAY2   1920x1080 at (-1920,0)  dpi=96 scale=1
virtual screen 4480x1440 at (-1920,0)
```

Every screenshot reports its `origin`, the screen point of image pixel
(0,0):

| Screenshot | `origin` |
| --- | --- |
| default / `--all` | virtual-screen top-left, e.g. `(-1920,0)` |
| `--monitor N` | that monitor's top-left |

Screenshots are never scaled, so image pixel `(px,py)` is screen point
`(px + origin.x, py + origin.y)`. Pass the origin to `--offset` and click
using image pixels directly:

```powershell
agent-desktop click 2100 640 --offset -1920,0   # clicks screen point (180,640)
```

When `origin` is `(0,0)`, which is the case for a single monitor or with
`--monitor 1` on the primary, image pixels and screen coordinates are the
same thing.

## Commands

Global options: `--json` (machine-readable output), `--backend windows|mock`
(defaults to `windows` on Windows; the `AGENT_DESKTOP_BACKEND` env var also
works), `--help`, `--version`. Options can come before or after the command.
Exit code 0 means success, 1 means a runtime error, 2 means a usage error.
With `--json`, an error prints `{"ok":false,"error":"..."}`.

### screenshot

```text
agent-desktop screenshot [--output PATH] [--monitor N | --all]
```

Captures the whole virtual screen (the default, same as `--all`) or monitor
`N` (1 = primary, then left to right) as a PNG. Missing parent directories
are created. Without `--output`, the file goes to
`%TEMP%\agent-desktop\screenshot-<timestamp>.png`.

```powershell
agent-desktop screenshot --output C:\work\shots\desk.png
agent-desktop screenshot --monitor 2 -o shots\right.png
agent-desktop --json screenshot -o shots\all.png
# {"ok":true,"command":"screenshot","path":"C:\\work\\shots\\all.png","width":4480,"height":1440,
#  "origin":{"x":-1920,"y":0},"monitor":null,"virtualScreen":{...},"dpiAwareness":"per-monitor-v2"}
```

### click

```text
agent-desktop click [X Y] [--button left|right|middle] [--count N] [--offset X,Y]
```

Moves to `X Y` and clicks. Without coordinates it clicks wherever the cursor
is. `--count 2` gives a double-click.

```powershell
agent-desktop click 640 400
agent-desktop click 640 400 --button right
agent-desktop click 640 400 --count 2
agent-desktop click 2100 640 --offset -1920,0     # image pixel + screenshot origin
```

### move

```powershell
agent-desktop move 100 200
agent-desktop move -1500 300          # a monitor left of the primary
```

### drag

```text
agent-desktop drag X1 Y1 X2 Y2 [--button B] [--steps N] [--duration MS] [--offset X,Y]
```

Presses at `X1 Y1`, moves to `X2 Y2` in `--steps` steps (default 10) over
`--duration` ms (default 200), then releases.

```powershell
agent-desktop drag 300 300 900 300
agent-desktop drag 50 50 400 400 --steps 30 --duration 600
```

### scroll

```text
agent-desktop scroll DY [--dx DX] [--at X,Y]
```

Scrolls by wheel notches. Positive `DY` scrolls down, negative scrolls up,
and `--dx` scrolls horizontally (positive = right). `--at` moves the cursor
there first, because Windows scrolls the window under the cursor.

```powershell
agent-desktop scroll 5
agent-desktop scroll -3 --at 800,600
```

### type

```text
agent-desktop type TEXT [--delay MS] [--stdin]
```

Types text as Unicode keystrokes into the focused window, so any language
and symbols work. `\n` becomes Enter and a tab character becomes Tab.
Separate positional words are joined with spaces. Put `--` before text that
starts with `-`.

```powershell
agent-desktop type "hello world"
agent-desktop type -- "-not an option"
Get-Content note.txt -Raw | agent-desktop type --stdin
```

### key

```text
agent-desktop key COMBO [COMBO...] [--delay MS]
```

Presses one or more combos in order. Modifiers are pressed first and released
in reverse order. Names are case-insensitive.

- Modifiers: `ctrl`, `shift`, `alt`, `win` (also `control`, `meta`, `cmd`).
- Keys: `a`-`z`, `0`-`9`, `f1`-`f24`, `enter`, `esc`, `tab`, `space`,
  `backspace`, `delete`, `insert`, `home`, `end`, `pageup`, `pagedown`,
  `up`, `down`, `left`, `right`, `capslock`, `printscreen`, `apps`,
  `numpad0`-`numpad9`, `volumeup`/`volumedown`/`volumemute`, media keys,
  and the punctuation keys `- = , . / ; ' [ ] \` `` ` `` (`plus` or a
  trailing `++` for `+`).

```powershell
agent-desktop key enter
agent-desktop key ctrl+shift+t
agent-desktop key alt+f4
agent-desktop key ctrl+l ctrl+c            # two combos in a row
agent-desktop key win+r
```

### windows

```text
agent-desktop windows [--filter TEXT] [--all]
```

Lists visible top-level windows in Z-order, topmost first, with each
window's handle, PID, process name, title, class, rectangle and flags.
`--filter` matches the title or the process name (case-insensitive). `--all`
also includes hidden and untitled windows.

```powershell
agent-desktop windows
agent-desktop windows --filter chrome
agent-desktop --json windows --filter notepad
# {"ok":true,"command":"windows","windows":[{"hwnd":132456,"pid":4242,"process":"notepad",
#   "title":"Untitled - Notepad","class":"Notepad","rect":{"x":100,"y":100,"width":800,"height":600},
#   "minimized":false,"maximized":false,"foreground":true}]}
```

### focus

```text
agent-desktop focus <TITLE-SUBSTRING|PID> [--title] [--hwnd]
```

Restores the window if it is minimized and brings it to the foreground. An
all-digit target is a PID, anything else is a case-insensitive title
substring. `--title` treats digits as a title, and `--hwnd` takes a window
handle from `windows`. When several windows match, the topmost one wins, and
the output reports how many matched. Focusing works from a background
process: the helper uses `AttachThreadInput` and falls back to an Alt-key
tap to get past Windows' foreground lock.

```powershell
agent-desktop focus "Google Chrome"
agent-desktop focus 4242
agent-desktop focus 132456 --hwnd
```

### monitors, cursor, wait

```powershell
agent-desktop monitors          # layout, DPI and scale per monitor, virtual-screen bounds
agent-desktop cursor            # current cursor position
agent-desktop wait 1500         # sleep 1.5 s (no backend call; handy in scripts)
```

## Recipe: reload an unpacked Chrome extension on chrome://extensions

A browser extension can't automate `chrome://` pages, but the desktop can.
This loop reloads an unpacked extension with screenshot, find, click:

```powershell
# 1. Bring the right Chrome window forward (use the hwnd when several profiles are open).
agent-desktop windows --filter chrome
agent-desktop focus 132456 --hwnd

# 2. Open the extensions page in a new tab.
agent-desktop key ctrl+t
agent-desktop type "chrome://extensions"
agent-desktop key enter
agent-desktop wait 1500

# 3. Screenshot it and note the origin.
agent-desktop --json screenshot -o shots\extensions.png
#   -> "origin":{"x":0,"y":0}

# 4. Find the extension's card in the PNG and the circular "Reload" arrow on
#    it (it appears for unpacked extensions; turn on "Developer mode" at the
#    top right if it is missing). Read that pixel off the image, e.g. (1212, 655).

# 5. Click it (add --offset when origin is not 0,0) and check the result.
agent-desktop click 1212 655 --offset 0,0
agent-desktop wait 1000
agent-desktop screenshot -o shots\after-reload.png
```

Check the after screenshot before going further: a wrong click on this page
can remove or disable an extension.

## Troubleshooting

- **`SendInput was blocked`**: the workstation is locked, or the target
  window runs elevated (as admin). Windows does not let a normal-user process
  send input to elevated windows (UIPI). Run the target app without
  elevation.
- **`Windows refused to bring it to the foreground`**: something else is
  holding focus, for example a full-screen app. Try again, or click the
  window's title bar with `click`.
- **Black or empty screenshot**: there is no interactive desktop. Typical
  causes are a disconnected RDP session or running from a service.
  agent-desktop must run in the logged-in user's session.
- **Slow first call**: the helper is compiling and caching itself. If
  `%LOCALAPPDATA%` is read-only, it compiles in memory on every call
  instead.
- **Execution policy**: the helper runs with `-ExecutionPolicy Bypass` for
  its own process only, which requires no admin and changes nothing
  system-wide.

## How it works

```text
bin/agent-desktop.js        CLI entry
src/cli.js                  argument parsing, validation, output (text / --json)
src/keys.js                 key-combo parser -> virtual-key codes
src/coords.js               image <-> screen mapping, SendInput normalisation
src/backends/index.js       backend selection
src/backends/mock.js        in-memory backend for tests and dry runs
src/backends/windows.js     spawns Windows PowerShell 5.1 per command
src/backends/windows/helper.ps1     compiles + caches the C# helper, runs one request
src/backends/windows/AgentDesktop.cs  P/Invoke: SetProcessDpiAwarenessContext,
                                      SendInput, EnumWindows, SetForegroundWindow,
                                      EnumDisplayMonitors, CopyFromScreen
```

Requests and responses travel between Node and PowerShell as base64 UTF-8
JSON, so window titles and typed text in any language come through intact.
Mouse moves use `SendInput` with absolute coordinates normalised across the
virtual desktop (`MOUSEEVENTF_VIRTUALDESK`). `SetCursorPos` then pins the
exact pixel.

## Development

```bash
npm test                 # unit tests (mock backend) + Windows smoke tests (skipped off Windows)
node bin/agent-desktop.js --backend mock --json monitors   # dry run anywhere
```

CI runs the unit tests on Ubuntu (Node 22 and 24) and the full suite plus a
CLI smoke run (screenshot, window list, cursor) on `windows-latest`.

## License

MIT
