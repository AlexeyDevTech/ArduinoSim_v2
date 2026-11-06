# ADR-019: Screens and transitions

- Status: Accepted (the owner's choices of 2026-11-06: a tab beside Схема; recognised code is edited in place).
- Date: 2026-11-06

## Context

A sketch with a 1602, 2004 or OLED display soon becomes a set of screens and a way between them. That structure lives scattered through `switch` statements and `print` calls, nobody can see it whole, and every change means finding the right `setCursor`. The owner asked for an editor of screens and transitions, and for the program to form that structure for an existing project when it can.

Hand-written projects differ widely. NanoOS, the owner's example, has an `enum Screen`, a `switch` over it that calls `drawX()`, `enterScreen(X)` inside its key handling, menus in arrays of strings and applications that draw themselves. It was an example to learn from, not a pattern to copy: the generated code here has its own shape.

## Decision

### Two kinds of project, one editor

- **From code.** The sketch is scanned on every open and after every edit, and the code stays the source of truth. A screen map is derived from it and nothing else is stored but where the nodes lie on the map. What was recognised exactly (a string, a number in `setCursor`, the expression printed, the screen named in a transition, the key compared with) is edited **in the source**: exactly that token, as an undoable edit in the file's document, refused if the text there is no longer what the scan saw. The same tab is a constructor for such a project too (see below): what it adds is new code in the sketch's own style.
- **Generated.** A project with no screens in its code can start them here. A model in `screens.json`, next to the project file, is the source of truth, and `ui_screens.h` and `ui_screens.cpp` are generated from it before a build. They are plain, readable C++ and say at the top that they are overwritten. What the code must supply (a value to show, an action to run) goes in `ui_hooks.cpp`, which is created once and only ever appended to.

### Recognising code

A tolerant scanner in `Core/Screens`, not a compiler: it works on sketches that do not compile yet, in milliseconds, and it never has to see the libraries. It reads tokens (comments and preprocessor lines aside, `#define` values kept), enums, functions with their class and parameters, arrays of strings, and objects of the known display classes. A class of the sketch's own with `setCursor(x, y)` counts as a display too, and so do its objects: NanoOS draws through a buffer in front of the panel, and that is a common shape.

- **The screen variable**: the enum, or failing that the integer variable with a screen-like name, that a `switch` or a comparison uses, scored by name and use. Otherwise each function that draws is a screen.
- **Going to a screen**: an assignment of a constant to that variable, or a call passing one to a function that assigns its parameter there, followed through wrappers such as `goHome()` and `launchApp()`.
- **From where and on what**: the conditions around that site, and around each call on the way to it. `case A:` or `screen == A` gives the source, and without one the transition is from any screen. `key == KEY_OK` or `case 'x':` gives the key, with `#define` values resolved. `case 1:` on a menu index gives the item.
- **What a screen shows**: the drawing calls reached from its `case`, walked with a cursor and the constant arguments bound, through helpers such as `clearLine(row)`. A literal is text at a known place; anything else is a value. A `print` of an array element inside a loop makes it a menu with the array's strings. An application that draws itself is matched to its screen by name (`ALARM_APP`, `AlarmApp::render`).

A class of the sketch's own with `setCursor(x, y)` draws for it, and its objects count as the display. Apart from what the screens show, the scan keeps what the constructor needs (`ScreenCodeShape`, the spans on elements, transitions and screens): where the screen's own drawing ends, where a `case` goes, how the sketch goes to a screen (`enterScreen({0});`), which function handles keys and with what variable.

It is a heuristic and says so: what it could not place is marked, and each screen and transition links to its line. Nothing is ever replaced that was not read as exactly one token, and nothing removed that does not stand alone.

### The constructor in code

The owner's second word on this (2026-11-06): an editor that only shows and lets you look in the code is not a constructor. So a project from code can be built here as well, by adding code:

- **An element**: `lcd.setCursor(2, 1); lcd.print(F("Text"));`, with the library's own calls for an OLED and its shapes, where the screen's own drawing ends: at the end of a function that only this screen calls, before what sends a buffer to the display, or before the `break` of its `case`.
- **A screen**: a constant in the enum (before one that counts the others) and a `case` in the switch that draws.
- **A transition**: `if (screen == A && key == '5') { enterScreen(B); return; }` first thing in the function that handles keys, where the key is known, using the sketch's own name for the key if it has one. At the top it cannot be swallowed by whatever else the function does with a key. Without a screen in it, it works from any screen.
- **Removing**: an element's statements with their `setCursor`; a transition's statement with the `if` that is only for it; a screen with its constant, its cases and what leads to it, only if nothing else names it. What hangs on a condition, has an `else` or comes from a helper other screens use cannot go alone, and the tab says so.
- **Renaming a screen** changes every place its constant is written. **The start screen** is the constant the variable starts with.

A set of edits is applied at once, each file checked first, as one undo step. What the sketch's shape does not allow (no enum, no switch that draws, no key handler) is refused with the reason, and the buttons for it do not appear: nothing is written on a guess. Integration tests build and run what these edits produce.

### The generated code's shape

A `switch` per screen for drawing and for events: longer than a table, but something a person can read and step through, and that does not need a runtime. `uiBegin()` and `uiUpdate()` are all a sketch calls, and `uiSend(event)` and `uiGoTo(screen)` are there for its own logic. No heap, no `String`; strings go through `F()`. Values are redrawn every 250 ms: on a character display only their own cells, on an OLED the whole buffer. Inputs come from the schematic, since it knows the wiring: buttons (with the pull-up or pull-down they are wired for), a keypad, an encoder. Libraries: LiquidCrystal, LiquidCrystal_I2C, Adafruit_SSD1306, Adafruit_SH110X, U8g2, GyverOLED. A display object the sketch already declares at file level stays its own: generated code declares it `extern` in the sketch's own type, calls `uiBegin()` at the end of `setup` so that the sketch has begun it, and sets nothing it relies on elsewhere (text color and U8g2's font are set where text is placed). Otherwise the display part decides, and generated code defines `uiDisplay` for an Adafruit or LiquidCrystal library. U8g2 in page mode is refused with the reason: its drawing loop is not this code's shape.

