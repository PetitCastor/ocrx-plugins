# SignatureReaderSpike

A spike, not a product: an offline test of reading the Star Citizen RS signature badge **without
Windows OCR and without moving the ROI**. It is outside `OcrxPlugins.slnx`, so CI does not build it
and nothing ships from it.

## Why

SignaturePlugin reads the badge through one small fixed ROI and Windows OCR. That has two limits:

- **Following.** The badge is anchored to the target in world space and drifts as the ship turns.
  A 70x44 rect loses it between two ticks, and both attempts at moving the ROI cost too much.
- **Resolving.** Windows OCR returns a string with no confidence. On small digits (wide FOV, low
  resolutions) a single frame reads right about half the time, and can read a wrong but valid number.

The idea tested here: take **one wide, fixed band** around screen centre, find the badge inside it
on every read, and classify each digit against templates so every digit carries a score.

## How it reads

1. **Band.** 240x190 reference pixels (1440-high reference, scaled by frame height, centred on the
   frame's centre column). It covers the badge row over the whole FOV range. See `Band.cs`.
2. **Ink map.** Red channel, then a white top-hat (image minus its morphological opening). Broad
   areas cancel; thin bright strokes stay. The red channel is what removes the cyan rock outlines
   of scan mode, which otherwise run across the digits. See `Ink.cs`.
3. **Badge search.** Connected components; the badge is a run of equal-height glyphs on one line
   with a taller pin icon just left of it. No position and no colour is assumed. See `BadgeFinder.cs`.
4. **Digits.** Merged blobs are cut at their emptiest columns; each glyph is resampled to 12x16,
   normalised, and correlated with one template per digit and per font. See `Reader.cs`.
5. **Several thresholds.** Four ink thresholds are tried and the most confident reading is kept,
   because no single threshold separates the glyphs on every frame.
6. **Gate.** A reading is accepted only above a mean and a worst-digit score. See `Gate.cs`.

Two fonts exist, one per cockpit style: a condensed one (orange pin, tag `c`) and a wide one (green
pin, tag `w`). Templates are kept per font and the font is chosen by total score.

## Result on the current corpus

27 hand-labelled crops, each read with templates built from the **other** frames only (leave one
frame out). 25 of the 27 segment cleanly enough to supply template samples.
The badge-free frames are the Item Tracker kiosk frames under `tests/fixtures/corpus`.

| Measure | Result |
|---|---|
| Badge found | 27/27 |
| Value right | 23/27 |
| Wrong value accepted | 0 |
| Accepted on badge-free frames | 0/11 |
| Band shifted by 60 px sideways or 25 px vertically | 92/92: all 4 shifts of each of the 23 frames read centred |
| Time per read | about 2 ms, unoptimised |

The four frames that are not read are rejected, not misread:

- the only `3,400` frame: no other frame supplies a template for 3 or 4 in that font;
- two frames where, looking at the crops, a rock outline covers part of the digits (the tool only
  reports that they were rejected, not why);
- one windowed 1602x1232 frame, read as `2000` but under the gate.

All six 2560x1440 frames at the top of the FOV range and all four 1600x1200 frames read right; their
digits are 7 to 9 px tall.

## What this does not prove

- **Digit coverage.** The corpus only holds 0, 2, 3, 4 in the condensed font and 0, 1, 2, 7 in the
  wide one, so this was never a choice between ten digits. It is weaker still for the condensed
  font: 3 and 4 exist in one frame only, so every other condensed frame is a `2,000` told apart
  from 0, 2 and two single-sample templates. This is the blocker.
- **Few distinct values.** Condensed: `2,000` seven times, `3,400` once. Wide: `7,200` eleven
  times, `2,000` four, `17,200` three, `10,000` once. A leave-one-out hit mostly means another
  frame of the same value was in the templates.
- **Near-duplicates.** Several frames come from the same scene a second apart, which flatters a
  leave-one-out score.
- **The gate** thresholds were picked on the same frames they are measured on.
- **Not built:** fusing scores across frames, snapping a doubtful digit to the signature table,
  any live run, 4K (the band exceeds the 256 KiB `Pixels` ROI budget there).

## Running it

From the repository root:

```powershell
dotnet run --project tools/SignatureReaderSpike -c Release
```

It prints one line per frame and a summary. Exit code: 1 if any wrong value or any badge-free frame
was accepted, 2 if the badge-free frames were not found (run it from the repository root), else 0.

Contact sheet of what the badge search found, enlarged, to check or label crops by eye:

```powershell
dotnet run --project tools/SignatureReaderSpike -c Release -- sheet sheet.png
```

## Adding frames

The corpus stores the band plus a margin, not whole frames. To add saved engine frames:

```powershell
dotnet run --project tools/SignatureReaderSpike -c Release -- export tools/SignatureReaderSpike/corpus frame1.png frame2.png
```

That writes the crops and prints one line per frame. Append the lines to `corpus/labels.tsv`,
replacing the two `?` with the value shown and the font tag (`c` or `w`). A line left at `?` is
skipped by the measurement and shown on the contact sheet.

Most wanted: frames showing 1, 5, 6, 7, 8, 9 in the condensed font and 3, 4, 5, 6, 8, 9 in the wide
one.

## Provenance

The approach (a fixed band, an ink map, template digits with per-digit scores) follows the general
technique of the open-source [IRIS](https://github.com/Niteb1rd/IRIS) companion app. IRIS is
GPL-3.0; none of its code or template data is used here. Everything in this folder was written for
this repository and its templates are built at run time from the corpus above.
