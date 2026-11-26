// Shared browser helpers for the website and the extension (ES module, loaded via IJSRuntime import).

const THEME_KEY = "of-theme";

export function getTheme() {
  const explicit = document.documentElement.getAttribute("data-theme");
  if (explicit) return explicit;
  return window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

export function setTheme(theme) {
  document.documentElement.setAttribute("data-theme", theme);
  try { localStorage.setItem(THEME_KEY, theme); } catch { /* storage blocked: theme still applies for this page */ }
  return theme;
}

export function toggleTheme() {
  return setTheme(getTheme() === "dark" ? "light" : "dark");
}

/** Posts the cookie choice; the server sets (or clears) the visitor cookie and returns the visitor id. */
export async function sendCookieConsent(url, accept) {
  const res = await fetch(url, {
    method: "POST",
    credentials: "same-origin",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ accept }),
  });
  if (!res.ok) throw new Error("Consent request failed: " + res.status);
  return await res.json();
}

export function attachDropZone(el, inputEl) {
  if (!el) return;
  const on = (e) => { e.preventDefault(); el.classList.add("over"); };
  const off = () => el.classList.remove("over");
  el.addEventListener("dragenter", on);
  el.addEventListener("dragover", on);
  el.addEventListener("dragleave", off);
  el.addEventListener("drop", off);
}

export function browserInfo() {
  const ua = navigator.userAgent;
  const brands = (navigator.userAgentData && navigator.userAgentData.brands || []).map(b => b.brand);
  const isEdge = /Edg\//.test(ua);
  const isBrave = !!navigator.brave;
  const isChrome = brands.includes("Google Chrome") || (/Chrome\//.test(ua) && !isEdge);
  const isMobile = (navigator.userAgentData && navigator.userAgentData.mobile) || /Android|iPhone|iPad|Mobile/.test(ua);
  const name = isBrave ? "Brave" : isEdge ? "Edge" : isChrome ? "Chrome" : /Firefox\//.test(ua) ? "Firefox" : /Safari\//.test(ua) ? "Safari" : "this browser";
  return { name, supported: (isChrome || isEdge || isBrave) && !isMobile, isMobile: !!isMobile };
}

/** Opens the phone's share sheet for a link, or copies it. Returns "shared", "copied" or "failed". */
export async function shareLink(title, url) {
  try {
    if (navigator.share) { await navigator.share({ title, url }); return "shared"; }
  } catch (e) { if (e && e.name === "AbortError") return "cancelled"; }
  try { await navigator.clipboard.writeText(url); return "copied"; } catch { return "failed"; }
}

/** Asks the installed extension for its version (externally_connectable). Returns null when not installed. */
export function pingExtension(extensionId) {
  return new Promise((resolve) => {
    try {
      if (!extensionId || !window.chrome || !chrome.runtime || !chrome.runtime.sendMessage) return resolve(null);
      const timer = setTimeout(() => resolve(null), 800);
      chrome.runtime.sendMessage(extensionId, { type: "ping" }, (reply) => {
        clearTimeout(timer);
        if (chrome.runtime.lastError || !reply) return resolve(null);
        resolve(reply.version || null);
      });
    } catch { resolve(null); }
  });
}

export function focusElement(el) { if (el) el.focus(); }

export function scrollToTop() { window.scrollTo({ top: 0, behavior: "smooth" }); }

/** Cloudflare Turnstile widget. Calls dotnet.TurnstileToken(token) when solved. */
export function renderTurnstile(el, siteKey, dotnet) {
  const draw = () => window.turnstile.render(el, {
    sitekey: siteKey,
    callback: (t) => dotnet.invokeMethodAsync("TurnstileToken", t),
    "expired-callback": () => dotnet.invokeMethodAsync("TurnstileToken", ""),
  });
  if (window.turnstile) return draw();
  const s = document.createElement("script");
  s.src = "https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit";
  s.async = true;
  s.onload = draw;
  document.head.appendChild(s);
}

/**
 * Fix-a-box editor. Boxes are absolutely positioned children (.bx[data-i]) of `root`.
 * Drag a box to move it, drag its handle to resize, or (when drawing) drag on empty space to add one.
 * Reports normalised (0-1) boxes back to .NET.
 */
export function initBoxEditor(root, dotnet) {
  if (!root || root.dataset.ofEditor) return;
  root.dataset.ofEditor = "1";
  let drag = null;

  const rect = () => root.getBoundingClientRect();
  const clamp = (v) => Math.min(1, Math.max(0, v));

  root.addEventListener("pointerdown", (e) => {
    const r = rect();
    const px = (e.clientX - r.left) / r.width, py = (e.clientY - r.top) / r.height;
    const handle = e.target.closest(".hd");
    const box = e.target.closest(".bx");
    if (box) {
      const b = { x: parseFloat(box.style.left) / 100, y: parseFloat(box.style.top) / 100, w: parseFloat(box.style.width) / 100, h: parseFloat(box.style.height) / 100 };
      drag = { mode: handle ? "resize" : "move", el: box, i: +box.dataset.i, sx: px, sy: py, b };
    } else if (root.classList.contains("drawing")) {
      const el = document.createElement("div");
      el.className = "bx";
      root.appendChild(el);
      drag = { mode: "draw", el, sx: px, sy: py, b: { x: px, y: py, w: 0, h: 0 } };
    } else return;
    root.setPointerCapture(e.pointerId);
    e.preventDefault();
  });

  root.addEventListener("pointermove", (e) => {
    if (!drag) return;
    const r = rect();
    const px = clamp((e.clientX - r.left) / r.width), py = clamp((e.clientY - r.top) / r.height);
    const dx = px - drag.sx, dy = py - drag.sy;
    let { x, y, w, h } = drag.b;
    if (drag.mode === "move") { x = clamp(x + dx); y = clamp(y + dy); x = Math.min(x, 1 - w); y = Math.min(y, 1 - h); }
    else if (drag.mode === "resize") { w = Math.max(0.03, Math.min(1 - x, w + dx)); h = Math.max(0.03, Math.min(1 - y, h + dy)); }
    else { x = Math.min(drag.sx, px); y = Math.min(drag.sy, py); w = Math.abs(dx); h = Math.abs(dy); }
    Object.assign(drag.el.style, { left: x * 100 + "%", top: y * 100 + "%", width: w * 100 + "%", height: h * 100 + "%" });
    drag.cur = { x, y, w, h };
  });

  const end = async () => {
    if (!drag) return;
    const d = drag; drag = null;
    if (!d.cur) { if (d.mode === "draw") d.el.remove(); return; }
    if (d.mode === "draw") {
      d.el.remove();
      if (d.cur.w > 0.03 && d.cur.h > 0.03) await dotnet.invokeMethodAsync("BoxAdded", d.cur.x, d.cur.y, d.cur.w, d.cur.h);
    } else {
      await dotnet.invokeMethodAsync("BoxChanged", d.i, d.cur.x, d.cur.y, d.cur.w, d.cur.h);
    }
  };
  root.addEventListener("pointerup", end);
  root.addEventListener("pointercancel", end);
}
