"use strict";

const channel = "statsdirect-help-editor-browser";
const byId = id => document.getElementById(id);
const ui = {
  editor: byId("editor-frame"), preview: byId("preview-frame"),
  previewPane: byId("preview-pane"), previewTitle: byId("preview-title"),
  previewDetails: byId("preview-details"), documentGrid: byId("document-grid"),
  closePreview: byId("close-preview"), website: byId("preview-website"),
  desktop: byId("preview-desktop"), save: byId("save"),
  title: byId("page-title"), pageState: byId("page-state"),
  search: byId("topic-search"), topicList: byId("topic-list"), count: byId("topic-count"),
  status: byId("status"), error: byId("error"), externalLink: byId("external-link"),
  unsaved: byId("unsaved-dialog"), unsavedMessage: byId("unsaved-message")
};

let topics = [];
let appearance = "flare";
let current = null;
let editorBaseline = null;
let lastDirty = false;
let busy = false;
let saving = false;
let switching = false;
let ready = false;
let lastPreviewTarget = null;

function editor() {
  const api = ui.editor.contentWindow?.HelpEditor;
  if (!api) throw new Error("The page editor is still starting.");
  return api;
}

function draftSource() {
  const api = editor();
  if (api.getState().dialogOpen) throw new Error("Apply or close the open editor dialog first.");
  return api.getSource();
}

function sameSource(left, right) {
  return typeof left === "string" && typeof right === "string" &&
    left.replace(/\r\n?/g, "\n") === right.replace(/\r\n?/g, "\n");
}

function hasUnsavedChanges() {
  if (!current) return false;
  try {
    const state = editor().getState();
    if (state.dialogDraftDirty) return true;
    // An earlier draft can finish saving while the editor still holds later
    // edits. In that case its internal saved baseline predates the disk save.
    if (!sameSource(editorBaseline, current.source)) return !sameSource(editor().getSource(), current.source);
    return state.dirty;
  } catch {
    return true;
  }
}

function updateControls() {
  lastDirty = hasUnsavedChanges();
  const editorDialog = ready && editor().getState().dialogOpen;
  const blocked = busy || !current || editorDialog || ui.unsaved.open;
  for (const button of [ui.save, ui.website, ui.desktop]) button.disabled = blocked;
  const pageState = !current ? "Scratch workspace" : lastDirty ? "Unsaved changes" : "Saved in scratch workspace";
  if (ui.pageState.textContent !== pageState) ui.pageState.textContent = pageState;
  ui.pageState.dataset.dirty = String(lastDirty);
  ui.topicList.setAttribute("aria-busy", String(busy));
}

function announce(message) {
  ui.status.textContent = message;
  ui.error.hidden = true;
  ui.externalLink.hidden = true;
}

function reportError(error) {
  ui.error.textContent = error instanceof Error ? error.message : String(error);
  ui.error.hidden = false;
}

function start(message, lockEditor = false) {
  busy = true;
  ui.editor.inert = lockEditor;
  announce(message);
  updateControls();
}

function finish() {
  busy = false;
  ui.editor.inert = false;
  updateControls();
}

async function fetchJson(url, request) {
  const response = await fetch(url, {
    cache: "no-store",
    ...request
  });
  let body;
  try {
    body = await response.json();
  } catch {
    throw new Error("The local editor service returned an unreadable response.");
  }
  if (!response.ok) throw new Error(typeof body?.error === "string" ? body.error : `The local request failed (${response.status}).`);
  return body;
}

function postJson(url, body) {
  return fetchJson(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body)
  });
}

function readTopic(result, expectedPath) {
  if (!result || result.path !== expectedPath || !["title", "source", "revision", "baseUrl"].every(key => typeof result[key] === "string")) {
    throw new Error("The local editor service returned an invalid topic.");
  }
  const assetBase = new URL(result.baseUrl, location.origin);
  if (assetBase.origin !== location.origin || !assetBase.pathname.startsWith("/source/")) {
    throw new Error("The topic asset address is outside the scratch workspace.");
  }
  return { ...result, baseUrl: assetBase.href };
}

