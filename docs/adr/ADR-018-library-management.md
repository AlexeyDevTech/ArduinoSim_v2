# ADR-018: Libraries are added, not configured

- Status: Accepted (the owner's choices of 2026-11-06).
- Date: 2026-11-06

## Context

Project properties had a text field, «Папки библиотек (через ;)». It asked for paths by hand, relative to the project, and it wanted a folder *holding* library folders: a path to one library, or a typo, found nothing and said nothing. Most users never need it, since the bundled libraries and `Documents\Arduino\libraries` are found without it, and those who do need it cannot tell what to type. A missing `#include` only told them to go and use that field.

The owner's decision: the field goes; a list and a button take its place, and a library that is added is copied into the project. A search of the Arduino library catalog comes with it.

## Decision

### Where libraries live

- `<project>/libs` is always a library folder of the project: no setting is needed. It comes first after the shim's own libraries, so a project's library wins over the same one in Documents or the bundled one.
- The project file does not change. `settings.libraryPaths` stays and is still honored (PlatformIO imports write it; it can be edited in the file), but the App no longer offers it.
- A copy per project: a project can move to another computer and still build, and updating a library for one project cannot break another. The price is a copy per project; a library in Documents is still found by every project without one.

### The list and the button

The Библиотеки group of the Проект page lists what is in `libs` and what the last build used, each with where it comes from: в проекте, встроенная, Документы, папка. A click opens its main header. A project's library has a ✕ that moves it to the Recycle Bin. **Добавить…** on the group's header opens the dialog.

### The dialog

A modal window, since a search with descriptions does not fit a 240 px panel. It searches the catalog and installs, and **Из папки…** and **Из ZIP…** are at its bottom, so nothing depends on the network.

- A row shows the name, the author, what it does, a version list and what is true of it here: встроена (nothing to install), в проекте (update or replace), есть в Документах.
- **A bundled library cannot be installed from the catalog.** The bundled ones are tested with the simulator and some have replacement files in `libraries/compat`, which match by folder name and were written for that version. From a folder or ZIP it can still be overridden, on the user's own judgment.
- A library whose `architectures` do not name this board's family or `*` says so, and can still be installed: the brief ignores that field, and the failure, if any, is a compile error at the line.
- The dialog says that a library's code runs on this computer, and that only devices in the palette are simulated: a library for anything else compiles and gets no answer.

### The catalog

`https://downloads.arduino.cc/libraries/library_index.json.gz`, the official index: 5.5 MB, 57 MB unpacked, about 10 400 libraries and 56 000 releases.

- Cached in `%LOCALAPPDATA%\ArduSim\library-index` with its ETag, and fetched at most once a day unless the user asks. If it cannot be fetched and a copy exists, the copy is shown and says how old it is.
- It is parsed into memory only while the dialog is open.
- Search ranks the exact name, then a name starting with the query, then a name containing it, then a header, then the description and the author. Names compare without spaces and punctuation, so `adafruit_ssd1306` finds `Adafruit SSD1306`. A query ending in `.h` looks for that header. Only 44 % of releases list their headers, so a header also matches a name equal to its stem.

### Installing

- A release is downloaded with its size shown, can be cancelled, and its SHA-256 must match the catalog's or nothing is kept. Only `https` URLs are accepted.
- Everything happens in `libs/.staging-*` first; only a complete, verified library is moved into place, and an older copy goes to the Recycle Bin only then. A failure at any stage leaves the project as it was.
- Archive entries that would land outside the staging folder are refused, and so is an archive unpacking to more than 1 GiB.
- The folder is named as the Arduino IDE names it: `Adafruit NeoPixel` becomes `Adafruit_NeoPixel`.
- Dependencies are installed with the library, newest release, unless something by that name is already found. The row says what it will bring along. Version constraints in the catalog are ignored: only 1 848 of the dependency entries have one.
- From a folder or ZIP: the folder itself if it is a library (`library.properties`, or headers at its root or in `src`, and no `.ino` without `library.properties`), otherwise the libraries found one or two levels down, which covers GitHub's `Name-master` wrapper and a whole `libraries` folder. A name without `library.properties` loses GitHub's `-master` or version suffix.

### A missing `#include`

The compiler's `'Foo.h' file not found` gets a **Найти библиотеку…** button beside it, which opens the dialog with `Foo.h` searched.

## Consequences

- The App talks to the network for one more thing, and only when the dialog opens. The catalog's code is compiled into `firmware.dll` and runs natively with the user's rights, as any sketch does; there is no sandbox.
- A library from a ZIP in a folder named like one in `libraries/compat` gets its replacement files, which may not fit that version; the result is a compile error.
- Not done: updating everything at once; telling that a library in `libs` has a newer release except when its row is on screen; removing a library from Documents.
