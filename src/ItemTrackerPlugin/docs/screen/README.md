# Screen reader (TASK-IT-03)

Turns frames of the kiosk, ship storage and personal inventory screens into observations: at the
time of a frame, panel P showed item C, with a quantity, a page and a tile. Nothing here compares
an observation with the Game.log ledger; that is reconciliation, in [`../reconcile`](../reconcile/README.md).

## What it reads

| Region | Kind | What |
|---|---|---|
| `header-right`, `header-left` | Text | Always subscribed. `LOCAL / LOCAL` is a kiosk, `<SHIP> / PERSONAL STORAGE` a ship, a left `PERSONAL / ALL` alone is the personal inventory. Anything else (the Freight Manager) reads nothing. |
| `tooltip-right`, `tooltip-left` | Detailed, 2x | The item tooltip. Only its first block is kept: the worn item's comparison tooltip starts at the name above a second `Volume:` line. |
| `page-s2`, `page-s3` | Text | The `n/m` under the grid. Windows OCR reads it at one scale and not the other on a given frame, so both are subscribed. |
| `capacity-right` | Detailed, 3x | `<used>/<max>` in µSCU (`662M/5G` is 662 SCU of 5000). |
| `band-0` .. `band-6` | Pixels | Seven 600x40 strips across the grid. The hovered tile has a bright border; its top edge is in one strip and its bottom edge in the next, as the same horizontal run. A pair with the same extent is the hovered tile, which gives its row, first slot column and span (one or two slots). 216 KB each at 4K, under the 256 KB wire cap. |
| `badge-<row>-<col>-s<scale>` | Text | The stack count (`2x`, `5x`, `X1`) in the corner of each of the 24 cells, at scales 4, 6 and 8. Only the tile carries it, never the tooltip. |

The item class comes from the game's own `global.ini` (`item_Name<Class>`), through the same loose
file the place names use; a name two classes share is broken by the tooltip's `Size` (`_S<n>`) and
otherwise left unresolved.

## Findings

- **FOV does not move the panels.** The same kiosk page with the same tooltip at FOV slider 85, 100
  and 116 reads the same item on the same tile with the same region contents, so there is no
  `StarCitizenFovZoom` here. `ReplayParityTests.Panels_do_not_move_with_the_field_of_view` pins it.
  The inventory view has its own camera and ignores the setting.
- **Aspect is not measured.** Every corpus frame is 2560x1440, so reference space equals the frame
  and nothing tells whether the right-hand grid keeps its distance from the right edge or from the
  centre on another aspect ratio. Until a windowed frame at a non-16:9 client size is measured, the
  regions are only correct at 16:9. If the panels are edge-anchored, the plugin has to offset its
  rects by the frame-width difference from `EngineInfo` and re-subscribe with `UpdateRoisAsync`,
  the mechanism the FOV fix in SignaturePlugin uses.
- **Scale mode.** The engine chooses it from the plugin's catalog entry (`game`), by client name,
  for a live connection and in a replay alike. Item Tracker has no catalog entry yet (TASK-IT-07), so
  both run in `fit` until it has one, which is the same as `height` at 16:9. The entry must declare
  `"game": "star-citizen"`.
- **A different id set is accepted.** `UpdateRoisAsync` takes a set whose ids differ and applies it
  from a later tick: the next one in a short probe, seven ticks later in the 11-frame replay, which
  feeds frames as fast as it can. The plugin therefore switches in live use (headers only, 2 regions,
  about 50 ms per scan, until an inventory screen is up; then the full set) and `ItemTracker(fixedSet:
  true)` subscribes everything from the start for replay tests.
- **Cost.** The full set is 86 regions and takes 220-290 ms per scan, about the whole 250 ms scan
  interval; the headers alone take 40-70 ms. Reading badges only for the hovered cell would cut most
  of it, but the hovered tile is known only from the same tick, so it needs the switch above and a
  frame of lag. Left as an optimisation.
- **Badge OCR is scale-sensitive.** Windows OCR reads a given badge at one of 4, 6 and 8 and not the
  others, a different one per badge, and never misread one in the corpus. The grid-wide Detailed
  region of the spike read none. Reading the white-on-icon text is not a red-channel problem: the
  engine's preprocessing already keeps it.
- **Not found on screen.** The tooltip never states a quantity. A tile without a badge cannot be told
  from an unread one, so `Quantity` is null in both cases.

## Open

- The non-16:9 frame (above).
- A locked (expired) item's tooltip and a crate or commodity tooltip, for the parser. The ship
  storage panel has no hovered item in the corpus, so its observation path is covered by unit tests
  only.
- A name OCR gets slightly wrong (`Odyssey 11` for `Odyssey II`) does not resolve to a class.

## Corpus

`tests/fixtures/corpus/item-tracker-kiosk` holds the 2026-10-28 frames and the same session's
`Game_20261028.log`; `item-tracker-fov` holds the 2026-10-29 FOV sweep. Frames are full size but
everything outside the regions above is black, so no chat, name or scene is in them. The log is cut
to the inventory lines and the player name is replaced with `Pilot`. `manifest.json` lists, per
frame in file-name order, the expected observation or `null`; the frame sequence number the engine
reports is the file's one-based position, and the capture time is in the file name.
