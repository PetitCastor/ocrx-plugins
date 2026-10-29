# What the user sees (TASK-IT-06)

Two places, and the rule that decides them: actions happen in game, out of game is read-only. There is
no browser, no web server in the plugin and no way to edit a placement by hand; a typed number is not
an observation.

## In game: the overlay status

`Overlay/InventoryStatus` produces one line while a kiosk or a ship locker is open, and nothing
otherwise. The line goes out as a `CaptureRecord` with a `status` field; the SDK overlay draws it with
its own look and its own 9-cell position (`TrackerConfig` sets only the template `{status}` and
`lingerMs: 0`; there is no per-plugin styling and no overlay setting here).

| Screen | Line |
|---|---|
| Kiosk, station known | `Keeger kiosk · p.2/5 · 14 ✓ · 1 ⚠` (`✓` verified, `⚠` conflict, `n stale` only when there are some) |
| Kiosk, station not known | `Kiosk · p.2/5` |
| Ship locker | `SHIV locker · p.1/2` |
| A page turns fully verified | `Keeger kiosk · page 2 verified ✓` for 4 s, once per page per visit |
| Anything else | cleared, after 3 ticks without an inventory header |

The open station is the one the log last said was opened (`ReconcileSources.LatestOpened`); its counts
and the per-page verification come from the latest reconciliation (`Reconciliation.At`,
`Reconciliation.Pages`). A page is verified when every kind of item the screen last showed on it has
all its placements at that station verified.

Every record the plugin emits reaches every sink, and the SDK overlay writes a record that lacks the
template's field as its raw text, so `StatusOnlySink` stands in front of it and lets only status lines
and clears through. Without it each hover would flash its JSON on screen.

## Out of game: the read-only tab

`view/index.html` beside the executable is the declaration (`PluginViewAssert.IsDeclaredAndSelfContained`
is asserted in the tests). The plugin writes one document, `view-data.json`, whole, after every
snapshot rebuild (`ViewData/ViewPublisher`). The engine finds it by the plugin's install folder, which
is its catalog id, so the data lives in `%LOCALAPPDATA%\OCRX\item-tracker\`, not in the tracker's own
data root. A replay writes nothing.

The page is plain ES modules (`model.js` pure rules, `render.js` DOM, `main.js` entry) under the
engine's CSP: no inline script, no external URL. Everything the document says reaches the page through
`textContent`, never as markup. Tabs: Locations (system, station, items), Ships (by model: the logs
cannot tell two ships of one model apart), Dropped (search leads). Each can be filtered by state and by
text; an item opens a detail with its state and why, the last time it was seen on screen, and its
recent movements. A station the game's text has not named yet shows its id.

The engine posts `{ "type": "ocrx:view-data", "data": <document> }` into the frame (the SDK guide shows
the bare document); `main.js` takes both.

### The document (`schema` 1)

`ViewData/ViewDocument` is the one place that builds it. `systems[].stations[].items[]`, `ships[]`,
`dropped[]` carry rows of `name, itemClass, quantity, state, reason, seenAt, lastMove, lastMoveType,
where, caveats[], history[]`. The size limit is 1 MiB; a ledger too big for it is written again with
3 moves per item, then with none, and a document that still does not fit is refused, which leaves the
last good one in place.

Tests: `tests/ItemTrackerPlugin.Tests/ViewDocumentTests.cs` (the document, the size ladder, the
declaration) and `tests/ItemTrackerView/` (the page, on `node --test` with a fake DOM and a fixture
document whose keys a C# test pins to the real ones).
