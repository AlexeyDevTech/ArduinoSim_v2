# ArduSim architecture

ArduSim compiles an Arduino sketch with a host C++ compiler against our own Arduino core shim into `firmware.dll`, runs it in a separate Runner process under a deterministic virtual clock, and simulates the wired parts at protocol level. The decisions behind this are in `docs/adr/`; this document shows how the pieces fit.

## Processes

```
 ArduSim.App (WPF)                         ArduSim.Runner (one process per run)
 ┌────────────────────────────┐            ┌───────────────────────────────────────────┐
 │ Views ── ViewModels        │            │ IPC thread ── command queue               │
 │            │               │  named     │                    │                      │
 │   ISimulationSession ──────┼── pipe ────┼──> Simulation thread                      │
 │            │               │  (msgpack) │      SimulationHost ── Core engine        │
 │   ArduSim.Build            │            │           │   ▲                           │
 │   (toolchain, preprocess,  │            │  calls    │   │ SimHostApi (C ABI,        │
 │    compile, cache)         │            │  exports  ▼   │  function pointers)       │
 │            │               │            │      firmware.dll (sketch + shim)         │
 │            ▼               │  shared    │                                           │
 │   firmware.dll + .pdb ─────┼── memory ──┼── crash info + last 20 host calls          │
 └────────────────────────────┘            │ Watchdog thread (runaway detection)       │
                                           └───────────────────────────────────────────┘
```

- The App builds the firmware (so build errors appear before any process starts), then starts a Runner and drives it through `ISimulationSession`.
- `--inproc` hosts the same `SimulationHost` inside the App for single-debugger sessions (ADR-003).
- The headless CLI is the Runner on its own: `ArduSim.Runner --project <path> --run-for <ms> --dump-display out.png --dump-serial out.txt` builds with `ArduSim.Build`, runs, writes the artifacts and exits.

## Projects and dependency rules

| Project | Contains | May reference |
| --- | --- | --- |
| `ArduSim.Core` | virtual clock, scheduler, circuit (nets), parts (definitions and behaviors), MCU model, project file, display controllers (M4+) | nothing of ours |
| `ArduSim.Boards` | board profiles, pin maps, timing tables, compiler macros | Core |
| `ArduSim.Build` | toolchain manager, `.ino` preprocessor, library resolver, compiler driver, object cache, diagnostics parser, PDB symbolization | nothing of ours |
| `ArduSim.Ipc` | message records, framing, `ISimulationSession`, `PipeSimulationSession`, crash block | nothing of ours |
| `ArduSim.Runner` | firmware loader, `SimHostApi` callbacks, `SimulationHost` (pause/step, watchdog), `SessionServer`, pipe server, in-process session, headless CLI | Core, Boards, Build, Ipc |
| `ArduSim.App` | WPF views, view models, rendering, editor, canvas | Core (scene data, part definitions), Boards, Build, Ipc, Runner (in-process session only) |

No UI assembly is referenced below the App, and Core never sees IPC or native interop. `tests/ArduSim.Core.Tests/ProjectBoundaryTests.cs` enforces both.

## Build pipeline (`ArduSim.Build`)

1. **Toolchain**: resolve the pinned llvm-mingw (ADR-002) under `%LOCALAPPDATA%\ArduSim\toolchain\<version>`; download with progress and SHA-256 check if missing; run a smoke compile once after install. A user-configured clang/MSVC/MinGW path replaces it.
2. **Sketch**: concatenate the folder's `.ino` files (main first, the rest alphabetically), insert `#include <Arduino.h>`, generate prototypes for functions defined in the `.ino` files, emit `#line` directives mapping back to the original files. `.h/.cpp/.c` next to the sketch compile as-is.
3. **Libraries**: scan `#include` directives transitively; search sketch folder → project library paths → `Documents\Arduino\libraries` → bundled `libraries/`; `library.properties` with `src/` or flat layout and `utility/`; `architectures` ignored. Per-library fixes from `libraries/compat/` are layered on top, never patched into vendored sources.
4. **Compile**: one process per translation unit, in parallel, flags from ADR-002 plus the board's macros. Objects cached in `%LOCALAPPDATA%\ArduSim\cache` keyed by a hash of source content, all included headers, flags, toolchain version and shim version.
5. **Link** `firmware.dll` + `firmware.pdb` into the project's build folder.
6. **Diagnostics**: raw compiler output is kept; errors and warnings are parsed into (file, line, column, severity, message) for the Build panel and editor markers.

## Firmware shim (`firmware/shim`)

