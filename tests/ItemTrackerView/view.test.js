// Tests of the Item Tracker's read-only view (src/ItemTrackerPlugin/view), on node:test with no
// dependencies. The DOM is a minimal fake whose nodes take text only through `textContent`: there is no
// innerHTML to set, which is the property that keeps a hostile item name from becoming markup.

import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

import { createView } from "../../src/ItemTrackerPlugin/view/main.js";
import { choosePlace, entries, matches, placeTree, visible } from "../../src/ItemTrackerPlugin/view/model.js";

const data = JSON.parse(readFileSync(new URL("./fixtures/view-data.json", import.meta.url), "utf8"));

class FakeNode {
  constructor(tag) {
    this.tag = tag;
    this.textContent = "";
    this.className = "";
    this.value = "";
    this.children = [];
    this.attributes = {};
  }

  append(...nodes) {
    this.children.push(...nodes);
  }

  replaceChildren(...nodes) {
    this.children = nodes;
  }

  setAttribute(name, value) {
    this.attributes[name] = String(value);
  }
}

const fakeDoc = { createElement: (tag) => new FakeNode(tag) };

function all(node) {
  return [node, ...node.children.flatMap(all)];
}

function texts(node) {
  return all(node).map((n) => n.textContent).filter((t) => t !== "");
}

function pageText(root) {
  return texts(root).join("\n");
}

function open(doc = data, memory) {
  const root = new FakeNode("div");
  const view = createView(root, fakeDoc, memory);
  view.receive(doc);
  return { root, view };
}

function click(root, label) {
  const button = all(root).find((n) => n.tag === "button" && n.textContent === label);
  assert.ok(button, `no button "${label}"`);
  button.onclick();
}

function type(root, value) {
  const box = all(root).find((n) => n.tag === "input");
  box.value = value;
  box.oninput();
}

function byClass(root, className) {
  return all(root).filter((n) => n.className === className).map((n) => n.textContent);
}

function picker(root, label) {
  const select = all(root).find((n) => n.tag === "select" && n.attributes["aria-label"] === label);
  assert.ok(select, `no ${label} picker`);
  return select;
}

function options(root, label) {
  return picker(root, label).children.map((o) => o.textContent);
}

/** The text of the option a picker has selected. */
function chosen(root, label) {
  const select = picker(root, label);
  return select.children.find((o) => o.value === select.value)?.textContent;
}

/** Chooses the option whose text starts with `text`, as a user picking it from the dropdown. */
function choose(root, label, text) {
  const select = picker(root, label);
  const option = select.children.find((o) => o.textContent.startsWith(text));
  assert.ok(option, `no ${label} option "${text}"`);
  select.value = option.value;
  select.onchange();
}

function itemNames(root) {
  return all(root).filter((n) => n.className === "item-name").map((n) => n.textContent);
}

function openItem(root, name) {
  const button = all(root).find(
    (n) => n.className === "item-button" && n.children.some((c) => c.className === "item-name" && c.textContent === name),
  );
  assert.ok(button, `no item "${name}"`);
  button.onclick();
}

function detail(root) {
  const pane = all(root).find((n) => n.tag === "aside");
  assert.ok(pane, "no detail pane");
  return pane;
}

function memoryOf(kept = null) {
  const memory = { kept, load: () => memory.kept, save: (next) => { memory.kept = structuredClone(next); } };
  return memory;
}

const KEEGER = "Keeger — Nyx Social (5) · you are here";

test("an item's state shows in its detail, with its label, and nowhere in the list", () => {
  const { root } = open();
  assert.equal(all(root).filter((n) => n.className.startsWith("badge ")).length, 0);

  const seen = new Map();
  const note = () => {
    const badge = all(detail(root)).find((n) => n.className.startsWith("badge "));
    seen.set(badge.className, badge.textContent);
  };
  for (const name of ["CF-227 Badger Repeater", "CRUZ Dark", "Aztalan Arms"]) {
    openItem(root, name);
    note();
  }
  choose(root, "System", "Unknown system");
  openItem(root, "Paint <b>Star</b> Runner");
  note();

  assert.deepEqual(Object.fromEntries(seen), {
    "badge badge-verified": "✓ Verified",
    "badge badge-conflict": "⚠ Conflict",
    "badge badge-deduced": "Deduced",
    "badge badge-stale": "Stale",
  });
});

