# ADR-003: Process model and IPC

- Status: Accepted (process model fixed by the brief, §3.2). Serializer choice **proposed**: MessagePack-CSharp.
- Date: 2026-11-02

## Context

Sketch code is native and untrusted in the sense that it can crash, hang, or corrupt memory. The UI must survive all three, stop runaway firmware, and reload a rebuilt DLL cleanly.

## Decision

Three executables:

- `ArduSim.App` — WPF UI.
- `ArduSim.Runner` — console process, **one per run**: loads `firmware.dll`, runs the simulation core on its simulation thread, serves IPC. Also the headless CLI.
- `firmware.dll` — built per sketch.

A crash in the sketch kills only the Runner; Stop on a busy loop kills the process; a rebuild starts a fresh process, so there is no `FreeLibrary`.

### Transport

- One named pipe per run, duplex, created by the App with a random name passed as `--pipe <name>`. Only the current user can open it (default pipe ACL).
- Framing: 4-byte little-endian payload length, then the payload. Frames over 16 MiB are a protocol error.
- First frame each way: `Hello { ProtocolVersion, ProductVersion }`. A major mismatch closes the pipe with a visible error.
- Contracts live in `ArduSim.Ipc` as records, versioned by `IpcProtocol.Version`.

### Messages

- UI → Runner commands: `Load`, `Start`, `Pause`, `Resume`, `Stop`, `Step(kind: Loop | Micros(n))`, `SetSpeed(factor | Max)`.
- UI → Runner input events: `ButtonDown/Up`, `PotValue`, `SerialInput`, `PinOverride`. Applied at the next scheduling point and stamped with that virtual time.
- Runner → UI: `Snapshot` at ≤ 60 Hz as deltas (pin levels and PWM duty, changed display framebuffers, component states, virtual time, pacing ratio) and events (`SerialOutput`, `Log`, `Warning` with component id, `CrashReport`, `BuildStats`, `FirmwareBusy`).

### Serializer: MessagePack-CSharp (proposed)

| | MessagePack-CSharp 3.1 | MemoryPack 1.21 |
| --- | --- | --- |
| License | MIT | MIT |
| Format | Self-describing MessagePack; any msgpack tool can decode a dump | Raw memory layout; needs our types to decode |
| Evolution | Int keys; unknown/missing members tolerated | Strict layout by default; version tolerance opt-in per type |
| Records, unions | `[MessagePackObject]` records, `[Union]` for the envelope | `[MemoryPackable]` records, `[MemoryPackUnion]` |
| AOT / source generator | yes (v3) | yes |
| Speed | fast | fastest |
| Maintenance | active (3.1.12 stable, 4.0 previews in Oct 2026) | last release Feb 2025 |

Our traffic is ≤ 60 snapshots/s of a few KiB plus events, so either is far below 1 % CPU. MessagePack wins on inspectability (dump a session and read it) and on tolerant evolution; MemoryPack's speed advantage buys nothing here, and its release cadence has slowed.

### ISimulationSession

The UI talks to the simulation only through `ISimulationSession` (in `ArduSim.Ipc`). Two implementations:

- `PipeSimulationSession` (default) — starts `ArduSim.Runner.exe`, speaks the protocol above.
- In-process session (`--inproc` dev switch) — hosts the Runner's simulation host in the App process so the whole stack can be stepped in one debugger. It lives in the Runner assembly, which the App references (this also places `ArduSim.Runner.exe` next to the App). A sketch crash in this mode takes the App down; that is accepted for a dev switch.

### Crash diagnostics

- The App creates a small named shared-memory block per run and passes its name to the Runner. It holds `SimCrashInfo` (ADR-001) and a ring of the last 20 host calls (call id, arguments, virtual time) that the Runner writes on every call — a few stores, no allocation.
- The shim installs a vectored exception handler in `sim_fw_init` that, for fatal exceptions (access violation, divide by zero, stack overflow, illegal instruction), writes code, address and the DLL base into `SimCrashInfo`, then lets the process die.
- After the Runner exits, the App reads the block and the Runner's stderr, symbolizes the address against `firmware.pdb` with `dbghelp`, and shows: exception type, `file:line` in the sketch, the last 20 host calls, and Rerun. A crash inside a static constructor surfaces as "DLL initialization failed" (Windows error 1114) from `LoadLibrary`; the handler's record still applies.

### Runaway detection

A watchdog thread in the Runner samples the host-call counter every 250 ms. If it has not moved for 2 s of wall time while the simulation is running (not paused, not inside a host-side `delay`), the Runner sends `FirmwareBusy`; the UI offers Stop, which kills the process. Native code is not preempted.

## Consequences

- Process start is expected to cost around 100 ms per run (to be measured in M2); acceptable next to the build.
- IPC lost without a crash report → the UI shows "runner exited" with its exit code and stderr.
