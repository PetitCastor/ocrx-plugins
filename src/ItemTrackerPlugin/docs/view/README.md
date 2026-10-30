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

## Out of game: the plugin's own window

The engine renders nothing for a plugin beyond its settings, so the plugin hosts its window itself
(`Window/`, TASK-IT-09): a WinForms form with a WebView2 that shows `view/` from beside the executable,
on a UI thread of its own, and a tray icon of its own (tooltip "Item Tracker"). A click on the icon, or
its "Open" item, shows the window; there is no "Quit", since the engine owns the plugin's lifetime.
Closing the window hides it, and the first close of a run shows a balloon: "Item Tracker is still
running. Click its icon in the notification area to open it again." A replay has neither.

The setting "Open window at startup" (`openAtStartup`, a Toggle in the Window group, on by default)
decides whether the window shows on the first connect of a run. Applying it only saves it for the next
start. "Window theme" (`theme`, a Select in the Window group: Follow Windows, Light, Dark; Follow Windows
by default) recolours the window at once: WebView2's preferred colour scheme answers the page's
`prefers-color-scheme`, and the form paints the title bar and the background behind the page to match.
Without the WebView2 Runtime the window shows a plain sentence saying so instead of the page.

The page is served from `https://item-tracker.example/` (a virtual host mapped to `view/`, because ES
modules do not load from `file://`); navigation anywhere else and new windows are refused, and the
page carries its own `default-src 'self'` policy. After every snapshot rebuild `Window/ViewFeed` sends
the document in-process (`PostWebMessageAsJson`), wrapped as `{ "type": "ocrx:view-data", "data": ... }`;
the latest one waits for a window not shown yet. There is no data file and no size limit.

The page is plain ES modules (`model.js` pure rules, `render.js` DOM, `main.js` entry). Everything the
document says reaches the page through `textContent`, never as markup. Tabs: Locations, Ships (by
model: the logs cannot tell two ships of one model apart), Dropped (search leads). Each can be searched
by text. The list shows names and quantities only; an item's state lives in its detail, with why, the
last time it was seen on screen, and its recent movements. The header (title, tabs, search, pickers)
stays put and only the list and the detail scroll.

Locations shows one station at a time, picked with two dropdowns above the list, System then Place, as
`inventory-tracker`'s pickers were. Each place option carries its units and "you are here" for where the
logs last placed the player. Changing system opens the place there the player stands at, else the one
something last happened at. A search keeps only the systems and places with a match, each with the units
that matched, and moves to the first one when the place on show has none; the place picked comes back
once the search is cleared. The page opens on the place picked last (kept in the page's `localStorage`,
which may be missing: the page then works the same), else where the player is (`here`), else the place
something last happened at. A station the game's text has not named yet says so, with its id.

### The document (`schema` 1)

`ViewData/ViewDocument` is the one place that builds it. `systems[].stations[].items[]`, `ships[]`,
`dropped[]` carry rows of `name, itemClass, quantity, state, reason, seenAt, lastMove, lastMoveType,
where, caveats[], history[]`, with up to 8 moves per item.

A station is a place of the catalogue, not a location id: the game re-issues a station's id from time to
time, and every id that resolves to the same place (`Place.AliasIds`) is one station, keyed by the
place's canonical id, with its items, counts and latest visit taken across all of them. Each station
carries `aliasIds[]`, a `name` without the system (the system is its heading), and `lastActivity`, the
newest move, sighting or visit there. `here` is the place the newest log's `session.cur_loc_id` belongs
to (`IngestHealth.CurrentLocationId`), or null; it can name somewhere that is not a station, such as open
space, and the page then ignores it.

Tests: `tests/ItemTrackerPlugin.Tests/ViewDocumentTests.cs` (the document, the shipped page),
`ViewMessageTests`, `WindowStartupTests`, `CloseNoticeTests` (the window's rules, without UI) and
`tests/ItemTrackerView/` (the page, on `node --test` with a fake DOM and a fixture document whose
keys a C# test pins to the real ones).
