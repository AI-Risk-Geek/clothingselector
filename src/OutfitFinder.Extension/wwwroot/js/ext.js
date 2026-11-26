// Chrome API helpers for the extension's Blazor pages (popup and side panel).

export function version() { return chrome.runtime.getManifest().version; }

export async function getVisitorId() {
  const { visitorId } = await chrome.storage.local.get("visitorId");
  if (visitorId) return visitorId;
  const id = crypto.randomUUID();
  await chrome.storage.local.set({ visitorId: id });
  return id;
}

export async function getFlag(key) { const r = await chrome.storage.local.get(key); return !!r[key]; }
export async function setFlag(key) { await chrome.storage.local.set({ [key]: true }); }

/** Right-click request (or a popup upload) waiting for the side panel. Removed once read. */
export async function takePending() {
  const { pending } = await chrome.storage.session.get("pending");
  if (pending) await chrome.storage.session.remove("pending");
  return pending ? JSON.stringify(pending) : null;
}

export function onPending(dotnet) {
  chrome.storage.onChanged.addListener((changes, area) => {
    if (area === "session" && changes.pending && changes.pending.newValue) dotnet.invokeMethodAsync("PendingArrived");
  });
}

export async function setPendingSearch(searchId) {
  await chrome.storage.session.set({ pending: { searchId, at: Date.now() } });
}

/** From a popup click (a user gesture): open the side panel in the current window. */
export async function openSidePanel() {
  const win = await chrome.windows.getCurrent();
  await chrome.sidePanel.open({ windowId: win.id });
}

/**
 * Fallback when the server can't download the image (login-only or expiring links):
 * read it from inside the page (activeTab + scripting), returning base64 JPEG/PNG bytes.
 */
export async function grabImage(tabId, frameId, srcUrl) {
  const [result] = await chrome.scripting.executeScript({
    target: { tabId, frameIds: [frameId || 0] },
    args: [srcUrl],
    func: async (src) => {
      const toBase64 = (blob) => new Promise((resolve, reject) => {
        const r = new FileReader();
        r.onload = () => resolve(String(r.result).split(",")[1]);
        r.onerror = reject;
        r.readAsDataURL(blob);
      });
      try {
        const res = await fetch(src, { credentials: "include" });
        if (res.ok) return await toBase64(await res.blob());
      } catch (e) { /* fall through to canvas */ }
      const img = Array.from(document.images).find((i) => i.currentSrc === src || i.src === src);
      if (!img) return null;
      const canvas = document.createElement("canvas");
      canvas.width = img.naturalWidth; canvas.height = img.naturalHeight;
      canvas.getContext("2d").drawImage(img, 0, 0);
      try { return canvas.toDataURL("image/jpeg", 0.92).split(",")[1]; } catch (e) { return null; } // tainted canvas
    },
  });
  return result ? result.result : null;
}

// ---------- Sign-in: OAuth 2 authorization code + PKCE via chrome.identity ----------

const b64url = (bytes) => btoa(String.fromCharCode(...new Uint8Array(bytes))).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
const randomString = () => b64url(crypto.getRandomValues(new Uint8Array(32)));

async function tokenRequest(authority, body) {
  const res = await fetch(authority.replace(/\/$/, "") + "/connect/token", {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams(body),
  });
  if (!res.ok) throw new Error("Token request failed: " + res.status);
  const t = await res.json();
  const claims = t.id_token ? JSON.parse(atob(t.id_token.split(".")[1].replace(/-/g, "+").replace(/_/g, "/"))) : null;
  const stored = (await chrome.storage.local.get("auth")).auth || {};
  const auth = {
    accessToken: t.access_token,
    refreshToken: t.refresh_token || stored.refreshToken,
    expiresAt: Date.now() + (t.expires_in - 60) * 1000,
    name: claims ? (claims.name || claims.preferred_username || claims.sub) : stored.name,
  };
  await chrome.storage.local.set({ auth });
  return auth;
}

export async function signIn(authority, clientId) {
  const verifier = randomString();
  const challenge = b64url(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(verifier)));
  const state = randomString();
  const redirectUri = chrome.identity.getRedirectURL();
  const url = new URL(authority.replace(/\/$/, "") + "/connect/authorize");
  url.search = new URLSearchParams({
    client_id: clientId, response_type: "code", scope: "openid profile roles outfit.api offline_access",
    redirect_uri: redirectUri, code_challenge: challenge, code_challenge_method: "S256", state,
  }).toString();
  const result = await chrome.identity.launchWebAuthFlow({ url: url.toString(), interactive: true });
  const back = new URL(result);
  if (back.searchParams.get("state") !== state) throw new Error("Sign-in state mismatch");
  const code = back.searchParams.get("code");
  if (!code) throw new Error(back.searchParams.get("error") || "Sign-in was cancelled");
  const auth = await tokenRequest(authority, {
    grant_type: "authorization_code", code, redirect_uri: redirectUri, client_id: clientId, code_verifier: verifier,
  });
  return auth.name;
}

export async function getAccessToken(authority, clientId) {
  const { auth } = await chrome.storage.local.get("auth");
  if (!auth) return null;
  if (auth.expiresAt > Date.now()) return auth.accessToken;
  if (!auth.refreshToken) return null;
  try {
    return (await tokenRequest(authority, { grant_type: "refresh_token", refresh_token: auth.refreshToken, client_id: clientId })).accessToken;
  } catch (e) {
    await chrome.storage.local.remove("auth");
    return null;
  }
}

export async function getUserName() {
  const { auth } = await chrome.storage.local.get("auth");
  return auth ? auth.name : null;
}

export async function signOut() { await chrome.storage.local.remove("auth"); }

export function openTab(url) { chrome.tabs.create({ url }); }
