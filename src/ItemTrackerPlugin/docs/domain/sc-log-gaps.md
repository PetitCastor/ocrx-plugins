---
name: sc-log-gaps
description: Game.log lines that move no item but tell the screen side and the reconciliation what the player was doing — freight totals, stack presses, station inventory open/close, kiosk buys
metadata:
  type: reference
---

# Log lines that are not moves (TASK-IT-02)

None of these changes what the ledger believes an item is. They are context for matching what the
game showed on screen against what the log says, and are stored in `freight_total` and
`log_signal` (parser version 8, schema 18). Verified against the 2026-10-28 `Game.log` and the
`logbackups` folder (build 12344265 and later).

## Freight total

```
<InventoryManagement> Freight Inventory Grid InventoryDataRequest Successful Inventory[204821708183:Location:3058615591] Elapsed[0.394277]. Page info: HasPreviousPage[0], HasNextPage[1], StartCursor[MA==], EndCursor[MjUw].
<InventoryManagement> Freight Inventory Grid InventoryDataRequest Successful for Player[X] Inventory[...] Inventory Result Item Count:[250].
```

- Pages are 250 items. `EndCursor` is base64 of a decimal position (`MjUw` = 250, `MTI=` = 12).
  The second line is the page size, not the total, and is not read.
- A page with `HasNextPage[1]` only gives a **floor**: the list holds at least `EndCursor` items.
- A page with `HasNextPage[0]` ends the list the player was looking at, so its `EndCursor` is that
  list's total. A searched or filtered list is shorter than the station's, so a last page is accepted
  as the station total only when it is **above the floor** and **no lower than any total before it**.
- The id in `Inventory[...:Location:<id>]` is the same location id `location_name` binds to a name, so
  the total is bound to a station through that table.
- Limit of the rule: it cannot tell a narrowed list from a short one, so a total only ever rises. A
  station the player empties keeps its old total until the table is rebuilt. The reliability report
  prints total, floor and the ledger's count per station to show how far apart they are.

Idea and floor rule from Aurelius-Draco/sc-inventory-tracker (MIT); see `THIRD_PARTY.md`.

## Stack presses

```
<Player Inventory Request Complete> Request[10] for 'X' [id] Type[StackAll] Result[Succeed] Item[0] CanLockQueue[Yes].
```

Stored as `log_signal` kind `stack`, `detail` = result. The line names no inventory; the press time is
what matters (the grid may have re-sorted). The Queued/Add lines of the same request were already
dropped as non-moves and stay dropped.

## Station inventory open and close

- **Open:** `<RequestLocationInventory> ... Location[<name>]`. Kind `opened`, `ref` = the name,
  `detail` = the location id the player was standing in, when known. The name-only line is still
  paired with the id exactly as before. For a freight kiosk the id of the same open is on the
  `Freight Inventory Grid Requesting Inventory[...]` line a millisecond earlier.
- **Close:** `<Request Terminate Access To Inventory> Player[X] terminating access to [<id>:Location:<loc>]`
  (preceded by `<Remove Inventory Container UI> ... removed from UI`, and in a full close by
  `<Close Inventory Grid> ... closing inventory in progress`). Kind `closed`, `ref` = the location id.
  Written for container inventories too; only `Location` ones are kept. The game also terminates
  access when another inventory replaces the open one, so a `closed` is not always the player leaving
  the screen: read it together with the next `opened`.

So the log does have a close signal. The header ROI is still the better source for which station a
frame shows; this one bounds the interval.

## Kiosk buys

```
<CEntityComponentShopUIProvider::SendShopBuyRequest> Sending SShopBuyRequest - playerId[..] shopId[..] shopName[SCShop_Cargo_Office] kioskId[..] client_price[19175.000000] itemClassGUID[..] itemName[grin_tractor_01] quantity[1]
<CEntityComponentShopUIProvider::RmShopFlowResponse> Received ShopFlowResponse - playerId[..] shopId[..] shopName[..] kioskId[..] kioskState[BuyRequestProcessing] result[Success] type[Buying]
```

Present in the August 2026 backups (two buys, 2026-08-09 and 2026-08-14), absent from the current
build's log. Stored as `shop_buy` (`ref` = shop, `detail` = `item xN @ price`) and `shop_flow`
(`detail` = `type: result (state)`). The buy does not say which inventory the item lands in, so it
does not feed the ledger.

**`SShopCommoditySellRequest` does not appear** in any log in `E:\Games\StarCitizen\LIVE`
(`Game.log` and all 95 backups). Not implemented; if a build writes it, add the pattern next to
`ShopBuy` in `InventoryEventParser` and bump `Version`.