test("the header carries no state counts and no state filter", () => {
  const { root } = open();
  const classes = all(root).map((n) => n.className);

  assert.ok(!classes.some((c) => /\b(pill|chip|tally|summary|state-filter)\b/.test(c)), classes.join(" "));
  assert.ok(!all(root).some((n) => n.tag === "button" && n.textContent === "All"));
});

test("locations pick a system, then a place, and show that one station's items", () => {
  const { root } = open();
  const page = pageText(root);

  assert.deepEqual(options(root, "System"), ["Nyx", "Unknown system"]);
  assert.equal(chosen(root, "System"), "Nyx");
  assert.deepEqual(options(root, "Place"), [KEEGER, "Levski (1)"]);
  assert.equal(chosen(root, "Place"), KEEGER);
  assert.deepEqual(itemNames(root), ["CF-227 Badger Repeater", "CRUZ Dark", "Aztalan Arms", "M4A Cannon Defense Division"]);
  assert.ok(page.includes("×5"));
  assert.ok(page.includes("Last visit 2026-10-28 19:28 UTC: 3/5 pages, 17 items on screen; the log's total is 40."));
});

test("changing system opens a place there", () => {
  const { root } = open();
  choose(root, "System", "Unknown system");

  assert.deepEqual(options(root, "Place"), ["location 999000111 (1)"]);
  assert.deepEqual(itemNames(root), ["Paint <b>Star</b> Runner"]);

  choose(root, "System", "Nyx");
  assert.equal(chosen(root, "Place"), KEEGER);
});

test("a station the game has not named yet says so, with its id", () => {
  const { root } = open();
  choose(root, "System", "Unknown system");

  assert.ok(pageText(root).includes("Not named by the game yet: id 999000111"));
  choose(root, "System", "Nyx");
  assert.ok(!pageText(root).includes("Not named by the game yet"));
});

test("picking a place shows its items alone and closes the open item", () => {
  const { root } = open();
  openItem(root, "CRUZ Dark");

  choose(root, "Place", "Levski");

  assert.equal(chosen(root, "Place"), "Levski (1)");
  assert.deepEqual(itemNames(root), ["Pembroke Helmet"]);
  assert.equal(all(root).filter((n) => n.tag === "aside").length, 0);
});

test("ships and drops have no place pickers", () => {
  const { root } = open();
  click(root, "Ships");
  assert.equal(all(root).filter((n) => n.tag === "select").length, 0);
  click(root, "Dropped");
  assert.equal(all(root).filter((n) => n.tag === "select").length, 0);
});

test("search matches the name, the station and the reason, every word", () => {
  const { root } = open();

  type(root, "badger");
  assert.deepEqual(itemNames(root), ["CF-227 Badger Repeater"]);

  type(root, "keeger kiosk origin unknown");
  assert.deepEqual(itemNames(root), ["M4A Cannon Defense Division"]);

  type(root, "999000111");
  assert.deepEqual(itemNames(root), ["Paint <b>Star</b> Runner"]);

  type(root, "BADGER repeater");
  assert.equal(itemNames(root).length, 1);
});

test("a search keeps only the places with a match, each with the units that matched", () => {
  const { root } = open();

  type(root, "helmet");
  assert.deepEqual(options(root, "System"), ["Nyx"]);
  assert.deepEqual(options(root, "Place"), ["Levski (1)"]);
  assert.deepEqual(itemNames(root), ["Pembroke Helmet"]);

  type(root, "cruz");
  assert.deepEqual(options(root, "Place"), [KEEGER]);
});

test("the picked place comes back once the search that hid it is cleared", () => {
  const { root } = open();
  choose(root, "Place", "Levski");

  type(root, "cruz");
  assert.equal(chosen(root, "Place"), KEEGER);

  type(root, "");
  assert.equal(chosen(root, "Place"), "Levski (1)");
});

test("an open item a search moves off the list closes its detail", () => {
  const { root } = open();
  openItem(root, "CRUZ Dark");

  type(root, "helmet");
  assert.equal(all(root).filter((n) => n.tag === "aside").length, 0);
});

test("a search that matches nothing says so", () => {
  const { root } = open();
  type(root, "no such thing");

  assert.ok(pageText(root).includes("Nothing matches this search."));
  assert.deepEqual(itemNames(root), []);
});

test("before the first document it says it is waiting", () => {
  const root = new FakeNode("div");
  const view = createView(root, fakeDoc);
  view.draw();

  assert.ok(pageText(root).includes("Waiting for the tracker's first read of your logs."));
  assert.equal(all(root).filter((n) => n.tag === "select").length, 0);
});

