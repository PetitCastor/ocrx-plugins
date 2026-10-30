// Builds the Item Tracker view into a DOM. Everything the document says reaches the page through
// textContent, never as markup, because item names come off the screen and out of the game's files.
// The view only looks: there is no control that changes the ledger, and no way for the page to send
// anything back. The only inputs are the tab, the place picked in the list of places, the state filter
// and the search box, which choose and filter what is shown.

import {
  STATES, TABS, byPlace, choosePlace, entries, filtering, formatTime, placeTree, stateHelp, stateLabel, tally, visible,
} from "./model.js";

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

function tallyLine(doc, counts) {
  const line = el(doc, "span", "tally");
  for (const state of STATES) {
    if (counts[state]) line.append(el(doc, "span", `tally-${state}`, `${counts[state]} ${stateLabel(state)}`));
  }
  return line;
}

/**
 * @param {Element} root where to build; its children are replaced
 * @param {object} options
 * @param {Document} options.doc the document to create nodes with
 * @param {object|null} options.data the plugin's document, or null before the first one arrives
 * @param {{tab: string, state: string, query: string, selected: string|null, place: string|null, collapsed: Set<string>}} options.ui
 * @param {{tab: Function, state: Function, query: Function, select: Function, place: Function, toggle: Function}} options.on
 */
export function renderView(root, { doc, data, ui, on }) {
  const shell = el(doc, "div", "shell");
  shell.append(header(doc, data, ui, on));

  if (!data) {
    shell.append(el(doc, "p", "empty", "Waiting for the tracker's first read of your logs."));
    root.replaceChildren(shell);
    return;
  }

  const all = entries(data);
  let list = visible(all, ui);

  const body = el(doc, "div", "body");
  if (ui.tab === "locations") {
    // One station at a time, picked from the list of places beside it: every station in one long page
    // stops being readable as soon as the player stores things in more than one system.
    const isFiltering = filtering(ui);
    const systems = placeTree(data, list, isFiltering);
    const shown = choosePlace(systems, ui.place, data.here);
    list = list.filter((e) => e.placeId === shown);
    if (systems.length) {
      body.className = "body body-tree";
      body.append(placesNav(doc, systems, shown, data.here, isFiltering, ui, on));
    }
  }
  // An item a filter, or a jump to another place, took off the list keeps no detail open beside it.
  const selected = list.find((e) => e.id === ui.selected);
  body.append(listPane(doc, data, all, list, ui, on));
  if (selected) body.append(detailPane(doc, selected, on));
  shell.append(body);
  root.replaceChildren(shell);
}

function header(doc, data, ui, on) {
  const head = el(doc, "header", "head");
  head.append(el(doc, "h1", "title", "Item Tracker"));

  if (data) {
    const summary = el(doc, "div", "summary");
    for (const state of STATES) {
      const pill = el(doc, "span", `pill pill-${state}`, `${data.counts?.[state] ?? 0} ${stateLabel(state)}`);
      pill.setAttribute("title", stateHelp(state));
      summary.append(pill);
    }
    head.append(summary);
  }

  const tabs = el(doc, "nav", "tabs");
  for (const tab of TABS) {
    const button = el(doc, "button", tab.id === ui.tab ? "tab tab-on" : "tab", tab.label);
    button.setAttribute("type", "button");
    button.onclick = () => on.tab(tab.id);
    tabs.append(button);
  }
  head.append(tabs);

  const filters = el(doc, "div", "filters");
  const search = el(doc, "input", "search");
  search.setAttribute("type", "search");
  search.setAttribute("placeholder", "Search items, stations, reasons");
  search.setAttribute("aria-label", "Search");
  search.value = ui.query;
  search.oninput = () => on.query(search.value);
  filters.append(search);

  const states = el(doc, "div", "state-filter");
  for (const state of ["all", ...STATES]) {
    const button = el(doc, "button", state === ui.state ? "chip chip-on" : "chip", state === "all" ? "All" : stateLabel(state));
    button.setAttribute("type", "button");
    button.onclick = () => on.state(state);
    states.append(button);
  }
  filters.append(states);
  head.append(filters);
  return head;
}

