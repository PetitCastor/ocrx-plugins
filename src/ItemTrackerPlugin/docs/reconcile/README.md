# Reconciliation (TASK-IT-04)

Every ledger placement gets one state with a reason a player can read. Nothing comes from typed
input: the screen, the log's moves and the log's freight totals are the only sources.

| State | When | Reason |
|---|---|---|
| verified | the kiosk showed as many of that kind at that station as the ledger had, after its last move | `Seen at Keeger kiosk, 28 Oct 15:28, page 2` |
| deduced | logs only | `Stored 21 Oct, not seen on screen since` |
| stale | a game build was first played after its last sighting or move | `Game updated to build 12519617 since` |
| conflict | the kiosk showed a different number | `Ledger says 2, kiosk showed 1` |

## How a frame meets the ledger

- **Stored first.** Each observation the screen reader emits goes into `screen_observation` with its
  time, panel, page, tile, region and raw read. The table is not derived from the logs, so neither a
  rebuild nor a schema change drops it. A replay writes to its own throwaway store.
- **Station from the log.** The kiosk header says `LOCAL / LOCAL`, not where. A frame belongs to the
  last `opened` line (`log_signal`, TASK-IT-02) before it. A frame with none before it, or after an
  open the log could not tie to an id, is counted as unplaced and changes nothing. Inside a station
  the open used to take the sub-location's id (`Location [0] -> [2204875565]`, landing
  `3058615591`); it now takes the id the name is already bound to (parser version 9).
- **Kind, not geid.** The screen shows a name, the ledger a class. A class is named by the game's own
  `global.ini`, trimming trailing numeric segments until a key exists
  (`rsi_odyssey_undersuit_helmet_01_01_01` is under `_rsi_odyssey_undersuit_helmet_01`), else by the
  wiki. Names are compared case- and spacing-blind, with I, l and 1 as one letter: OCR swaps them.
- **Count at the time of the frame.** What the ledger has at the station now, plus what left after
  the frame, less what arrived after it. The log writes some stores on two lines naming the same
  geid, so a named arrival counts once.
- **Count on screen.** The sum over the distinct tiles of that name in one visit, each at its last
  read badge. A move of that kind in or out of the station re-sorts the grid, so only the frames
  after the last one count. A tile without a read badge counts one.
- **Verdict.** Equal verifies. More on screen than the ledger had is a conflict on any view. Fewer
  is a conflict only on a view of the whole station with every badge read: a filter, an unvisited
  page, a tile nobody hovered or an unread badge each hide some, so otherwise it proves nothing.
- **Only direct contents.** An item inside a container at the station is not on the grid; it stays
  deduced.

## Coverage

A category filter or a search shortens the list, so a visit verifies only what it showed. A visit is
the whole station when it saw every page of the list and its observed item count equals the freight
total. Then every kind the ledger has there that no tile showed is a sighting of none, and a conflict.

## Unknown arrivals

A kind the ledger had none of at the station at the time of the frame becomes an arrival of unknown
origin: no geid, the name and count seen. That is how the starting inventory the logs lack fills in.

## Limits

- **Stale is any build change.** That is the ledger's own rule ([sc-item-movement](../domain/sc-item-movement.md)):
  a change of build, not age, is what makes a placement stale. The ledger only moves belongings when
  the update changed where the player spawned; the state still flags every placement older than a
  build, because nothing logged says whether the server moved it.
- **The freight total is the latest one.** A visit is judged complete against the station's current
  total, not the total at the time of the visit.
- **Observation time is the read's.** Live, the plugin stamps the moment it read the frame: a replay
  tick carries replay time, not capture time.

## Not reconciled

- **Ship lockers.** The header names the model (`SHIV`), not the ship, and the ledger keys a ship's
  lockers by class. Stored, not matched.
- **Personal inventory.** It is the player's body, whose ledger side is the worn sightings. Stored,
  not matched.

The reliability study reports rows per state; a log-only corpus has no observations, so it splits
deduced from stale only.
