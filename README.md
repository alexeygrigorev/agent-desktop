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
- `agent-desktop doctor` tells you whether the session can be controlled.
  The common failure is a minimized Remote Desktop client. When nothing is
  being displayed, screenshots fall back to PrintWindow, and input can be
  posted to a window with `--window`. Posted input works for classic Win32
  apps but **not for Chrome**, which needs an interactive display. See
  [Window targets](#window-targets-and-no-display-sessions).

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
agent-desktop screenshot [--output PATH] [--monitor N | --all | --window W]
                         [--method auto|screen|printwindow]
```

Captures the whole virtual screen (the default, same as `--all`), monitor
`N` (1 = primary, then left to right), or a single window (`--window`, see
[Window targets](#window-targets-and-no-display-sessions)) as a PNG.
Missing parent directories are created. Without `--output`, the file goes to
`%TEMP%\agent-desktop\screenshot-<timestamp>.png`.

The JSON output has a `method` field:

| `method` | When |
| --- | --- |
| `screen` | Normal BitBlt of the screen. |
| `printwindow-composite` | BitBlt failed, e.g. with `The handle is invalid` when the RDP client is minimized. Every visible window is captured with `PrintWindow` and painted bottom-up onto a canvas the size of the requested area. Menus, tooltips and the taskbar may be missing, and `warnings` says so. |
| `printwindow` | `--window`: just that window, which works even if it is covered or not in the foreground. A minimized window has to be restored first. |

`--method screen` disables the fallback, and `--method printwindow` forces
it. If the image is a single flat colour, `blank` is `true` and a warning
suggests running `doctor`.

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
agent-desktop focus <TITLE-SUBSTRING|PID|HWND> [--title] [--hwnd]
```

Restores the window if it is minimized and brings it to the foreground.
A bare number is a PID if that process has a visible window, and otherwise
a window handle from `windows`. Anything else is a case-insensitive title
substring. The explicit forms `pid:N`, `hwnd:N` and `title:TEXT` also work.
`--title` treats digits as a title, and `--hwnd` forces a handle. When several windows match, the topmost one wins, and
the output reports how many matched. Focusing works from a background
process: the helper uses `AttachThreadInput` and falls back to an Alt-key
tap to get past Windows' foreground lock.

```powershell
agent-desktop focus "Google Chrome"
agent-desktop focus 4242
agent-desktop focus 132456 --hwnd
```

### Window targets and no-display sessions

```text
--window W     W = title substring | pid | hwnd   (also explicit hwnd:N, pid:N, title:TEXT;
               a bare number is tried as a pid first, then as an hwnd)
--input auto|sendinput|post
```

`click`, `move`, `scroll`, `type` and `key` accept `--window`. With it,
**coordinates are relative to the window**: they are pixels in
`screenshot --window W`, whose `origin` is the top-left corner of the
window's visible frame. The flow is the same as on the full screen:
screenshot the window, find the pixel, click that pixel.

The input path is chosen by `--input`:

- `auto` (default): use SendInput, as without `--window`. The point is
  converted to screen pixels, and for `type`/`key` the window is focused
  first. When there is **no interactive display**, the input is posted to
  the window instead. No interactive display means there is no foreground
  window or the input desktop cannot be opened, as with a minimized RDP
  client or a locked session. Input is also posted when SendInput is
  rejected. In those cases the output has `"input":"postmessage"` and a
  `fallbackReason`.
- `post`: always post window messages. `click`/`move`/`scroll` go to the
  deepest visible child window under the point, for example Chrome's
  `Chrome_RenderWidgetHostHWND`. They are sent as `WM_MOUSEMOVE`,
  `WM_LBUTTONDOWN`/`UP` (likewise for the other buttons, plus
  `DBLCLK` for `--count 2`) and `WM_MOUSEWHEEL`, with client coordinates.
  `type` sends `WM_CHAR`. `key` sends `WM_KEYDOWN`/`UP`, or
  `WM_SYSKEYDOWN`/`UP` when the combo includes Alt. Keys go to the window's
  focused child, or to the child under `--at X,Y` (window-relative).
- `sendinput`: never fall back.

**Posted input has limits:**

- **Chrome ignores it.** In a live test with a non-rendering RDP session,
  posting a click on Chrome's omnibox and then typing into
  `Chrome_WidgetWin_1` had no effect. Chrome, like other Chromium,
  Electron and GPU/DirectComposition apps, needs an interactive display and
  real input. For Chrome, fix the display (see [doctor](#doctor)) and use
  normal SendInput.
- **Classic Win32 apps** with standard controls (Notepad-style edit boxes,
  dialogs) usually accept posted clicks, text and keys.
- **Modifiers are not applied.** The app sees no real cursor, and posted
  modifiers do not change its keyboard state, so shortcuts like `ctrl+t`
  may arrive as a bare `t`. The output includes a warning when this applies.

`screenshot --window` (PrintWindow) is still useful without a display: it
shows what a window contains. Without `--window`, input in a no-display
session gets a warning, because SendInput may report success while doing
nothing.

```powershell
agent-desktop --json screenshot --window "Notepad" -o shots\notepad.png
#   -> "method":"printwindow","origin":{"x":120,"y":40},"window":{"hwnd":132456,...}
agent-desktop click 300 200 --window hwnd:132456           # pixel from notepad.png
agent-desktop type "hello" --window hwnd:132456
agent-desktop key enter --window hwnd:132456 --input post
```

### doctor

```text
agent-desktop doctor
```

Prints the session state: session id and name (`Console` or `RDP-Tcp#N`),
connection state, remote or console, the foreground window, the input
desktop, whether BitBlt screen capture, PrintWindow capture and SendInput
work, and DPI awareness. It ends with a `status` (`ok`, `degraded` or
`unusable`) and hints. `--json` returns the raw facts plus `status`,
`problems` and `hints`.

```text
> agent-desktop doctor
status: degraded
session: id=2 name=RDP-Tcp#0 state=active remote (RDP); console session id=1
foreground window: none (0)
input desktop: Default
screen capture (BitBlt): FAILED (The handle is invalid)
window capture (PrintWindow): ok tested on "Extensions - Google Chrome"
SendInput: ok
problems:
  - Remote Desktop session is not rendering (RDP client minimized or not drawing)
  - screen capture (BitBlt) fails: The handle is invalid
hints:
  - On the machine running the Remote Desktop client, stop it from pausing the session while minimized:
    reg add "HKCU\Software\Microsoft\Terminal Server Client" /v RemoteDesktop_SuppressWhenMinimized /t REG_DWORD /d 2 /f
    (then reconnect), or keep the RDP window restored (not minimized).
  - Chrome (and other Chromium/Electron apps) need an interactive display: fix the RDP client first.
    Until then, screenshot --window ... still captures windows; posted --window input only reaches classic Win32 apps.
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

**This recipe needs an interactive display.** If `doctor` does not say
`status: ok` (for example the RDP client is minimized), fix the display
first, because Chrome ignores posted `--window` input. The fix is to set
`RemoteDesktop_SuppressWhenMinimized=2` on the RDP client machine and
reconnect, or to keep the RDP window restored.

Re-check the target right before clicking. Something else, such as a
browser-automation bridge, can switch tabs or windows between your
screenshot and your click. Run `--json windows --filter chrome`, confirm the
target window has `"foreground": true`, take a fresh screenshot, and click
only if the button is still at the same pixel.

## Troubleshooting

Start with `agent-desktop doctor`.

- **`The handle is invalid` / `method: printwindow-composite` / no
  foreground window**: the session is not rendering. Usually the Remote
  Desktop client is minimized. Either set `RemoteDesktop_SuppressWhenMinimized=2`
  on the client machine (see `doctor`) and reconnect, or keep the RDP window
  restored. In the meantime, `--window` screenshots work, and `--window`
  input reaches classic Win32 apps but not Chrome.
- **`key` fails with `no key combos given`**: you are on 88b238d or older,
  which had a helper bug. Update.
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
src/doctor.js               session facts -> status, problems, hints
src/backends/index.js       backend selection
src/backends/mock.js        in-memory backend for tests and dry runs
src/backends/windows.js     spawns Windows PowerShell 5.1 per command
src/backends/windows/helper.ps1     compiles + caches the C# helper, runs one request
src/backends/windows/AgentDesktop.cs  P/Invoke: SetProcessDpiAwarenessContext,
                                      SendInput, EnumWindows, SetForegroundWindow,
                                      EnumDisplayMonitors, CopyFromScreen,
                                      PrintWindow, PostMessage, WTS session info
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
