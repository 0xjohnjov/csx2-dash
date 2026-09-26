# UGT Manager 1.6.819: in-race wheel protocol (Cube Controls CSX2 / "UGT G4:CC01/01/20rC")

Source: ILSpy decompile of UGT Manager 1.6.819 (`UGTManager.exe`). All `Lnnnn` references point to line numbers in that decompiled file. The decompiled source is **not** distributed with this project.

**Status (26/09/2026):** written first as code analysis, then **checked on a real CSX2** (firmware `UGT G4:CC01/01/20rC`) with the CSX2 Dash plugin and the test tools in `tools\`. Section 9 lists what the hardware testing confirmed or corrected. **Read section 9 first.** In particular, the `0x90` licence handshake **is required**. Without it the wheel freezes 30–60 s after power-up.

---

## 0. Main finding: there is no telemetry packet

UGT does **not** send numeric telemetry, and there is **no channel-ID/value scheme**. The wheel works as a **remote 480x272 display terminal plus an LED bitmask**. Everything is formatted and laid out on the PC:

* A **layout** (`%APPDATA%\UltimateGameTech\layouts\N.xml`) is a list of *widgets*. Each widget has a `KeyShort` (for example `SPD`, `Gear`, `RPM`, `LastLpt`), X/Y pixel position, RGB565 colour, font number, size multipliers and a `Digits` field width.
* The game reader (for AC: `DSclassAC`) converts game data into **strings**, one per widget `KeyShort`, stored in `MainForm.CurStr_dict` (plus optional per-widget colour overrides in `CurCol_dict`).
* A background worker diffs each string against the last one sent. For every changed widget it sends a **0x51 "draw text" packet** (x, y, colour, font, ASCII). Every 0x51 packet also carries the **5-byte LED bitmask**.
* Bars (RPM bar, fuel bar and so on) are drawn with **0x22 filled/empty rectangle** packets.
* Static labels and artwork are pre-rendered into a **background image** stored on the wheel's SD card. It is shown with **0x35 display raw image (by SD sector address)** when the page changes.
* Fonts other than the two built-in ones must be **loaded into the wheel from its SD card** (0x46 or 0x30) before use. That load is request/response.

Consequences:
* The `Mappings\` folder is **not** a telemetry channel map. It holds button→command key bindings (for example `Default_General.txt` = `ShiftLightMode_*`, `Page_Up`, `Brightness_UP`...). They are read by `Read_KeyBindings` (L202184) and `DXSettings.getCommands_1` (L27997). They are irrelevant to display output.
* There are no checksums, no sequence counters, no multi-packet framing and no heartbeat/keep-alive during a race. Packets are only sent when something changes.

---

## 1. Transport recap (with corrections)

* `Send_Async_with_timeOut` (L21729): builds a 65-byte buffer with `[0]=0x02` (report ID) and `[1..64]=payload[0..63]`, then calls `WriteFile(..., 64, ...)` (L21746). **Only 64 bytes are written: the report ID plus payload[0..62].** Payload byte 63 is never transmitted, so treat the usable payload as **63 bytes**. All packets below fit.
* Fire-and-forget: `SendFastData` → `SendAsyncData` (L21910 → L21767). There is no pad-data toggling on this path, and it is used for every in-race draw.
* Request/response: `SendData` (L21619) wraps the call in 0xA0/0xA1 (only if pad data is currently enabled) and reads one 64-byte input report. In `Send_with_timeOut` (L21545) the reply buffer is `INBuffer[0]` = report ID (0x02 on success), `INBuffer[1..]` = reply payload.
* Pacing: before each draw packet UGT busy-waits until `stopWatch - LastSend >= Delay.DataItems` (for example L18856). Defaults are in `class Delays` (L196877): `DataItems=5 ms`, `Page_Change=50`, `DisplayImage=10`, `ClearScreen=5`, `Brightness=10`, `PadChange_Pre=5/Post=25`, `LoadFonts_Pre/Post=10`. Selectable presets are at L175395: HighPerf `DataItems=0`, Resistant 15, LowPerf 25, Max 30. **Use at least 5 ms between packets.**
* Multi-byte coordinates are **big-endian**. Colours are **RGB565, MSB first** (decoder at L661: `R=(v&0xF800)>>11, G=(v&0x7E0)>>5, B=v&0x1F`).

---

## 2. The race-time send loop

### 2.1 Threads

| Thread | Code | Role |
|---|---|---|
| Sim detector | `backgroundWorker2_DoWork` L7693 | Polls process names every ~200 ms (L8004). On AC it calls `DSdataAC.Connect()` (L7714), which blocks while the sim runs. When the sim ends: `GameData.Unset_Sim(); SimRunning_id=None; CleanUp(Dim:false)` (L7819-7821). |
| AC reader | `DSclassAC.Connect` L201185 | Tight loop with no sleep. It reads the shared memory. If `AC_STATUS == AC_LIVE`, it runs `ProcessPassiv / ProcessPhysic / ProcessGrafic`, sets `MainForm.SendDataPending = true` (L201239-201242) and calls `ProcessLCD()` (L201339). If not live, or no packet has arrived for 2 s, it sets `paused`. |
| Display sender | `backgroundWorker3_DoWork` L18389 | Runs forever. It does `Thread.Sleep(50)` per cycle (L18405). If `SendDataPending` is set and not paused, changing page, on the brand screen or syncing, it walks the current layout: bars (L18447-18641), then widgets (L18651-18737). It calls `Update_LEDs()` after every item and at the end of every cycle (L18763). |

**Effective update rate:** one pass every 50 ms plus the send time. Each changed widget costs one packet, spaced at least `Delay.DataItems` (5 ms) apart. Unchanged widgets are not re-sent.

### 2.2 `ProcessLCD` (AC): produces the strings

`ProcessLCD` L201339:

1. `GameData.ProcessLEDs(CarObj.RPM, CarObj.Gear, CarObj.LimiterOn)` computes `LedBytes[0..4]` (see section 4).
2. If `PauseScreen || BrandScreen`, it calls `ChangePage(layout.ID)` to redraw the page (L201353).
3. For each `bar`, it sets `CurStrings.BarValues.<Key>` to a fraction between 0 and 1 (L201357-201415), for example `RPM_Bar = RPM / MaxRPM`.
4. For each `widget`, it sets `CurStr_dict[KeyShort] = Transform.align(<formatted>, widget.Digits)` (L201416-202175). Unknown keys get `"???"`. Keys that are unavailable for this sim get `"NA"` (L27751-27753).

### 2.3 Diff and send, per widget (L18651-18737)

* **Full redraw** (`Write_Whole_Str`) happens if the widget has never been sent on this page, if the colour changed, or if the string length changed. It also happens if the font is not `Monospaced`, if `BMPwidth <= 0`, or if the font RefNum is below 3 (L18848).
* **Partial redraw** otherwise: only runs of changed characters are sent, at `x = Xpos + BMPwidth*W_Multiplier*charIndex` (L18883-18941). This is an optimisation only; always sending the whole string is valid.
* Widgets with `Xpos > 480` or `Ypos > 272` are skipped (L18835-18842).
* `ChangePage` clears `CurStr/OldStr/CurCol/OldCol` dicts (L18331-18334), which forces a full redraw of everything.

---

## 3. Packet reference (payload bytes, report ID 0x02 not shown)

Packets marked "0xFF-filled" start as 64 x 0xFF before fields are written. The others start zero-filled.

### 3.1 `0x51`: draw text plus set LEDs (the main race packet)

`CreatePacket` L18947-19055. The array is 0xFF-filled.

| Offset | Field | Encoding |
|---|---|---|
| 0 | `0x51` | command |
| 1..5 | `LedBytes[0..4]` | LED bitmask (section 4). **Always included.** |
| 6..7 | X | uint16 **big-endian**, pixels 0..479 |
| 8..9 | Y | uint16 big-endian, pixels 0..271 |
| 10..11 | colour | RGB565 MSB, LSB. If both are 0, UGT substitutes 0xFFFF (white) (L18998). |
| 12 | width multiplier | `max(1, W_Multiplier)` |
| 13 | height multiplier | `max(1, H_Multiplier)` |
| 14 | font RefNum | `AllFonts_V2[widget.Font].RefNum` (built-in: 1 = 8x12, 2 = 12x16. Loaded SD fonts: 3..18) |
| 15.. | ASCII chars | 1 byte per char, **max 47 chars**. Terminated by the pre-filled 0xFF. Empty string → `[15]=0xFF`. |

```csharp
// L18954-19017 (trimmed)
array[0] = 81;                       // 0x51
array[1..5] = LedBytes[0..4];
array[6] = xHi; array[7] = xLo; array[8] = yHi; array[9] = yLo;
array[10] = cmsb; array[11] = clsb;  // RGB565
array[12] = w_multi; array[13] = h_multi;
array[14] = AllFonts_V2[font].RefNum;
// chars from [15], rest stay 0xFF
```

The X/Y hex-string encoding at L18955-18982 is equivalent to big-endian uint16 for values up to 4095.

**Erasing:** strings are right-padded or left-padded with spaces to `widget.Digits` (`Transform.align`, L196168). Shorter values therefore overwrite old glyphs with spaces. *Unverified:* whether glyph cells are drawn opaque (background colour). UGT relies on space-padding working, which suggests they are opaque.

### 3.2 `0x51` LED-only update

`Update_LEDs` L18778-18828. The buffer `outBuffer_LEDs` is 0xFF-filled (L18397-18400). It sets `[0]=0x51`, `[1..5]=LedBytes`, and everything else stays 0xFF, so X=0xFFFF and no text. It is sent only when `LedBytes` differs from the last LED-only packet, and only if at least `Delay.DataItems` ms have passed since the last LED send.

### 3.3 `0x22`: rectangles (bars; only when `DeviceVersion >= 2.3`, L21445-21448)

`Filled_Rectange` L19057 / `Empty_Rectange` L19108. The array is zero-filled.

| Offset | Field |
|---|---|
| 0 | `0x22` |
| 1 | `0x04` = filled, `0x03` = outline |
| 2..3 | X1 BE |
| 4..5 | Y1 BE |
| 6..7 | X2 (`Right`) BE |
| 8..9 | Y2 (`Bottom`) BE |
| 10..11 | colour RGB565 |

Bar geometry, ticks, frames and delta bars come from `Destys_DrawingFunctions` (`Send_Bar_Item_NEW` L836, `DrawBar` L1455, `Draw_Delta_Bar` L1632). On the first draw it paints frame, background and foreground. After that it only repaints the rectangles that changed between foreground and background.

### 3.4 Screen / page commands

| Cmd | Builder | Bytes | Meaning |
|---|---|---|---|
| `0x44` | `ClearScreen` L19176 | `[0]=0x44`, [1..63]=0xFF | Clear LCD. Wait `Delay.ClearScreen` afterwards. |
| `0x35` | `Raw_Image_Display` L13031 | `[0]=0x35`, `[1..4]` = **byte** address on SD raw partition (`SectOffset*512`) uint32 BE, `[5..6]` X BE, `[7..8]` Y BE; zero-filled | Show a stored .gci image (the layout background). Images smaller than 480x272 are centred, and the screen is cleared first (L12994-13028). |
| `0x37` | in `CleanUp` L19259 | `[0]=0x37`, rest 0xFF | Show brand/idle image (the EMU shows `pic00000.gci`, L19279-19283) |
| `0x10` | `RestartLcd` L18360 | `[0]=0x10`, rest 0xFF | Restart LCD (not used in race) |
| `0x09` | `DisplayFirmwareVersion` L18372 | `[0]=0x09`, rest 0xFF | Show FW version (not used in race) |

### 3.5 Font load/unload (request/response, via `SendData`)

`LoadFont_New_v3` L6275:

| Offset | Field |
|---|---|
| 0 | `0x46` (font file in the raw partition, `Raw=true`, which is the normal case) or `0x30` (FAT) |
| 1 | RefNum to assign (3..18, allocated round-robin, L6334-6362) |
| 2..13 | dat filename, ASCII, right-aligned and zero-padded on the left to 12 bytes, for example `font0009.dat` |
| 14..25 | gci filename, same format, for example `font0009.gci` |
| rest | 0xFF |

Success: reply `[0]==0x02` and `[1..7]=="ONEFONT"` (L6326). Font file names are `"font" + 4-digit font number` (from TOC names, `Get_RawFonts` L7362-7363, L7462-7464). The layout's widget `<Font>` N is that number. Built-in fonts: `Font` 1 → RefNum 1 (8x12), `Font` 2 → RefNum 2 (12x16) (`StandardFonts` L6225-6259). Font 0 is a placeholder. An unloaded font number falls back to RefNum 2.

Unload: `[0]=0x42, [1]=RefNum`, rest 0xFF, via `SendData`. Success is a reply that does **not** contain "ONEFONT" (L6483-6487).

`ChangePage` L18200-18353 loads missing fonts from `layout.UsedFonts` (numbers > 2), unloading all if more than `MaxFontsLoaded` would be needed. It does this inside `Disable_Pad_Data`/`Enable_Pad_Data` (L18274-18279).

### 3.6 Brightness (device v2.x path)

`SetGlobal_Brightness` L19682 is called at connect (L21379) and at sim start (`Set_Brightness_Initially` L27941). It sends only the values that changed:

* `SetColourBrightness` L19715: `[0]=0x66, [1]=colour (1=Red, 2=Green, 3=Blue, 4=Yellow), [2]=OnTime (0..255)`, rest 0xFF. Wait `Delay.Brightness` afterwards.
* `LCD_Contrast_Change` L19825: `[0]=0x61, [1]=level (1..LCD_Max; LCD_Max defaults to 16, L2153)`, [2..15]=0xFF, the rest zero.
* Value formula: `v = (int)Math.Round((max-min)*Global_Brightness + min)`. Defaults (L197054-197074): Global 0.4, R 2..128, G 1..64, B 3..128, Y 3..128, LCD 4..16. That gives R=52, G=26, B=53, Y=53, LCD=9 (uses .NET banker's rounding; `Convert.ToInt16`).
* On close with "turn off" enabled (L23840-23872): clear, LEDs off, `0x61 0x00`. **Verified on hardware:** level 0 turns the backlight fully off, and the wheel keeps responding while dark. UGT resets its cached LCD level to 0 on connect (L21013), so it always re-sends brightness and a dark screen can't get stuck. CSX2 Dash uses this for "screen off when idle". For "CC" firmware it also sends `0x20 0x10 idx 0 0 0 0 0` for idx 0..14. That is the **button-LED mode = Off** command (`SetLedMode` L189977), not the rev lights.
* Alternative brightness commands exist in the button-box config form: `0x20 0x50 colour OnTime` (L189746) and `0x20 0x60 0x01 lcd` (L189826). They are not used in race.

### 3.7 RGB rev-LED colour (not used for your firmware)

`SetLedRGBColour(led, r, g, b)` L19336: `[0]=0x20, [1]=0x53, [2]=led, [3]=r, [4]=led, [5]=g+8, [6]=led, [7]=b+16`, zero-filled. Levels are 0..7 (3-bit). `led` is the `LED_Num` index (3=Rev_15 … 17=Rev_1). It is used only by `Set_RevLight_Colors` (L19361), which is **gated by `is_RGB_Allowed`**. That flag is true only if the firmware string contains **"rD"** (L22113-22120). **Your FW "…rC" gives `is_RGB_Allowed=false`, so UGT never sends 0x20 0x53 to a CSX2.** Rev-LED colours on the CSX2 are therefore firmware-defined. Brightness is per colour group via 0x66. *Unverified:* whether a CSX2 accepts 0x20 0x53 anyway. `SetLedColour` (`0x20 0x52 colour led value`, L19350) is unused in race.

---

## 4. Rev / status LEDs: computation and bitmask

### 4.1 Bitmask layout (`LedBytes[0..4]` = payload bytes 1..5 of 0x51)

`ProcessLEDs` builds a 40-char '0'/'1' string `PortArray`. `LedBytes[k] = Convert.ToByte(PortArray[8k..8k+7], 2)` (L29484-29493). So **port p → `LedBytes[p/8]`, mask `0x80 >> (p%8)`**. The LED→port map is `LED_Port_idx.LED_Num_2_Port_Map` (L197465-197470) with the enum `LED_Num` (L197432).

| LED | port | byte | mask |
|---|---|---|---|
| Rev_1 (first to light) | 26 | 3 | 0x20 |
| Rev_2 | 25 | 3 | 0x40 |
| Rev_3 | 24 | 3 | 0x80 |
| Rev_4 | 31 | 3 | 0x01 |
| Rev_5 | 30 | 3 | 0x02 |
| Rev_6 | 21 | 2 | 0x04 |
| Rev_7 | 22 | 2 | 0x02 |
| Rev_8 | 23 | 2 | 0x01 |
| Rev_9 | 1 | 0 | 0x40 |
| Rev_10 | 0 | 0 | 0x80 |
| Rev_11 | 4 | 0 | 0x08 |
| Rev_12 | 5 | 0 | 0x04 |
| Rev_13 | 6 | 0 | 0x02 |
| Rev_14 | 7 | 0 | 0x01 |
| Rev_15 | 3 | 0 | 0x10 |
| Yellow_Left | 27 | 3 | 0x10 |
| Red_Left | 29 | 3 | 0x04 |
| Blue_Left | 28 | 3 | 0x08 |
| Yellow_Right | 34 | 4 | 0x20 |
| Red_Right | 39 | 4 | 0x01 |
| Blue_Right | 38 | 4 | 0x02 |
| External_1..6 (not on CC: `Has_Extra_LEDS=false`, L22095) | 11,10,8,9,13,37 | 1,1,1,1,1,4 | 0x10,0x20,0x80,0x40,0x04,0x04 |

*Unverified:* the physical left-to-right order of Rev_1..Rev_15 on the CSX2, and which side LEDs physically exist. Bit = 1 means LED on (`Toggle_ALL_LEDs(false)` sends 0x00 x 5, L19310-19320).

**Caution, 0xFF x 5:** `Toggle_ALL_LEDs(true)` sends `0x51 FF FF FF FF FF` (rest 0xFF). `CleanUp` calls it at the idle/brand screen (L19274). `LedBytes` is also initialised to 0xFF x 5 on connect (L21046-21050). Either the idle state is "all on", or all-0xFF is a firmware sentinel such as "no change" or "firmware control". **Unknown.** Send explicit masks and test.

### 4.2 Shift-light algorithm (`GameData.ProcessLEDs` L29181-29494)

* **Thresholds:** `UpdateRpmTbl` L28178: `thr[i] = (short)Math.Round(MaxRPM * table[i] / 1000.0)`. The tables are per-mille. Defaults are in `Standard_values` (L197150-197198). The user's are in `Settings_xml\General.xml` `<RpmTables>`. User "Single": 820,840,860,870,880,890,900,910,915,920,925,930,935,938,940 and **980 = shift point (OSP)**.
* **Lighting** (L29441-29460): 16-entry tables: `for m in 0..14: if RPM > thr[m] → LED_Num[17-m] ON`, so m=0 → Rev_1 … m=14 → Rev_15. 11-entry tables (Red_Blue_*): `LED_Num[12-m]`, m=0..9 → Rev_6..Rev_15. The last entry is never a LED; it is the **OSP**.
* **Pattern types** (`UpdateOSP` L28197-28345): `Single`, `Custom`, `Green_Red_Blue` = sequential over the 15 LEDs with OSP LEDs {10..14} (= Rev_11..15). `Side_To_Center`/`Center_To_side` use non-monotonic tables so the LEDs fill from the ends or the centre (OSP LEDs {5..9} / {0..4,10..14} when not RGB). `Red_Blue_*` use only rev LEDs 5..14. The user setting is `Single`.
* **Over-shift (OSP) flash** (L29461-29475): if `RPM > OSP` and gear ≠ 0 (or `Neutral_OSP`) and (gear ≠ MaxGear or `MaxGear_OSP`) and `OSP_Type != Off`, then the OSP LEDs are forced **off** whenever `FlashOn` is false. `FlashOn` toggles when `DateTime.Now.Ticks > Flash_Ticks`, with `FlashRate = 10000` ticks = **1 ms** (L27623, L29197-29208). In practice it toggles on almost every `ProcessLEDs` call, so the visible rate is set by the sender (≈50 ms loop plus 5 ms spacing). The `BlinkTime` setting is not used here. `OSP_Type=All_RPM_Blink` makes all used rev LEDs flash. **Note:** the user's `OSP_Type=Blue_Flags_Blink` is rewritten to `Off` by `UpdateOSP` (L28331-28335), so the user currently has no shift flash.
* **Pit limiter** (L29380-29415): if `LimiterOn` and `SPD_Limit_Type != Off`, the rev LEDs are driven by `SlowBlink` (toggles every 500 ms, `SlowBlink_Rate=5,000,000` ticks). `Alternate_Blinking` lights even/odd LEDs alternately. `All_Blinking` toggles all of them together. The user setting is Alternate_Blinking.
* **Event LEDs** (L29275-29375): `Settings.Mappings_LED` maps events (flags, DRS, TC/ABS active, low fuel, lockups...) to an LED plus a state (ON / Slow_Blink 500 ms / Fast_Blink 100 ms / Flash), with priority. They cannot override LEDs currently used for revs. The user's General.xml has none.
* **MaxGear quirk (AC):** `ProcessPhysic` sets `CarObj.MaxGear = gear-1` whenever it differs (L203210-203214), so for AC `Gear == MaxGear` always holds. The "MaxGear" tables/OSP are then used. With `MaxGear_ShiftLights=Off` they equal the normal ones.

---

## 5. Session lifecycle: what the wheel needs

There is **no "game started", sim-ID or session command** sent to the wheel. The firmware is stateless with respect to the sim. Sequence for AC:

1. **Device connect** (`DeviceConnectionTasks` L20992): licence challenge `0x90` (L21066-21119; **required**, see section 9.1), `0x47` lock, reads the SD raw-partition info/TOC (L21157, `Read_RawFiles` L12576 → `Read_TOC_new` L15095, raw reads with `0x34` + BE byte addr + len, L17328-17388), brightness (L21379). With no sim running, it sends `RefreshFromEEPROM (0x20 0x01)` and `CleanUp(false)` (L21488-21494): 0x44 clear, 0x37 brand image, 0x51 LEDs 0xFF.
2. **Sim detected** → `DSclassAC.Connect` → `GameData.ReloadSettings(Sim_ID.AC,"","")` (L201191, L28035). That sets brightness (0x66/0x61) and, if the per-sim XML has `<Layouts>`, calls `ChangePage`. `paused=true` initially.
3. **ChangePage** (L18200): `SendDataPending=false`, sleep 25 ms, load fonts (0x46 via SendData, inside 0xA0/0xA1), then **0x35 background image** for this layout (TOC entry `BGI0XXXX` whose number is the layout ID, `Add_IMG_ToList` L10982-10991). If there is no background it sends **0x44 clear**. It clears the diff dictionaries, sleeps 25 ms and restores `SendDataPending`.
4. **Live** (`AC_STATUS == AC_LIVE`): `SendDataPending=true`. The widget and LED packets stream as in section 2.
5. **Paused / not live** for more than 4 s: `SetPauseScreen` (L29012) sets `PauseScreen=true` (drawing stops) and sends **0x35** with the sim logo (for AC, TOC `AC_01`, via `IMG_AC`). If there is no logo it sends `Drawstring(sim name)` = 0x44 then 0x51 at (20,150), font 2, 2x3 (L19155-19174). When live again, `ProcessLCD` sees `PauseScreen` and calls `ChangePage`, which does a full redraw.
6. **Game exits** (`No_Data_Received` returns false when the process is gone, L28988): `CleanUp(Dim:false)` (L19231) sends `SendDataPending=false`, **0x44** clear, **0x37** brand image, then `Toggle_ALL_LEDs(true)` (0x51 + FF x5, rest FF).

---

## 6. Generic data model (what your SimHub plugin fills)

The game-agnostic layer is: **layout widgets + `CurStr_dict` (strings) + `CurCol_dict` (RGB565 overrides) + `CurStrings.BarValues` (0..1) + `ProcessLEDs(rpm, gear, limiter)`**. The internal `CarObj`/`TimeObj`/`BaseObj` structs are per-sim-class fields. The string formatting is what matters. AC mapping (`ProcessLCD` L201416-202175, `ProcessPhysic` L203204, `ProcessPassiv` L203410) with SimHub equivalents:

| KeyShort | UGT source (AC) | Formatting (exact) | SimHub |
|---|---|---|---|
| `SPD` | `speedKmh` (int; ×0.621371 if !KPH) | `Number(v, Digits, Digits, 0)` → **zero-padded** to Digits (e.g. `087`), then right-aligned | `SpeedKmh` / `SpeedLocal` |
| `Gear` | `physics.gear-1` (−1=R, 0=N) | `"N"` / `"R"` / integer, right-aligned to Digits | `Gear` ("N","R","1".."n") |
| `RPM` | `rpms` | zero-padded to Digits like SPD | `Rpms` |
| `Pos` | `position` | `"P"+n`, right-aligned, left-cut on overflow | `Position` |
| `Laps` | lap/total | `"n/total"` or `"n/--"`, centred | `CurrentLap`/`TotalLaps` |
| `Fuel` | `physics.fuel` | `Number(v, Digits, 2, prefix:false)` → up to 2 decimals as room allows | `Fuel` |
| `CurLpt` | current lap time (s) | `toClock(t, Digits, false, false, 1)` → `1:23.4` | `CurrentLapTime` |
| `LastLpt` / `BestLpt` / `PBlpt` / `SBlpt` / `OptLpt` | seconds | `toClock(t, Digits)` → `1:23.456` (3 decimals if room) | `LastLapTime`, `BestLapTime` |
| `DffBest` / `DffPB` | live delta (s) | `toClock(d, Digits, prefix:true)` → `+0.123` / `-1.234` | `DeltaToSessionBest` etc. (via plugins) |
| `BBias`, `ABS`, `TC`, `StbltyAss` | `BrakeBias*100`, `abs*100`, `tc*100` | `Number(v, Digits, 1, false, false)` | `BrakeBias`, `ABSLevel`, `TCLevel` |
| `FLTyretemp`… | tyre core temp (int) | `Number(v, Digits, 0, …)` | `TyreTemperatureFrontLeft`… |
| `FLTyrePress`… | pressure | 1 decimal | `TyrePressureFrontLeft`… |
| `WaterTemp`/`OilTemp`/`AirTemp`/`TrackTemp` | °C (or °F if !Metric) | 1 decimal | `WaterTemperature`… |
| `Track`/`Car`/`Session`/`CarClass`/`Compound` | strings | `align(..., "scroll")`: marquee using the global `scroll.ActNum`, which advances once per `ScrollSpeed` in the sender loop (L18410-18419) | `TrackName`, `CarModel`, `SessionTypeName` |
| `SysTime` | now | `HH:mm` or `HH:mm:ss` if Digits ≥ 7 | – |
| `DRS` | text "DRS" | colour = `DRS_Active (0x0F00)` / `DRS_Off (0xFF35)` | `DRSEnabled` |
| others | – | `"???"` if the key is unknown to the sim class, `"NA"` if unavailable | – |

Formatters: `Transform.Number` L196122-196166, `Transform.toClock` L195996-196120 (returns a string right-aligned to `Totaldigits`, truncated if longer; 0 → `0.000`), `Transform.align` L196168-196221 (default right-align with space padding; truncates at the right on overflow).

Bars (`CurStrings.BarValues`, 0..1): `RPM_Bar = RPM/MaxRPM`, `Fuel_Bar = fuel_percent`, `Kers_Bar`, `Wear_xx_Bar`. Delta bars: `DffBest_D_Bar = delta/2.0`, `FlLpsD_D_Bar = /10`, `FlTargD_D_Bar = /5` (L201357-201415).

Dynamic colours (`Dynamic_Colors` L195701; RGB565 MSB,LSB): best = 0x0F00 (green), PB = 0x3ABF, session best = 0x814F, invalid = 0xFBA0, temp low/norm/high = 0x03D5/0x0F00/0xF900.

**Layout file** (for example the user's `layouts\1.xml`, XmlSerializer UTF-16; class `Layout` L197230, `Widget` L196929, `bar` L197251): `<ID>`, `<UsedFonts>`, `<Widgets><Widget>` with `KeyShort, Xpos, Ypos, ColMsb, ColLsb, W_Multiplier, H_Multiplier, Width, Height, Font, Digits, Info_Font{BMPwidth,BMPheight,TypeFlags}`, and `<Bars>`. Labels are **baked into the background image** (`Use_BkGnd` L11317), not drawn live.

**Finding background/font sector addresses without re-implementing the TOC reader:** UGT writes the TOC to `SDCard\<sd-guid>\TOC_List.csv` (L12822). Columns include `Layout_ID` and `Sect_Offset`, for example `BGI00001;gci;Sect_Offset=12206`. A PC can have several GUID folders (one per SD card UGT has seen). The newest is normally the current one. To be certain, **verify the GUID against the device** (the partition info read at L14499). A 0x35 packet with a stale sector address will draw garbage.

---

## 7. Minimum viable implementation

For rpm, gear, speed, lap times and rev LEDs on the CSX2, pace every packet at least 5 ms apart:

1. Open the col02 HID interface. Write 64 bytes: `0x02` + payload[0..62].
2. (Optional) Brightness: `66 01 R`, `66 02 G`, `66 03 B`, `66 04 Y` (0xFF-filled, 10 ms apart), then `61 <1..16>`.
3. `44 FF…FF`: clear screen. Optionally follow with `35 <addr BE32> 00 00 00 00` to show a layout background (addr = TOC `Sect_Offset * 512`).
4. Fonts:
   * **Simplest:** use the built-in RefNum 2 (12x16) or 1 (8x12) with W/H multipliers (for example ×3 for gear). No loading is needed.
   * **For nicer fonts:** one-time `SendData` of `46 <ref 3..18> "font00NN.dat"(12B) "font00NN.gci"(12B) FF…`, wrapped in `A0` … read reply … `A1`. Expect `ONEFONT`.
5. Per update tick, for each field whose padded string changed, send `51 L0 L1 L2 L3 L4 xH xL yH yL cH cL wm hm ref <ascii…> FF…`. Example gear "3" at (200,40), green 0x07E0, font 2 ×4 → `51 LL LL LL LL LL 00 C8 00 28 07 E0 04 04 02 33 FF FF …`.
6. Whenever the LED mask changes and no text packet is due, send `51 L0..L4 FF FF … FF`. Build L0..L4 from the table in 4.1 using `RPM > MaxRpm*table[i]/1000`.
7. On game stop: `44 FF…` (clear), then `51 00 00 00 00 00 FF…` (LEDs off). Optionally `37 FF…` for the brand image.

**Required first, within ~30 s of power-up:** the `0x90` licence handshake (section 9.1).

Not required: 0x47 lock, 0x20 0x01 EEPROM refresh, 0x98 or 0x02 0x01 queries (0x98 is still a handy health check), any sim/session command, keep-alive. The 0xA0/0xA1 pad-data toggles are only needed around request/response calls.

---

## 8. Open questions: resolved on hardware (26/09/2026)

1. **Payload length:** confirmed. `HidP_GetCaps` reports `OutputReportByteLength = 64`, so writes are report ID + 63 payload bytes. For reads, use a buffer of **at least 65 bytes**.
2. **0xFF x 5 LED bytes:** means **all LEDs on**. The firmware's own power-up idle state is also "all LEDs on + Cube Controls logo".
3. **LED mapping:** the section 4.1 table drives the rev sweep and the side LEDs correctly.
4. **Glyph opacity:** text cells are drawn **opaque**, so space-padding erases old digits.
5. **Bars:** `0x22` rectangles work on the CSX2.
6. **0x90 licence handshake:** **wrong assumption, it is required.** See 9.1.
7. **TOC GUID folder:** using the most recently written `SDCard\<guid>\TOC_List.csv` has worked. If backgrounds draw as garbage, the TOC is stale: open UGT Manager once with the wheel connected so it rewrites it.

---

## 9. Hardware-verified rules for a reliable third-party host

Found while building CSX2 Dash. Each rule was a real failure, and each was confirmed by a test in `tools\`.

### 9.1 Licence handshake within ~30 s of power-up (otherwise the wheel freezes)

If no host runs UGT's device-licence challenge, the firmware **freezes 30–60 s after it is plugged in**. It stops answering commands, **and** button/paddle reports stop, even with no program talking to the wheel. Only a USB power cycle clears it. When this happens every LED is lit and the screen shows the brand logo. UGT Manager always did this handshake on connect, which is why "replug + start UGT" always fixed it.

Handshake (`UGTManager.cs` L21066-21119): 15 rounds of request/response, wrapped in one `0xA0` … `0xA1`. Each request is 63 random bytes with `[0]=0x90` and `[4]=idx` (a random number from 1 to 34). A genuine wheel replies with `reply[8]` (counting the report-ID byte as `reply[0]`) equal to the ASCII character at position `idx` of the fixed string `6e7151d9-c5d9-439d-b81e-2bea345c3d55`. Implemented in `Csx2Device.LicenceHandshake()`.

Test: `tools\diagnostics\LicenceTest.cs`. With the handshake the wheel stayed alive for 3+ minutes. Without it, it died within 30–60 s on every replug, even when completely idle.

### 9.2 Page changes must copy UGT's `ChangePage` exactly

A rough page change (pad data toggled around every font load, no settle time, evicting one font at a time) froze the wheel when pages were flipped quickly. What works (`Csx2Device.ShowPage`):

1. Sleep `Page_Change/2` (100 ms, "Resistant" profile).
2. If the new fonts won't fit in the 15 slots (RefNum 3..17), **unload all** loaded fonts (`0x42`, 60 ms before each).
3. `0xA0` **once**, then load each missing font (`0x46`, 80 ms before, 20 ms after, 150 ms extra on error), then `0xA1` **once**.
4. Background `0x35` (or `0x44` if the layout has none), then sleep another 100 ms.

Request/response timeout: **2000 ms**, as UGT uses. Spacing between packets: 15 ms ("Resistant" profile).

Tests: `tools\diagnostics\PageStressTest.cs` (three back-to-back rounds through every layout) and `LiveStressTest.cs` (two rounds, then repeated flips between the two most font-heavy layouts, each followed by 3 s of live-style text and LED traffic; `--all-leds` keeps every LED lit). Both passed on a 9-layout setup (27 and 28 page changes).

### 9.3 Only one program on the command channel

Two hosts writing to the vendor channel at once lock the firmware up. Seen with UGT Manager and **FanaLEDs**: FanaLEDs detects the CSX2 as Fanatec hardware and streams Fanatec LED commands at it. CSX2 Dash therefore pauses while `UGTManager.exe` runs. Don't run FanaLEDs with a CSX2 connected.

### 9.4 Symptoms and what they mean

| What you see | Meaning |
|---|---|
| All LEDs lit + brand logo, buttons work | Freshly powered up and not yet licensed. It will freeze in 30–60 s unless a host connects. |
| All LEDs lit + brand logo, buttons dead | Licence watchdog froze it. Power-cycle, with the host already running. |
| Screen dark, no LEDs | Normal with CSX2 Dash 1.1.0+ when no game is running (screen off when idle). |
| Screen stuck mid-page, buttons dead | Firmware hang during drawing/page change. Power-cycle. |
| `0x98` write times out | Firmware hung. Software resets (`0x94`, `pnputil /restart-device`) don't help; only a USB power cycle does. |