### The model

`screens.json`, schema version 1: the display (family, size, library, object name, part), the start screen, the screens with their map position, elements (text, value, line, frame, box) and optional menu, and the transitions with their trigger (key, button, encoder step or press, timeout, event). Unknown members are kept, as in the project file. In a project from code it holds only the map positions.

### The editor

A tab, **Экраны**, beside Схема, shown when the scene has a character LCD or an OLED, the project has `screens.json`, or the sketch draws screens. The map shows each screen as a thumbnail with its transitions as labelled arrows; a double-click opens the screen, where elements drag by character cell or by pixel. The left panel lists the screens, and the right panel shows what is selected. With Рядом on, the tab takes the schematic's place and the code stays beside it, and a link to a line shows it there without leaving the tab.

In a generated project `screens.json` and the generated files are written as the model changes and before each build, not at Сохранить: the model is not a text in an editor, and Ctrl+Z goes back through it for as long as the project is open. `ui_screens.h` and `ui_screens.cpp` open read-only in a code tab. A menu made on the tab already works: its controls are chosen from what is on the schematic (an encoder, a keypad's 2, 8 and #, the buttons in their order).

The preview's font is the HD44780 ROM's, on a character display and on an OLED alike. OLED libraries' fonts differ, so positions are right and glyphs approximate; Запуск shows the real thing.

## Consequences

- A sketch that keeps its state in some unusual way shows fewer transitions or none, and a wrong guess is possible. It costs nothing, because recognising changes no code.
- New code in a hand-written project follows a recognised shape, and goes through the document with undo. A menu cannot be added there: it would mean writing a menu's logic into code whose shape is only guessed at.
- The generated code holds only what the model says, so logic that does not fit (the calculator's arithmetic, say) belongs in hooks.
- Not done: custom LCD characters, bitmaps, animated transitions, more than one display per project, and converting a hand-written project into a generated one.
