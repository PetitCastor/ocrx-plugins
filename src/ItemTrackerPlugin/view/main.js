// Entry of the Item Tracker view. The plugin's own window (a WebView2) posts its data document to this
// page, once it has loaded and again on every change; nothing is ever sent back.

import { renderView } from "./render.js";

// The window wraps the document as { type: "ocrx:view-data", data }. A bare document is taken as it
// comes, which is what a plain test harness posts.
const VIEW_DATA_MESSAGE = "ocrx:view-data";

export function createView(root, doc) {
  const ui = { tab: "locations", state: "all", query: "", selected: null };
  let data = null;

  const draw = () => renderView(root, { doc, data, ui, on });
  const on = {
    tab: (tab) => { ui.tab = tab; ui.selected = null; draw(); },
    state: (state) => { ui.state = state; draw(); },
    // Typing rebuilds the list but not the search box, or the caret would jump out from under the user.
    query: (query) => { ui.query = query; drawKeepingSearch(); },
    select: (id) => { ui.selected = id; draw(); },
  };

  function drawKeepingSearch() {
    const search = root.querySelector?.(".search");
    const position = search?.selectionStart;
    draw();
    const again = root.querySelector?.(".search");
    if (again && search) {
      again.focus();
      if (position != null) again.setSelectionRange(position, position);
    }
  }

  return {
    ui,
    receive(message) {
      const next = message?.type === VIEW_DATA_MESSAGE ? message.data : message;
      // Only the plugin's document, or its absence, replaces what is shown: a stray message must not blank the page.
      if (next !== null && (typeof next !== "object" || next.schema === undefined)) return;
      data = next;
      drawKeepingSearch();
    },
    draw,
  };
}

if (typeof window !== "undefined" && typeof document !== "undefined") {
  const view = createView(document.getElementById("app"), document);
  // Only the host can post here: chrome.webview carries the plugin's messages and nothing else.
  window.chrome?.webview?.addEventListener("message", (event) => view.receive(event.data));
  view.draw();
}
