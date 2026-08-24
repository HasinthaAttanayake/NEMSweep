---
description: "Use when changing NEMSweep.Web Blazor pages, Razor components, navigation, charts, page styling, or consumption of generated JSON artifacts."
applyTo: ["NEMSweep.Web/**/*.razor", "NEMSweep.Web/**/*.razor.css"]
---
# Blazor Web UI

- Keep the site a static Blazor WebAssembly consumer of committed artifacts; do
  not move simulation, parsing, or domain rules into components.
- Deserialize artifacts through `NEMSweep.Contracts`, verify supported schema versions,
  and show explicit loading, invalid-data, and failure states.
- Follow the existing page pattern: colocated scoped CSS, MudBlazor controls and
  charts, semantic sections, and responsive layouts consistent with nearby pages.
- State region, period, resolution, and units visibly where needed to prevent NEM
  data from being misread. Do not imply a single-region result is NEM-wide.
- After UI changes, build `NEMSweep.Web` and inspect the affected route at desktop and
  mobile widths. Check loading, error, empty, and populated states when applicable.