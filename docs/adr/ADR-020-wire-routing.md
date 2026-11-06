# ADR-020: Wires are laid out by the editor

- Status: Accepted (the owner's request of 2026-11-06).
- Date: 2026-11-06

## Context

A wire was its pins and the corners stored in the project file, drawn as L-shaped legs between them. Move a part and its pin went with it, while the corners stayed where they were: the wire kinked across the schematic, away from the pin it belonged to. A new wire was a single L, through parts and along other wires alike. The owner's project showed all of it: "the wires are not attached to the board, they lie chaotically, also when a board or part is moved".

## Decision

### Who owns a route

A wire with no corners in the file belongs to the editor (`WireModel.IsAuto`). Its corners exist only in memory, are worked out on opening, and are not saved: the file format does not change, and a schematic with no hand-made routes is just parts and connections.

A wire with corners in the file belongs to the user, and keeps them until they say otherwise, or until one of its ends moves on its own. Editing a route by hand (a segment, a corner) makes a wire the user's. Corners a person sets are on the 0.1" grid, so corners off the grid come from another tool and are laid out on opening. So is the one bend the old editor chose by itself, which nobody asked for.

### What moves what

- Both ends move together: the wire's shape goes along, whoever owns it.
- One end moves: the wire is laid out anew, because no corner of its old route means anything any more. Corners set by hand are lost then, and undo brings them back.
- A part comes to lie over a wire of the editor's: that wire moves aside. A wire of the user's stays where the user put it.
- Rotating or flipping a part, or changing its size or pins, lays out what ends on it.

**Разложить** (canvas button and menu) lays out every wire, hand-made routes too, in one undo step. Сбросить маршрут does it for one wire.

### The router

`Editing.WireRouter` searches over (cell, direction) with A*, on a grid of half a tenth of an inch.

- A wire leaves a pin straight out through the part's nearest edge, to a grid line clear of it, and arrives the same way. This is what ties it to the pin visually.
- Prices, in the same units: length, a bend, a step on a halfway line, a crossing, a corner touching another wire, running along another net's wire, and crossing a part's body. Nothing is forbidden outright, so a route always exists, and it is the least bad one when a pin is boxed in.
- Wires keep to the 0.1" grid and use the lines halfway between only where it gets crowded. Pins of a header are a tenth of an inch apart, so a bundle between two rows of pins would not fit otherwise.
- Wires of one net may share a track: that reads as the connection it is.
- A whole layout goes shortest first, then takes again each wire that crosses or overlaps something, up to three times, keeping a new route only if it is better.

During a drag only the wires that must change are routed, with a search that leans towards the goal (a weighted heuristic) instead of the best route, so that a drag stays usable: on the dev machine in a Debug build, 15 ms a grid step dragging a board with twelve wires, and 45 ms dragging a keypad with eight through a crowded schematic. Undo steps keep the routes, so that undo shows what was on screen rather than a new layout.

## Consequences

- A layout depends on history within a session and is worked out from scratch on opening, so a route can differ a little after reopening. That is what corners set by hand are for.
- Crossings are allowed, since a bundle wired in reverse order cannot avoid them. The router does not move parts to make room.
- Moving one end of a hand-made route loses it. The alternative is what the owner complained about.
- Project files from before this keep their on-grid corners until Разложить or a move. Bundled examples were cleared of theirs.
