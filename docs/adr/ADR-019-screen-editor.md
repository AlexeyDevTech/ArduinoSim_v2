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

It is a heuristic and says so: what it could not place is marked, and each screen and transition links to its line. Nothing is ever replaced that was not read exactly, and nothing removed that does not stand alone.

### Positions that are not plain numbers

Code rarely places everything with `setCursor(3, 1)`. A position is read as what it is, and what can be done with it follows from that:

| In the code | Shows | A drag |
|---|---|---|
| A number | the number | replaces it |
| A name or expression of constants: `MARGIN`, `LCD_COLUMNS - 5`, `const`, `enum` | as written, at its value | writes a number at this call; the definition stays |
| A local given a value once, from constants and `strlen`: `start = (LCD_COLUMNS - len) / 2` | as written, worked out with the width of what is printed | writes a number, and the declarations nothing else then uses go too |
| Anything the run changes: a loop's variable, a field, a call, a local assigned twice | as written, at what a small function comes to if that can be told | leaves that axis alone, since a number would freeze it. Typing a number or an expression in the panel replaces it, because that is a decision |
| Nothing of its own: text printed after other text, or after a helper left the cursor | where the cursor is | adds a `setCursor` before it; what is printed next follows, as on the display |
| Inside a function other screens call too | | never edited from here. What such a helper prints first takes its `setCursor` before the call to it, unless the helper sets a cursor itself |

A shape whose size or place the run decides is shown all the same, at a size to find it by, instead of disappearing from the screen.

A file's text is the record of what was decided, so each of these says what it did, and one Ctrl+Z takes back all of it.

### A display class of the sketch's own

A class with `setCursor(x, y)` in front of the panel is drawn on as a display is, and it can hold the layout: `lcd.printRight(3, F("..."))`, `lcd.print2(minutes)`.

- Its other methods are followed like any helper, so what they print appears where their own `setCursor` puts it, worked out with the width of the text. What is written at the call (a row, a text) can be edited there; nothing inside the class is, since every screen goes through it. The call to a method can be removed if the method drew one thing.
- What `write()` puts text out through writes one cell. Reached any other way (a bell, the parts of big digits), its shape is known only at run time, so it shows as a block the size it takes. To put those blocks in their places, a `for` over known numbers in the class's own code is run, not just read.
- A `write()` that looks at UTF-8's lead bytes shows letters by itself (`ScreenDisplay.OwnFont`), so the preview draws Cyrillic instead of `?`. The shapes are the ROM's and a few of ours, not the sketch's own font: to read, not to compare. A plain display keeps `?`, which on real hardware would be wrong characters too.

A value with nothing known to show draws as its last name when it has one (`dt.day` as `day`) and as dots otherwise: the start of `reinterpret_cast<...>` says nothing. What could not be placed draws only where it hides nothing. Prints in a row that a guessed width would push off the display are drawn narrower, because what a sketch lines up fits.

A list kept in one string (`"one\0" "two"`) makes a menu as an array does, reached through locals and parameters. Its items cannot be edited from here, since a literal holds its terminator. A screen with no `case` of its own is what the switch's `default` draws.

### Small functions of the sketch

One function can draw every list, and ask others for its heading, its length and its items, each of which switches on the screen. Their text says nothing about what a list shows: the answer is in what they return, and a reading of shapes breaks the next time the sketch is tidied. So they are run. `SketchRun` follows the small part of C++ such functions are written in, on what the sources alone can tell. It is for the picture and does not simulate anything: the sketch itself never runs here, and a wrong guess draws a wrong picture, with no effect on the program.

- **What it knows.** The screen being read, constants, enums with their numbering, `#define`s, arrays of strings, a string holding several items (walked by real pointer arithmetic, so `list += strlen_P(list) + 1` needs nothing special), arrays of structs with their `sizeof`, its own locals and arguments, and a default argument wherever that was declared. The place of a list counts as its first item, since that is what the picture shows: what a list's loop compares its variable with (`i == selected`) or starts from (`for (i = top; ...)`).
- **What it does not.** Anything else: a variable of the sketch, the hardware, a library's function. A condition on one of those ends the run, the answer is that it cannot be told, and the print stays a value as written. Nothing changes but its own locals. It gives up past 20000 steps or 16 calls deep, and does not follow `do`. Numbers do not wrap as on the board: a cast does not narrow.
- **`#if`.** A table's row or a part of a string under an `#if` that the sketch's own `#define`s turn off is left out. What depends on something else (a flag from the build, `defined(...)`) counts as built, and so does its `#else`, because shown is better than lost. `#ifndef` counts as on, being mostly a guard or a default that follows at once.
- **What comes of it.** Text becomes an element. Text in a loop, with the loop's variable in it, becomes a menu, over what the loop's header allows: a bound that mentions a list's place is a window of the list and does not count. A number becomes what a value shows and what a position is.
- **What can be edited.** Only a literal that a function returned from a `case` of this screen alone. What a table, a constant or a `#define` holds, and what a `default` returns, other screens may come to as well. An item of a menu read this way is not edited from here, and a position worked out from a call is left alone by a drag, as the table above says.

