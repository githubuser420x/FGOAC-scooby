# Security

FGOAC scooby runs with administrator rights, because the game it launches requires them - without
them the game stops with ERROR 4105 about ninety seconds after boot. That is the only reason.

What the launcher touches: its own folder and the FGO Arcade install it sits in - the game's
configuration file `App\fgo-launcher.json`, the English files under `App\zh`, the local server under
`Server\`, and the log folder. It adds three program-scoped inbound Windows Firewall rules for the
local server, the game and the cabinet service so that Windows does not interrupt the first launch
with a prompt that opens behind the game window. It contacts the internet for exactly one thing: the
GitHub Releases API, to see whether a newer version exists.

The installer (`patch\Apply-EN-Patch.ps1`) backs up every file it replaces, checks every file it
copies against a SHA-256 manifest, refuses any manifest entry that points at accounts, decks, the
server state folder or the database, and can undo itself with `-Rollback`.

If you find something that lets this do more than the above - a path that escapes the install root,
a manifest entry that gets written where it should not, an update that is fetched or applied without
the checks - please open an issue and say what you found and how to reproduce it. There is no private
disclosure channel and no bounty; this is a fan project with no users' data in it.
