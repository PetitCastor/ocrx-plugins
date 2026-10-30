// Entry of the Item Tracker view. The plugin's own window (a WebView2) posts its data document to this
// page, once it has loaded and again on every change; nothing is ever sent back.

import { renderView } from "./render.js";

// The window wraps the document as { type: "ocrx:view-data", data }. A bare document is taken as it
// comes, which is what a plain test harness posts.
const VIEW_DATA_MESSAGE = "ocrx:view-data";

/**
 * @param {Element} root where the view is built
 * @param {Document} doc the document to create nodes with
 * @param {{load: () => object|null, save: (kept: object) => void}} [memory] keeps the picked place between
 *   openings of the window; the view works the same without it
 */
export function createView(root, doc, memory) {
  const kept = memory?.load() ?? {};
  const ui = {
    tab: "locations",
    query: "",
    selected: null,
    place: typeof kept.place === "string" ? kept.place : null,
  };
  let data = null;

  const remember = () => memory?.save({ place: ui.place });
  const draw = () => renderView(root, { doc, data, ui, on });
  const on = {
    tab: (tab) => { ui.tab = tab; ui.selected = null; draw(); },
    // Typing rebuilds the list but not the search box, or the caret would jump out from under the user.
    query: (query) => { ui.query = query; drawKeepingFocus(); },
    select: (id) => { ui.selected = id; draw(); },
    place: (id) => { ui.place = id; ui.selected = null; remember(); drawKeepingFocus(); },
  };

  // A redraw replaces the search box and the dropdowns. The one in use gets the focus back: the caret
  // must not jump out from under someone typing, and arrowing through a closed dropdown must not stop
  // after its first step.
  function drawKeepingFocus() {
    const focused = doc.activeElement;
    const which = ["search", "picker-system", "picker-place"].find((c) => focused?.classList?.contains(c));
    const position = which === "search" ? focused.selectionStart : null;
    draw();
    const again = which ? root.querySelector?.(`.${which}`) : null;
    if (!again) return;
    again.focus();
    if (position != null) again.setSelectionRange(position, position);
  }

  return {
    ui,
    receive(message) {
      const next = message?.type === VIEW_DATA_MESSAGE ? message.data : message;
      // Only the plugin's document, or its absence, replaces what is shown: a stray message must not blank the page.
      if (next !== null && (typeof next !== "object" || next.schema === undefined)) return;
      data = next;
      drawKeepingFocus();
    },
    draw,
  };
}

// Browser storage can be missing or refuse access (a cleared or locked profile): the view then just
// opens where the player is, as on a first run.
const MEMORY_KEY = "itemTracker.view";
const browserMemory = {
  load() {
    try {
      return JSON.parse(window.localStorage.getItem(MEMORY_KEY) ?? "null");
    } catch {
      return null;
    }
  },
  save(kept) {
    try {
      window.localStorage.setItem(MEMORY_KEY, JSON.stringify(kept));
    } catch {
      // Nothing to do: the choice lasts for this opening only.
    }
  },
};

if (typeof window !== "undefined" && typeof document !== "undefined") {
  const view = createView(document.getElementById("app"), document, browserMemory);
  // Only the host can post here: chrome.webview carries the plugin's messages and nothing else.
  window.chrome?.webview?.addEventListener("message", (event) => view.receive(event.data));
  view.draw();
}
