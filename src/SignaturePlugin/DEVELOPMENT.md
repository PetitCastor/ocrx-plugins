# SignaturePlugin — development notes

SignaturePlugin is a console process that declares screen regions (ROIs) and what to do with the
OCR result each time a tick carrying them arrives. It never captures a frame, never runs OCR, and
never speaks gRPC; `OcrxPluginHost` (from `Ocrx.Sdk`) owns connecting, subscribing, reconnecting,
and shutdown.

Maintainer-facing material split out of the [README](README.md): ROI calibration, the replay
corpus, how the overlay decides to show and hide, and build/test commands. Users do not need any of
this.

## Calibrate The Signature ROI

> **The calibrated rect is the starting point, not a fixed position.** At runtime the plugin moves it
> onto the badge (see [Badge Tracking](#badge-tracking)); calibrate it for where the badge sits with
> the ship still.
>
> **Calibration status — `Rois.Counter` uses scale 6.0.** The checked-in corpus currently contains
> one four-digit reference frame, so it is not replay proof for five- or six-digit readings. Capture
> representative wide-number frames with `--save-frames` before changing the ROI or trusting a new
> game patch; the parser and overlay fail safely on partial OCR, but that is not a substitute for
> calibration.

`SignaturePlugin.cs` subscribes to `Rois.Counter`, the mining-mode RS signature number. Star
Citizen draws its HUD in the 3D scene, so its field-of-view slider zooms the badge about screen
centre. The plugin reads the stored vertical FOV from the running game's
`user\client\0\Profiles\default\attributes.xml`, which the game rewrites as soon as an option is
applied. It then zooms the counter ROI to match (`StarCitizenFovZoom`) and pushes the moved rect to
the engine without a restart. A replaying engine keeps the calibrated rect, since recorded frames
carry the FOV they were captured at. Below a vertical FOV of about 60.3° (slider ≈92) the badge does not
move. `Rois.Counter` is calibrated in that zone, so calibrate at a slider of 92 or lower. Before
trusting replay parity, calibrate that rectangle against your own capture:

1. Get an engine running with `--save-frames`, either a
   [release zip](https://github.com/PetitCastor/ocrx-releases/releases)
   (`Ocrx.Engine-vX.Y.Z-win-x64.zip`) or a clone of the engine repo built locally.
2. Set `"saveDebugFrames": true` in `%LOCALAPPDATA%\OCRX\SignaturePlugin\config.json`. The engine saves full replay frames;
   `saveDebugFrames` saves the cropped ROI dumps used to tune the rectangle.
3. Run the plugin with `--verbose` against the running engine. In game, open scan mode with a
   known ore, asteroid, or debris signature visible.
4. Press the engine's capture hotkey (`engine-config.json`'s `hotkey`, default `Ctrl+Shift+F12`).
5. Compare the dumped PNG path printed by `--verbose` against `Rois.Counter`'s rectangle. It is
   declared in reference space, 2560x1440, always. Nudge `RoiRect(x, y, width, height)` and `Scale`
   until the crop lands on the number. This target currently uses a `Scale` of 6.0; do not lower it
   without comparing representative captures.
6. Keep `saveDebugFrames` enabled until manual ticks reliably dump only the signature number.

## Badge Tracking

`Rois.Counter` is where the badge sat when it was calibrated, not where it stays. The badge is
anchored to the scanned target in world space, so it crosses the screen as the ship turns: measured
on 2026-11-01 at roughly 8-10 px per 100 ms, out of even a 400x260 rect within seconds. A fixed rect
therefore read the number only while the ship held still.

`BadgeTracker` follows it **with the counter ROI itself, by reading, not by looking**: other ships draw
the badge differently (an orange one was seen on 2026-09-28), so nothing depends on colour, shape or
icon. The counter is a `Detailed` ROI (same rect, scale 6, same red-channel preprocessing as a `Text`
one: the engine's `ReadRegionDetailedAsync` and `ReadRegionAsync` run the same crop, scale, grayscale and
recognition, and the text is the same — 58 of 58 `counter_*.png` crops and 720 of 720 shifted rects on the
cockpit frames read identically), so every read says where the number is: the word box OCR draws around
it, accurate to about half a pixel at scale 6 (sd 0.5 px, max 1.5 across and 3.5 down over 324 shifted
reads). `CounterReader` decides what each read is — the number (and where), a fragment of it that an
edge of the rect cut, or nothing credible — and everything that observes, locks onto or follows the number
goes through it. A read that is only the number is used byte for byte; with the pin icon (a stray "9") or a
distance line in it, just the number word.

**Cost is the design.** The tracker runs in the engine's scan loop on a game machine, and every change of
the ROI set is a push the engine republishes to its UI and overlay. Searching the screen with Detailed tiles
on every tick (a 63-cell sweep with scale cycling was built and measured) spends 4.8x the calibrated counter's
OCR on a standing badge, 10x while searching, and a push on almost every tick, and showed live as a lag spike on
every read; so the counter reads the number itself and the search is rare:

| State | Search tiles | Counter ROI | OCR per tick (vs counter alone, 70x44 at 6x = 110,880 px) |
| --- | --- | --- | --- |
| Idle (nothing read, or given up) | none | calibrated, never pushed | 1.00x |
| Tracking | none | on the number; moved only when the number would leave its safe zone | 1.4x standing, up to 2.4x moving fast |
| Lost (read within 3 s, not now) | one 400x250 at 2x tile every 5 ticks, from the 4th missed tick | holds its place | counter + 3.6x on 1 tick in 5 |
| After 3 s | none | back at the calibrated rect, pushed once | 1.00x |

- **Headroom, not tracking pushes.** The counter is placed with the calibrated margins around the number
  (9 px left, 15 right, digits' centre 17.5 below the top, 26.5 above the bottom) plus 2 / 4 px of slack for
  box wobble, and widened in the direction of motion by 5 ticks of travel (at most 60 px sideways, 30
  vertically, and never beyond 2.4x the calibrated area). It stays until the number, one tick on, would
  come within the calibrated margins. The measured motion (10 px a tick) is then a push about every 5 ticks
  (2 a second), a standing number none, and margins only ever grow past the calibrated ones: a tight crop
  misreads 4 as 8. Why not more headroom, fewer pushes: OCR time barely grows with area on this machine
  (a 120x44 crop at scale 6 reads in the same ~4-8 ms as 70x44, a 160x90 in ~13 ms), but the area is still
  CPU on a CPU-bound game machine, so it is capped. Why not less: a push is a UI broadcast and overlay refresh.
- **What margins OCR tolerates** (four cockpit frames, one margin grown at a time, the rest calibrated; read
  correct if the observed text parses to the right value): left margins of 5-75 px (past ~9 the pin
  icon comes into the crop) read correctly on all four frames at 30 of 36 settings and on three of four at
  the other six; right margins of 5-75 px 14 of 15; 8-68 px above the digits' centre 15 of 16; 10-70 px
  below it 12 of 16 — never fewer than three of four. The misses are single frames at single settings (OCR
  is chaotic about the exact crop), not a trend.
- **Lost.** Four unread ticks (one in three reads is blank even with the badge in the rect, so fewer would
  be noise) make the number lost: the counter holds, and every 5th tick one tile 400x250 at scale 2 (about
  12 ms; found a number that was plainly there at 28-30 of 35 positions) is centred where the number
  should be by dead reckoning. A candidate in it that is not cut by the tile's edge is a new place: the
  counter goes there and has 6 ticks to read a number, or the place is rejected and ignored for 150 ticks
  (doubling when it comes back). After 3 seconds of the engine's scan interval (never fewer than 5 ticks: at an interval of 750 ms or more it
  is 5 ticks, 3.75 s at 750 ms) the search ends and the
  counter returns to the calibrated rect, once.
- **What it does not do.** It does not find a badge that appears far from the calibrated rect, or comes back
  after the 3 seconds: idle is exactly the plugin before tracking existed. Whole-screen search was measured
  and rejected on cost.
- **What is a number** (`CounterReader`; two live failures, 2026-11-01, are why it is strict). A number is a
  comma-grouped word of at least 32 px (an 18 px "40" is a fragment), with no letters attached, no decimal
  point and no unit-like word right after it; or a read with no letters at all that parses to a whole
  number of 1,000 or more (OCR dropped the comma, or wrote "2.000"); or a bare run of 4-6 digits that is on the
  signature table. The distance line under the badge — "15.9km", "15,9km" (the dangerous one: a comma where
  the point is), "15.9krn", "kin", "kfrn", "gkm", "gkrn", "krTl", "IS.9krn", "fknmYD100", and "1,500" "km" as two
  words — is none of these, so it never locks, acquires, moves the rect or becomes an observation. Root
  cause: when no word was comma-grouped, the tracker used to accept the whole read if *the parser* would —
  and the parser takes "15.9km" as 15.9 and "40" as 40 — and then followed the digit-bearing words, whatever
  they were; and the plugin observed a parseable read as it stood. Text that is not a signature counts as a
  blank read for the absence debounce (it used to count as a number that matched nothing, which holds the
  overlay).
- **Cut reads.** The rect's left edge slicing the first digit turns "17,140" into "7,140", a valid signature
  (Borase x2) that was once emitted. Measured with real OCR on the four cockpit frames, the edge moved across
  the first glyph, the box of the fragment sits 0-6 px from the edge, while a whole number reads correctly
  0-14 px from it — so position alone does not settle it. With the number being followed, a box within 8 px
  of the left edge that is 5 px narrower than the number was has lost a digit: 0 of 40 fragment reads were
  emitted as a number (72 reads: 4 frames x 18 positions of the edge, from 14 px inside the number to
  10 px outside it), and one whole read was dropped (OCR's own failure at that crop). With nothing followed — idle — only a box
  touching the edge (< 2 px) is called cut, and 8 of 40 fragment reads still get through, at the cost of 14 whole
  reads dropped; that is the residue, and it only arises while the badge is first read more than ~12 px left of
  the calibrated spot. A cut read is never an observation and never feeds the consensus; it counts as
  *presence* for the absence debounce (the badge is there, misaligned, like a number matching no cluster), so
  it neither confirms an absence nor shows a value. On a followed number it widens the box one glyph towards
  the cut and the counter is placed again — one push — so the next read has the whole number; from idle, or on
  a place a search tile only suggested, it acquires nothing. A fragment reaching the right edge (never a valid
  number: thousands groups are three digits) widens the rect to the right the same way.
- **The second line under the badge** starts about 22 px below the number's centre (from the live log: number
  box y 462-478, line top 493), 4 px inside the calibrated rect's bottom margin (26.5): it cannot be kept out
  without shrinking a calibrated margin, which is not allowed, so it is in the rect from the start and is kept
  harmless by the filter above. Capping the extra margin below the number at 10 px (the cockpit frames
  carry no second line, so the data speaks only to cost) doubles the pushes for a number moving down — 4.2 a
  second against 1.9 at 6 px a tick — and saves one fifth of the OCR area, so the headroom below stays at
  30 px.
- **Candidate filter** (`SignatureCandidates`). A word, or up to three adjacent fragments, that is a
  one-to-three digit head plus comma-and-three-digit groups, value 1,000-999,999. Folded: '/' for the comma,
  O, I, l, S, B, Z for digits when the word has a real digit. Rejected, measured on four cockpit frames:
  "15.9km", "90º" (read "902"), "16.9K", "10.2K", "4.3K", "27m/s", "88.5", "0/ 20", "89%", "READY TO
  SCAN", and the power MFD's "4.314" — a period is **not** the thousands comma here, nor is a bare digit run.
  Windows OCR sometimes splits the number ("17", ",", "200", with boxes up to 4 px apart vertically); the
  pieces are joined.
- **FOV.** `StarCitizenFovZoom` sets the counter's height, scale and the fallback position; margins and the
  headroom scale with the zoom, and a FOV change zooms the remembered box about screen centre.
- **Replay.** With the engine replaying recorded frames tracking is off exactly like the FOV: the calibrated
  rect, no tiles, no pushes, and the text is read as before.
- **Logs.** Under `--verbose`: `signature badge acquired at (x,y) WxH`, `lost near (x,y)`,
  `candidate at (x,y) ... found by search`, `rejected: no number read`, and `back at the calibrated
  position`. And once every 5 s — never per tick — the performance line a live run is judged by:
  `signature perf 5.0s: ticks=48 state idle/tracking/lost=2/98/0% ocr-area/tick vs counter alone (110880 px)
  idle=1.00x tracking=1.41x lost=- search-tiles=0 pushes=0 handler avg=0.40ms max=2.1ms`.
- **Manual-tick dumps.** With `saveDebugFrames` on, a hotkey press writes the counter crop (the rect the
  text was read through, up to a tick behind the badge). The whole frame — about 4 MB a press at 2560x1440 —
  is written only with its own opt-in, `"dumpFullFrameOnManual": true` in the plugin's `config.json`
  (default false; it is how `Fixtures/Ocr/cockpit-frames.json` was recorded).

The tests (`BadgeTrackerTests`, `TrackingBudgetTests`, `TrackingStatsTests`, `SignatureCandidatesTests`,
`SignaturePluginTrackingTests`) run on an `OcrScene` that scans whatever ROI set the plugin last pushed, one
tick late, like the engine, and on the recorded OCR of four real cockpit frames. `TrackingBudgetTests` pin
the cost: idle is the baseline with 0 pushes, a standing number at most 1.5x with no further pushes, a number
moving 10 px a tick about 2 pushes a second within 2.4x, and a lost number at most one tile in five ticks and
an end to the search. They cannot run Windows OCR, so what OCR does — which crops it reads, how its boxes
wobble — is in the fixture and in `OcrScene.Blind`, not simulated.

Full walkthrough: ROI kinds, scale, error handling, session events, testing, config, and CLI are in
the hosted plugin-authoring guide:
[`docs/PLUGIN-AUTHORING.md`](https://github.com/PetitCastor/ocrx-sdk/blob/master/docs/PLUGIN-AUTHORING.md).

## Replay Corpus

`tests/SignaturePlugin.Tests/ReplayParityTests.cs` contains the carried integration gate for this
plugin. It runs against the `scan-signature` corpus below; a plugin with no corpus yet should keep
this test `Skip`-attributed rather than deleting it.

Capture known frames with an active engine:

1. Calibrate `Rois.Counter` with `saveDebugFrames` as above.
2. Start the engine with `--save-frames` so each hotkey press writes a full-frame PNG replay source.
3. Leave the game in scan mode with only the RS signature number inside the ROI.
4. Press the engine hotkey once per known ore, asteroid, or debris signature. A screenshot that
   contains only the number inside the ROI is enough; the manifest supplies the label.
5. Copy the engine-saved PNGs into `tests/fixtures/corpus/scan-signature/`.
6. Add `tests/fixtures/corpus/scan-signature/manifest.json`:

```json
{
  "frames": [
    { "file": "0001-bexalite.png", "name": "Bexalite", "kind": "ore", "signature": 3600, "count": 1 }
  ]
}
```

`signature` and `count` are optional: add them when you know the exact reading a frame should
produce (from the source table, not a guess) so the test also pins the emitted `signature`/`count`
fields, not just `name`/`kind` — those two alone let a misread that lands on a different cluster
count of the *same* ore (e.g. a doubled signature) pass unnoticed, since matching searches unit
signature x count 1-6. Omit them for a hand-labelled frame where only the ore name is known.

The test project links those files to `Fixtures/Replay/scan-signature/` in the test output. Point
`OCRX_ENGINE_PATH` at a built or unpacked `Ocrx.Engine.exe` and run the `Integration`
test. It loads the embedded default table, replays each manifest-labelled PNG as its own one-frame
corpus, and asserts the emitted observation against the manifest. It also checks that every copied
PNG has exactly one manifest entry.

On first launch, the plugin creates a user-editable default table at
`%LOCALAPPDATA%\OCRX\SignaturePlugin\signature-table.json`. It preserves that file's entries on
later launches, so edit it when captured measurements disagree with the shipped defaults. Update
`Resources/signature-table.json` in the same change only when the new values should become the
default for future users; note the source or patch context in the PR body.

Each entry has an optional `kind` (`ore` when absent; the debris panel entry is `debris`), which is
what the emitted `kind` field carries. To ship a new entry to existing installs, not only to first
runs, give it `"addedIn": N` and raise the table's `tableVersion` to `N`, where `N` is greater than
the current `tableVersion`. An entry with no `addedIn` reaches first runs only. `SignatureTableSeed`
adds entries newer than the user file's stamp once, skips any whose signature or name the user
already lists, and restamps the file, so an entry the user deleted is not re-added. First runs get
the stamp and no `addedIn` fields. A user file it cannot parse, with a non-integer stamp, or that it
cannot write is left as it is. Changing the value of an existing entry does not reach existing
installs, by design, since that would overwrite a user's measurement.

## Output and overlay internals

The default config created at `%LOCALAPPDATA%\OCRX\SignaturePlugin\config.json` appends every
signature observation to `captures/signatures.jsonl`, beside that config file. Each observation line is one OCRX
record whose `rawText` is the structured signature JSON emitted by this plugin. Repeated unchanged
observations are de-duplicated; the cleared record is retained as `kind: "Cleared"` with empty
`rawText` so a consumer can remove stale state.

It also configures an `overlay` sink, so a matched signature is drawn on screen as `{cluster}` —
`Ice x4` — and hidden again when the signature clears. `Program.cs` registers `OverlaySinkFactory`
through `PluginHostOptions.OverlayFactory`, which is what makes that entry live: an `overlay` output
with no factory registered is silently a no-op. The overlay draws only from `CaptureRecord.Fields`,
so a template placeholder that is not one of `cluster`, `alternate`, `name`, `kind`, `signature`,
`count`, or `delta` falls back to printing the raw JSON.

`{cluster}` rather than `{name} x{count}` because two table entries can derive the same cluster
total: 19200 is Savrilium x6 and Aslarite x5 alike, and nothing in the number tells them apart. Such
a reading used to resolve to nothing at all, which the plugin then counted as the badge having
vanished — so scanning one of those rocks actively hid the overlay. It now renders as
`Savrilium x6 / Aslarite x5`, and `{alternate}` carries the runner-up on its own (empty string when
the reading is unambiguous). `name` and `count` still hold the primary candidate alone, so existing
JSONL consumers are unaffected — but a template built from those two cannot express the tie, and
printing only the winner of it would be a confident wrong answer.

No data leaves the machine: the JSON sink is a local file and the overlay is a local window.
Change the `outputs` array in that local `config.json` to disable either (`[]`) or choose another supported sink;
the [plugin-authoring guide's output section](https://github.com/PetitCastor/ocrx-sdk/blob/master/docs/PLUGIN-AUTHORING.md#outputs-sinks)
lists the JSON, CSV, HTTP, and optional overlay configurations.

A config file written before the overlay existed does not have that entry, and earlier versions
only ever wrote `config.json` when it was missing — so upgrading left the overlay silently absent.
It is now added on the next run, once. Whatever you do to it afterwards stands: delete the entry, or
empty `outputs` entirely, and it stays that way through later upgrades.

Matched observations show the configured template and `EmitCleared` hides stale text after the
signature disappears. The overlay is deliberately hard to dislodge, because OCR on this crop is
fragile and every wobble used to read as "the badge is gone":

- **Only a blank crop is evidence of absence.** A legible number that resolves to no ore cluster
  means the badge is still drawn — one misread digit is enough to land in a gap in the table's
  derived grid, and that must not hide anything. Six consecutive *blank* ticks (1.5 s at the default
  250 ms scan interval) are required before the overlay is cleared.
- **A partial read is rejected, not truncated.** `SignatureParser` refuses any token that leaves
  digits behind, so the captured misread `21/425` fails outright instead of yielding `21`. It also
  folds `/` to `,`, which is how Windows OCR renders this HUD font's thousands separator about half
  the time. Returning a prefix was worse than returning nothing: `21` parses, matches no cluster,
  and was then defended as if it were a real reading.
- **A value that matches nothing is never defended.** The consensus only protects a reading that is
  actually on screen. Without that rule an accepted-but-unmatchable value blocks its own replacement,
  and because the misread producing it recurs, it kept resetting the true reading's confirmation
  streak — one captured run sat on a stale ore for sixteen seconds that way.
- **A changed value has to repeat before it is believed.** The first reading of a scan shows
  instantly, but replacing it takes two consecutive identical readings. Blank ticks are neutral and
  do not restart the challenger, because this crop reads blank frequently while the badge remains on
  screen. This stops a single slipped digit from renaming the ore mid-scan — 17200 is
  Ice x4 exactly, while 18200 is one 7→8 slip away and sits close to Bexalite x5.
- **Cluster matching uses an absolute tolerance,** not a percentage of the cluster total. A relative
  window widened exactly where the derived grid is densest, so six-ore clusters were the readings
  most likely to be named as the wrong ore.
- **A brief reconnect does not clear.** Only a sustained outage (roughly two seconds of failed
  dials) hides the overlay; a stream that drops and comes straight back says nothing about what is
  on screen.

As a backstop, the overlay cannot outlive its last real match by more than 20 ticks (~10 s),
whatever the crop is yielding — otherwise a rect that has drifted off the badge, or a table gone
stale against a game patch, would pin a dead value on screen indefinitely. The plugin also preserves
its clear across dropped ticks and a lost session, so a gap cannot leave an old signature stuck.

### Overlay themes (config.json)

`overlayTheme` selects the visual treatment for the overlay in
`%LOCALAPPDATA%\OCRX\SignaturePlugin\config.json`:

```json
"overlayTheme": "default"
```

- `default` preserves the existing configurable pill unchanged.
- `citizen` uses a compact navy, cyan-outlined technical pill with diagonal corner tabs.
- `retro` uses a square-cornered purple arcade pill with one-bit pixel text.

The `citizen` and `retro` presets own their visual settings, including their packaged fonts. Switch
back to `default` to keep or manually edit the regular `overlay` properties such as colours, size,
and padding. The theme selector does not change the template, placement, or edge-driven clearing
policy.

### Overlay position (config.json)

`position` selects where the overlay is anchored on the primary screen, in
`%LOCALAPPDATA%\OCRX\SignaturePlugin\config.json`:

```json
"position": "topcenter"
```

One of `topleft`, `topcenter`, `topright`, `middleleft`, `center`, `middleright`, `bottomleft`,
`bottomcenter`, or `bottomright`. Independent of the theme selector above — switching theme never
moves the overlay, and switching position never changes its visual preset.

None of this can rescue a reading that is *steadily* wrong. The derived grid is dense — Corundum x3
is 12675 and Quantanium x4 is 12680, five apart — so a persistent misread can land exactly on a
neighbouring cluster with a delta of zero, indistinguishable from a correct reading. The debounce
and the consensus buy stability against wobble; accuracy against a stable misread would need a
better crop or a second ROI, not a tighter threshold.

Running with `--verbose` prints one line per tick tracing the whole chain the overlay depends on:
the raw OCR text, the number parsed from it, the value actually being acted on (`held=` appears
exactly on the ticks a change is being refused), and what the table made of it. That is the log to
capture when the overlay behaves oddly — the failures worth diagnosing are about how often an
unchanged reading is reported as something else, which is invisible in a log that only records
changes.

**Existing installs:** `ConfigSeed`'s merge adds missing *entries* but never changes a value you
already have, so both of the defaults below reach **new** installs only. If you ran this plugin
before, edit `%LOCALAPPDATA%\OCRX\SignaturePlugin\config.json` by hand:

- `overlay.lingerMs` must be `0`. A non-zero linger auto-hides on a timer, and since an unchanged
  observation is emitted only once, nothing ever refreshes it — the overlay vanishes that many
  milliseconds after it appears and does not come back for that rock, whatever the debounce decides.
  This one silently defeats every other fix above, so check it first.
- `overlay.template` should be `{cluster}`. The older `{name} x{count}` still renders, but it prints
  only the primary candidate for an ambiguous total, so a 19200 rock shows `Savrilium x6` with no
  hint that `Aslarite x5` is equally likely.

## Build & Test

```powershell
dotnet build
dotnet test tests/SignaturePlugin.Tests/SignaturePlugin.Tests.csproj --filter "Category!=Integration"
```

The one test tagged `Integration` needs `OCRX_ENGINE_PATH` and a Windows OCR language pack;
run with `dotnet test tests/SignaturePlugin.Tests/SignaturePlugin.Tests.csproj` (no filter) once
both are available.

## Run

```powershell
dotnet run --project . -- --verbose
```

Needs `Ocrx.Engine.exe` running first, listening on the `OCRX.Engine` pipe configured in `%LOCALAPPDATA%\OCRX\SignaturePlugin\config.json`
(`pipeName`, must match the engine's).
