# ADR-017: Nodal DC solver

- Status: Accepted. Narrows "No SPICE, no currents" in ADR-006 and brief §4 on the owner's decision of 2026-11-05 (milestone M12).
- Date: 2026-11-06

## Context

ADR-006 resolves nets by rules: strong drivers win, weak sources combine like a Thevenin equivalent over the shortest resistor paths, an LED lights when its nets are far enough apart. That answers "HIGH or LOW", but not the questions a beginner has at the bench: why is this LED dim, how much current does this pin give, what does a loaded divider really read. The owner chose a DC calculation: voltages and currents on every change of the circuit, an LED with a voltage drop and brightness from its current, pin overload above 40 mA, an LED without a resistor, currents on the schematic; no capacitors or transients; virtual time and sketch speed untouched.

## Decision

### What is solved

Modified nodal analysis of what is connected right now, in `Core/Circuits`:

- **Known nets** hold an ideal source (a rail, a part's `Strong` output with no resistance). Their voltage is given, and they split the circuit: a solve never crosses one.
- **Unknown nets** are solved together when they are coupled, as one **island**: a matrix of conductances, with a tiny leak (1 pS) to ground on every net so that nothing is singular. Only the island around a change is solved. A net with no resistor or LED on it, and nothing toggling, is the usual case and the hot one: it has a closed-form path without the matrix.
- **Sources behind a resistance** are Thevenin sources on their net: pull-ups, and now MCU outputs, which get the board's output resistance (`BoardProfile.PinOutputResistance`: 25 Ω on 5 V AVR, 40 Ω on 3.3 V AVR, 20 Ω on ESP32, 40 Ω on ESP8266; from the datasheets' VOH/VOL against current).
- **Resistors** couple nets, and can change at run time (`CircuitResistor.Set`): potentiometers and joysticks are now two real resistors instead of a watcher that reprograms a driver, so a load on the wiper pulls it down as it does on the bench.
- **LEDs** are `CircuitDiode`s: piecewise linear, off below a knee (1 nS), a series resistance above it. It is continuous and monotonic, so a network has exactly one solution. Under 0.1 µA a diode counts as not conducting: a few nanoamps at the knee are the off state's leak, and they should neither light it nor connect the net behind it. The state of each diode is found by solving, flipping those that disagree with the result, and solving again; after a few rounds only the first one is flipped at a time (a least-index rule, which terminates on such networks), so it cannot cycle.

### What stays a rule

- **Condition**: a net with a strong driver is `Driven` (or `Conflict` when they disagree by more than 0.5 V), otherwise `Pulled` if a source reaches it through resistors and conducting diodes, otherwise `Floating`. A diode that never conducts does not connect anything, so a pin behind an LED that is off still floats. Everything that reads `HasVoltage`, `Voltage` or `Duty` keeps working.
- **PWM and tone stay states**, not waveforms. Each source that toggles is either on or off, and an island is solved once per combination (at most 4 sources in one island, 16 solves; beyond that the others in the board's order count as their average voltage, with a note in the log). Phases are taken as independent, weighted by duty. A net reports its high phase as `Voltage`, the time spent there as `Duty`, and its true `AverageVoltage`, and it takes the tone of the source that moves it most. Readers take `Duty` to mean "high this long, about 0 V otherwise", so a net counts as toggling only if no steady output holds it and its low phase falls below half of its high phase; otherwise it is steady at its average. A HIGH pin whose load goes on and off with PWM at the far end droops a little, and it still reads HIGH.
- **Digital behaviour of parts is unchanged**: protocol-level parts do not load their pins and draw nothing from their supplies. Seven-segment displays stay logic-level (their share of time from the nets).

### LED model

`LedRule`: the color's forward voltage is its drop at the rated 20 mA, with 10 Ω in series, so the knee is 0.2 V below it. Brightness is `(average / peak current) × sqrt(min(peak / 5 mA, 1))`: PWM scales linearly as before; the current gives a perceptual curve that reaches full at 5 mA (a modern LED is already bright there), so 220 Ω and 330 Ω look full, 1 kΩ is visibly dimmer, 10 kΩ and an `INPUT_PULLUP` glow faintly.

### Warnings

- `pin-overload`: a pin's peak current above `BoardProfile.PinCurrentLimit` (40 mA; 12 mA on ESP8266), once per pin per run, unless the net is a short circuit, which keeps its own warning.
- `led-overcurrent`: an LED's peak current above 30 mA, which is what "no series resistor" looks like. The LED does not burn out: the warning says it would.
- `short-circuit` and `floating-input` stay as they are.
- What is reported while the scene is built (an LED across the rails, say, and everything else at that stage: wrong parts, bad wires) is replayed to the session and to headless output, which subscribe only after it.

### Currents on the schematic

`Circuit.Read()` gives every pin's voltage and current and every wire's current. Elements know their own current; a rail's is what the others draw from it; a wire's or a closed switch's comes from walking a spanning tree of its net. A wire that closes a loop has no determined current, since wires have no resistance, and reports none.

`SessionServer` polls this at snapshot rate and sends what changed as the state of a reserved component id, `~circuit` (`CircuitState`), so the IPC protocol does not change and the simulation thread does no work for the view between snapshots. The Runner prints the table with `--dump-circuit <file>`.

In the App: pin and wire tooltips and the Свойства pin rows show voltage and current; LEDs and resistors carry a small readout above them while running, which Параметры can turn off.

## Consequences

- A loaded pin no longer reads exactly its logic level (4.7 V with an LED on it), which is true; thresholds are far enough away that only an overloaded pin changes its reading.
- Solving happens inside host calls and costs no virtual time. Measured in a Release build, on `digitalWrite` in a tight loop, 10 s of virtual time: a bare pin 0.89 s (the old resolution: 1.01 s), a pin with a resistor and an LED, which has to flip the diode on every write, 1.47 s (old: 1.18 s). So the worst usual case costs 25 % more and still runs 7 times faster than real time.
- `SourceResistance` left `NetState`: only the I2C pull-up check wants it, and it costs a solve per net, so `Circuit.SourceResistanceOf` works it out when asked.
- Limits, written down so nobody assumes otherwise: no capacitance or inductance, so no RC delay; no load from modules, servos, relays or buzzers on a supply or a pin; no limit on the rails themselves (a 5 V pin gives whatever is asked); LEDs do not burn; seven-segment displays are not in the model; PWM phases are independent, though real pins on one timer are in phase.
