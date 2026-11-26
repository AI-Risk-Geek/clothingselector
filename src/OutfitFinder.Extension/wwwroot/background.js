// Service worker. Plain JS on purpose: chrome.sidePanel.open() must be called synchronously
// inside the context-menu click handler (a user gesture), before any await.
const MENU_ID = "find-with-ai";

chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.create({ id: MENU_ID, title: "Find with AI", contexts: ["image"] });
  chrome.sidePanel.setPanelBehavior({ openPanelOnActionClick: false });
});

chrome.contextMenus.onClicked.addListener((info, tab) => {
  if (info.menuItemId !== MENU_ID || !tab || tab.id === undefined) return;
  // a. open the side panel immediately (still inside the gesture)
  chrome.sidePanel.open({ tabId: tab.id });
  // b-d. hand the image to the side panel, which runs the search and shows the results
  chrome.storage.session.set({
    pending: { srcUrl: info.srcUrl, pageUrl: info.pageUrl || tab.url || null, tabId: tab.id, frameId: info.frameId || 0, at: Date.now() },
  });
});

// The website's install page asks whether the extension is installed (externally_connectable).
chrome.runtime.onMessageExternal.addListener((message, _sender, reply) => {
  if (message && message.type === "ping") reply({ version: chrome.runtime.getManifest().version });
});