Around this, a `switch` on the screen inside what is being drawn draws this screen's case. A `switch` on anything else shows one thing at a time, so its cases are alternatives of its `default`, or of its first case if it has none, and the cursor goes on from where that one leaves it. What a screen with no `case` of its own draws, other screens draw too, so nothing is added to it from the tab.

### The constructor in code

The owner's second word on this (2026-11-06): an editor that only shows and lets you look in the code is not a constructor. So a project from code can be built here as well, by adding code:

- **An element**: `lcd.setCursor(2, 1); lcd.print(F("Text"));`, with the library's own calls for an OLED and its shapes, where the screen's own drawing ends: at the end of a function that only this screen calls, before what sends a buffer to the display, or before the `break` of its `case`.
- **A screen**: a constant in the enum (before one that counts the others) and a `case` in the switch that draws.
- **A transition**: `if (screen == A && key == '5') { enterScreen(B); return; }` first thing in the function that handles keys, where the key is known, using the sketch's own name for the key if it has one. At the top it cannot be swallowed by whatever else the function does with a key. Without a screen in it, it works from any screen.
- **Removing**: an element's statements with their `setCursor`; a transition's statement with the `if` that is only for it; a screen with its constant, its cases and what leads to it, only if nothing else names it. What hangs on a condition, has an `else` or comes from a helper other screens use cannot go alone, and the tab says so.
- **Renaming a screen** changes every place its constant is written. **The start screen** is the constant the variable starts with.

A set of edits is applied at once, each file checked first, as one undo step. What the sketch's shape does not allow (no enum, no switch that draws, no key handler) is refused with the reason, and the buttons for it do not appear: nothing is written on a guess. Integration tests build and run what these edits produce.

### The generated code's shape

