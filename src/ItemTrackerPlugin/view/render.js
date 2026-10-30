// Builds the Item Tracker view into a DOM. Everything the document says reaches the page through
// textContent, never as markup, because item names come off the screen and out of the game's files.
// The view only looks: there is no control that changes the ledger, and no way for the page to send
// anything back. The only inputs are the tab, the system and place pickers, and the search box, which
// choose and filter what is shown.

import { TABS, byPlace, choosePlace, entries, filtering, formatTime, placeTree, stateHelp, stateLabel, visible } from "./model.js";

const SHIPS_NOTE =
  "The logs cannot tell two ships of the same model apart, so storage is grouped by model.";
const DROPPED_NOTE =
  "Items left on the ground are search leads: the game despawns what lies outside a hangar.";

function el(doc, tag, className, text) {
  const node = doc.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined && text !== null) node.textContent = String(text);
  return node;
}

function stateBadge(doc, state) {
  const badge = el(doc, "span", `badge badge-${state}`, stateLabel(state));
  badge.setAttribute("title", stateHelp(state));
  return badge;
}

/**
 * @param {Element} root where to build; its children are replaced
 * @param {object} options
 * @param {Document} options.doc the document to create nodes with
 * @param {object|null} options.data the plugin's document, or null before the first one arrives
 * @param {{tab: string, query: string, selected: string|null, place: string|null}} options.ui
 * @param {{tab: Function, query: Function, select: Function, place: Function}} options.on
 */
export function renderView(root, { doc, data, ui, on }) {
  const shell = el(doc, "div", "shell");
  const head = header(doc, ui, on);
  shell.append(head);

  if (!data) {
    shell.append(el(doc, "p", "empty", "Waiting for the tracker's first read of your logs."));
    root.replaceChildren(shell);
    return;
  }

  const all = entries(data);
  let list = visible(all, ui);

  if (ui.tab === "locations") {
    // One station at a time, picked by system then place above the list, as inventory-tracker did:
    // every station in one long page stops being readable once things are stored in several places.
    const systems = placeTree(data, list, filtering(ui));
    const shown = choosePlace(systems, ui.place, data.here);
    list = list.filter((e) => e.placeId === shown);
    if (systems.length) head.append(pickers(doc, systems, shown, data.here, on));
  }

  // The header stays put; only the list (and the detail beside it) scrolls.
  const body = el(doc, "div", "body");
  // An item the search, or a jump to another place, took off the list keeps no detail open beside it.
  const selected = list.find((e) => e.id === ui.selected);
  body.append(listPane(doc, data, all, list, ui, on));
  if (selected) body.append(detailPane(doc, selected, on));
  shell.append(body);
  root.replaceChildren(shell);
}

function header(doc, ui, on) {
  const head = el(doc, "header", "head");
  head.append(el(doc, "h1", "title", "Item Tracker"));

  const tabs = el(doc, "nav", "tabs");
  for (const tab of TABS) {
    const button = el(doc, "button", tab.id === ui.tab ? "tab tab-on" : "tab", tab.label);
    button.setAttribute("type", "button");
    button.onclick = () => on.tab(tab.id);
    tabs.append(button);
  }
  head.append(tabs);

  const search = el(doc, "input", "search");
  search.setAttribute("type", "search");
  search.setAttribute("placeholder", "Search items, stations, reasons");
  search.setAttribute("aria-label", "Search");
  search.value = ui.query;
  search.oninput = () => on.query(search.value);
  head.append(search);
  return head;
}

/**
 * System, then place: two dropdowns over the list. Changing system opens the place there the player
 * stands at, else the one something last happened at. A search keeps only the places with a match.
 */
function pickers(doc, systems, shown, here, on) {
  const row = el(doc, "div", "pickers");
  const current = systems.find((s) => s.places.some((p) => p.id === shown)) ?? systems[0];

  const system = el(doc, "select", "picker picker-system");
  system.setAttribute("aria-label", "System");
  for (const s of systems) {
    const option = el(doc, "option", "", s.name);
    option.value = s.name;
    system.append(option);
  }
  system.value = current.name;
  system.onchange = () => {
    const picked = systems.find((s) => s.name === system.value);
    if (picked) on.place(choosePlace([picked], null, here));
  };

  const place = el(doc, "select", "picker picker-place");
  place.setAttribute("aria-label", "Place");
  for (const p of current.places) {
    const option = el(doc, "option", "", `${p.name} (${p.count})${p.id === here ? " · you are here" : ""}`);
    option.value = p.id;
    place.append(option);
  }
  place.value = shown;
  place.onchange = () => on.place(place.value);

  row.append(system, place);
  return row;
}