```
firmware/shim/
  include/sim_abi.h        the C ABI (ADR-001)
  include/Arduino.h ...    Arduino API headers, String, Print/Stream, HardwareSerial, Wire, SPI, EEPROM
  include/avr/*.h, util/*  compatibility headers (pgmspace, io proxies, interrupt, delay, sleep, wdt)
  src/<module>.cpp         one .cpp per module: wiring (pins, time, analog, interrupts, random), WString, Print, Stream, HardwareSerial, registers, stdlib_ext
  src/entry.cpp            sim_fw_* exports (the vectored exception handler joins it in M2)
  src/host_bootstrap.cpp   finds the host table for static constructors (ARDUSIM_HOST_API)
  test/                    standalone harness: compiles the shim against a fake host and checks behavior
```

- Sketch-visible headers never include `<windows.h>`; Win32 calls live in `host_bootstrap.cpp` only.
- Each host call is a single indirect call through the `SimHostApi` table (~5 ns measured).
- Register proxies (`PORTB`, `DDRD`, `TCCR1A`, …) are C++ objects with overloaded assignment and bit operators, so `PORTB |= _BV(5)` works and unsimulated registers warn once.

## Runner internals

- **Simulation thread** owns everything mutable: Core engine, firmware calls, the clock. Host callbacks are `[UnmanagedCallersOnly]` static methods that forward to `SimulationHost`, charge the timing-table cost, record the call in the crash ring, and run due events.
- **IPC thread** reads commands into a queue; the simulation thread drains it at scheduling points (and while paused it blocks on the queue).
- **Snapshot publisher**: at scheduling points, at most every 1/60 s of wall time, the simulation thread collects deltas (changed pins, changed framebuffers by version, component states, serial output since last) and hands them to the IPC thread to send.
- **Watchdog thread**: see ADR-003.

## Core engine (`ArduSim.Core`)