function visibleTopicButtons() {
  return Array.from(ui.topicList.querySelectorAll("button"));
}

function focusTopicButton(button) {
  for (const item of visibleTopicButtons()) item.tabIndex = item === button ? 0 : -1;
  button.focus();
}

function renderTopics() {
  const focusedPath = ui.topicList.contains(document.activeElement) ? document.activeElement.dataset.path : null;
  const words = ui.search.value.trim().toLocaleLowerCase().split(/\s+/).filter(Boolean);
  const matches = topics.filter(topic => {
    const text = `${topic.title} ${topic.path}`.toLocaleLowerCase();
    return words.every(word => text.includes(word));
  });
  const selected = matches.find(topic => topic.path === current?.path) ?? matches[0];
  const list = document.createDocumentFragment();
  let nextFocus = null;
  for (const topic of matches) {
    const item = document.createElement("li");
    const button = document.createElement("button");
    button.type = "button";
    button.className = "topic-button";
    button.dataset.path = topic.path;
    button.tabIndex = topic === selected ? 0 : -1;
    if (topic.path === current?.path) button.setAttribute("aria-current", "page");
    if (topic.path === focusedPath) nextFocus = button;
    const title = document.createElement("strong");
    title.textContent = topic.title;
    const path = document.createElement("span");
    path.textContent = topic.path;
    button.append(title, path);
    item.append(button);
    list.append(item);
  }
  ui.topicList.replaceChildren(list);
  if (nextFocus) focusTopicButton(nextFocus);
  ui.count.textContent = `${matches.length} ${matches.length === 1 ? "topic" : "topics"}${words.length ? ` of ${topics.length}` : ""}`;
}

function confirmNavigation(title) {
  ui.unsavedMessage.textContent = `Save your changes to ${current.title} before opening ${title}?`;
  ui.unsaved.returnValue = "cancel";
  return new Promise(resolve => {
    ui.unsaved.addEventListener("close", () => resolve(ui.unsaved.returnValue), { once: true });
    ui.unsaved.showModal();
  });
}

function clearPreview() {
  ui.previewPane.hidden = true;
  ui.documentGrid.classList.remove("has-preview");
  ui.preview.removeAttribute("src");
}

async function openTopic(topic, fragment) {
  if (!ready || busy || switching) return;
  if (topic.path === current?.path) {
    if (fragment) editor().revealFragment(fragment);
    return;
  }
  switching = true;
  try {
    if (editor().getState().dialogOpen) throw new Error("Apply or close the open editor dialog first.");
    if (hasUnsavedChanges()) {
      const decision = await confirmNavigation(topic.title);
      if (decision === "cancel") return;
      if (decision === "save" && !(await savePage())) return;
    }
    start(`Opening ${topic.title}…`, true);
    try {
      const query = new URLSearchParams({ path: topic.path });
      const next = readTopic(await fetchJson(`/api/topic?${query}`), topic.path);
      editor().load({ ...next, fragment, topics, target: "Desktop", appearance });
      current = next;
      editorBaseline = next.source;
      ui.title.textContent = next.title;
      clearPreview();
      renderTopics();
      announce(`Opened ${next.path}`);
    } finally {
      finish();
    }
  } catch (error) {
    announce("The current draft is open.");
    reportError(error);
  } finally {
    switching = false;
  }
}

