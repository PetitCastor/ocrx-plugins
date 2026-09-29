# SignatureOcrSweep

Console tool for TASK-SIG-OCR-00: measures how well the SignaturePlugin counter read (Windows OCR on the
RS badge) survives different resolutions and preprocessing, on a directory of PNG frames plus a manifest.
Windows only; offline mode needs a Windows OCR language pack.

## Modes

- **Engine mode** (ground truth). ReplayHarness against a real `Ocrx.Engine.exe`, with a probe plugin named
  `SignaturePlugin` (the name matters: it makes the engine scale ROIs by height, as for the shipped plugin).
  It exposes only ROI rect and scale, so only `scale`, `norm` and `margin` knob sets run here. Each ROI is
  also subscribed as a Detailed ROI so the engine reports the crop and scale it applied; engine-mode CSV rows
  carry those measured values, and a difference from the plan is flagged in the `note` column.
- **Offline mode** (knob sweep). A local copy of the engine read path (`OcrPipeline.CropAndScaleAsync`,
  red-channel grayscale, `Windows.Media.Ocr`) with pluggable knobs: scale, resolution-normalised scale,
  margin, channel, contrast, binarization, interpolation, pre-sharpen.
- **Gate.** `gate` runs both modes, requires identical raw text on every read, and requires the engine's
  measured crop and scale to equal the plan. It exits 1 otherwise. Trust offline sweep results only after
  the gate passes on the corpus you are sweeping.

Engine binary: `--engine`, else `OCRX_ENGINE_PATH`, else `%LOCALAPPDATA%\OcrxEngine\current`. The plugins'
pinned engine is in `engine-version.txt`. `--ocr-lang` applies to offline mode only; `gate` rejects it.

## Manifest

`manifest.json` in the corpus directory, in the parity corpus shape, `{ "frames": [ ... ] }`:

| Field | Meaning |
|---|---|
| `file` | PNG name in the directory (required, unique) |
| `signature` | the number the badge shows; frames without one are reported as unlabelled |
| `configuration` | free-text label the pivot groups by, e.g. `windowed-1600x1200` |
| `verticalFov` | Star Citizen's stored (vertical) FOV in degrees. `StarCitizenFovZoom` is applied for it in both modes, exactly as the plugin does. Omit for the calibrated rect. |
| `lossy` | `true` for frames that are not exact engine pixels (converted screenshots); shown as `[lossy]` and to be treated as a secondary set |
| `note` | free text |

The parity corpus reader ignores the fields it does not know, so one manifest can serve both.

## Commands

```
dotnet run --project tools/SignatureOcrSweep -c Release -- gate  --corpus DIR --knob baseline --scales 3,4,5,6,8,10,12 --out OUT
dotnet run --project tools/SignatureOcrSweep -c Release -- sweep --corpus DIR --mode offline --knob baseline --knob scale=8+channel=lum --out OUT
dotnet run --project tools/SignatureOcrSweep -c Release -- synth --from E.png --size 1920x1080 --to F.png
```

Knob specs are `baseline` or `+`-joined `key=value` terms: `scale=8`, `norm=6`, `margin=4`,
`channel=lum|max|value`, `contrast=stretch|gamma:0.7`, `bin=otsu|fixed:128`, `interp=fant|linear|nearest`,
`sharpen=unsharp:1`. `norm=K` is the scale at 1440 px height, multiplied by 1440 / frame height and divided
by the FOV zoom. Scales must be positive.

Output in `OUT`: `rows-<mode>.csv` (one row per frame x knob set), `pivot-<mode>.csv`, and for `gate`
`gate.csv`. Verdicts: `Correct`, `WrongNumber`, `Unreadable` (text that does not parse), `Blank`, `Error`
(the read itself failed; never counted as blank). Pivot cells read `correct/total`, then `wN`, `uN`, `eN`.
A frame that fails is retried once, then recorded as an `Error` row; rows are written even if the run dies.

## Deviation from the task file

`margin` is in **reference-space** pixels (added to the rect after the FOV zoom, before mapping), not frame
pixels. That is what lets engine mode express it, and it keeps both modes mapping the same rect.