function listPane(doc, data, all, list, ui, on) {
  const pane = el(doc, "main", "list");
  if (ui.tab === "ships") pane.append(el(doc, "p", "note", SHIPS_NOTE));
  if (ui.tab === "dropped") pane.append(el(doc, "p", "note", DROPPED_NOTE));

  if (list.length === 0) {
    const inTab = all.filter((e) => e.tab === ui.tab).length;
    pane.append(el(doc, "p", "empty", inTab === 0 ? emptyTab(ui.tab) : "Nothing matches this filter."));
    return pane;
  }

  const stations = new Map((data.systems ?? []).flatMap((s) => s.stations ?? []).map((s) => [s.id, s]));
  for (const place of byPlace(list)) {
    if (ui.tab === "locations") pane.append(el(doc, "h2", "system", place.group));
    pane.append(placeBlock(doc, place, ui.tab === "locations" ? stations.get(place.placeId) : null, ui, on));
  }
  return pane;
}

/**
 * The places the Locations tab can show, by system. A system's heading folds its places away, all but
 * the one on show; with a single system there is nothing to tell apart, so the places are listed bare.
 * A filter unfolds everything, or a match could sit out of reach in a folded system.
 */
function placesNav(doc, systems, shown, here, isFiltering, ui, on) {
  const nav = el(doc, "nav", "places");
  nav.setAttribute("aria-label", "Places");
  const titled = systems.length > 1;

  for (const system of systems) {
    const folded = titled && !isFiltering && ui.collapsed.has(system.name);
    if (titled) {
      const toggle = el(doc, "button", "system-toggle", system.name);
      toggle.setAttribute("type", "button");
      toggle.setAttribute("aria-expanded", String(!folded));
      toggle.onclick = () => on.toggle(system.name);
      nav.append(toggle);
    }
    const listed = folded ? system.places.filter((p) => p.id === shown) : system.places;
    if (listed.length === 0) continue;

    const places = el(doc, "ul", "place-list");
    for (const place of listed) {
      const item = el(doc, "li", place.id === shown ? "place-entry place-on" : "place-entry");
      const button = el(doc, "button", "place-button");
      button.setAttribute("type", "button");
      if (place.id === shown) button.setAttribute("aria-current", "true");
      button.onclick = () => on.place(place.id);

      const label = el(doc, "span", "place-label", place.name);
      button.append(label);
      if (place.id === here) {
        const marker = el(doc, "span", "here", "here");
        marker.setAttribute("title", "Where the logs last placed you");
        button.append(marker);
      }
      if (place.attention) {
        const dot = el(doc, "span", `dot dot-${place.attention}`);
        dot.setAttribute("title", stateLabel(place.attention));
        button.append(dot);
      }
      button.append(el(doc, "span", "place-count", place.count));
      item.append(button);
      places.append(item);
    }
    nav.append(places);
  }
  return nav;
}

function emptyTab(tab) {
  switch (tab) {
    case "ships": return "Nothing is recorded in a ship's storage.";
    case "dropped": return "Nothing is recorded as left on the ground.";
    default: return "No items are recorded at any station yet.";
  }
}

function placeBlock(doc, place, station, ui, on) {
  const block = el(doc, "section", "place");
  const title = el(doc, "h3", "place-name");
  title.append(el(doc, "span", "", place.placeName));
  // A station the game's text has not named yet is shown by its id, which is all there is to go by.
  if (station && !station.resolved) title.append(el(doc, "span", "place-id", ` id ${place.placeId}`));
  block.append(title, tallyLine(doc, tally(place.entries)));

  if (station?.coverage) block.append(el(doc, "p", "coverage", coverageText(station.coverage)));

  const rows = el(doc, "ul", "items");
  for (const entry of place.entries) rows.append(itemRow(doc, entry, ui, on));
  block.append(rows);
  return block;
}

function coverageText(c) {
  const seen = `Last visit ${formatTime(c.at)}: ${c.pagesSeen}/${c.pages || "?"} pages, ${c.observedItems} items on screen`;
  if (c.complete) return `${seen}; matches the log's total of ${c.freightTotal}.`;
  return c.freightTotal == null ? `${seen}.` : `${seen}; the log's total is ${c.freightTotal}.`;
}

function itemRow(doc, entry, ui, on) {
  const { item } = entry;
  const row = el(doc, "li", entry.id === ui.selected ? "item item-on" : "item");
  const button = el(doc, "button", "item-button");
  button.setAttribute("type", "button");
  button.onclick = () => on.select(entry.id === ui.selected ? null : entry.id);
  button.append(
    el(doc, "span", "item-name", item.name),
    el(doc, "span", "item-qty", item.quantity > 1 ? `×${item.quantity}` : ""),
    stateBadge(doc, item.state),
  );
  row.append(button);
  return row;
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