async function savePage() {
  if (!current || busy || ui.unsaved.open) return false;
  try {
    const source = draftSource();
    if (sameSource(source, current.source)) {
      editor().markSaved(current.source);
      editorBaseline = current.source;
      announce("The page is saved.");
      updateControls();
      return true;
    }
    const submitted = { path: current.path, revision: current.revision, source };
    saving = true;
    start("Saving the page…");
    try {
      const saved = readTopic(await postJson("/api/topic", submitted), submitted.path);
      current = saved;
      // Update the next request's revision even if new edits arrived during
      // this save. Only mark the editor clean when its buffer still matches.
      let liveSource;
      try { liveSource = draftSource(); } catch { /* Preserve incomplete source or a dialog draft. */ }
      if (sameSource(liveSource, submitted.source) || sameSource(liveSource, saved.source)) {
        editor().markSaved(saved.source);
        editorBaseline = saved.source;
      }
      const entry = topics.find(topic => topic.path === saved.path);
      if (entry && entry.title !== saved.title) {
        entry.title = saved.title;
        renderTopics();
        editor().setTopics(topics);
      }
      ui.title.textContent = saved.title;
      const clean = !hasUnsavedChanges();
      announce(clean ? "Saved the page to the scratch workspace." : "Saved the submitted draft. Your later edits are still open.");
      return clean;
    } finally {
      saving = false;
      finish();
    }
  } catch (error) {
    announce("Review the page before saving again.");
    reportError(error);
    return false;
  }
}

async function buildPreview(target) {
  if (!current || busy || switching || ui.unsaved.open) return;
  const title = target === "website" ? "Website preview" : "App help preview";
  try {
    const source = draftSource();
    const submitted = { path: current.path, revision: current.revision, source, target };
    const isDraft = !sameSource(source, current.source);
    start(`Building ${title.toLowerCase()}…`);
    try {
      const result = await postJson("/api/preview", submitted);
      if (typeof result?.url !== "string" || !Number.isInteger(result.topicCount) || !Number.isInteger(result.warningCount)) {
        throw new Error("The builder returned an invalid preview response.");
      }
      const url = new URL(result.url, location.origin);
      if (url.origin !== location.origin || !url.pathname.startsWith("/preview/")) throw new Error("The preview address is outside the scratch workspace.");
      lastPreviewTarget = target;
      ui.previewTitle.textContent = title;
      ui.previewDetails.textContent = `${isDraft ? "Draft snapshot" : "Saved source"} · ${result.topicCount} topics · ${result.warningCount} warnings`;
      ui.preview.title = title;
      ui.preview.src = url.href;
      ui.previewPane.hidden = false;
      ui.documentGrid.classList.add("has-preview");
      let liveSource;
      try { liveSource = draftSource(); } catch { /* An incomplete draft is still editable. */ }
      announce(sameSource(liveSource, source) ? `${title} ready.` : `${title} shows the draft captured when the build started.`);
      if (matchMedia("(max-width: 1120px)").matches) ui.previewPane.scrollIntoView({ block: "nearest" });
    } finally {
      finish();
    }
  } catch (error) {
    announce("Review the page and build the preview again.");
    reportError(error);
  }
}

async function openLink(href) {
  if (typeof href !== "string") return;
  let url;
  try { url = new URL(href, location.origin); } catch { return; }
  if (url.origin === location.origin && url.pathname.startsWith("/source/")) {
    let path;
    try { path = decodeURIComponent(url.pathname.slice("/source/".length)); } catch { return; }
    const target = topics.find(topic => topic.path.toLowerCase() === path.toLowerCase());
    if (target) await openTopic(target, url.hash);
    return;
  }
  if (!["https:", "http:", "mailto:"].includes(url.protocol) || url.origin === location.origin) return;
  ui.externalLink.href = url.href;
  ui.externalLink.hidden = false;
  ui.externalLink.focus();
}

