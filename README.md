# To build

Requires the .NET 10 SDK and Vintage Story 1.22.x installed locally (the build references the game's DLLs).

1. Set `VINTAGE_STORY` to the game directory (the one containing `VintagestoryAPI.dll`).
    - In PowerShell, source `init.ps1`: '`. ./init.ps1`' (Windows: `%APPDATA%\vintagestory`, Linux: `/opt/vintagestory`).
2. Build by running '`dotnet build -c Release`'.
    - `.vscode/launch.json` launches the game with the Debug build loaded (via `${env:VINTAGE_STORY}`), so start VS Code
      from a shell where `VINTAGE_STORY` is set.
3. Output is under `bin/Release/VsTrashcan.zip`.

# To test

- Unit tests: '`dotnet test tests/VsTrashcan.Tests`'. They use real game `Item`/`Block` objects (and the game's own
  `BlockCrock`) in a mocked world, with deliberate item/block id and code collisions. Most of them guard the one thing
  that matters most: **a filter never matches anything that wouldn't stack with its sample**, including an exhaustive
  every-filter-against-every-stack check.
    - The test assembly is named `VSTests` on purpose: as of 1.22 `IPlayer` has an `internal` member, so only assemblies
      the game grants `InternalsVisibleTo` (it grants `VSTests`) can implement `IServerPlayer` — see `FakeServerPlayer.cs`.
- Smoke test: '`bash tests/smoke/server-smoke.sh`' boots a throwaway dedicated server with the mod and the test-only
  `tests/selftest` mod, which checks the filter rule (against an independent oracle), filter saving, 1.0.x conversion and
  the trash slots against **every item and block in the game** (~18 500 of them, plus creative variants, filled
  containers and worn tools, and the ~4 500 item/block id collisions in a vanilla world). Takes ~20 s, needs no account.
- Both are VS Code tasks (`Test`, `Smoke test (dedicated server)`).

# To use

1. Copy `VsTrashcan.zip` into the `Mods` folder, on the server **and** every client.
    - Upgrading from 1.0.x: **delete the old zip first.** The mod ID changed from `VsTrashcan` to `vstrashcan` (the mod
      DB requires lowercase IDs), so the game would otherwise load both. Also remove the community continuations
      (`vstrashcancontinued`, `vstrashcancontinuedcontinued`), which are the old code under other IDs.
    - Filters set up with 1.0.x are converted automatically the first time each player joins.
2. Open your inventory; the trash window appears next to it.
    - **Trash slot:** drop items on it. Left click trashes the held stack, right click trashes one item.
    - **Recently trashed:** the last 10 things you trashed. Take them back until you close your inventory, which
      destroys them for good. Trashing an eleventh pushes the oldest out.
    - **Auto-trash filters** (the tinted slots; they hold copies, not items): click one with an item to make a filter;
      click with an empty hand to clear it. Setting a filter moves the held stack and matching stacks already in your
      backpack and hotbar into Recently trashed, once, so you can take them back. From then on,
      matching items you pick up are destroyed instead of picked up. Equipped bags are never touched. "Matching" means
      it would stack with the filter: a copper ingot filter never takes tin, a filled crock, or a block.
3. `.trashcan` in chat hides or shows the window (your filters keep working while it's hidden).

# Copyright Info

This mod is only possible because of the following projects:

- https://github.com/copygirl/howto-example-mod
    - Published under public domain

- https://github.com/p3t3rix-vsmods/VsProspectorInfo
    - Published under MIT.
