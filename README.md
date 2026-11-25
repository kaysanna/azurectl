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

Choose the checkout root or its `server` folder. Accounts are discovered by
reading and verifying the active save's checksum; save mutation is delegated
exclusively to CTL. Local app settings are stored under `%LOCALAPPDATA%\AzureCTL`.

## Build

Install the .NET 10 SDK for Windows, then run `build.cmd`. The self-contained
executable is written to `publish\AzureCTL.exe`; users of this executable do
not need the SDK or a separate .NET runtime. `run.cmd` launches it.

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

Selectors include embedded character, equipment, Anima and Kibo thumbnails
from the official JP client (resource revision 3075915). They work offline and
require no image downloads. An initial appears if the client has no artwork
for an entry. The AGPL license applies to the application code; game artwork
remains the property of its respective owners.

## License

GNU AGPL-3.0. Visual theme adapted from LiveLog/LiveMeter. AzureCTL does not
bundle a game client, private saves, credentials or server binaries.