let signalReady;
const editorReady = new Promise(resolve => { signalReady = resolve; });
window.addEventListener("message", event => {
  if (event.origin !== location.origin || event.source !== ui.editor.contentWindow) return;
  const message = event.data;
  if (!message || message.channel !== channel) return;
  if (message.action === "ready") {
    signalReady();
    return;
  }
  if (!ready) return;
  if (message.action === "save" && message.path === current?.path) void savePage();
  if (message.action === "preview-request" && ["browser", "app"].includes(message.view)) {
    void buildPreview(message.view === "browser" ? "website" : "desktop");
  }
  if (["dirty", "view", "equation-dialog", "editor-dialog"].includes(message.action)) updateControls();
  if (message.action === "search-replace") ui.search.focus();
  if (message.action === "open-link") void openLink(message.href);
});

ui.search.addEventListener("input", renderTopics);
ui.search.addEventListener("keydown", event => {
  if (event.key !== "ArrowDown") return;
  const selected = ui.topicList.querySelector("button[tabindex='0']");
  if (selected) {
    event.preventDefault();
    focusTopicButton(selected);
  }
});
ui.topicList.addEventListener("keydown", event => {
  if (!["ArrowUp", "ArrowDown", "Home", "End"].includes(event.key)) return;
  const buttons = visibleTopicButtons();
  const index = buttons.indexOf(event.target);
  if (index < 0) return;
  event.preventDefault();
  let next = index;
  if (event.key === "Home") next = 0;
  if (event.key === "End") next = buttons.length - 1;
  if (event.key === "ArrowUp") next = Math.max(0, index - 1);
  if (event.key === "ArrowDown") next = Math.min(buttons.length - 1, index + 1);
  focusTopicButton(buttons[next]);
});
ui.topicList.addEventListener("click", event => {
  const button = event.target.closest("button[data-path]");
  if (!button) return;
  const topic = topics.find(item => item.path === button.dataset.path);
  if (topic) void openTopic(topic);
});
ui.save.addEventListener("click", savePage);
ui.website.addEventListener("click", () => buildPreview("website"));
ui.desktop.addEventListener("click", () => buildPreview("desktop"));
ui.closePreview.addEventListener("click", () => {
  clearPreview();
  (lastPreviewTarget === "website" ? ui.website : ui.desktop).focus();
});
document.addEventListener("keydown", event => {
  if (!(event.ctrlKey || event.metaKey) || event.altKey || event.key.toLowerCase() !== "s" || ui.unsaved.open) return;
  event.preventDefault();
  void savePage();
});
window.addEventListener("beforeunload", event => {
  if (saving || lastDirty || hasUnsavedChanges()) {
    event.preventDefault();
    event.returnValue = "";
  }
});

async function initialize() {
  let timer;
  try {
    // MathLive delegates iframe keyboards to the top document. Initialize its
    // local receiver before the child requests a keyboard, using the same assets.
    const { MathfieldElement } = await import("/editor/mathlive/mathlive.min.mjs");
    MathfieldElement.fontsDirectory = new URL("/editor/mathlive/fonts", location.origin).href;
    MathfieldElement.soundsDirectory = null;
    window.mathVirtualKeyboard.originValidator = "same-origin";
    const timeout = new Promise((_, reject) => {
      timer = setTimeout(() => reject(new Error("The page editor did not start. Reload to try again.")), 20000);
    });
    ui.editor.src = "/editor/";
    const [workspace] = await Promise.all([fetchJson("/api/workspace"), Promise.race([editorReady, timeout])]);
    if (!Array.isArray(workspace?.topics) || !workspace.topics.every(topic => typeof topic?.path === "string" && typeof topic?.title === "string")) {
      throw new Error("The scratch workspace did not return a valid topic list.");
    }
    topics = workspace.topics;
    appearance = workspace.desktopAppearance || "flare";
    editor();
    ready = true;
    renderTopics();
    updateControls();
    const first = topics.find(topic => /(^|\/)paired_t\.htm$/i.test(topic.path)) ?? topics[0];
    if (first) await openTopic(first);
    else announce("The scratch workspace has no topics.");
  } catch (error) {
    announce("The editor could not open the scratch workspace.");
    reportError(error);
  } finally {
    clearTimeout(timer);
  }
}

void initialize();
