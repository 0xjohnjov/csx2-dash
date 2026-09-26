# Installing CSX2 Dash

About 10 minutes. You don't need to build anything unless you want to (see "Building from source" at the end).

## 1. Before you start

You need:

| | |
|---|---|
| **Wheel** | Cube Controls CSX2 with UGT electronics. In Windows it's called **"UGT Sim LCD and CONTROLS V2"** (USB ID `04D8:F4C3`). |
| **Windows** | 10 or 11 |
| **SimHub** | Installed. Tested with 9.10.15; free or licensed both work. |
| **UGT Manager** | Installed and **set up at least once** with the wheel connected. CSX2 Dash reads the dash layouts, shift-light settings and SD-card index that UGT Manager saves in `%APPDATA%\UltimateGameTech`. It doesn't need UGT Manager running afterwards. |

Quick check: open `%APPDATA%\UltimateGameTech` in Explorer (paste it into the address bar). You should see a `layouts` folder with files like `1.xml`, and an `SDCard` folder. If you don't, start UGT Manager with the wheel connected, let it finish connecting, then close it.

**Also:** if you have **FanaLEDs** installed, uninstall it, or at least stop it starting with Windows. It sends Fanatec LED commands to the CSX2 and freezes it.

## 2. Install the plugin

1. **Close SimHub and UGT Manager** (check the system tray too).
2. Download the latest **`CSX2Dash-vX.Y.Z.zip`** (or just `CSX2Dash.dll`) from the [Releases page](https://github.com/0xjohnjov/csx2-dash/releases), and unzip it.
3. *(Recommended)* Check the download is genuine. Every release is built by GitHub Actions from this repo's source, not on anyone's PC, and GitHub signs a record of that build. Either:
   - **Strongest:** with the [GitHub CLI](https://cli.github.com/) installed, run
     ```
     gh attestation verify CSX2Dash.dll --repo 0xjohnjov/csx2-dash
     ```
     It should say the verification succeeded.
   - **Quick:** in PowerShell, `Get-FileHash .\CSX2Dash.dll -Algorithm SHA256`. The result must match the `CSX2Dash.dll` line in `SHA256SUMS.txt` on the release page.
4. **Unblock it:** right-click `CSX2Dash.dll` → **Properties** → tick **Unblock** (if it's there) → **OK**. Windows blocks downloaded DLLs, and SimHub can fail to load them otherwise.
5. Copy `CSX2Dash.dll` into your **SimHub folder**, normally `C:\Program Files (x86)\SimHub\`. Windows will ask for admin permission.
   *Alternative:* put `CSX2Dash.dll` next to `src\CSX2Dash\install.cmd`, then right-click `install.cmd` → **Run as administrator**. If SimHub is somewhere else, first open a command prompt and run `set SIMHUB=D:\path\to\SimHub`, then run `install.cmd` from that prompt.
6. **Start SimHub.** If it says it found a new plugin, choose to **enable** it.

## 3. First run

1. In SimHub, click **CSX2 Dash** in the left menu.
2. Check the **Status** line:
   - **"connected (UGT G4:…) – waiting for a game"**: all good.
   - **"wheel not responding"**: the wheel froze before SimHub started (see section 5). **Unplug the wheel's USB, wait 5 seconds and plug it back in** with SimHub open. It should say "connected" within a few seconds.
   - **"paused – UGT Manager is running"**: close UGT Manager.
3. **Pick your game in SimHub** (the game name/logo at the top left), or turn on SimHub's automatic game switching. *This matters:* if SimHub is set to a different game than the one you're driving, the wheel just keeps showing its logo.
4. Start the game and get on track. Your UGT page appears and the LEDs follow the revs.

## 4. Set up buttons and startup (recommended)

- **Page buttons:** SimHub → **Controls and events** → add a new mapping → press the wheel button → choose the action **CSX2Dash.NextPage** (and **PreviousPage**, if you like). Other actions: `CycleDriverInfo`, `NextDriverPage`, `ToggleShiftFlash`.
- **Start SimHub with Windows** (SimHub settings). The wheel has to be licensed within ~30 seconds of powering up, so the simplest routine is "SimHub is always running".
- **Brightness, shift flash, flag LEDs:** on the CSX2 Dash page.
- **Changed a layout in UGT Manager?** Close UGT Manager, then press **Reload UGT layouts / settings** on the CSX2 Dash page.

## 5. Everyday rules

1. **SimHub must be running when the wheel powers up**, or you replug the wheel after starting SimHub. Otherwise the wheel's firmware freezes after 30–60 seconds: all LEDs lit, logo on screen, and eventually dead buttons. UGT Manager used to prevent this without telling anyone; now CSX2 Dash does.
2. **Close UGT Manager before racing.** Use it only to edit layouts or SD-card content.
3. **Don't run FanaLEDs** with the CSX2 connected.

## 6. Troubleshooting

| Problem | Fix |
|---|---|
| Wheel shows the logo, all LEDs lit, and buttons stop after a minute | Not licensed in time. Replug the wheel **with SimHub running**. |
| Status "wheel not responding" | The wheel is frozen. Unplug/replug the USB (both leads, if you use a Y-splitter). Software can't unfreeze it. |
| Status "connected – waiting for a game" while you're driving | SimHub has the wrong game selected (top left in SimHub), or the game's telemetry isn't enabled in SimHub. |
| Page buttons do nothing | They only work while a game is live. Also check the mapping under Controls and events. |
| Backgrounds look like garbage | SimHub's SD-card index is stale (for example after swapping SD cards). Open UGT Manager once with the wheel connected, close it, then press **Reload UGT layouts**. |
| Some fields show `NA` or `???` | That value isn't available from this game through SimHub, or the key is unknown. Please report it. |
| CSX2 Dash isn't in SimHub's menu | The DLL is in the wrong folder, still blocked (step 2.4), or SimHub needs a restart. Check `SimHub\Logs\SimHub.txt` for "CSX2Dash". |
| It freezes mid-session | Send the newest `%APPDATA%\CSX2Dash\freeze-*.log` and the `CSX2 Dash` lines from `SimHub\Logs\SimHub.txt`. The freeze log shows exactly what was sent before it happened. |

## 7. Optional: CSX2 Recovery tool

`tools\CSX2Recovery` builds a small admin tool. It checks whether the wheel is frozen and tries software resets before asking you to replug, and it logs which step worked (in `logs\recovery.log` next to the exe). On the CSX2, the lock-ups we've seen have always needed a physical replug, so this is mostly useful for diagnosing problems.

- Keep it somewhere **only admins can write to** (for example `C:\Program Files\CSX2Recovery\`), because it runs with admin rights.
- It closes UGT Manager and FanaLEDs, then reopens UGT Manager at the end. You can decline or close UGT; CSX2 Dash will take over again once UGT is closed.
- `CSX2Recovery.exe --check` only reports whether the wheel is responding. It changes nothing.

## 8. Uninstall

Close SimHub, delete `CSX2Dash.dll` from the SimHub folder, and optionally delete `%APPDATA%\CSX2Dash`. Nothing else was changed.

---

## Building from source

No Visual Studio needed. Everything builds with the C# compiler that ships with Windows (.NET Framework 4).

```bat
rem If SimHub isn't in C:\Program Files (x86)\SimHub:
set SIMHUB=D:\path\to\SimHub

src\CSX2Dash\build.cmd          -> src\CSX2Dash\CSX2Dash.dll
src\Tests\build.cmd             -> builds and runs the formatter tests (expect "ALL PASS")
tools\diagnostics\build.cmd     -> LicenceTest, PageStressTest, LiveStressTest, probe
tools\CSX2Recovery\build.cmd    -> CSX2Recovery.exe
```

Then install with `src\CSX2Dash\install.cmd` (run as administrator, with SimHub closed).

**Hardware tests** (in `tools\diagnostics`; close SimHub and UGT Manager first). Each waits for you to unplug and replug the wheel:
- `LicenceTest.exe`: licence handshake, then 3 minutes of health checks (`--no-licence` for the control run, where the wheel should freeze).
- `PageStressTest.exe`: three back-to-back rounds of page changes through all your layouts.
- `LiveStressTest.exe`: two rounds of page changes through all your layouts, then repeated flips between the two layouts that use the most fonts, each followed by 3 s of race-like text and LED traffic. Use `--all-leds` to keep every LED lit.