- `Timing.SimulationClock` (ns, with the event queue ordered by time + sequence) and `Timing.Pacer`.
- `Simulation.SimulationEngine`: charges call costs, slices `delay()`, waits from event to event, and invokes the host's scheduling point.
- `Simulation.Mcu`: the MCU as the firmware sees it — one method per host call, each charging its timing-table cost. Pins (mode, PORT bit, PWM, tone), ADC, `UartPort`s, `EepromMemory`, `InterruptController`. The Runner's callbacks are thin forwards to it, so all behaviour is testable without native code.
- `Circuits.Circuit`: pins joined into nets by wires and closed switches; `Driver`s on pins (strong: outputs, rails; weak: pull-ups, pot wipers), resistor coupling, Thevenin-style resolution of the resistor-coupled group around a change, pin watchers, short/floating/undefined warnings (ADR-006).
- `Components`: `ComponentDefinition` (pins with positions, properties), `SimComponent` behaviors registered by `[SimComponent]`, `ComponentRegistry`, `ComponentGeometry` (rotation/flip), parts in `Components.Parts`; `Simulation.BoardSimulation` builds circuit + parts + MCU from a scene.
- `Buses.I2cBus`: the board's master bus over its SDA/SCL nets; devices (`II2cDevice`) register while they attach and answer when their address matches and their pins sit on those nets. The MCU charges 9 bit times per byte (START and STOP one each) at the Wire clock. `Buses.SpiBus`: hardware SPI over the board's SCK/MOSI nets; a byte goes to the one `ISpiDevice` whose CS net is LOW (none: reads 0xFF; several: a conflict warning), 8 bit times at the SPI clock. Bit-banged SPI needs no bus: a device watches the rising edges on its own SCK pin.
- `Displays`: controllers as pure state machines, unit-tested with recorded streams: `Hd44780` (instruction set, DDRAM/CGRAM, 4/8-bit, busy flag), `Hd44780Bus` (latches on the falling edge of E, says what the controller drives during reads), `Hd44780Font` (A00 ROM), `Pcf8574` and `LcdBackpackMap`; `OledController` (SSD1306 and SH1106: command tables, addressing modes, remap/scan, COM pin mapping, multiplex, offset, start line, contrast, invert, scrolling driven by the frame clock, `Render(OledPanel)` maps GDDRAM to the glass of a module); `DisplayFrame` is the immutable picture a part hands to the view, `DisplayImage` turns it into grayscale pixels (the Runner's `--dump-display` PNG).
- `Components.Parts.Lcd`: the LCD module on the nets (parallel) or as an I2C device (backpack), with its power, pull-up, contrast and backlight rules. `Components.Parts.Oled`: an SSD1306/SH1106 module as an I2C device (control byte, Co and D/C bits) or on 4-wire SPI (D/C and RES pins, CS gating, hardware or bit-banged), with its power check and panel sizes. A part's size and pins may depend on its properties (`ComponentDefinition.Layout`); palette presets are `ComponentPreset`s.
- `BoardRuntime`: applies a board profile to the engine — maps Arduino pin numbers to board pins and nets, models pin modes, PWM state, ADC, UART ring buffers, EEPROM image.
- `Editing`: the scene as the editor changes it, UI-free and unit-tested. `SceneModel` (mutable parts and wires, to and from `SceneDocument`), `SceneEditor` (every edit: place with ids, move, rotate about the selection keeping pins on the 0.1" grid, flip, delete, wires with waypoints, properties, rename, copy/paste as JSON; snapshot undo/redo; pin positions, bounds, orthogonal routes, nets and their kind for coloring).
- `Diagnostics.SimulationLog`: messages are interpolated strings; each entry keeps the English text, a key (`code` or `code.variant`) and the formatted arguments, so the App shows its own translation.
- `Projects`: `ProjectFile` (load, atomic save that keeps unknown members) and `ProjectTemplate` (a new project: the board and an empty sketch).

## UI (`ArduSim.App`)

- Layout, visual language and interaction rules: `docs/UI.md` (approved 2026-11-02). One window with custom chrome: a 48 px title bar that is also the toolbar, palette, center tabs (the schematic first, then one AvalonEdit tab per sketch file), properties, bottom tabs, status bar; a start view when no project is open.
- `MainViewModel` owns the project (open, new, save, recent, dirty state, FileSystemWatcher on the sketch folder), builds and runs; `SceneViewModel` wraps a `SceneEditor` with view state (selection, hover, the wire being drawn, live part state); `PaletteViewModel`, `PropertiesViewModel`, `CodeDocumentViewModel`.
- `Views.SchematicCanvas` draws the scene in world units (0.01 inch, 2 px at 100 %) under one transform and turns gestures into view-model calls; hit testing uses view-model geometry, not WPF hit testing. Part art is a XAML template per type in `Themes/Parts.xaml`.
- Display rendering converts frames from snapshots into `WriteableBitmap`s shown with nearest-neighbor scaling: `Views.LcdScreen` draws the HD44780 glass dot by dot and `Views.OledScreen` the OLED pixels (with a gap between them when zoomed in, a glow, the panel color and contrast) at the zoom's resolution (the canvas reports device pixels per unit through `SceneViewModel.SetPixelsPerUnit`); identical frames are skipped.
- `PlotterViewModel` parses Serial lines for the Serial Plotter tab, `Views.PlotterView` draws it. `Services.Theme` swaps the token dictionary (`Themes/Dark.xaml` or `Themes/Light.xaml`, same keys); views that draw with code-side colors (canvas grid, code editor, plotter, wire palette) follow its `Changed` event. `Views.ExampleThumbnails` renders each example's scene with an offscreen `SchematicCanvas` for the start view, at idle priority.
- Dialogs: unsaved changes, Параметры (theme), and the "О точности симуляции" side sheet.
- All strings come from `Resources/Strings.resx` (Russian neutral); simulation log entries are translated by key (`Log_<key>`), falling back to Core's English text.

## Storage

| What | Where |
| --- | --- |
| Toolchain | `%LOCALAPPDATA%\ArduSim\toolchain\<version>` |
| Object cache | `%LOCALAPPDATA%\ArduSim\cache` |
| Logs | `%LOCALAPPDATA%\ArduSim\logs` |
| Recent projects, panel layout, theme | `%LOCALAPPDATA%\ArduSim\ui-state.json` |
| Project | its folder: `project.simproj.json`, `sketch/`, `eeprom.bin` (ADR-008) |

## Testing

- `ArduSim.Core.Tests`: controllers against recorded command streams and golden framebuffers; bus routing (NACK, CS gating, bit-banged SPI); net resolution; scheduler ordering; serial TX timing; project boundaries.
- `ArduSim.Build.Tests`: preprocessor (prototypes, `#line`), library resolution order, error parsing, cache invalidation.
- `ArduSim.Integration.Tests`: start the real Runner process; from M1 build and run every `examples/` project headlessly and assert on pins, serial and framebuffers (OLED pixels from `--dump-display`). A fixture fetches the toolchain once into the normal toolchain folder. `PerformanceTests` (host calls per second) run alone, after the rest.
- `ArduSim.App.Tests`: view-model logic that needs no window (Serial Plotter parsing, in-place wire updates, user state). The window itself is checked by rendering it offscreen in scenario scripts, not in `dotnet test`.
- `tools/toolchain-eval`: reproducible ADR-002 measurements, not part of `dotnet test`.
