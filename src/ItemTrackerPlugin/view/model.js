// The pure parts of the Item Tracker view: what the data document means, and what a filter keeps.
// No DOM here, so the rules run under node:test.

export const STATES = ["verified", "deduced", "stale", "conflict"];

export const TABS = [
  { id: "locations", label: "Locations" },
  { id: "ships", label: "Ships" },
  { id: "dropped", label: "Dropped" },
];

const STATE_LABELS = {
  verified: "✓ Verified",
  deduced: "Deduced",
  stale: "Stale",
  conflict: "⚠ Conflict",
};

const STATE_HELP = {
  verified: "Seen on screen at its place since its last logged move.",
  deduced: "Known from the logs only.",
  stale: "A game update changed things since it was last verified or moved.",
  conflict: "The screen and the ledger disagree on how many there are.",
};

export function stateLabel(state) {
  return STATE_LABELS[state] ?? String(state);
}

export function stateHelp(state) {
  return STATE_HELP[state] ?? "";
}

/** "2026-10-28T19:28:26Z" -> "2026-10-28 19:28 UTC"; anything else comes back as given. */
export function formatTime(iso) {
  const m = /^(\d{4}-\d{2}-\d{2})T(\d{2}:\d{2})/.exec(iso ?? "");
  return m ? `${m[1]} ${m[2]} UTC` : (iso ?? "");
}

/**
 * Every item of the document as one flat entry with the place it sits at, so the tabs, the filter and
 * the detail panel all work from the same list. `id` is stable across updates of the document.
 */
export function entries(data) {
  const out = [];
  const seen = new Map();
  const add = (tab, placeId, placeName, group, item) => {
    const base = [tab, placeId, item.itemClass ?? item.name, item.where ?? ""].join("|");
    const n = seen.get(base) ?? 0;
    seen.set(base, n + 1);
    out.push({ id: `${base}|${n}`, tab, placeId, placeName, group, item });
  };

  for (const system of data?.systems ?? []) {
    for (const station of system.stations ?? []) {
      for (const item of station.items ?? []) add("locations", station.id, station.name, system.name, item);
    }
  }
  for (const ship of data?.ships ?? []) {
    for (const item of ship.items ?? []) add("ships", ship.class, ship.name, "", item);
  }
  for (const drop of data?.dropped ?? []) {
    for (const item of drop.items ?? []) add("dropped", drop.place, drop.place, "", item);
  }
  return out;
}

/** True when every word of the query is somewhere in the entry's name, place, class, location or reason. */
export function matches(entry, query) {
  const words = (query ?? "").toLowerCase().split(/\s+/).filter(Boolean);
  if (words.length === 0) return true;
  const { item } = entry;
  const haystack = [item.name, item.itemClass, entry.placeName, entry.placeId, item.where, item.reason]
    .filter(Boolean)
    .join(" ")
    .toLowerCase();
  return words.every((w) => haystack.includes(w));
}

/** The entries of one tab that pass the state filter and the search. */
export function visible(all, ui) {
  return all.filter(
    (e) => e.tab === ui.tab && (ui.state === "all" || e.item.state === ui.state) && matches(e, ui.query),
  );
}

/** Entries grouped by place, in the order they first appear. */
export function byPlace(list) {
  const groups = new Map();
  for (const e of list) {
    if (!groups.has(e.placeId)) groups.set(e.placeId, { placeId: e.placeId, placeName: e.placeName, group: e.group, entries: [] });
    groups.get(e.placeId).entries.push(e);
  }
  return [...groups.values()];
}

/** Units per state over a list, counting a stack's quantity. */
export function tally(list) {
  const t = { verified: 0, deduced: 0, stale: 0, conflict: 0 };
  for (const { item } of list) if (item.state in t) t[item.state] += item.quantity ?? 1;
  return t;
}
