# UGT Manager 1.6.819: widget string formatting for Assetto Corsa

Source: ILSpy decompile of UGT Manager 1.6.819 (line numbers below refer to that decompiled file, which is not distributed). Written as design notes for the plugin author; "you" means whoever implements a UGT-compatible host. The ported formatters live in `src\CSX2Dash\Fields.cs` and are checked by `src\Tests\FormatTests.cs`.
This document extends `UGT-telemetry-protocol.md` sections 2 and 6. It covers the exact text and colour UGT produces per widget key. It does not re-describe the transport.

AC path in the decompile:

* `DSclassAC` spans L199856-203605.
* `ProcessLCD` L201339-202182.
* `ProcessGrafic` L202288-203202 (lap, sector, fuel and delta logic).
* `ProcessPhysic` L203204-203408.
* `ProcessPassiv` L203410-203440.
* `Load_Dictionary` L203442-203532.
* `Update_Dictionary` L203534-203604.

---

## 0. Most important finding: many of your keys show `NA` on AC

`ProcessLCD` only formats a key if it is in the AC availability dictionary `dict` with a value above `available.Boarder`. The enum is at L197383: `Off, Off_CarDepending, Off_TrackDepending, Off_SessionDepending, Boarder, On_CarDepending, On_SessionDepending, On_TrackDepending, On`.

* Keys not in `dict`, or at `Off*`, get `Transform.align("NA", Digits)` (L202166-202174).
* Keys in `dict` and `On*` but with no branch in `ProcessLCD` get `"???"` (L202163).
* `NA_Default = "NA"` and `Error_Default = "???"` are defined at L27751/27753.

**Keys from your list that are NOT in the AC `dict`, so UGT always draws right-aligned `NA`:**

| Key(s) | Why |
|---|---|
| `SBlpt`, `SBS1`-`SBS4` | AC has no session-best data. A `SBlpt` branch exists (L201639) but the key is not in `dict`. |
| `BestS4`, `CurS4`, `LastS4`, `PBS4` | Never registered for AC, and there is no branch for them. |
| `CarClass` | Branch exists (L201901) but the key is not in `dict`. `BaseObj.CarClass` is never set by AC. |
| `OilTemp`, `WaterTemp` | Branches exist (L202097/202106) but the keys are not in `dict`. AC does not provide these values (`CarObj.WaterTemp/OilTemp` are never assigned). |
| `PitDelt` | No AC branch and not in `dict`. `PLDelta` is computed from `pits.ini` (L202577-202600) but never displayed. |
| `InfoState` | No AC branch and not in `dict`. |
| `D_n_Pos/Name/Lap/S1/S2/S3/info1/info2` | No AC opponent code at all: `DSclassAC` reads no car list and has no `D_` parsing. |

**Keys whose availability depends on the car or track** (`Update_Dictionary` L203534):

* `CurS3/LastS3/BestS3/PBS3` show `NA` when `static.sectorCount == 2`.
* `DRS` shows `NA` unless `static.hasDRS > 0`.
* `Kers` shows `NA` unless `static.hasERS > 0 || static.hasKERS > 0`.

The layouts are presumably shared with other sims (F1, rF2, R3E and so on), where those keys are live. Section 7 gives the reference formatting for them from the generic rFactor2 class, so your plugin can choose to fill them from SimHub instead of printing `NA`. **Decision for you:** show `NA` (faithful to UGT on AC) or fill them (better than UGT).

---

## 1. Core formatters, verbatim

### 1.1 `Transform.toClock(double, ...)` L195996-196120

```csharp
public static string toClock(double time, int Totaldigits, bool prefix = false, bool leadZero = false, int trailDigits = 3)
{
	bool flag = true;
	string text = "0";
	string text2 = ".000";
	if (leadZero)
	{
		text = "00";
	}
	text2 = trailDigits switch
	{
		0 => "", 
		1 => ".0", 
		2 => ".00", 
		_ => ".000", 
	};
	if (text.Length + 2 > Totaldigits)
	{
		text2 = "";
	}
	if (double.IsInfinity(time) || double.IsNaN(time))
	{
		time = 0.0;
	}
	if (time != 0.0)
	{
		string text3 = "";
		if (time < 0.0)
		{
			text3 = "-";
		}
		else if (prefix)
		{
			text3 = "+";
		}
		double num = Math.Abs(time);
		if (num > 0.0)
		{
			int num2 = Convert.ToInt16(Math.Floor(num / 86400.0));
			double num3 = num - (double)(num2 * 86400);
			int num4 = Convert.ToInt16(Math.Floor(num3 / 3600.0));
			num3 -= (double)(num4 * 3600);
			int num5 = Convert.ToInt16(Math.Floor(num3 / 60.0));
			num3 -= (double)(num5 * 60);
			int num6 = Convert.ToInt16(Math.Floor(num3));
			num3 -= (double)num6;
			string text4 = "";
			if (num2 > 0)
			{
				text4 = num2 + "d";
			}
			text = text3 + text4;
			if (text.Length + 2 > Totaldigits)
			{
				flag = false;
			}
			if ((num4 > 0 || num2 > 0) && flag)
			{
				text = ((!(num2 > 0 || leadZero) || num4 >= 10) ? (text + num4) : (text + "0" + num4));
				if (text.Length + 1 < Totaldigits)
				{
					text += "h";
				}
				else
				{
					text += "h";
					flag = false;
				}
			}
			if (num5 > 0 || num2 > 0 || (num4 > 0 && flag))
			{
				text = ((!(num2 > 0 || num4 > 0 || leadZero) || num5 >= 10) ? (text + num5) : (text + "0" + num5));
				if (text.Length + 1 < Totaldigits)
				{
					text += ":";
				}
				else
				{
					text += "m";
					flag = false;
				}
			}
			if (flag)
			{
				text = ((!(num2 > 0 || num4 > 0 || num5 > 0 || leadZero) || num6 >= 10) ? (text + num6) : (text + "0" + num6));
			}
			if (text.Length + 1 < Totaldigits && trailDigits > 0)
			{
				switch (trailDigits)
				{
				case 1:
					text2 = "." + (int)Convert.ToInt16(Math.Floor(num3 * 10.0));
					break;
				case 2:
				{
					int num7 = Convert.ToInt16(Math.Floor(num3 * 100.0));
					text2 = ((num7 >= 10) ? ("." + num7) : (".0" + num7));
					break;
				}
				default:
				{
					int num7 = Convert.ToInt16(Math.Floor(num3 * 1000.0));
					text2 = ((num7 >= 10) ? ((num7 >= 100) ? ("." + num7) : (".0" + num7)) : (".00" + num7));
					break;
				}
				}
			}
			else
			{
				text2 = "";
			}
		}
	}
	string text5 = text + text2;
	int num8 = Totaldigits - text5.Length;
	for (int i = 0; i < num8; i++)
	{
		text5 = " " + text5;
	}
	if (num8 < 0)
	{
		text5 = text5.Substring(0, text5.Length + num8);
	}
	return text5;
}
```

