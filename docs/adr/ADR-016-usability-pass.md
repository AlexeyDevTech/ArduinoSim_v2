# ADR-016: The usability pass: the IDE side of the editor, code beside the schematic, a project explorer

- Status: Accepted (the owner's answers of 2026-11-05: the Рядом mode; toolbar folding stages with a 960 × 560 minimum; board tiles and a searchable gallery; new wiring rules, canvas hints and F1, the editor's commands, the palette's new order; a project explorer as a page of the left panel; "IDE-like editing").
- Date: 2026-11-05

## Context

The owner asked for the interface to be as convenient as it can be, with web research behind it. Comparable tools (Wokwi, Tinkercad, SimulIDE, Arduino IDE 2, VS Code, PlatformIO, Saleae Logic, PulseView) and the Windows and WCAG guidelines were read; the interface was audited against them state by state, in 28 offscreen renders and in the code. The audit found one blocking defect: `CanBuild` and `CanRun` read a field that raises no change, so Собрать and Запуск stayed grey by mouse after the first project of a session (the keys worked, which is how it went unnoticed).

## Decision

### Language intelligence comes from clangd

The pinned llvm-mingw toolchain (ADR-002) already ships `clangd.exe` and `clang-format.exe`, so they need no download and no new dependency. `Services.ClangdClient` speaks LSP over stdin and stdout, with no NuGet package: completion, signature help, hover, go to definition and the file's outline. A hand-written table of the Arduino API was the alternative; it would drift from the shim and would know nothing of libraries. clangd reads what the build reads.

- It is configured through `initializationOptions.fallbackFlags` from `FirmwareBuildService.EditorFlags`: the shim, the board's variant and macros, every library's headers, `-include Arduino.h`, `-xc++`. No compile database is written into the project.
- It only reads. Diagnostics shown are always the build's own, because a sketch is preprocessed before it is compiled (forward declarations), so clangd's opinion of a raw `.ino` can differ.
- Without the toolchain, or if clangd fails to start, every call answers nothing and the editor still edits.

### Plain Russian beside clang's messages

`ViewModels.DiagnosticHints` is a rule table from clang's text to a Russian line and, where clang names one, a one-click fix (`;`, "did you mean"). The original stays under it. A message with no rule is shown as it is: nothing is invented.

### The toolbar folds by measuring

The first of four stages whose natural width fits is used, measured rather than taken from a width table, because the project name, the board name and the status vary. Запуск, the board, the clock and the status never leave. Measuring has to invalidate the whole subtree first: `Measure` does not descend into clean ancestors, so a label hidden inside a button would otherwise not count.

### Other interface decisions

- A dropped path, or a folder with `.ino` files and no project, opens in place: only `project.simproj.json` is added (as for PlatformIO, ADR-015). A folder with neither offers to create a project there.
- New projects go to `Documents\ArduSim\Projects\<name>`; «Другая папка…» keeps the old way.
- The example gallery's texts live in `Strings.resx`, and `Services.ExampleCatalog` holds topics and the panel each example reports to. An example copy is recognized by its folder (`<name>` or `<name> N` under the projects folder), so the project file format does not change.
- Deleting a sketch file goes to the Recycle Bin and asks nothing: nothing is lost.
- Pin labels on the canvas follow the zoom between 4.2 and 5.4 canvas units, so they stay readable at the zoom a scene opens with.
- The baud list in the project's properties is gone: it changed nothing the sketch did, as `Serial.begin` sets the rate. The Monitor no longer shows a rate it does not know.

## Consequences

- A clangd process runs while a project is open (about 100 MB). The first completion in a project waits for the headers to be read; until then clangd answers with the file's words.
- Go to definition can open a library or shim header in a tab. It is an ordinary tab; those files should not be edited.
- A file changed on both sides stops Save, Build and Run at its tab (the owner's later choice).
- The audit's first verification stopped partway on a usage limit. A second pass checked all 120 findings against the code and ran the missing audit of the bottom tabs, which gave 76 ranked items. The owner then set a 30-minute limit, so the worst of them went in and the rest are in docs/UI-BACKLOG.md with their fixes; those were read from the code and are not all verified.
- Gestures are still not automated; the view-model calls they make are.
