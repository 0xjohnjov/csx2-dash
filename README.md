# CSX2 Dash: a SimHub plugin for the Cube Controls CSX2 (UGT) wheel

[![Build](https://github.com/0xjohnjov/csx2-dash/actions/workflows/build.yml/badge.svg)](https://github.com/0xjohnjov/csx2-dash/actions/workflows/build.yml)

CSX2 Dash lets **SimHub** drive the screen and shift lights of a **Cube Controls CSX2** steering wheel (the version with Ultimate Game Tech electronics, which shows up in Windows as "UGT Sim LCD and CONTROLS V2"). UGT Manager no longer needs to run while you race.

It draws **your existing UGT dash layouts** with the same backgrounds, fonts, positions and colours, fed by SimHub telemetry. It works in **any game SimHub supports**, not only the ones UGT Manager knew.

> UGT Manager is no longer maintained. This project exists so these wheels keep working. It is a community project and is **not affiliated with Cube Controls or Ultimate Game Tech**.

**New here? Go to [INSTALL.md](INSTALL.md).**

---

## Features

- Shows **your own** UGT Manager layouts (however many you have in `%APPDATA%\UltimateGameTech\layouts`), using the backgrounds and fonts on your wheel's SD card. Next/previous page cycles through them in order.
- Values are formatted with UGT's own rules, ported line for line (lap times, deltas, fuel, temperatures and so on) and checked by unit tests.
- Rev/shift LEDs use your UGT shift-light table. Optional: flash at the shift point, and flag LEDs (yellow/blue/red).
- Fills in several fields UGT showed as `NA` in some games: sector 4, oil/water temperature, car class, pit delta, leaderboards.
- SimHub actions you can map to wheel buttons: `CSX2Dash.NextPage`, `CSX2Dash.PreviousPage`, `CSX2Dash.CycleDriverInfo`, `CSX2Dash.NextDriverPage`, `CSX2Dash.ToggleShiftFlash`.
- Settings page in SimHub: status, page picker, screen and LED brightness, shift flash, flag LEDs, and a "Reload UGT layouts" button.
- Steps aside automatically while UGT Manager is running, so you can still use UGT to edit layouts.
- If the wheel ever stops responding, it writes a "flight recorder" log of the last 300 commands to `%APPDATA%\CSX2Dash\freeze-*.log`, to help with bug reports.

## What's in this repository

```
README.md, INSTALL.md
.github\workflows\build.yml builds, tests and publishes every release (see "Security and safety")
src\CSX2Dash\               the SimHub plugin (C#, builds with the compiler built into Windows)
src\Tests\                  unit tests for the ported UGT formatters
tools\CSX2Recovery\         optional: one-click recovery for a frozen wheel (runs as admin)
tools\diagnostics\          developer test tools (licence, page-change and live stress tests, raw probe)
docs\                       the reverse-engineered UGT wheel protocol and formatting rules
```

## Important things we learned about this wheel

These are all verified on real hardware. Details are in `docs\UGT-telemetry-protocol.md`, section 9.

1. **The wheel freezes 30–60 seconds after power-up unless a program "licenses" it.** UGT Manager did this silently, and CSX2 Dash does it too. So **SimHub must be running when the wheel is plugged in**, or you replug the wheel after starting SimHub. A frozen wheel has dead buttons and needs a USB unplug/replug.
2. **Only one program may talk to the wheel's screen at a time.** Two at once can lock it up. Close UGT Manager before racing. **Don't run FanaLEDs** (a Fanatec LED tool): it mistakes the CSX2 for Fanatec hardware, sends it LED commands and locks it up.
3. Page changes have to follow UGT's exact timing and sequence, or fast page flipping can freeze the wheel. The plugin does this, and it's covered by stress tests in `tools\diagnostics`.

## Security and safety

- **The plugin** runs inside SimHub as a normal user. It has **no network code**. It reads UGT Manager's files (read-only) and writes only to `%APPDATA%\CSX2Dash` (personal-best sectors, freeze logs). It talks to the wheel only through its USB HID command channel.
- **Wheel commands:** it uses the same commands UGT Manager uses during a race (draw text, show SD image, LEDs, brightness, load/unload font, the licence handshake). It **never** sends firmware-update, EEPROM-lock or SD-card write commands.
- **Admin rights:** only `install.cmd` (to copy the DLL into SimHub's folder) and the optional **CSX2 Recovery** tool (to restart USB devices) need them. The recovery tool starts system programs by full path only, refuses to write its log through file links, and only ever restarts a USB hub that the wheel is alone on.
- **Downloads are built in public, not on anyone's PC.** Every file on the [Releases page](https://github.com/0xjohnjov/csx2-dash/releases) (from v1.0.1 on) is compiled and tested by GitHub Actions from this repository's source. GitHub signs a build-provenance attestation for each one. Check any download with `gh attestation verify <file> --repo 0xjohnjov/csx2-dash`, or compare it against `SHA256SUMS.txt`. You can also build it yourself (see INSTALL.md); only the C# compiler built into Windows is needed.
- **File details:** the DLL and EXE carry product, version and author information (right-click → Properties → Details).
- `tools\diagnostics\probe.exe` can send any raw command to the wheel. It's for developers; don't use it unless you know what you're sending.

## Compatibility

Tested with: CSX2 firmware `UGT G4:CC01/01/20rC`, UGT Manager 1.6.819, SimHub 9.10.15, Windows 11, Assetto Corsa. Other games work through SimHub's normal telemetry. Other UGT-based wheels may work but are untested. If you try one, please report back.

## Contributing / reporting problems

Please include:
- the **Status** line from the CSX2 Dash settings page
- any `CSX2 Dash` lines from `SimHub\Logs\SimHub.txt`
- the newest `%APPDATA%\CSX2Dash\freeze-*.log`, if there is one
- your wheel's firmware string (shown in the status line)

The protocol notes in `docs\` were reverse-engineered for interoperability, from UGT Manager 1.6.819 and from testing on a real wheel. UGT Manager's own code is **not** included.

## Licence

MIT © 2026 0xjohnjov. See [LICENSE](LICENSE). "Cube Controls" and "Ultimate Game Tech" are the names of their respective owners and are used here only to describe compatibility.