A `switch` per screen for drawing and for events: longer than a table, but something a person can read and step through. Each piece of the runtime goes in only if something in the project calls for it, so that a small project stays small. `uiBegin()` and `uiUpdate()` are all a sketch calls, and `uiSend(event)` and `uiGoTo(screen)` are there for its own logic. No heap, no `String`; strings go through `F()`. A character display is drawn into a frame in RAM, and only the cells that differ go to the panel, a few on each pass: a whole new screen neither flickers nor keeps the keys waiting. It is composed again every 100 ms. An OLED's whole buffer is sent when something changed and every 250 ms on a screen with something that changes by itself. Inputs come from the schematic, since it knows the wiring: buttons (with the pull-up or pull-down they are wired for), a keypad, an encoder. Libraries: LiquidCrystal, LiquidCrystal_I2C, Adafruit_SSD1306, Adafruit_SH110X, U8g2, GyverOLED. A display object the sketch already declares at file level stays its own: generated code declares it `extern` in the sketch's own type, calls `uiBegin()` at the end of `setup` so that the sketch has begun it, and sets nothing it relies on elsewhere (text color and U8g2's font are set where text is placed). Otherwise the display part decides, and generated code defines `uiDisplay` for an Adafruit or LiquidCrystal library. U8g2 in page mode is refused with the reason: its drawing loop is not this code's shape.

### The model

`screens.json`, schema version 2: the display (family, size, library, object name, part), the start screen, the screens with their map position, elements and optional menu, the transitions with their trigger, the project's variables and symbols. Unknown members are kept, as in the project file. In a project from code it holds only the map positions. Version 1 files open as they were. What nobody set is left out of the file and reads back as its default.

### A whole device, without a template

The owner's third word on this (2026-11-06): the program did not have the tools to make a system like NanoOS, and NanoOS is only an example, so no template of it and no button that makes one. The owner chose data and actions in the panel over only more places for C++, and sections in the left panel over a tab of their own. So what such a device is made of became general mechanisms of a generated project, each usable without the others.

- **Data.** A variable is a number with limits, a flag, a choice from a list of words, a time, a stopwatch or a countdown. Any but the last two can be kept in EEPROM: at the end of it, with a mark that says it is this project's and these variables. They are `int32_t uiVar_name`, and a stopwatch or countdown is `UiTimer uiTimer_name`, so that a hook can use them.
- **Expressions.** Where something depends on data, a short expression says what: `alarmOn && clock / 60 == alarmAt / 60`. Whole numbers, the variables and what is built in, with arithmetic, comparison and logic; `ScreenExpression` checks them where they are typed, and they become C++ with the names replaced. Built in: `hour minute second day month year weekday`, `clock` (seconds of the day), `date` (yyyymmdd), `uptime`, `menuItem`, `menuCount`, and for a stopwatch or countdown `name` (seconds), `name_ms`, `name_on`.
- **Actions.** A transition, a menu item or arriving on a screen does a list of steps first: set, add, toggle or next word, start, stop, reset, take what was typed, erase a digit, beep on a buzzer part, drive an output part, call a hook. A list, not a language: what does not fit is C++ in a hook.
- **Going places.** A transition can stay where it is, or go back to where it came from, with a menu's place restored. A screen already on the way here is gone back to, so that the way never grows. A key can have several transitions with different conditions, tried in order. New triggers: any key nothing else took, a condition becoming true (once, not while it stays true, and not at the start if it already is), a countdown reaching zero. A notification over anything is no special thing, only a screen entered from any screen, with any key going back.
- **Elements.** Text and values align in a field at run time. Any element can hang on a condition. A value has a format: two digits, times, dates, a word from a list. Big digits are two rows high on a character display and a large font on an OLED. A bar shows how full something is. A symbol is five dots by eight. An input takes digits from a keypad into a variable as a number, a time or a date, from the left into a mask or from the right after zeros, and refuses what makes no sense when it is taken. A drawn element is a place on the screen for a hook. A menu can say which item this is as `3/7`, mark the current item with a symbol, and show a value or a star at each row's right edge.
- **Text beyond ASCII.** Text is UTF-8 in the model and in hooks. Letters shaped like ASCII ones use the ROM, and the rest take the panel's eight programmable characters, loaded as needed and only when nothing shows the one they replace, so that text on the display never changes shape. Russian capitals, arrows and a few signs have shapes in `ScreenFont`, the same on every HD44780 ROM and on an OLED, where they are drawn dot by dot. The generator scans `ui_hooks.cpp` too, so that what a hook prints has its shapes. Past eight on one screen, the nearest ASCII character says what was meant.
- **Clock.** Asking for the time brings a clock: a count from the board's start, or the DS3231 if the schematic has one, read and set through RTClib.
- **The hooks stay.** A screen can hand its keys to `uiKey_id(char)` and its passes to `uiTick_id()`, a drawn element is `uiCode_name()`, and `uiOut` and `uiAt` print on the display as the generated code does. A month's grid or a calculator's arithmetic is code; data and actions are for what most devices repeat.
- **The tab says what will not work** (`ScreenCheck`): an expression it cannot read, a name nothing has, an action on the wrong kind of variable, more than eight programmable characters at once with the worst place a menu can scroll to, a letter with no shape, an input with no keypad on the schematic, a part that is not wired.

What a value will read as is known only at run time, so the picture of a screen shows what the variable starts as, and a fixed time and date for what is built in. Of elements on different conditions in one place, it draws the selected one or the first, and the right panel lists all of them.

The check of all this was a system like NanoOS put together from these tools in a scratch folder and driven key by key: clock, menus, formats kept in EEPROM, time and date editors, stopwatch, countdown, alarm, month, calculator, monitor, notifications. It needed C++ only for the month and the calculator. It is not in the repository: the example that is, ScreensKitchenTimer, is another device, with no code of its own.

### The editor

A tab, **Экраны**, beside Схема, shown when the scene has a character LCD or an OLED, the project has `screens.json`, or the sketch draws screens. The map shows each screen as a thumbnail with its transitions as labelled arrows; a double-click opens the screen, where elements drag by character cell or by pixel. The left panel lists the screens, and the right panel shows what is selected. With Рядом on, the tab takes the schematic's place and the code stays beside it, and a link to a line shows it there without leaving the tab.

In a generated project `screens.json` and the generated files are written as the model changes and before each build, not at Сохранить: the model is not a text in an editor, and Ctrl+Z goes back through it for as long as the project is open. `ui_screens.h` and `ui_screens.cpp` open read-only in a code tab. A menu made on the tab already works: its controls are chosen from what is on the schematic (an encoder, a keypad's 2, 8 and #, the buttons in their order).

The preview's font is the HD44780 ROM's, on a character display and on an OLED alike. OLED libraries' fonts differ, so positions are right and glyphs approximate; Запуск shows the real thing.

## Consequences

- A sketch that keeps its state in some unusual way shows fewer transitions or none, and a wrong guess is possible. It costs nothing, because recognising changes no code.
- New code in a hand-written project follows a recognised shape, and goes through the document with undo. A menu cannot be added there: it would mean writing a menu's logic into code whose shape is only guessed at.
- The generated code holds only what the model says, so logic that does not fit (the calculator's arithmetic, say) belongs in hooks.
- What the panel offers belongs to generated projects. A project from code keeps its own logic, and the tab reads and edits what it draws.
- Not done: lower-case Cyrillic (eight programmable characters would not hold it; on an OLED it could come later), bitmaps, animated transitions, more than one display per project, a screen's id other than the one it got, and converting a hand-written project into a generated one.
