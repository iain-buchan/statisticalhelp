(() => {
  "use strict";

  // The editor is the existing WebView2 document. Supply its message sink
  // only when the browser shell embeds it on the same local origin.
  const channel = "statsdirect-help-editor-browser";
  const shell = window.parent;
  const origin = window.location.origin;
  try {
    if (shell === window || shell.location.origin !== origin) return;
  } catch {
    return;
  }

  const stylesheet = document.createElement("link");
  stylesheet.rel = "stylesheet";
  stylesheet.href = "/editor-shell.css";
  document.head.append(stylesheet);

  const actions = new Set([
    "ready", "dirty", "view", "save", "preview-request", "open-link",
    "equation-dialog", "editor-dialog", "search-replace"
  ]);
  if (typeof window.chrome?.webview?.postMessage === "function") return;
  if (!window.chrome) window.chrome = {};
  window.chrome.webview = {
    postMessage(message) {
      if (!message || typeof message !== "object" || !actions.has(message.action)) return;
      // Keep an editor event small. The shell retrieves source directly from
      // the editor API at the start of a save or preview operation.
      const { action, path, dirty, view, reason, href } = message;
      shell.postMessage({ channel, action, path, dirty, view, reason, href }, origin);
    }
  };
})();
