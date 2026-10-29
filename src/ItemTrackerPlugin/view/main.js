// Entry of the Item Tracker view. The OCRX window posts the plugin's data document into this frame,
// once on load and again on every change; nothing is ever sent back.

import { renderView } from "./render.js";

// The window wraps the document as { type: "ocrx:view-data", data }. A bare document is taken as it
// comes, which is what the SDK's guide shows and what a plain test harness posts.
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
      data = message?.type === VIEW_DATA_MESSAGE ? message.data : message;
      draw();
    },
    draw,
  };
}

if (typeof window !== "undefined" && typeof document !== "undefined") {
  const view = createView(document.getElementById("app"), document);
  window.addEventListener("message", (event) => {
    if (event.source !== window.parent) return;
    view.receive(event.data);
  });
  view.draw();
}
