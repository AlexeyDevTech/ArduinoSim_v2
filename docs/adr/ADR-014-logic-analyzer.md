# ADR-014: Logic analyzer and buzzer audio (M11)

- Status: Accepted (the owner's answers of 2026-11-05: edges plus bus events, live over IPC protocol 2; the Диаграмма tab as signal rows with a time ruler; sound on by default with a toolbar button; a port list in the Serial Monitor).
- Date: 2026-11-05

## Decision

### What is recorded

The simulator hands hardware I2C, SPI and UART bytes over whole: their nets have no edges of their own. A logic analyzer that only watched nets would show nothing for `Wire`, `SPI` or `Serial`. So `Tracing.SignalRecorder` (one per `Mcu`) records two things while it is on:

- `PinSample`: what a board pin's net carries from a time on: LOW, HIGH, floating, a PWM duty, or a tone. PWM and tones are not waveforms in the simulator (ADR-006), so they are bands with the duty or frequency, not edges.
- `BusEvent`: a hardware bus byte with its time, duration, value, ACK or MISO byte, and the pins it ran on.

Off, the recorder costs one test per event. Between two snapshots it holds at most 400 000 samples and 200 000 events; beyond that it drops and says so, and the tab shows "Часть событий потеряна".

### IPC

Protocol version 2. `Snapshot` gains `Trace` (a `TraceBlock` of flat arrays) and `OtherSerial` (output of ports other than the first). `SetRecording` turns recording on and off; `SessionStartInfo.Record` (`--record`) starts it before the first instruction, so `setup()` is on the diagram when the tab is open at Run. It records only while the tab is open with its Запись box on: the traffic and the per-edge work are not worth paying for otherwise.

### Viewing

`Tracing.TraceStore` (App side, Core code, unit-tested) keeps samples per pin and events per pin, bounded by dropping the oldest. `Render` paints pixel columns: inside a bus event the levels come from `BusWaveform`, which draws the byte at the bus's own bit time (I2C START, 8 bits, ACK, STOP; SPI in mode 0; UART frame, LSB first); between bytes SCL stays low inside an I2C transaction, and a UART line rests high. Too many events for the width collapse into a band, jumping by binary search, so a far zoom over a million events costs about `columns x log n`.

`BusDecoder`s label a bus row: `I2cDecoder` (`S 0x3C W A`, `0x40 A`, `P`, NACKs in the error color), `SpiDecoder` (`0xA5`, `0xA5 / 0x3C` with MISO), `UartDecoder` (`'H'`, `0x0A`). Each takes hardware events as they come and decodes real edges on its pins as well, so bit-banged buses (`shiftOut`, software serial) work through the same row: «+ Шина → Другие выводы…» picks the pins. They are fed incrementally; a row added later replays what is recorded.

`Views.TimelineView` draws names, ruler and rows. Wheel zooms about the pointer, a drag pans, a click sets the marker Δ measures from. A click on a wire on the running schematic adds its board pins. The window follows the latest time until the user zooms or pans, and follows again when panned back to the end.

### Headless

`--dump-trace <file>` writes the recording as text, one line per sample or event, for tests.

### Serial ports

Every UART's output reaches the App. A list next to the baud rate, shown on boards with several ports, picks what the Monitor and the Plotter show and where input goes. This also ends the old limit that ESP32 `Serial1` and `Serial2` output was shown nowhere.

### Buzzer audio

`Services.WaveOutTonePlayer` plays square waves through winmm's wave output (no new dependency): three buffers of about 23 ms, opened at the first tone and closed after a second of silence. The tones are the buzzers' frequencies from each snapshot, so the pitch is always the sketch's and durations follow the simulation speed; tones shorter than a snapshot (about 16 ms) can be missed. Silent while paused. The toolbar's speaker button is remembered in `ui-state.json`, on by default.

## Consequences

- Hardware bus waveforms are drawn from the bytes, not sampled: SPI modes other than 0 and clock stretching do not show. Decoded values are exact.
- `analogWrite` on a buzzer stays silent: the simulator has a duty cycle, not a PWM frequency, and only `tone()` sounds.
- The rows are not saved with the project.