function listPane(doc, data, all, list, ui, on) {
  const pane = el(doc, "main", "list");
  if (ui.tab === "ships") pane.append(el(doc, "p", "note", SHIPS_NOTE));
  if (ui.tab === "dropped") pane.append(el(doc, "p", "note", DROPPED_NOTE));

  if (list.length === 0) {
    const inTab = all.filter((e) => e.tab === ui.tab).length;
    pane.append(el(doc, "p", "empty", inTab === 0 ? emptyTab(ui.tab) : "Nothing matches this search."));
    return pane;
  }

  const stations = new Map((data.systems ?? []).flatMap((s) => s.stations ?? []).map((s) => [s.id, s]));
  for (const place of byPlace(list)) {
    pane.append(placeBlock(doc, place, ui.tab === "locations" ? stations.get(place.placeId) : null, ui, on));
  }
  return pane;
}

function placeBlock(doc, place, station, ui, on) {
  const block = el(doc, "section", "place");
  // A station is already named by the place picker; ships and drops are named here.
  if (!station) block.append(el(doc, "h3", "place-name", place.placeName));
  // A station the game's text has not named yet is shown by its id, which is all there is to go by.
  if (station && !station.resolved) block.append(el(doc, "p", "place-id", `Not named by the game yet: id ${place.placeId}`));
  if (station?.coverage) block.append(el(doc, "p", "coverage", coverageText(station.coverage)));

  const rows = el(doc, "ul", "items");
  for (const entry of place.entries) rows.append(itemRow(doc, entry, ui, on));
  block.append(rows);
  return block;
}

function itemRow(doc, entry, ui, on) {
  const { item } = entry;
  const row = el(doc, "li", entry.id === ui.selected ? "item item-on" : "item");
  const button = el(doc, "button", "item-button");
  button.setAttribute("type", "button");
  button.onclick = () => on.select(entry.id === ui.selected ? null : entry.id);
  button.append(el(doc, "span", "item-name", item.name), el(doc, "span", "item-qty", item.quantity > 1 ? `×${item.quantity}` : ""));
  row.append(button);
  return row;
}

function emptyTab(tab) {
  switch (tab) {
    case "ships": return "Nothing is recorded in a ship's storage.";
    case "dropped": return "Nothing is recorded as left on the ground.";
    default: return "No items are recorded at any station yet.";
  }
}

function coverageText(c) {
  const seen = `Last visit ${formatTime(c.at)}: ${c.pagesSeen}/${c.pages || "?"} pages, ${c.observedItems} items on screen`;
  if (c.complete) return `${seen}; matches the log's total of ${c.freightTotal}.`;
  return c.freightTotal == null ? `${seen}.` : `${seen}; the log's total is ${c.freightTotal}.`;
}

function detailPane(doc, entry, on) {
  const { item } = entry;
  const pane = el(doc, "aside", "detail");

  const close = el(doc, "button", "close", "Close");
  close.setAttribute("type", "button");
  close.onclick = () => on.select(null);
  pane.append(close, el(doc, "h2", "detail-name", item.name), stateBadge(doc, item.state));
  pane.append(el(doc, "p", "reason", item.reason));

  const facts = el(doc, "dl", "facts");
  const fact = (label, value) => {
    if (value === null || value === undefined || value === "") return;
    facts.append(el(doc, "dt", "", label), el(doc, "dd", "", value));
  };
  fact("Quantity", item.quantity);
  fact("Where", item.where);
  fact("Last seen on screen", item.seenAt ? formatTime(item.seenAt) : "never");
  fact("Last logged move", item.lastMove ? `${item.lastMoveType}, ${formatTime(item.lastMove)}` : item.lastMoveType);
  fact("Class", item.itemClass);
  pane.append(facts);

  if (item.caveats?.length) {
    const caveats = el(doc, "ul", "caveats");
    for (const c of item.caveats) caveats.append(el(doc, "li", "", c));
    pane.append(el(doc, "h3", "", "Caveats"), caveats);
  }

  pane.append(el(doc, "h3", "", "Movements"));
  if (!item.history?.length) {
    pane.append(el(doc, "p", "empty", "No logged movements to show."));
  } else {
    const moves = el(doc, "ol", "moves");
    for (const m of item.history) {
      const amount = m.amount > 1 ? ` ×${m.amount}` : "";
      const failed = m.result && m.result !== "succeed" ? ` (${m.result})` : "";
      moves.append(el(doc, "li", "", `${formatTime(m.at)} · ${m.type}${amount}: ${m.from} → ${m.to}${failed}`));
    }
    pane.append(moves);
  }
  return pane;
}