test("a name that looks like markup is shown as text and builds no element", () => {
  const { root } = open();
  const hostile = "Paint <b>Star</b> Runner";
  choose(root, "System", "Unknown system");

  assert.ok(itemNames(root).includes(hostile));
  assert.equal(all(root).filter((n) => n.tag === "b").length, 0);
});

test("nothing on the page can change the ledger: no edit control, only search and navigation", () => {
  const { root } = open();
  openItem(root, "CRUZ Dark");

  const tags = new Set(all(root).map((n) => n.tag));
  for (const forbidden of ["textarea", "form", "a", "iframe", "script"]) assert.ok(!tags.has(forbidden), forbidden);

  const inputs = all(root).filter((n) => n.tag === "input");
  assert.equal(inputs.length, 1);
  assert.equal(inputs[0].attributes.type, "search");
  assert.deepEqual(all(root).filter((n) => n.tag === "select").map((n) => n.attributes["aria-label"]), ["System", "Place"]);

  const labels = all(root).filter((n) => n.tag === "button" && n.className !== "item-button").map((n) => n.textContent);
  assert.deepEqual(labels, ["Locations", "Ships", "Dropped", "Close"]);
  const everyButton = all(root).filter((n) => n.tag === "button").map((n) => texts(n).join(" "));
  for (const word of ["Edit", "Rename", "Adjust", "Delete", "Save", "Add", "Confirm", "Correct"]) {
    assert.ok(!everyButton.some((l) => l.includes(word)), word);
  }
});

test("the view opens on where the player stands and says so", () => {
  const { root } = open();

  assert.equal(chosen(root, "Place"), KEEGER);
  assert.equal(options(root, "Place").filter((o) => o.includes("you are here")).length, 1);
});

test("with no position, the view opens on the place something last happened at", () => {
  const elsewhere = structuredClone(data);
  elsewhere.here = "somewhere-in-space";
  elsewhere.systems[0].stations[1].lastActivity = "2026-10-29T08:00:00Z";
  const { root } = open(elsewhere);

  assert.equal(chosen(root, "Place"), "Levski (1)");
  assert.ok(!options(root, "Place").some((o) => o.includes("you are here")));
});

test("the picked place is kept for the next opening", () => {
  const memory = memoryOf();
  choose(open(data, memory).root, "Place", "Levski");

  assert.deepEqual(memory.kept, { place: "141810852" });
  assert.equal(chosen(open(data, memory).root, "Place"), "Levski (1)");
});

test("a kept place that is gone, or kept state that is not the view's, falls back to where the player is", () => {
  for (const kept of [{ place: "gone" }, { place: 42 }, "garbage", 5, []]) {
    assert.equal(chosen(open(data, memoryOf(kept)).root, "Place"), KEEGER, JSON.stringify(kept));
  }
});

test("ships say the logs cannot tell two of a model apart", () => {
  const { root } = open();
  click(root, "Ships");

  assert.ok(pageText(root).includes("cannot tell two ships of the same model apart"));
  assert.deepEqual(itemNames(root), ["Medical Pen"]);
  assert.ok(pageText(root).includes("Grey's Shiv"));
});

test("dropped items are listed as search leads", () => {
  const { root } = open();
  click(root, "Dropped");

  assert.deepEqual(itemNames(root), ["Gold Bar"]);
  assert.ok(pageText(root).includes("search leads"));
});

test("a document with nothing in a tab gives that tab its own empty message", () => {
  const empty = { schema: 1, counts: { verified: 0, deduced: 0, stale: 0, conflict: 0 }, systems: [], ships: [], dropped: [] };
  const { root } = open(empty);

  assert.ok(pageText(root).includes("No items are recorded at any station yet."));
  click(root, "Ships");
  assert.ok(pageText(root).includes("Nothing is recorded in a ship's storage."));
  click(root, "Dropped");
  assert.ok(pageText(root).includes("Nothing is recorded as left on the ground."));
});

test("an item opens its detail: state and why, last seen, movements", () => {
  const { root } = open();
  assert.equal(all(root).filter((n) => n.tag === "aside").length, 0);

  openItem(root, "CF-227 Badger Repeater");
  const text = pageText(detail(root));

  assert.ok(text.includes("CF-227 Badger Repeater"));
  assert.ok(text.includes("✓ Verified"));
  assert.ok(text.includes("Seen at Keeger kiosk, 28 Oct 15:28, page 2"));
  assert.ok(text.includes("2026-10-28 19:28 UTC"));
  assert.ok(text.includes("Store, 2026-10-21 18:00 UTC"));
  assert.ok(text.includes("2026-10-21 18:00 UTC · Store: your character → Keeger — Nyx Social (Nyx)"));
});

