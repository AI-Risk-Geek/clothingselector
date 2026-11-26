# Outfit Finder — Handoff

Start here. This folder is everything a new chat needs to build the project from scratch.

## What's in this folder

| File | What it is |
|---|---|
| `HANDOFF.md` | This file: context, status, how to start, the kickoff prompt |
| `docs/PLAN.md` | The full build plan in 11 phases (0–10), each with steps and a "Done when" checklist, plus data model, AI instructions and settings |
| `design/tokens.css` | Starter design tokens (colors, fonts, shapes; light + dark) taken from the approved prototype |
| `design/prototype-source/Prototype.dc.html` | Source of the clickable prototype: all screen markup, CSS and sample data. **Reference only** — it runs inside Claude's design canvas, not standalone. Read its `<style>` block and markup when building the Blazor components |
| `design/screenshots/` | Screenshots of the prototype at phone (`m-*`, 390 px) and desktop (`d-*`, 1280 px) widths |

Live clickable prototype (private to the owner's Claude account): https://claude.ai/artifact/9hciHP2fFCYya94QjYuD7f#play-Prototype

## The product in one paragraph

A user uploads a photo of a person (or right-clicks any image in Chrome and picks **"Find with AI"**). An AI model with built-in web search finds every clothing item and accessory in the photo and returns where to buy each one ("Buy it"). For sewn garments it also offers a sewing pattern sized to the user, a printable life-size PDF and a materials shopping list ("Make it"). Results are saved to the user's history. It ships as a Blazor website and a Chrome extension, both in C#.

## Status

- **Planning: complete.** All decisions are in `docs/PLAN.md` under "Decisions (locked)".
- **Design: direction approved.** Colorful consumer-retail look, light and dark mode, mobile layout with hamburger menu. Prototype covers: home, searching, results (Buy it / Make it), my looks, install, sign in / create account, legal pages, AI admin settings.
- **Code: none written.** No repository exists yet.
- **Not designed yet:** account & privacy page and extension popup in the new style (old plain-style versions exist on the canvas), extension side panel, takedown form.

## Open decisions (owner must answer; each blocks the phase shown)

1. Exact AI model name, and that it supports image input + its own web search ("GPT Luna" was mentioned but is unconfirmed) — Phase 0
2. Which AI provider to launch on (Azure AI Foundry / OpenAI / Google) — Phase 4
3. Collect email at sign-up? Without it there is no password reset — Phase 2
4. Business model or monthly budget cap for AI costs — Phase 4
5. Shopping preferences (country, men's/women's, budget) in v1? — Phase 5
6. Pattern engine: FreeSewing (JavaScript helper service) or own C# code — Phase 9
7. Azure region — Phase 10
8. Lawyer review of privacy policy, terms and the face-detection question — before public launch

## Things the plan depends on that are unverified

- That one AI call returns usable item boxes, real product links and correct prices. **Phase 0 exists to test this before any code is written.** If it fails, the search design must change.
- That the MySQL EF Core provider (Pomelo) supports the EF Core version in use. Check when scaffolding; fallback is Oracle's provider or .NET 9.
- The Azure AI SDK package names and Azure pricing figures quoted in planning were from memory.
- Face blurring uses face boxes from the AI model; it is approximate, not guaranteed.
- Chrome only installs extensions from the Chrome Web Store for normal users; the website's "manual install" route is for testers.

## How to start (on your laptop)

1. Create an empty private GitHub repo and clone it.
2. Unzip this folder into the repo root (so you have `HANDOFF.md`, `docs/`, `design/`). Commit.
3. Open Claude Code in the repo and paste the kickoff prompt below.
4. Work one phase at a time. Don't start a phase until the previous one's "Done when" list is ticked.

Prerequisites: .NET SDK, Docker Desktop, Node (only if FreeSewing is chosen), an AI provider key, an Azure AI Content Safety resource (Phase 4).

## Kickoff prompt (paste into the new chat)

```
We're building "Outfit Finder" (placeholder name) from scratch in this repo.

Read HANDOFF.md first, then docs/PLAN.md in full. Those two files are the
source of truth: every product and technical decision is already made there.
Don't re-open locked decisions; if something in the plan looks wrong or
out of date (package names, versions), tell me before changing course.

Design reference: design/tokens.css is the starting token file.
design/prototype-source/Prototype.dc.html holds the approved markup, CSS and
sample data for every screen (it's a design-canvas file, not runnable — read
it as a reference). design/screenshots/ shows phone and desktop renders.

Rules:
- Work one phase at a time, in order. For each phase: restate the steps,
  do them, then walk the "Done when" checklist and show me evidence
  (test output, screenshots) before moving on.
- Brand name comes from the single BrandName property; colors and fonts
  only from tokens.css. Never hard-code either.
- All database access through EF Core on MySQL. No raw SQL.
- No secrets in appsettings.json; use dotnet user-secrets locally.
- If the AI provider isn't configured, fail loudly. No silent fallbacks.
- Check mobile (390 px) and dark mode for every page you build.

Start by listing the open decisions from HANDOFF.md that block Phase 0 and
Phase 1 and ask me for them. Phase 0 is a manual playground test I run
myself: give me a checklist and a results template for it. Then begin
Phase 1.
```
