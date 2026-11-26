# AzureCTL

A dedicated Windows CTL app for Azur Promilia's official JP Azurepro server.
It uses LiveLog's dark surfaces, lavender accents, searchable libraries and
two-column editor layout. It contains only administration controls.

## Use

1. Build Azurepro using its top-level `build.cmd`.
2. Open `AzureCTL.exe` and choose the Azurepro checkout folder.
3. Select the player account, an entry and an action, then Apply.

Keep the game and server open for live updates. AzureCTL invokes Azurepro's
existing CTL wrapper; the running server validates and applies edits to
`server/saves/active.bin`. It does not stop the server, launch the game, create
per-command backup saves, or write directly to your save. Offline commands use
the same active save through Azurepro's CTL editor.

Characters, Talent, Runes, Star Blessing, skill levels, unlocking, gear, Anima,
Kibo grades/bond/aptitudes/genes/Star Soul and world difficulty are supported.
Inspect and Catalog show official limits. Azurepro remains the authority for
all prerequisites and caps. Changing the current map's world level requires
leaving combat/campaigns and reloads that scene.

In **Kibo**, select **Add missing Kibos** and apply it to the chosen player
account. It adds one Level 1 Kibo for each missing configuration supported by
Azurepro, with grade F where applicable. Existing Kibos are kept, and repeat
runs skip owned configurations. This action requires an Azurepro build with the
`pet add-missing` command.

Choose the checkout root or its `server` folder. Accounts are discovered by
reading and verifying the active save's checksum; save mutation is delegated
exclusively to CTL. Local app settings are stored under `%LOCALAPPDATA%\AzureCTL`.

## Build

Install the .NET 10 SDK for Windows, then run `build.cmd`. The self-contained
executable is written to `publish\AzureCTL.exe`; users of this executable do
not need the SDK or a separate .NET runtime. `run.cmd` launches it.

## Updates

Download `AzureCTL.exe` from this repository's latest GitHub release and run it. It checks for updates in the background on
launch. New versions download automatically and are verified against their
GitHub asset SHA-256 digest. No ZIP or separate manifest is needed. Click **Restart to update**, or close the app normally to
install the downloaded update without reopening. **Check for updates** retries
the check manually.

Only `AzureCTL.exe` is replaced. Your Azurepro installation, active save and
app settings are kept. Updates need a writable app folder; use a folder you
own, rather than Program Files. Update errors are logged under
`%LOCALAPPDATA%\AzureCTL\update-error.txt`.

The initial 0.1.0 build used a ZIP update feed. Replace that version with the
latest standalone EXE once to enable the EXE-only update feed.

New releases contain only `AzureCTL.exe`. After verifying a new release, the
publisher removes older published releases and downloads while retaining Git
tags and commit history. Maintainers can run `scripts/publish-release.ps1`
after building and pushing the matching version tag.

For development: `dotnet run -- --root "D:\Azurepro"`.

## Verification

Run `dotnet run --project tests/AzureCTL.Checks.csproj -c Release -- "D:\Azurepro"`
against a built server checkout. The checks copy the curated fixture to a new
temporary directory and start a loopback-only server. Your active save is never
used. They verify checksum/account parsing, command templates, offline output,
live edits, official limit rejection, menu-refresh ordering and persistence.

Display names come from official JP revision 3075915, matched exactly against
the existing English patch and term database. Untranslated names retain their Japanese text. Names never grant
availability; the connected server's filtered owned list/catalog determines
which entries appear.

Actions are always-visible buttons. The compact editor fits their fields and
Apply without scrolling. Selecting an entry uses the already-loaded list;
only Inspect requests its detailed CTL output.

Characters includes **4★ C6 · 5★ C0**: set all owned four-star characters to
Star Blessing 6 and five-star characters to Star Blessing 0. Anima includes
**4★ R5 · 5★ R1**: set all owned four-star Anima to refinement 5 and five-star
Anima to refinement 1. Select the preset, then Apply; no individual selection
is needed. These change duplicate progression only, leave other rarities
unchanged and require the matching Azurepro `rarity-preset` CTL commands.

Tab lists preload in the background and are reused per server folder and player.
Refresh reloads them; edits invalidate the player's cached lists. Use Refresh
to see changes made outside AzureCTL.
Read-only lists use the compiled CTL reader or the running server's local
control connection, avoiding PowerShell startup and repeated build checks.
Tab changes keep navigation responsive while an uncached list finishes.

Selectors include embedded character, equipment, Anima and Kibo thumbnails
from the official JP client (resource revision 3075915). They work offline and
require no image downloads. An initial appears if the client has no artwork
for an entry. The AGPL license applies to the application code; game artwork
remains the property of its respective owners.

## License

GNU AGPL-3.0. Visual theme adapted from LiveLog/LiveMeter. AzureCTL does not
bundle a game client, private saves, credentials or server binaries.
