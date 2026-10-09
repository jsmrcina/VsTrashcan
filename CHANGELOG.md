# Changelog

## 1.1.0 — 2026-10-08

Updated for Vintage Story 1.22.7 (.NET 10) and largely rewritten to fix item loss reported on the mod DB and GitHub.

### Upgrading

- **Delete the old zip first** (server and every client). The mod ID changed from `VsTrashcan` to `vstrashcan` because
  the mod DB requires lowercase IDs, so the game would otherwise load both. Also remove the community continuations
  (`vstrashcancontinued`, `vstrashcancontinuedcontinued`); they are the 1.0.x code under other IDs.
- Filters set up with 1.0.x are converted the first time each player joins, keeping whether each one was an item or a
  block.

### Behavior changes

| | 1.0.x | 1.1.0 |
|---|---|---|
| What a filter matches | Anything with the same numeric id, so an item could match an unrelated block (steel pickaxes vs bamboo shoots, fired crocks) | Only stacks that would stack with the filter: same item or block, same attributes (container contents, tool wear), ignoring stack size, temperature and spoilage |
| When auto-trash happens | A sweep of the backpack and hotbar every 5 seconds, forever | Once when you set a filter (into Recently trashed, so you can take it back), then only on pickup: matching items are destroyed instead of picked up |
| Setting a filter | Consumed the held item | Records a copy and moves the held stack into Recently trashed |
| Undo | None | "Recently trashed": the last 10 things you trashed, recoverable until you close your inventory |
| Right-click on the trash slot | Deleted the whole held stack on the server while the client still showed the rest | Trashes exactly one item |
| Shift-click | Could desync, leaving items that seemed gone | Never moves anything into the trash |
| Equipped bags | Swept like any other slot | Never touched, even by a backpack filter |
| Filter slots | Looked like normal slots | Tinted, with "Auto-trash filter (copy)" in the tooltip |
| Saved filters | Packed list, slot positions lost; a second player's filters could load into the wrong slots | Saved per player and per slot, by item code |
| Window | The close button did nothing | Close button works; `.trashcan` hides or shows the window (filters keep working) |
| Immersive mouse mode | Broke free look; later overlapped the inventory | Free look works; the window moves to the left edge, since the game moves the inventory right in that mode |
| Logging | Warnings on every save and filter change | Quiet; one line at startup |

### Fixes

- Steel pickaxes deleted by a bamboo shoots filter (GitHub #4) and fired crocks disappearing (GitHub #3, mod DB): items
  and blocks are numbered separately and 1.0.x ignored which one a filter was.
- "Items deleted that were not in the filter": same cause.
- Right-clicking the trash slot deleting the whole stack (reported on the 1.20 continuation).
- Filters loading into the wrong slots or failing to load when more than one player had them.
- The trash window's close button did nothing (reported on the 1.20 continuation).

### Under the hood

- The trash and filter slots are one inventory the server owns, so the game's own slot handling moves items, the same
  as any chest. 1.0.x kept them on the client and told the server to "clear the held item" afterwards, which is where
  most of the desyncs came from.
- Auto-trash on pickup replaces the game's `collectitems` behavior on each player (the server logs an error if another
  mod already replaced it, instead of failing silently).
- A filter holds exactly what was clicked: some items (empty meal bowls, cooked pots) turn into a different block when
  placed in a slot, and filter slots don't let that happen.

### Testing

- 118 unit tests with real game items and blocks, including the game's own crock, deliberate item/block id and code
  collisions, and an exhaustive every-filter-against-every-stack check. Each safety rule was checked by breaking it on
  purpose (the 1.0.x matching rule fails 13 tests).
- An in-game self-test on a real 1.22.7 dedicated server checks every item and block in the game (18 558, plus creative
  variants, filled containers and worn tools): over 2 million filter/stack pairs with no wrong matches, the 4 468
  item/block id collisions in a vanilla world never matching, and every filter surviving saving and 1.0.x conversion.
  With the 1.0.x rule put back it reports over a million failures.
- Played on a 1.22.7 dedicated server: trashing, right-click, history, filters with held and existing stacks, pickup,
  shift-click, `.trashcan` and immersive mouse mode.
