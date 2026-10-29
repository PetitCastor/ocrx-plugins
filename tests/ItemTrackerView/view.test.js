// Tests of the Item Tracker's read-only view (src/ItemTrackerPlugin/view), on node:test with no
// dependencies. The DOM is a minimal fake whose nodes take text only through `textContent`: there is no
// innerHTML to set, which is the property that keeps a hostile item name from becoming markup.

import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

import { createView } from "../../src/ItemTrackerPlugin/view/main.js";
import { entries, matches, visible } from "../../src/ItemTrackerPlugin/view/model.js";

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

function open(doc = data) {
  const root = new FakeNode("div");
  const view = createView(root, fakeDoc);
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

function itemNames(root) {
  return all(root).filter((n) => n.className === "item-name").map((n) => n.textContent);
}

test("every state is rendered with its label", () => {
  const { root } = open();
  const page = pageText(root);

  for (const label of ["✓ Verified", "Deduced", "Stale", "⚠ Conflict"]) assert.ok(page.includes(label), label);
  const badges = all(root).filter((n) => n.className.startsWith("badge "));
  assert.deepEqual(
    new Set(badges.map((b) => b.className)),
    new Set(["badge badge-verified", "badge badge-deduced", "badge badge-stale", "badge badge-conflict"]),
  );
});

test("the header sums each state from the document", () => {
  const { root } = open();
  const pills = all(root).filter((n) => n.className.startsWith("pill "));

  assert.deepEqual(pills.map((p) => p.textContent), ["4 ✓ Verified", "3 Deduced", "1 Stale", "2 ⚠ Conflict"]);
});

test("locations go system, then station, then items with quantity", () => {
  const { root } = open();
  const page = pageText(root);

  assert.deepEqual(all(root).filter((n) => n.className === "system").map((n) => n.textContent), ["Nyx", "Unknown system"]);
  assert.ok(page.includes("Keeger — Nyx Social (Nyx)"));
  assert.ok(page.includes("×5"));
  assert.ok(page.includes("Last visit 2026-10-28 19:28 UTC: 3/5 pages, 17 items on screen; the log's total is 40."));
});

test("a station the game has not named yet shows its id", () => {
  const { root } = open();
  const names = all(root).filter((n) => n.className === "place-name").map((n) => texts(n).join(""));

  assert.ok(names.some((t) => t.includes("location 999000111") && t.includes("id 999000111")), names.join(" | "));
  assert.ok(!names.find((t) => t.includes("Keeger"))?.includes(" id "));
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

test("the state filter keeps one state and All brings the rest back", () => {
  const { root } = open();

  click(root, "⚠ Conflict");
  assert.deepEqual(itemNames(root), ["CRUZ Dark"]);

  click(root, "Stale");
  assert.deepEqual(itemNames(root), ["Paint <b>Star</b> Runner"]);

  click(root, "All");
  assert.equal(itemNames(root).length, 5);
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

test("search and the state filter combine", () => {
  const { root } = open();
  click(root, "Deduced");
  type(root, "arms");

  assert.deepEqual(itemNames(root), ["Aztalan Arms"]);
});

test("a search that matches nothing says so", () => {
  const { root } = open();
  type(root, "no such thing");

  assert.ok(pageText(root).includes("Nothing matches this filter."));
  assert.deepEqual(itemNames(root), []);
});

test("before the first document it says it is waiting", () => {
  const root = new FakeNode("div");
  const view = createView(root, fakeDoc);
  view.draw();

  assert.ok(pageText(root).includes("Waiting for the tracker's first read of your logs."));
  assert.equal(all(root).filter((n) => n.className.startsWith("pill ")).length, 0);
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

test("a name that looks like markup is shown as text and builds no element", () => {
  const { root } = open();
  const hostile = "Paint <b>Star</b> Runner";

  assert.ok(itemNames(root).includes(hostile));
  assert.equal(all(root).filter((n) => n.tag === "b").length, 0);
});

test("nothing on the page can change the ledger: no edit control, only filters and navigation", () => {
  const { root } = open();
  openItem(root, "CRUZ Dark");

  const tags = new Set(all(root).map((n) => n.tag));
  for (const forbidden of ["textarea", "select", "form", "a", "iframe", "script"]) assert.ok(!tags.has(forbidden), forbidden);

  const inputs = all(root).filter((n) => n.tag === "input");
  assert.equal(inputs.length, 1);
  assert.equal(inputs[0].attributes.type, "search");

  const labels = all(root).filter((n) => n.tag === "button" && n.className !== "item-button").map((n) => n.textContent);
  assert.deepEqual(labels, [
    "Locations", "Ships", "Dropped", "All", "✓ Verified", "Deduced", "Stale", "⚠ Conflict", "Close",
  ]);
  for (const word of ["Edit", "Rename", "Adjust", "Delete", "Save", "Add"]) {
    assert.ok(!labels.some((l) => l.includes(word)), word);
  }
});

test("the window's wrapped message and a bare document are both taken", () => {
  const wrapped = new FakeNode("div");
  createView(wrapped, fakeDoc).receive({ type: "ocrx:view-data", data });
  const bare = new FakeNode("div");
  createView(bare, fakeDoc).receive(data);

  assert.deepEqual(itemNames(wrapped), itemNames(bare));
  assert.equal(itemNames(wrapped).length, 5);
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
  assert.equal(visible(entries(data), { tab: "locations", state: "all", query: "" }).length, 5);
});
