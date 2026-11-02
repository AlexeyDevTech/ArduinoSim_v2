# ADR-008: Project format

- Status: Accepted (shape fixed by the brief, §3.8; schema v1 confirmed by the owner on 2026-11-02).
- Date: 2026-11-02

## Decision

A project is a folder:

```
MyProject/
  project.simproj.json   scene, board, settings (schema-versioned)
  sketch/                .ino + companion .h/.cpp/.c (absent when sketchPath points elsewhere)
  eeprom.bin             EEPROM contents, raw bytes, board EEPROM size (Nano: 1024), erased = 0xFF
```

### `project.simproj.json`, schema v1

```json
{
  "schemaVersion": 1,
  "board": "arduino-nano-v3",
  "sketchPath": "sketch",
  "scene": {
    "components": [
      { "id": "board", "type": "arduino-nano-v3", "x": 0, "y": 0, "rotation": 0, "flip": false },
      { "id": "led1", "type": "led", "x": 400, "y": 120, "rotation": 90, "flip": false,
        "properties": { "color": "red" } },
      { "id": "r1", "type": "resistor", "x": 400, "y": 200, "rotation": 0, "flip": false,
        "properties": { "resistance": 220 } }
    ],
    "wires": [
      { "id": "w1",
        "from": { "component": "board", "pin": "D13" },
        "to": { "component": "r1", "pin": "1" },
        "waypoints": [[300, 40], [300, 200]],
        "color": "auto" }
    ]
  },
  "settings": {
    "speed": 1.0,
    "serial": { "baud": 9600, "lineEnding": "LF" },
    "libraryPaths": ["libs"]
  }
}
```

- `sketchPath`: relative to the project folder (default `sketch`) or absolute for an external sketch folder. The main `.ino` is the one named after the folder, as in Arduino.
- Coordinates are canvas units of 0.01 inch; the grid and pin pitch are 10 units (0.1 inch). Rotation is 0/90/180/270; `flip` mirrors horizontally before rotation.
- `board` selects the board profile (compiler macros, timing table); the scene holds exactly one component of that type, which is where the board sits on the canvas.
- Component `id`s are stable across saves (wires and log warnings refer to them). `properties` holds only values that differ from the definition's defaults.
- Wires connect pin to pin; several wires on one pin join their nets. `waypoints` are the manual bends; orthogonal segments between them are derived.
- Unknown properties are kept on load and written back (forward compatibility within a major version); an unknown component type is shown as a placeholder and reported, never dropped.

### Versioning

The loader migrates older `schemaVersion`s in memory, step by step; saving writes the current version. A file from a newer schema is refused with a message naming both versions.

### Around the format

- The scene autosaves on every Run (brief).
- Recent projects: `%LOCALAPPDATA%\ArduSim\recent.json`.
- Bundled examples open as copies under `Documents\ArduSim\Projects\<Name>` (numbered if it exists), so the bundled originals stay pristine.

## Consequences

- Plain JSON diff-friendly in git; the sketch folder stays the source of truth for code (a FileSystemWatcher picks up external edits).
