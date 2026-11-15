# Changelog

## Unreleased

- Batch multiple row deselections in one native selection write instead of repeated UnselectRow calls.
- Preserve pending state updates after structural refresh instead of promoting their overlap to a full reload.
- Incrementally refresh copied group aliases and use affected object counts to budget multi-layer updates.
- Reuse copy-group reconciliation until objects or group memberships change, with a final transform-completion reconciliation.
- Add debug-only selection timing breakdowns, refresh queue latency, dispatch IDs, command event counts, and copy timings to the background JSONL log.
- Reuse copy-name allocation within an undo operation and cache object presentation to reduce Gumball/Array stalls in large documents.
- Capture only the source objects' groups instead of scanning every group; add debug-only copy timings.
- Add per-object notes with a note column, multiline popup, previews, and a notes-only filter.
- Store notes in plugin-specific object attributes with native save, copy, and undo support.

## 0.9.3 - Stability and display fixes

- Defer block expansion row rebuilding until after native tree events and avoid rebuilding rows on collapse to address a macOS crash.
- Use a fixed blue for reference links, including History inputs, and preserve selected text colors in custom name cells.
- Keep Isolate and IsolateLock session-only; saved files open with temporary isolation released.
- Preserve manual Show/Hide changes and the current isolated view across saving.
- Consume legacy saved isolation data without restarting Isolate on file open.
- Keep performance logging off by default in every build.
- Add `ObjectOutlinerDebugMode` to toggle command-history and JSONL diagnostics at runtime.

## 0.9.2 - Rhino compatibility update

- Lower the minimum Rhino version from 8.32 to 8.15 on macOS and Windows.
- Build against RhinoCommon 8.15.25013.13001, the API floor required by transform-copy tracking.
- Keep existing features and the `.3dm` storage format unchanged.
- Verify both `net7.0` and `net48` builds; runtime testing on Rhino 8.15 itself remains pending.

## 0.9.1 - Outliner workflow update

- Add previewed batch rename for layers, groups, and objects.
- Add a filter-aware count column for layers and groups.
- Add drag-and-drop ordering for sibling groups and save the order in `.3dm` files.
- Add a command to create a sublayer and move selected objects into it.
- Keep layer rows visible while object filters narrow object and group results.
- Use persistent popups for columns, layer filters, and display modes.
- Follow Rhino theme colors for outliner names on Windows dark mode.
- Improve Japanese labels to match Rhino's layer terminology.

## 0.9.0 - Public Beta

Initial public beta of Object Outliner by 9dpbQ for Rhino 8.32 or later on macOS and
Windows.

- Browse layers, groups, and objects in one hierarchical panel.
- Synchronize selection with Rhino viewports.
- Manage visibility, locking, layer properties, favorites, and object names.
- Reorganize layers, groups, and objects with drag and drop.
- Search and filter large models by layer scope and object type.
- Follow group and Rhino history relationships.
- Submit objects and groups from the outliner during compatible Rhino commands.
- Restore saved outliner workspace and isolate state from `.3dm` files.

This release is provided as a free public beta. Please keep backups when working
with important `.3dm` files.
