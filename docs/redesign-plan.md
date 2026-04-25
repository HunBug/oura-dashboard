# UI Redesign Plan

**Origin:** Designer proposal, April 2025  
**Philosophy:** The app is a morning ritual tool, not a general-purpose dashboard. Primary use case: *"How was last night, how are we trending?"* Everything else serves investigation.

---

## Navigation

### Current pages
`Home` · `UserDetail` · `UserCard` · `Compare` · `Sync` · `MetricsGuide` · `NightDetail`

### Target navigation (5 items)
| Page | Status | Notes |
|---|---|---|
| **Home** | Redesign | Morning briefing, both users |
| **Night** | Restructure | Single-night deep dive (was `NightDetail`) |
| **History** | New (from `UserDetail`) | 30-day per-person view |
| **Compare** | Redesign | Fix scale problem, add correlation |
| **Sync** | No changes | Utility page, keep as-is |

**Remove from nav:** `MetricsGuide` — dissolve into contextual `?` popovers (see [Metrics Guide section](#metrics-guide--dissolve-into-contextual-help)).  
**Remove entirely:** `Counter`, `Weather`, `UserCard` (scaffold leftovers).

---

## Home Page — Morning Briefing

> Most important redesign. Primary entry point every morning.

### Zone 1 — Last night header strip

Two-column layout, Boo left · Maa right. Compact — only the 5 metrics that matter for the morning verdict:

| Metric | Boo | Maa |
|---|---|---|
| RRS (verdict score) | colored prominently | colored prominently |
| HRV avg | value | value |
| HR above 75% | value | value |
| Restorative sleep | value | value |
| Temp deviation | value | value |

- Color signal (green / amber / red) based on **each person's own recent baseline**, not absolute thresholds.
- No Oura readiness or sleep score shown here.
- Each column ends with `→ Boo's night` / `→ Maa's night` link → `Night` page.

### Zone 2 — Two comparison charts

**Chart A: HRV trend — 30 days, both users, dual Y-axis**
- Boo on left axis (approx. 20–80 ms range)
- Maa on right axis (approx. 5–35 ms range)
- Shared X axis (date). Both lines fully visible — neither one flatlines.
- Hover: shows both users' values for that date.

**Chart B: HR above 75% + Resp rate — 30 days, both users**
- Four lines: HR above 75% Boo, HR above 75% Maa, Resp rate Boo, Resp rate Maa.
- These are leading signals — they move before scores do (illness, stress, cycle phase).
- No date-range controls here; controls live in History/Compare.

### Zone 3 — Pattern callouts *(defer to later milestone)*

Space reserved in the layout now. 3–4 auto-generated text observations, e.g.:
- *"Maa's resp rate has risen 0.8 brpm over the last 5 days"*
- *"Boo's HRV has been improving for 6 consecutive nights"*
- *"Both slept worse on Apr 19–20 — possible shared environment event"*

Mark with a `<!-- TODO: pattern callouts -->` placeholder in the Razor component.

---

## Night Page — Single Night Deep Dive

> Replaces `NightDetail.razor`. Restructured top-to-bottom as a story, not a flat list.

### Zone 1 — Verdict bar (full width, slim)
- Background color: green / amber / red based on RRS.
- Shows: date, RRS score, one generated summary sentence.
- Examples: *"Strong night — HRV improving all night, HR fully settled within 30 min."*  
  or: *"Elevated HR for 60% of night despite good sleep duration — check temp trend."*

### Zone 2 — Charts (move before metric breakdown)
- Sleep stage bar.
- HRV intra-night chart.
- HR intra-night chart.
- Keep existing chart implementations; just reorder them to the top.

### Zone 3 — Custom metrics (3 collapsible sections, open by default)

**Cardiovascular**
- HR above 75%, HR above 80%
- HR settling time
- HR curve shape
- Early/late HR avg

**HRV**
- Distribution buckets (% of night below 12 ms, 12–20 ms, above 20 ms)
- HRV peak + approximate timing
- Night direction (early half avg vs late half avg)
- Early/late HRV avg

**Sleep quality**
- Restorative sleep (Deep + REM combined)
- Awake %
- Efficiency

RRS score sits **above** all three sections as the synthesized number.

### Zone 4 — Oura's calculated scores *(collapsed by default)*
- Score contributors grid.
- Oura averages.
- Labeled explicitly: *"Oura's calculated scores"* to distinguish from custom metrics.

### Zone 5 — Daytime context *(collapsed by default)*
- Steps, SpO2, resilience, etc.

### Zone 6 — Raw data *(collapsed by default)*
- Keep existing raw data display exactly as-is.

### Navigation
- Prev/next night arrows (keep existing).
- Add breadcrumb: `Home → Boo → Apr 23`.

---

## History Page — Per-Person 30-Day View

> Replaces `UserDetail.razor`. Two changes to the existing layout.

### Chart section (above table) — consolidate from 4 to 2 charts

**Chart 1: HRV (line) + Resp rate (line, secondary axis)**
- Slow-moving baseline signals; plotting together shows whether they co-move.

**Chart 2: HR above 75% (bar) + Restorative sleep (line)**
- Bar chart makes "how many bad nights in a row" pattern immediately visible.

Oura sleep score and readiness: move to a **toggleable overlay, off by default**.

### Table — add heatmap coloring
- Each cell colored relative to that column's own 30-day range.
- Green = best end, red = worst end, personal-relative scale.
- Preserves all existing numbers; coloring is additive only.

### Date range toggle
- Add `7 / 14 / 30 / 90` day toggle.
- Default: 30 days.

---

## Compare Page — Fix the Scale Problem

### Core fix: dual Y-axis for HRV
Non-negotiable. Current single-axis view is misleading — Maa's line flatlines against Boo's range.

### Section 1 — Synchronized charts (shared X axis)
| Chart | Type | Notes |
|---|---|---|
| HRV | Line, dual Y-axis | Each person's own scale |
| HR above 75% | Clustered bar | Bar reads zero/spike pattern better than jagged line |
| Resp rate | Line, shared axis | Values are close enough |
| Temp deviation | Line, shared axis | Both users |

### Section 2 — Correlation highlight *(new)*
Small table: for each night both users have data, show ✓/✗ for whether they were in the same zone (both good / both bad / opposite). Makes shared environment effects visible at a glance.

### Section 3 — Per-night comparison table
Keep existing table. Add **heatmap coloring per column per user** (separate color scale per person).

---

## Metrics Guide — Dissolve into Contextual Help

Replace the dedicated `MetricsGuide` nav entry with `?` icons on every metric label throughout the app.

- Each `?` opens a small popover/modal with the metric's definition.
- Existing content in `MetricsGuide.razor` maps 1:1 — one card → one popover.
- If a full reference page is wanted, link it from the footer or an `About` page, not primary nav.

**Migration:** `MetricsGuide.razor` content → shared `MetricHelpContent` dictionary or partial components, consumed by the `?` popovers.

---

## Implementation Order (suggested)

Work can be broken into independent slices, roughly in priority order:

0. **RRS formula** — prerequisite for steps 5 and Night verdict bar. Agree on formula, implement as a service method, write unit tests with known-good nights.
1. **Nav cleanup** — remove `Metrics Guide` from nav; remove scaffold pages (`Counter`, `Weather`, `UserCard`).
2. **Night page restructure** — zone ordering + collapsible sections. No new data needed.
3. **History page** — heatmap coloring on table + 2 consolidated charts + date toggle.
4. **Compare page** — dual Y-axis HRV + clustered bar HR + correlation table.
5. **Home page Zone 1** — last night header strip, both users, 5 metrics + RRS color coding.
6. **Home page Zone 2** — dual-axis HRV chart + 4-line combo chart.
7. **`?` popovers** — wire existing MetricsGuide content to inline popovers across all pages. Use Bootstrap 5 `Popover` (already a dependency; `focus` trigger works on mobile tap without JS complexity).
8. **Home page Zone 3** — pattern callout engine (most logic-heavy, defer last).

Each slice is independently deployable and testable.

---

## Decisions Made

| # | Decision | Resolution |
|---|---|---|
| RRS score vs. color coding | Keep **absolute thresholds** for the RRS *number* (HR 75 bpm / HRV 20 ms ceiling / restorative 150 min / resp 14–18 brpm). Use a **14-day personal rolling baseline** for the *color band* only (green/amber/red). Green = today ≥ 95% of your 14-day avg RRS; amber = 80–95%; red = below 80%. The score stays comparable across nights; only the display signal is personal. | Confirmed |
| Popover component for `?` help | Use Bootstrap 5 `Popover` (already a dependency). The `focus` trigger works on mobile tap without additional JS. | Confirmed |
| 90-day History date toggle | Implement the toggle UI as part of step 3. Verify DB row count before enabling the 90-day option at runtime — if fewer than 60 rows exist, disable it with a tooltip. No separate pre-check step needed. | Confirmed |
