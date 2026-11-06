# ADR-020: Wires are laid out by the editor

- Status: Accepted (the owner's request of 2026-11-06).
- Date: 2026-11-06

## Context

A wire was its pins and the corners stored in the project file, drawn as L-shaped legs between them. Move a part and its pin went with it, while the corners stayed where they were: the wire kinked across the schematic, away from the pin it belonged to. A new wire was a single L, through parts and along other wires alike. The owner's project showed all of it: "the wires are not attached to the board, they lie chaotically, also when a board or part is moved".

## Decision

### Who owns a route

A wire belongs to the editor (`WireModel.IsAuto`) unless someone edited its route by hand (a segment, a corner). The editor's corners exist only in memory, are worked out on opening, and are not saved: a schematic with no hand-made routes is just parts and connections.

A wire edited by hand keeps its corners until the user says otherwise, or until one of its ends moves on its own. In the file they are marked `"byHand": true`, an optional field that older versions ignore. Corners without the mark came from a version before this, or from another tool, and are laid out on opening: a file cannot say otherwise who chose them, and leaving them in place is what the owner found on NanoOS, where corners written for pins 30 units apart ran along the rows of pins of a part whose pins are 10 apart.

Whoever set a corner, a wire leaves a pin along the pin's way out, not along its row: a leg to a corner off that line starts straight out of the pin.

### What moves what

- Both ends move together: the wire's shape goes along, whoever owns it.
- One end moves: the wire is laid out anew, because no corner of its old route means anything any more. Corners set by hand are lost then, and undo brings them back.
- A part comes to lie over a wire of the editor's: that wire moves aside. A wire of the user's stays where the user put it.
- Rotating or flipping a part, or changing its size or pins, lays out what ends on it.

A finished edit, including the end of a drag, then lays out every wire of the editor's from scratch. What is on screen is what opening the file gives, however it got there, and a wire pushed aside comes back when the part goes away. On the dev machine in a Debug build that takes under 50 ms for the busiest example. Undo steps keep the routes they had.

**Разложить** (canvas button and menu) lays out every wire, hand-made routes too, in one undo step. Сбросить маршрут does it for one wire.

### The router

`Editing.WireRouter` searches over (cell, direction) with A*, on a grid of half a tenth of an inch.

- A wire leaves a pin straight out through the part's nearest edge, to a grid line clear of it, and arrives the same way. This is what ties it to the pin visually.
- Prices, in the same units: length, a bend, a step on a halfway line, a crossing, a corner touching another wire, running along another net's wire, and crossing a part's body. Nothing is forbidden outright, so a route always exists, and it is the least bad one when a pin is boxed in.
- Wires keep to the 0.1" grid and use the lines halfway between only where it gets crowded. Pins of a header are a tenth of an inch apart, so a bundle between two rows of pins would not fit otherwise.
- Wires of one net may share a track: that reads as the connection it is.
- A whole layout goes shortest first, then takes again each wire that crosses or overlaps something, up to three times, keeping a new route only if it is better.

During a drag only the wires that must change are routed, with a search that leans towards the goal (a weighted heuristic) instead of the best route, so that a drag stays usable: on the dev machine in a Debug build, 15 ms a grid step at most on the examples. Undo steps keep the routes, so that undo shows what was on screen rather than a new layout.

### Bundles

One by one, the first wires of a bundle take the good tracks and the last ones go around them. So wires between two rows of pins that face each other, with nothing in the channel between them, are laid out together first (`Editing.WireChannel`), as a channel router on a circuit board does it:

- Each wire goes straight out of its pin, along a track of its own, and straight into the other pin. A pin in line with its partner gets a straight wire.
- If a wire's pin stands right above another wire's far pin, the first one's track must come first. When two wires need that of each other, which every pair does in a bundle wired in reverse order, one of them steps aside half a pitch.
- The tracks' order is the one with the fewest crossings: none for a bundle in the same order, one for each pair in reverse order. Where the crossings do not decide, the outer wires go outermost, which makes an hourglass of a reversed bundle.
- Tracks are on the 0.1" grid if they fit and on the half grid otherwise, in the middle of the channel. If they do not fit, or something is in the channel, the search takes these wires like any others.

## Consequences

- Laying out everything after each finished edit means a wire can shift when another one is added. In exchange the layout does not depend on history and survives reopening exactly.
- Crossings are allowed, since a bundle wired in reverse order cannot avoid them: eight wires make 28. The router does not move parts or change the wiring to avoid them.
- Moving one end of a hand-made route loses it. The alternative is what the owner complained about.
- Corners set by hand before the `byHand` mark existed are laid out on opening like any other. Nothing in those files told them apart, and what they mostly held was stale.
