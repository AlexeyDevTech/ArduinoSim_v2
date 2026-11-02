# ADR-004: UI stack

- Status: Accepted (stack fixed by the brief, §3.7; custom split layout confirmed by the owner on 2026-11-02).
- Date: 2026-11-02

## Decision

- **WPF on .NET 10** (LTS; SDK 10.0.401 is installed, `global.json` pins 10.0.100 with `latestFeature` roll-forward). All non-UI projects target `net10.0`; only `ArduSim.App` targets `net10.0-windows`.
- **MVVM** with CommunityToolkit.Mvvm 8.4 (source-generated observable properties and commands). Code-behind holds view glue only.
- **DI and lifetime** with Microsoft.Extensions.Hosting 10; logging through `Microsoft.Extensions.Logging` with Serilog (file sink in `%LOCALAPPDATA%\ArduSim\logs`, plus an in-app log panel sink from M2).
- **Editor**: AvalonEdit 6.3 (MIT) with a C/C++ highlighting definition matched to our theme.
- **Icons**: Fluent UI System Icons (MIT), Regular style, embedded as vector path geometry in a resource dictionary — one set throughout.
- **Typography**: Segoe UI Variable, falling back to Segoe UI (both in-box; no font bundling).
- **Localization**: every string in `Resources/Strings.resx`; the neutral language is Russian (`NeutralLanguage=ru`), so English is added later as `Strings.en.resx`. XAML uses `{l:Loc Key}`; a missing key renders as `!Key!` so it cannot go unnoticed.
- **Theme**: our own resource dictionaries of tokens (colors, spacing on an 8 px grid, radii, type ramp) and control templates; dark by default, light variant from M5. No stock control look survives into M3.
- **Boundary**: `ArduSim.Core`, `Boards`, `Build` and `Ipc` reference no UI assembly — enforced by `ProjectBoundaryTests` — so an Avalonia port stays possible.

### Docking: custom split layout

| | AvalonDock (Dirkster) 5.0 | Custom split layout |
| --- | --- | --- |
| License | MS-PL | ours |
| Look | ships VS2013/Metro themes; our look means re-templating its whole control set | built from our tokens from the start |
| Features | floating windows, drag-to-dock, layout serialization | collapsible, resizable side and bottom panels, tab strips, persisted sizes |
| Cost | integration plus re-theming; airspace and focus quirks with floating panes | a few hundred lines of XAML |

The brief's layout is fixed (palette left, canvas/code tabs center, properties right, tool tabs bottom), so drag-to-dock buys little and costs a theme fight. It is also unclear whether the brief's "Microsoft" license category covers MS-PL; the custom layout avoids the question. It can grow floating panels later if they are ever wanted.

## Consequences

- No rearranging panels by drag in the MVP; panel visibility and sizes are persisted per user.
- `docs/UI.md` (wireframe, visual language, interaction rules) is written and approved before M3.