test("an item never seen on screen says never, and one with no movements says so", () => {
  const { root } = open();
  openItem(root, "Aztalan Arms");
  assert.ok(pageText(detail(root)).includes("never"));

  openItem(root, "CRUZ Dark");
  const text = pageText(detail(root));
  assert.ok(text.includes("No logged movements to show."));
  assert.ok(text.includes("the game never confirmed the move that put it here"));
});

test("a screen-only item has no logged move to show", () => {
  const { root } = open();
  openItem(root, "M4A Cannon Defense Division");

  const text = pageText(detail(root));
  assert.ok(text.includes("Seen on screen"));
  assert.ok(text.includes("origin unknown"));
});

test("opening the same item again closes it, and so does Close and changing tab", () => {
  const { root } = open();

  openItem(root, "Aztalan Arms");
  openItem(root, "Aztalan Arms");
  assert.equal(all(root).filter((n) => n.tag === "aside").length, 0);

  openItem(root, "Aztalan Arms");
  click(root, "Close");
  assert.equal(all(root).filter((n) => n.tag === "aside").length, 0);

  openItem(root, "Aztalan Arms");
  click(root, "Ships");
  assert.equal(all(root).filter((n) => n.tag === "aside").length, 0);
});

test("an open item stays open when a new document arrives", () => {
  const { root, view } = open();
  openItem(root, "CRUZ Dark");

  view.receive({ type: "ocrx:view-data", data: structuredClone(data) });

  assert.ok(pageText(detail(root)).includes("Ledger says 5, kiosk showed 2"));
});

test("the window's wrapped message and a bare document are both taken", () => {
  const wrapped = new FakeNode("div");
  createView(wrapped, fakeDoc).receive({ type: "ocrx:view-data", data });
  const bare = new FakeNode("div");
  createView(bare, fakeDoc).receive(data);

  assert.deepEqual(itemNames(wrapped), itemNames(bare));
  assert.equal(itemNames(wrapped).length, 4);
});

test("a document the plugin has deleted (null) goes back to waiting", () => {
  const { root, view } = open();
  view.receive({ type: "ocrx:view-data", data: null });

  assert.ok(pageText(root).includes("Waiting for the tracker's first read"));
});

test("entry ids are stable across documents and distinct for identical items", () => {
  const twins = structuredClone(data);
  twins.systems[0].stations[0].items.push(structuredClone(twins.systems[0].stations[0].items[1]));

  const ids = entries(twins).map((e) => e.id);
  assert.equal(new Set(ids).size, ids.length);
  assert.deepEqual(entries(data).map((e) => e.id), entries(structuredClone(data)).map((e) => e.id));
});

test("matching ignores case and extra spaces, and an empty query matches everything", () => {
  const [first] = entries(data);

  assert.ok(matches(first, ""));
  assert.ok(matches(first, "   "));
  assert.ok(matches(first, "  KEEGER   badger "));
  assert.ok(!matches(first, "badger pistol"));
  assert.equal(visible(entries(data), { tab: "locations", query: "" }).length, 6);
});

test("a stray message leaves the page as it was", () => {
  const { root, view } = open();
  const before = pageText(root);

  view.receive("hello");
  view.receive({ type: "something-else" });
  view.receive({ unrelated: true });

  assert.equal(pageText(root), before);
});

test("choosing a place: picked, else here, else newest activity, else first, else none", () => {
  const places = (...ps) => [{ name: "S", places: ps }];
  const a = { id: "a", lastActivity: "2026-10-01T00:00:00Z" };
  const b = { id: "b", lastActivity: "2026-10-05T00:00:00Z" };
  const c = { id: "c", lastActivity: null };

  assert.equal(choosePlace(places(a, b, c), "c", "a"), "c");
  assert.equal(choosePlace(places(a, b, c), "x", "a"), "a");
  assert.equal(choosePlace(places(a, b, c), null, "x"), "b");
  assert.equal(choosePlace(places(c), null, null), "c");
  assert.equal(choosePlace([], "a", "a"), null);
  assert.deepEqual(placeTree({ systems: [] }, [], false), []);
});