Behaviour to remember:

* **Time 0 (not yet available) prints `0.000`**, or `0.0` when `trailDigits` is 1. Leading spaces pad it to `Totaldigits`. There is **never** a `-:--.---` placeholder and no blank; a blank appears only during best-time blinking (see 2.3). If `Totaldigits < 3` the decimals are dropped, so `0` prints as `"0"`, right-aligned.
* Minutes are not zero-padded: `1:23.456`, and `23.456` below one minute (no `0:`). Seconds are zero-padded only after minutes: `1:05.123`.
* The fraction is **truncated with `Math.Floor`**, not rounded. It is done on the double left after subtracting whole units, so FP noise can show, e.g. `83.1` gives `1:23.099`. Port the arithmetic 1:1 (C# `double`, `Convert.ToInt16`) and you get the same digits.
* The decimals are only appended if `text.Length + 1 < Totaldigits`. After that, the whole string is right-padded, or **cut on the right** if it is too long. For example, with Digits = 7, `1:23.456` becomes `1:23.45`, and with Digits = 6 it becomes `1:23.4`.
* `prefix: true` puts `+` in front of positive values, and negative values always get `-`. `+0.123`, `-1.234`, `+1:02.345`. Zero stays `0.000` with no sign.

### 1.2 `Transform.toClock(DateTime, ...)` L195979-195994 (only used by `SysTime`)

```csharp
public static string toClock(DateTime DT, int Totaldigits, bool leadZero = true)
{
	string text = "H:mm";
	_ = DT.Hour;
	_ = DT.Minute;
	_ = DT.Second;
	if (leadZero)
	{
		text = "HH:mm";
	}
	if (Totaldigits > text.Length + 1)
	{
		text = "HH:mm:ss";
	}
	return DT.ToString(text);
}
```

### 1.3 `Transform.Number` (both overloads) L196122-196166

```csharp
public static string Number(double value, int Totaldigits, int trailDigits = 0, bool prefix = true, bool Round = true)
{
	return Number(value, Totaldigits, 0, trailDigits, prefix, Round);
}

public static string Number(double value, int Totaldigits, int LeadDigitsZero, int trailDigits, bool prefix = false, bool Round = false, bool TrimRight = true)
{
	if (double.IsInfinity(value) || double.IsNaN(value))
	{
		value = 0.0;
	}
	string text = "0";
	if (LeadDigitsZero > 1)
	{
		text = ((!(value < 0.0 || prefix)) ? new string('0', LeadDigitsZero) : new string('0', LeadDigitsZero - 1));
	}
	if (value > 0.0 && prefix)
	{
		text = "+" + text;
	}
	string text2 = value.ToString(text);
	if (text2.Length >= Totaldigits)
	{
		if (TrimRight && text2.Length > Totaldigits)
		{
			return text2.Substring(0, Totaldigits);
		}
		return text2;
	}
	if (trailDigits <= 0)
	{
		return text2;
	}
	int num = Totaldigits - (text2.Length + 1);
	if (num <= 0)
	{
		return text2;
	}
	if (trailDigits > num)
	{
		trailDigits = num;
	}
	text = text + "." + new string('0', trailDigits);
	return value.ToString(text, CultureInfo.InvariantCulture);
}
```

**Which overload a call hits** matters, because the first overload defaults `prefix = true`:

| Call shape in ProcessLCD | Resolves to | Effective args |
|---|---|---|
| `Number(v, D, D, 0)` (SPD, RPM, Kers, D_n_Pos) | overload 2 | `LeadDigitsZero=D, trail=0, prefix=false`, which gives a **zero-padded integer** (`087`) |
| `Number(v, D, 0, 0)` (Gear) | overload 2 | plain integer |
| `Number(v, D, 2, prefix:false)` (Fuel) | overload 1 | trail up to 2, no sign |
| `Number(v, D, 2)` (**FlLpsDelt**) | overload 1 | trail up to 2, **`prefix=true`**, so `+1.25` / `-0.50` / `0.00` |
| `Number(v, D, 0, prefix:false, Round:false)` (temps) | overload 1 | integer, no sign |
| `Number(v, D, 1, prefix:false, Round:false)` (Water/Oil) | overload 1 | 1 decimal if room |

Notes:

* `Round` is unused.
* The first `ToString(text)` uses the current culture and only decides the integer width. The second uses InvariantCulture.
* Custom-format `"0"` in .NET Framework rounds half away from zero, so a SimHub plugin on .NET Framework 4.8 reproduces it exactly.
* Decimals are added only if at least one decimal fits after the dot. On overflow the string is cut on the right (`TrimRight`).

### 1.4 `Transform.align` L196168-196221 and `scroll` L196242

```csharp
public static string align(string line, int TotalLength, string align = "right", bool cutright = true)
{
	string text = "";
	string text2 = "";
	string result = line;
	int num = 0;
	if (line != null)
	{
		num = line.Length;
	}
	int num2 = TotalLength - num;
	if (num2 > 0)
	{
		switch (align)
		{
		case "left":
			text = new string(' ', num2);
			break;
		case "center":
		case "scroll":
		{
			int num3 = num2 / 2;
			text2 = ((num2 % 2 == 0) ? new string(' ', num3) : new string(' ', num3 + 1));
			text = new string(' ', num3);
			break;
		}
		default:
			text2 = new string(' ', num2);
			break;
		}
		result = text2 + line + text;
	}
	else if (num2 < 0)
	{
		if (!(align == "scroll"))
		{
			result = ((!cutright) ? line.Substring(Math.Abs(num2)) : line.Substring(0, num + num2));
		}
		else
		{
			int num4 = scroll.ActNum;
			if (line.Length < num4 + num)
			{
				line = line + "      " + line;
				if (line.Length < num4 + num)
				{
					num4 = 0;
				}
			}
			result = line.Substring(num4, TotalLength);
		}
	}
	return result;
}

public class scroll
{
	public static int ActNum { get; set; }

	public void ScrollNum(int num)
	{
		ActNum = num;
	}
}
```

How the scroll ticker works (sender loop L18410-18419, `resetStrings` L10457-10460):

* `ActNum` is one **global** counter shared by every scrolling widget. It increments every `ScrollSpeed` = 150 ms (L2219 default) and wraps from 80 to 0.
* A page change or `resetStrings` sets `ActNum = 0` and delays the first step by 4 s.
* When a string fits, "scroll" is the same as "center", with the odd extra space on the **left**.
* When it overflows, the text is shown as a marquee over `line + 6 spaces + line`. Once `ActNum > len+6` it shows the start (offset 0) until the counter wraps.

With `cutright:false` (used for `Pos` and `Sect`), the **leftmost** characters are dropped on overflow: `P12` in 2 digits becomes `12`.

---

## 2. Colours (CurCol_dict), verbatim helpers

The value is RGB565 = `MSB<<8 | LSB` (`Col16Bit(msb, lsb)` L195680). If a key has no `CurCol_dict` entry, the widget's own `ColMsb/ColLsb` from the layout is used (L18691-18694). An entry persists until it is overwritten, removed (`TryRemove`), or cleared by `ChangePage`.

### 2.1 Palette (`Dynamic_Colors` L195701-195727)

| Name | RGB565 | Looks | Used for |
|---|---|---|---|
| `Neutral_Col` | `0xFF35` | off-white | neutral time, `DRS_Off` |
| `Best_Col` | `0x0F00` | green | own session best, `DRS_Active`, temp Norm, pressure Norm, energy High |
| `PBest_Col` | `0x3ABF` | blue | personal best, player row name, `DRS_Allowed` (unused on AC) |
| `SBest_Col` | `0x814F` | purple | session best (overall) |
| `Inv_Col` | `0xFBA0` | orange | invalid lap or sector, opponent in pit |
| `Plus_Col` | `0xFF00` | yellow | slower than best by 0 to 0.5 s, energy Norm |
| `Plus05_Col` | `0xF900` | red | slower than best by more than 0.5 s, temp High, energy Low, pressure Low/High, DNF/DSQ |
| `ColTemps` Low/Norm/High | `0x03D5` / `0x0F00` / `0xF900` | teal / green / red | temps |
| `ColEnergy_Wear` Low/Norm/High | `0xF900` / `0xFF00` / `0x0F00` | red / yellow / green | Kers, tyre wear, low-fuel colour (`.Low`) |
| `ColPressure` Low/Norm/High | `0xF900` / `0x0F00` / `0xF900` | | tyre pressure |

Defaults (can be overridden by UGT per-game settings, which were not found in the archived AppData, so treat them as defaults):

* `Dyn_Used` L197756: `Times = DynTime.On`, all temperature, pressure, wear and energy flags `true`, `BlinkNewBest = 3`.
* `DynTime` enum: `Off, TimeTable, TT_Inv, On, Max`.
* `Plus05val = 0.5` (L27757).
* Low/High thresholds (`High_Low_Values` L197726-197753), °C:

| Quantity | Low | High |
|---|---|---|
| Brake temp | 300 | 700 |
| Tyre temp | 70 | 100 |
| Water temp | 90 | 110 |
| Oil temp | 95 | 120 |
| Tyre pressure | 1.0 | 2.0 |
| Tyre wear | 0.3 | 0.8 |
| Energy | 0.2 | 0.7 |

* Low fuel threshold `LowFuel = 5.0` litres (L27771 / L197124).

### 2.2 Helpers, verbatim

```csharp
// L195744
private Col16Bit Get_Three_Colors(double Value, double Low, double High, ThreeColors ColType)
{
	Col16Bit result = ColType.Norm;
	if (Value < Low)
	{
		result = ColType.Low;
	}
	else if (Value > High)
	{
		result = ColType.High;
	}
	return result;
}
// Get_Temperature_Color -> ColTemps, Get_Energy_Wear_Color -> ColEnergy_Wear, Get_Pressure_Color -> ColPressure

// L195758
public Col16Bit Get_Time_Color(double time, double Best, double PB, bool Invalid, double PlusVal, double SB = 0.0, bool Ignore_PB = false)
{
	Col16Bit result = Neutral_Col;
	if (Invalid)
	{
		result = Inv_Col;
	}
	else if (time > 0.0)
	{
		if (time <= SB && SB > 0.0)
		{
			result = SBest_Col;
		}
		else if (time <= PB && PB > 0.0 && !Ignore_PB)
		{
			result = PBest_Col;
		}
		else if (Best > 0.0)
		{
			result = ((time > Best + PlusVal) ? Plus05_Col : ((!(time <= Best)) ? Plus_Col : Best_Col));
		}
	}
	return result;
}

// L195783
public Col16Bit Get_Curr_Time_Color(double RTdiff_Best, double RTdiff_PB, bool Invalid, double PlusVal, bool Ignore_PB = false)
{
	Col16Bit result = Neutral_Col;
	if (Invalid)
	{
		result = Inv_Col;
	}
	else if (RTdiff_PB < 0.0 && !Ignore_PB)
	{
		result = PBest_Col;
	}
	else if (RTdiff_Best > PlusVal)
	{
		result = Plus05_Col;
	}
	else if (RTdiff_Best < 0.0)
	{
		result = Best_Col;
	}
	else if (RTdiff_Best > 0.0)
	{
		result = Plus_Col;
	}
	return result;
}

// L195809
public Col16Bit Get_Curr_Time_Color(double RTdiffPB, bool Invalid, double PlusVal)
{
	Col16Bit result = Neutral_Col;
	if (Invalid)
	{
		result = Inv_Col;
	}
	else if (RTdiffPB > PlusVal)
	{
		result = Plus05_Col;
	}
	else if (RTdiffPB < 0.0)
	{
		result = Best_Col;
	}
	else if (RTdiffPB > 0.0)
	{
		result = Plus_Col;
	}
	return result;
}

// L195831 (opponent gaps; GapPlusVal/GapPlus5Val = 5/30 in "Race", else 0.5/5, SetSession L27868)
public Col16Bit Get_Gap_Color(double LpsGap, double TimeGap, double PlusVal, double Plus30Val)
{
	Col16Bit result = Neutral_Col;
	if (LpsGap > 1.0 || LpsGap < -1.0)
	{
		result = Plus05_Col;
	}
	else if (TimeGap > Plus30Val || TimeGap < 0.0 - Plus30Val)
	{
		result = Plus05_Col;
	}
	else if (TimeGap > PlusVal || TimeGap < 0.0 - PlusVal)
	{
		result = Plus_Col;
	}
	else if (TimeGap > 0.0 || TimeGap < 0.0)
	{
		result = Best_Col;
	}
	return result;
}

// GameData L29547
public void SetCurrColor(string KeyShort, bool invalid, DynTime Lowest_Inv, DynTime Lowest_Neutral)
{
	if (invalid)
	{
		if (MainForm.GameData.DynColSetting.Times >= Lowest_Inv)
		{
			MainForm.CurCol_dict[KeyShort] = MainForm.dyn_col.Inv_Col;
		}
	}
	else if (MainForm.GameData.DynColSetting.Times >= Lowest_Neutral)
	{
		MainForm.CurCol_dict[KeyShort] = MainForm.dyn_col.Neutral_Col;
	}
	else if (MainForm.GameData.DynColSetting.Times >= Lowest_Inv)
	{
		MainForm.CurCol_dict.TryRemove(KeyShort, out var _);
	}
}

// GameData L29566
public void CheckInvalidColor(string KeyShort, bool invalid, DynTime Lowest)
{
	if (MainForm.GameData.DynColSetting.Times >= Lowest)
	{
		if (invalid)
		{
			MainForm.CurCol_dict[KeyShort] = MainForm.dyn_col.Inv_Col;
		}
		else
		{
			MainForm.CurCol_dict.TryRemove(KeyShort, out var _);
		}
	}
}
```

With the default `Times = On`, the `else` branches that call `CheckInvalidColor` are never taken on AC.

### 2.3 New-best blinking (`Set_BlinkString` L29520, `AdvanceBlinkcycle` L29496)

```csharp
public bool Set_BlinkString(string KeyShort, int Digits, BestType Type)
{
	if ((double)DynColSetting.BlinkNewBest < DiffObj.BlinkCycleCount[(int)Type] || DiffObj.BlinkCycleCount[(int)Type] == 0.0)
	{
		return false;
	}
	int num = (int)DiffObj.BlinkCycleCount[(int)Type];
	if (DiffObj.BlinkCycleCount[(int)Type] - (double)num < 0.07)
	{
		TmpString = "";
	}
	else if (DiffObj.BlinkCycleCount[(int)Type] - (double)num < 0.25)
	{
		TmpString = Transform.toClock(DiffObj.diff[(int)Type], Digits);
	}
	else
	{
		if (!(DiffObj.BlinkCycleCount[(int)Type] - (double)num < 0.31))
		{
			return false;
		}
		TmpString = "";
	}
	MainForm.CurStr_dict[KeyShort] = Transform.align(TmpString, Digits);
	return true;
}

public void AdvanceBlinkcycle()
{
	if (Tick_change)
	{
		for (int i = 0; i < DiffObj.BlinkCycleCount.Length; i++)
		{
			if (DiffObj.BlinkCycleCount[i] > 0.0)
			{
				int num = (int)DiffObj.BlinkCycleCount[i];
				DiffObj.BlinkCycleCount[i] += 0.01;
				if (DiffObj.BlinkCycleCount[i] - (double)num > 0.3)
				{
					DiffObj.BlinkCycleCount[i] = (double)num + 1.07;
				}
				if (num >= 5)
				{
					DiffObj.BlinkCycleCount[i] = 0.0;
				}
			}
		}
	}
	Tick_change = false;
}
```

How it plays out:

* `Tick_change` is set about every 45 ms (`TickCount > Ticker_50 + 44`, L29192). `AdvanceBlinkcycle()` runs at the end of each `ProcessLCD` (L202181).
* A new best sets `BlinkCycleCount[type] = 0.01` in `ProcessGrafic`.
* The widget then shows:
  1. blank for about 6 ticks;
  2. the **diff** (`toClock(diff, Digits)`, no `+` prefix, so an improvement shows `-0.234`) for about 18 ticks;
  3. blank for about 6 ticks;
  4. two more diff/blank cycles.
* Blinking stops once the count exceeds 3.0 (`BlinkNewBest = 3`). That is about 3 × 1.1 s in total.
* The diff is taken *before* the best is updated: `diff[BestLpt] = LastLap - oldBest`. On the first valid lap `oldBest = 0`, so the "diff" shown is the full lap time.

`BestType` indices (L197784):

| Index | Types |
|---|---|
| 0 | `BestLpt` |
| 1-14 | `BestS1`-`BestS14` |
| 15 | `SB` |
| 16-29 | `SBS1`-`SBS14` |
| 30 | `PB` |
| 31+ | `PBS1`… |
| 45 | `Optimal` |

On AC the diffs are set at L202893-202996.

Which widget blinks for which type:

| Widget(s) | BestType |
|---|---|
| `LastLpt` and `BestLpt` | `BestLpt` (index 0) |
| `PBlpt` | `PB` |
| `LastSn` and `BestSn` | `BestSn` |
| `PBSn` | `PBSn` |

---

## 3. AC state that feeds the widgets (ProcessGrafic / Physic / Passiv)

Terms used below: `Grafic` = AC graphics page, `Physic` = physics page, `Passive` = static page.

| UGT field | AC source and meaning | Line |
|---|---|---|
| `CarObj.Sector` | `graphics.currentSectorIndex + 1` (1..3) | 202668 |
| `CarObj.LapNum` | `graphics.completedLaps + 1` | 202665 |
| `CarObj.LapsTotal` | `graphics.numberOfLaps`. `Unlimited_Laps = numberOfLaps <= 0` (set on reset) | 202666, 202402 |
| `CarObj.Position` | `graphics.position` | 202667 |
| `TimeObj.CurrLapTime` | `graphics.iCurrentTime / 1000` (s) | 203113 |
| `TimeObj.LastLapTime` | `graphics.iLastTime / 1000`, latched when `completedLaps` increments | 202811 |
| `TimeObj.CurrS1` | While in S1 it equals `CurrLapTime` (running). On entering S2 it is set to `graphics.lastSectorTime/1000`. **Reset to 0 at the line.** | 203122, 202786, 202855 |
| `TimeObj.CurrS2` | While in S2 it is `CurrLapTime - CurrS1` (only if `CurrS1 > 0`). On entering S3 it is `lastSectorTime/1000`. Reset to 0 at the line. | 203128, 202797 |
| `TimeObj.CurrS3` | While in S3 it is `CurrLapTime - (CurrS1+CurrS2)` (only if both > 0). Reset to 0 at the line, so it never shows a "completed" S3. | 203133 |
| `LastS1/2/3` | At the line: `LastS1=CurrS1`, `LastS2=CurrS2`, `LastS3=lastSectorTime/1000` (3-sector track). On 2-sector tracks `LastS2=lastSectorTime`. | 202845-202854 |
| `BestLpt` | Own best **valid** lap in this UGT session (reset on session, car or track change). Updated at the line if `LastLap <= Best && !invalid`. | 202985-202987 |
| `BestS1..3` | Own best valid sector this session. **Only updated at lap completion**, from `LastSn`, not at the sector line. | 202903-202959 |
| `PB`, `PBS1..3` | Personal best, **persisted per car + track + session type** in `telemetry\AC\profile_<n>\<carModel>\stats.ini`, section `[Practice]/[Qualy]/[Race]/...`, keys `<track>` and `<track>_S1..S3`. Loaded on reset (L202561-202568) and updated when a new session best also beats the PB (L202907-202993). Real files live under `%APPDATA%\UltimateGameTech\telemetry\AC\profile_<n>\...`. | |
| `RTdiff_Best` / `RTdiffPB` | Live delta. `CurrLapTime - BestTBL[permille]` / `PBTBL[permille]`, where the tables are indexed by `normalizedCarPosition*1000` and hold elapsed lap time at each 0.1 % of the lap. If the reference cell is 0 the delta is **0**. At the line it is set to `LastLap - Best` / `LastLap - PB` and **held until `CurrLapTime > 3 s`** (`refreshDelta`). The PB table is persisted in `PB_<Session>_Data.log`. | 203136-203167, 202812-202829 |
| `invalid / invSect / inLap / OutLap / invalid_Sn` | `isInPit||isInPitLane` in S3 sets `inLap`, in S1 sets `OutLap`, otherwise `invSect`. A penalty flag sets `invSect`. A user key "Sector_Invalid" toggles `invSect`. These are latched into `invalid_S1/S2/S3` and `invalid` at the sector and lap lines. AC's own "lap invalid" (tyres out) is **not** used. | 202671-202693, 202787-202803, 202878-202887 |
| `CarObj.FuelLeft` | `physics.fuel` (litres) | 202725 |
| `CarObj.RT_DeltaFuelLaps` | See `FlLpsDelt` below. | 202757-202780 |
| `CarObj.Speed` | `Convert.ToInt32(speedKmh)`, or `speedKmh*0.621371` if `!KPH` | 203267-203274 |
| `CarObj.RPM` / `Gear` | `physics.rpms` / `physics.gear - 1` (−1 = R, 0 = N) | 203317-203318 |
| `CarObj.DRS` | `physics.drsEnabled != 0` | 203217 |
| `CarObj.Kers` | See `Kers` below. | 203219-203236 |
| `CarObj.Damage` | `max(physics.carDamage[0..4])` | 203319-203340 |
| Tyre temps | `(int)physics.tyreTempM[i]`, the **middle surface** temperature (UGT's struct field `TireTemp_Center` sits after `brakeTemp[4]` and `clutch`, in the `tyreTempI/M/O` block, L200157-200189), **not** `tyreCoreTemperature`. `(int)` truncates. °F = `v*1.8+32` if `!Metric`. | 203279-203303 |
| Brake temps | `(int)physics.brakeTemp[i]` (truncated) | 203291-203294 |
| `BaseObj.Session` | `graphics.session` mapped to `"Practice"`, `"Qualy"`, `"Race"`, `"Hotlap"`, `"TimeAttack"`, `"Drift"`, `"Drag"`, else `"Unknown"` | 202336-202367 |
| `BaseObj.TrackName` | `static.track` + (`"_" + static.trackConfiguration` if not empty), internal ids, e.g. `ks_red_bull_ring_layout_gp` | 203417-203433 |
| `BaseObj.CarName` | `static.carModel`, the internal id, e.g. `rss_formula_hybrid_2024` | 203412-203415 |

A lap-count decrease, a session-type change, a car change or a track change triggers `reset`. That zeroes all of `CarObj` and `TimeObj` and reloads PBs (L202295-202503).

---

## 4. Per-key reference: car and status

Every string is finally wrapped as `CurStr_dict[k] = Transform.align(text, Digits)`: right-aligned, cut on the right, unless noted otherwise. `D` = `widget.Digits`.

| Key | (a) Exact C# (AC) | (b) Meaning / units | (c) Colour | (d) SimHub |
|---|---|---|---|---|
| `SPD` L201426 | `Transform.Number(CarObj.Speed, D, D, 0)` then `align` | Integer speed, km/h (mph if KPH off), **zero-padded to D** (`087`) | none (widget colour) | `SpeedKmh`, rounded with `Convert.ToInt32` (banker's) |
| `Gear` L201431 | `Gear==0 ? align("N",D) : Gear>=0 ? Number(Gear, D, 0, 0) : align("R",D)` | N, R, 1..n | none | `Gear` (already "N"/"R"/"1"...) |
| `RPM` L201436 | `Transform.Number(CarObj.RPM, D, D, 0)` | Engine rpm, zero-padded to D | none | `Rpms` (int) |
| `DRS` L201441 | text is always `align("DRS", D)` | Fixed text "DRS". **NA** if the car has no DRS (`static.hasDRS==0`). | `drsEnabled` gives `DRS_Active 0x0F00`, otherwise `DRS_Off 0xFF35`. (`drsAvailable` is **not** shown on the text widget, only on the LEDs.) | `DRSEnabled`. Availability: compute from raw `StaticInfo.HasDRS` (SimHub `GameRawData`; verify the field name). |
| `Pos` L201453 | `"P" + CarObj.Position`, `align(text, D, "right", cutright:false)` | `P5`. On overflow the **left** chars are dropped (`P12` with D=2 gives `12`). | none | `Position` |
| `Sect` L201458 | `align("S" + CarObj.Sector, D, "right", cutright:false)` | `S1`/`S2`/`S3` (currentSectorIndex+1) | none | `CurrentSectorIndex`. **Verify whether SimHub's value is 1-based**; UGT is 1-based. |
| `Laps` L201462 | `Unlimited_Laps ? LapNum+"/--" : LapNum+"/"+LapsTotal`, `align(text, D, "center")` | Current lap / total. `--` when the session has no lap count. | none | `CurrentLap` + `"/"` + (`TotalLaps>0 ? TotalLaps : "--"`) |
| `Damage` L201482 | `(Damage > 1f \|\| PartsDetached) ? "Major" : !(Damage > 0f) ? "None" : "Minor"` | Max of AC `carDamage[5]`: 0 → None, (0,1] → Minor, >1 → Major. `PartsDetached` is never set on AC. | none | compute: `max(raw Physics.CarDamage[0..4])`. **Uncertain:** whether SimHub's `CarDamagesMax` is the same raw scale (it may be normalised to %). Use the raw array to be exact. |
| `Kers` L201502 | `Transform.Number(CarObj.Kers, D, D, 0)` (zero-padded integer) | **KERS car** (`hasKERS`): `Kers = ToInt16(kersMaxJ/1000 - kersCurrentKJ)`, kJ left this lap. **ERS car** (`hasERS` and not `hasKERS`, e.g. RSS hybrids): `Kers = ToInt16(kersInput*100)`, the **current deployment input %**, not battery. NA if neither. | if `DynColSetting.Energy`: `Get_Energy_Wear_Color(Kers_percent, 0.2, 0.7)`, i.e. <0.2 red `0xF900`, >0.7 green `0x0F00`, else yellow `0xFF00`. `Kers_percent` is `E_LapLeft_Percent` (KERS) or `kersInput` (ERS). | compute from raw physics `KersInput`/`KersCurrentKJ` and static `KersMaxJ`/`HasERS`/`HasKERS`. `ERSPercent` in SimHub is probably battery charge, which is **not** what UGT shows for ERS cars (flagged). |
| `Fuel` L201536 | `Transform.Number(CarObj.FuelLeft, D, 2, prefix:false)` | Litres, as many decimals (max 2) as fit: D=4 `45.7`, D=5 `45.67`, D=3 `46` | if `DynColSetting.LowFuel`: `FuelLeft < 5.0` gives `ColEnergy_Wear.Low 0xF900` (red), otherwise **the override is removed** (widget colour) | `Fuel` |
| `FlLpsDelt` L201562 | `Transform.Number(CarObj.RT_DeltaFuelLaps, D, 2)` (**prefix = true**) | **Fuel-laps delta**, i.e. surplus (+) or shortfall (−) in laps. Non-race: `(Fuel-1)/avg - (1 - normPos)`, laps of fuel beyond the next S/F line with a 1 L reserve. Race: `(Fuel-1)/avg - (LapsTotal - (completedLaps+normPos))`, laps of fuel left over at the flag. `avg` = litres per lap measured since stint or race start, only on non-in/out laps. Formats: `+1.25`, `-0.50`, `0.00` (0 until avg is known). Quirk: timed race (`LapsTotal=0`) gives a meaningless large + value. | none | compute: `(Fuel-1)/LitersPerLap - lapsRemaining`. SimHub `DataCorePlugin.Computed.Fuel_LitersPerLap` or your own average. For the non-race variant, `lapsRemaining = 1 - TrackPositionPercent`. |
| `FLTyretemp`…`RRTyretemp` L201953-201987 | `Transform.Number(CarObj.<wheel>.TempCenter, D, 0, prefix:false, Round:false)` | Integer °C (truncated by `(int)`, then formatted) of the **middle** tread temperature | if `TyreTemp`: `Get_Temperature_Color(v, 70, 100)`: <70 `0x03D5`, >100 `0xF900`, else `0x0F00` | `TyreTemperatureFrontLeftMiddle` etc. (not the averaged `TyreTemperatureFrontLeft`, flagged: check the names in your SimHub build) |
| `FLBrakeTemp`…`RRBrakeTemp` L202061-202095 | `Transform.Number(CarObj.<wheel>.BrakeTemp, D, 0, prefix:false, Round:false)` | Integer °C (truncated) | if `BrakeTemp`: `Get_Temperature_Color(v, 300, 700)`, same three colours | `BrakeTemperatureFrontLeft` etc. |
| `WaterTemp` / `OilTemp` L202097 / 202106 | **AC: `NA`** (not in dict). Code if enabled: `Number(v, D, 1, prefix:false, Round:false)` | °C, 1 decimal if room | Water `Get_Temperature_Color(v, 90, 110)`, Oil `(v, 95, 120)` | `WaterTemperature`, `OilTemperature` (AC reports 0 or nothing; you may choose to fill them) |

---

## 5. Per-key reference: lap and sector times

The `invalid` flag combos are verbatim from the code. "TT" means `DynColSetting.Times >= DynTime.TimeTable` and "On" means `>= DynTime.On`; both are true by default.

| Key | (a) Exact C# (AC) | (b) Meaning | (c) Colour | (d) SimHub |
|---|---|---|---|---|
| `CurLpt` L201598 | `Transform.toClock(TimeObj.CurrLapTime, D, prefix:false, leadZero:false, 1)` | Running lap time, **1 decimal** (`1:23.4`); `0.0` before the timer starts | On: `Get_Curr_Time_Color(RTdiff_Best, RTdiffPB, invSect\|\|invalid\|\|inLap\|\|OutLap, 0.5)`: invalid orange, ahead of PB blue, >+0.5 red, <0 green, >0 yellow, =0 neutral | `CurrentLapTime` |
| `LastLpt` L201611 | blink (`BestType.BestLpt`), else `Transform.toClock(TimeObj.LastLapTime, D)` | Last completed lap, 3 decimals; `0.000` before the first lap | On: `Get_Time_Color(LastLapTime, BestLpt, PB, Inv_LastLap, 0.5, SB=0)`: invalid orange, ≤PB blue, >Best+0.5 red, >Best yellow, else green | `LastLapTime` |
| `BestLpt` L201627 | blink (`BestLpt`), else `Transform.toClock(TimeObj.BestLpt, D)` | Own best **valid** lap this session; `0.000` until one exists | TT: always `Best_Col 0x0F00` | `BestLapTime` (SimHub may count invalid laps; UGT drops laps invalidated by pit or penalty. Flagged.) |
| `PBlpt` L201651 | blink (`PB`), else `Transform.toClock(TimeObj.PB, D)` | Personal best for car + track + **session type** (from stats.ini) | TT: `PBest_Col 0x3ABF` | `AllTimeBest`. It is keyed by car and track, **not** by session type. Alternatively read UGT's `stats.ini` for identical numbers. |
| `SBlpt` | **AC: `NA`**. Code if enabled: blink (`SB`), else `toClock(TimeObj.SB, D)` | Overall session best (all drivers) | TT: `SBest_Col 0x814F` | compute: min `BestLapTime` over `Opponents` + player |
| `DffBest` L201572 | `Transform.toClock(TimeObj.RTdiff_Best, D, prefix:true)` | Live delta to own session best, `+0.123`/`-0.456`; `0.000` if no best or no reference at this point; holds the final lap delta for 3 s after the line | On: `Get_Curr_Time_Color(RTdiff_Best, RTdiffPB, OutLap\|\|inLap\|\|invalid\|\|invSect, 0.5, Ignore_PB:true)`: invalid orange, >+0.5 red, <0 green, >0 yellow, 0 neutral | `DeltaToSessionBest` (persistant tracker; may be null, so treat null as 0) |
| `DffPB` L201585 | `Transform.toClock(TimeObj.RTdiffPB, D, prefix:true)` | Live delta to PB lap | On: `Get_Curr_Time_Color(RTdiffPB, OutLap\|\|inLap\|\|invalid\|\|invSect, 0.5)`, same thresholds | `DeltaToAllTimeBest` |
| `CurS1` L201676 | if `Sector==1`: `toClock(CurrS1, D, false, false, 1)`, otherwise `toClock(CurrS1, D)` | S1 of the current lap: running with 1 decimal while in S1, then fixed with 3 decimals; `0.000` again after the line (it is reset while the new S1 runs, but in S1 it shows the running time) | In S1: `SetCurrColor(k, invSect\|\|OutLap, TT_Inv, On)`, i.e. orange if invalid, else neutral `0xFF35`. After S1: On: `Get_Time_Color(CurrS1, BestS1, PBS1, invalid_S1, 0.5, SBS1=0)` | running: `CurrentLapTime` while `CurrentSectorIndex==1`; completed: `Sector1Time` |
| `CurS2` L201697 | `Sector==2 ? toClock(CurrS2, D, false, false, 1) : toClock(CurrS2, D)` | S2 of the current lap; `0.000` during S1 | In S2: `SetCurrColor(k, invSect, TT_Inv, On)`. Otherwise: `Get_Time_Color(CurrS2, BestS2, PBS2, invalid_S2, 0.5, SBS2)` | running: `CurrentLapTime - Sector1Time`; completed: `Sector2Time` |
| `CurS3` L201718 | `Sector==3 ? toClock(CurrS3, D, false, false, 1) : toClock(CurrS3, D)` | S3 running time; outside S3 it is always `0.000` on AC (reset at the line). **NA** on 2-sector tracks. | In S3: `SetCurrColor(k, invSect\|\|inLap, TT_Inv, On)`. Otherwise: `Get_Time_Color(CurrS3, BestS3, PBS3, invalid_S3, 0.5, SBS3)` | running: `CurrentLapTime - S1 - S2` |
| `CurS4` | **NA** on AC | n/a | n/a | n/a |
| `LastS1..3` L201739-201785 | blink (`BestSn`), else `toClock(TimeObj.LastSn, D)` | Sector times of the last completed lap | On: `Get_Time_Color(LastSn, BestSn, PBSn, Inv_LastSn, 0.5, SBSn)` | `Sector1LastLapTime`…`Sector3LastLapTime` |
| `LastS4` | **NA** | | | |
| `BestS1..3` L201787-201821 | blink (`BestSn`), else `toClock(TimeObj.BestSn, D)` | Own best valid individual sector this session (updated only at lap end) | TT: `Best_Col 0x0F00` | `Sector1BestTime`… (best individual sector; **not** `SectorXBestLapTime`, which is the sectors of the best lap) |
| `BestS4` | **NA** | | | |
| `PBS1..3` L201823-201857 | blink (`PBSn`), else `toClock(TimeObj.PBSn, D)` | All-time best sector for car + track + session type (stats.ini) | TT: `PBest_Col 0x3ABF` | compute and persist yourself, or read UGT `stats.ini` keys `<track>_S1..S3`. **No confirmed SimHub property.** |
| `PBS4` | **NA** | | | |
| `SBS1..4` | **NA** on AC | | | |

---

## 6. Per-key reference: session strings

| Key | (a) Exact C# | (b) Meaning | (c) Colour | (d) SimHub |
|---|---|---|---|---|
| `Session` L201889 | `Transform.align(BaseObj.Session, D, "scroll")` | `Practice` / `Qualy` / `Race` / `Hotlap` / `TimeAttack` / `Drift` / `Drag` / `Unknown` | none | map `SessionTypeName` onto these exact words |
| `Track` L201893 | `Transform.align(BaseObj.TrackName, D, "scroll")` | `static.track` + `"_" + static.trackConfiguration` when a config exists (AC internal ids) | none | build from raw `StaticInfo.Track` + `"_"` + `StaticInfo.TrackConfiguration` to match exactly. SimHub `TrackCode`/`TrackId` may use a different separator (unverified). |
| `Car` L201897 | `Transform.align(BaseObj.CarName, D, "scroll")` | `static.carModel` (internal folder id) | none | `CarId` (internal id). `CarModel` is the pretty name, which differs from UGT. |
| `CarClass` | **AC: `NA`** (code: `align(BaseObj.CarClass, D, "scroll")`) | n/a | | `CarClass` if you want to fill it |

---

## 7. `PitDelt`, `InfoState`, `D_n_*`: NA on AC; reference from other sim classes

None of these exist for AC in UGT 1.6.819. If you want to fill them, this is how UGT formats them elsewhere. I used **`DSclassRFactor2.ProcessLCD` L134261-134599** as the generic reference; R3E L127621, AMS2 L115086, iRacing L109996 and the test class L170513 share its 3-state InfoState. F1 20xx (L56994-57270) differs and is noted.

### 7.1 `PitDelt` (F1 2021 L56976; the same pattern appears in 2013-2016 and GridA classes)

`Transform.toClock(PLDelta, D)`. `PLDelta = PitLaneTime − DrvByTime` from `pits.ini` (`[Stop]<track>` minus `[OnTrack]<track>`), clamped at 0. That is the learned **time lost by making a pit stop** compared with driving past (Layout-Compendium: "Time Lost During a Pitstop compared to YOUR last Regular Time (from Pit entrance to Pit exit)"). Format: 3 decimals, no sign, `0.000` if unknown. No colour. If `pits.ini` has only `[OnTrack]` entries (common), the value is 0.

SimHub: no direct equivalent. Compute it by timing pit-lane transit (`IsInPitLane` edges) against a normal lap segment, or hard-code per track. `LastPitStopDuration` is a stationary or lane time, not a delta.

### 7.2 `InfoState` and the button cycle

* `GameData.SubInfo ∈ {0,1,2}` is advanced by the button command **`Extra_Info_Cycle`** (L28700-28726): `SubInfo < 2 ? SubInfo++ : 0`. It only works if the current layout has `Drivers > 0`. Each press calls `resetStrings(OldStringsOnly:true)` and `ChangePage(current)`, which forces a full redraw and resets the scroll counter.
* `SubPage` is advanced by **`Sub_Page_Cycle`** (L28673-28699): next page if `NumOfDrivers / Drivers > SubPage+1`, else back to 0. `Page_Up`/`Page_Down` reset it when it is out of range.
* `layout.Drivers` = number of distinct `n` in the layout's `D_n_*` widgets (L161947). For example, a layout using n = 0..7 shows 8 drivers.
* `InfoState` text, `align(text, D, "scroll")`:
  * rF2 family: 0 → `Best`, 1 → `Last`, 2 → `Curr`.
  * F1 family: 0 → `Best`, 1 → `Best`, 2 → `Last`.

### 7.3 How rows are chosen

`num2 = n + 1 + SubPage * layout.Drivers`, and the row shows **the car whose race position == num2** (L134298-134307). It is a plain **leaderboard window** (P1-P8, then P9-P16 with Sub_Page_Cycle). It is **not** relative to the player. If no car has that position, the key is removed from `CurStr_dict`/`CurCol_dict`, so the widget is drawn as an **empty string** (L134308-134312 plus the send loop L18668-18671).

### 7.4 Per-field formatting (rFactor2 reference; `me` = row is the player)

| Key | SubInfo 0 ("Best") | SubInfo 1 ("Last") | SubInfo 2 ("Curr") | Colour |
|---|---|---|---|---|
| `D_n_Pos` | `Transform.Number(num2, D, D, 0)`, zero-padded (`01`) | same | same | none |
| `D_n_Name` | `align(Driver, D, "scroll")` | same | same | `me` gives `PBest_Col 0x3ABF`, else `Neutral_Col 0xFF35` |
| `D_n_Lap` | `toClock(BestLapTime, D)` | `toClock(LastLapTime, D)` | `toClock(Curr_Lap_Time, D, false, false, 1)` | S0: `Get_Time_Color(best, best, myPB, false, 0.5, SB, ignore_PB: !me)`. S1: `Get_Time_Color(last, best, myPB, Invalid_LastLap, 0.5, SB, !me)`. S2: `me` uses `Get_Curr_Time_Color(RTdiff_Best, RTdiffPB, invalid, 0.5)`, others `SetCurrColor(invalid, TT, TT)` |
| `D_n_S1..S3` | `toClock(Best_Sn, D)` | `toClock(Last_Sn, D)` | in that sector: `toClock(Cur_Sn, D, false, false, 1)`, else `toClock(Cur_Sn, D)` | analogous, with the player's `PBSn`/`SBSn` |
| `D_n_info1` | gap to **leader** | gap to **player** | `align(Car, D, "scroll")`, colour Neutral | gaps: `Get_Gap_Color(laps, time, GapPlusVal, GapPlus5Val)` (Race: 5 s / 30 s, else 0.5 / 5) |
| `D_n_info2` | `"Pit"` if in pits (colour `Inv_Col 0xFBA0`), else `""` | `NumPitstops + "ST"` (e.g. `2ST`) | `NumPenalties + "PN"` (red `0xF900` if > 0) | Finish status overrides every state: `Fin` (green) / `DNF` / `DSQ` (red). All use `align(..., "scroll")`. Colour is always set, Neutral by default. |

`info1` gap string (L134553), verbatim:

```csharp
value = ((num4 > 1.0 || num4 < -1.0)
    ? (Transform.Number(num4, widget.Digits - 1, 1, prefix: true, Round: false) + "L")
    : ((BaseObj.SessionID <= 9) ? Transform.toClock(num5, widget.Digits, prefix: true)
                                : Transform.toClock(num5, widget.Digits, prefix: true, leadZero: false, 1)));
```

* `num4` is laps and `num5` is seconds.
* More than 1 lap apart shows `+2.3L`. Otherwise it shows `+1.234` / `-0.567`, and `0.000` for the reference car.
* Sign convention (F1 2021 computation L58810-58896): in **Race**, `TimeBehindLeader` is positive, and `Time_2_Player = (their gap to leader) − (my gap to leader)`, which is **positive when the opponent is behind you**. `Laps_2_Player = myLaps+myFrac − theirLaps−theirFrac`. In **non-race** sessions the gaps are best-lap differences: `their Best − leader/my Best`, or 0 if either is missing.

F1 20xx differences (L57075-57268):

* `Lap` and `S*` show Best for SubInfo 0 **and** 1, and Last for SubInfo 2.
* `info1` state 2 is tyre compound text (e.g. `S-C3`) with a tyre colour.
* The race gap uses 1 decimal (`SessionID == 2`).

SimHub mapping for the opponent table:

* `Opponents` sorted by `Position`, taking `Position == n+1+SubPage*8`.
* `Name`; `BestLapTime` / `LastLapTime` / `CurrentLapTime`.
* Sector times: SimHub `Opponent` exposes best, last and current sector splits under names I could not confirm from this source. **Verify in your SimHub SDK.**
* `GapToLeader` / `GapToPlayer`: **verify the sign**, and flip it so that "behind player" is positive.
* `IsCarInPit` / `IsCarInPitLane`, and `IsPlayer` for the blue name.
* Pit-stop and penalty counts are probably not available for AC, so show `0ST`/`0PN` or leave them blank.

---

## 8. Porting checklist and open uncertainties

1. Wrap every value with `align(text, Digits)`. The exceptions are `Pos`/`Sect` (`cutright:false`), `Laps` (`"center"`) and the name strings (`"scroll"`, with the global `ActNum` advancing 1 every 150 ms, wrapping at 80 and reset on page change with a 4 s hold).
2. Unavailable time is `0.000` (or `0.0`), never dashes. An unavailable key on AC is `"NA"`. An unknown key is `"???"`. An empty opponent slot is `""`.
3. Keep `CurCol_dict` semantics. Once set, a colour persists. `Fuel` explicitly removes its override when not low. Other keys always set one when the dynamic setting is on.
4. Blinking after a new best replaces `LastLpt/BestLpt/PBlpt/LastSn/BestSn/PBSn` text for about 3.3 s (section 2.3).

Flagged uncertainties:

* AC `carDamage` scale compared with SimHub `CarDamagesMax` (it affects the Minor/Major threshold of 1.0).
* Whether SimHub `CurrentSectorIndex` is 1-based for AC.
* SimHub AC tyre temperature property names (UGT uses the **middle** tread temperature, `tyreTempM`). This corrects `UGT-telemetry-protocol.md` §6, which says "core temp".
* The ERS `Kers` value is `kersInput*100` (deployment), not battery; confirm this is what you want. UGT's hasKERS path shows remaining kJ.
* UGT dynamic-colour settings could be overridden per game in UGT settings files not present in the archive. The defaults are documented above.
* SimHub property names for opponent sector splits, and the sign convention of `GapToPlayer`.
* `BestLapTime`/`AllTimeBest` validity rules differ from UGT's: UGT only excludes laps touching the pits, penalty laps and manual sector-invalid laps, and does not use AC's tyres-out invalidation. `AllTimeBest` is not split by session type.
