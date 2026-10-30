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

/** True when the state filter or the search narrows what is shown. */
export function filtering(ui) {
  return ui.state !== "all" || (ui.query ?? "").trim() !== "";
}

// Worst first: the dot a place carries in the list says the most urgent thing about it.
const ATTENTION = ["conflict", "stale"];

/**
 * The places of the Locations tab, by system, as the list beside the items shows them. Unfiltered, every
 * station is listed with its units; filtered, only the stations with a match are, each with how many of
 * its items matched. `attention` is the worst state among what the place lists, when it needs a look.
 */
export function placeTree(data, list, isFiltering) {
  const matched = new Map();
  for (const e of list) {
    if (e.tab !== "locations") continue;
    if (!matched.has(e.placeId)) matched.set(e.placeId, []);
    matched.get(e.placeId).push(e);
  }

  const systems = [];
  for (const system of data?.systems ?? []) {
    const places = [];
    for (const station of system.stations ?? []) {
      const hits = matched.get(station.id) ?? [];
      if (isFiltering && hits.length === 0) continue;
      const counts = isFiltering ? tally(hits) : { ...tally([]), ...station.counts };
      places.push({
        id: station.id,
        name: station.name,
        system: system.name,
        lastActivity: station.lastActivity ?? null,
        count: isFiltering ? hits.length : STATES.reduce((n, s) => n + (counts[s] ?? 0), 0),
        attention: ATTENTION.find((s) => counts[s] > 0) ?? null,
      });
    }
    if (places.length) systems.push({ name: system.name, places });
  }
  return systems;
}

/**
 * The place to show: the one picked, while it is still listed; else where the player stands; else the
 * place something last happened at; else the first. Null when nothing is listed.
 */
export function choosePlace(systems, picked, here) {
  const places = systems.flatMap((s) => s.places);
  const listed = (id) => id != null && places.some((p) => p.id === id);
  if (listed(picked)) return picked;
  if (listed(here)) return here;
  const dated = places.filter((p) => p.lastActivity);
  if (dated.length) return dated.reduce((a, b) => (b.lastActivity > a.lastActivity ? b : a)).id;
  return places[0]?.id ?? null;
}

/** Units per state over a list, counting a stack's quantity. */
export function tally(list) {
  const t = { verified: 0, deduced: 0, stale: 0, conflict: 0 };
  for (const { item } of list) if (item.state in t) t[item.state] += item.quantity ?? 1;
  return t;
}
