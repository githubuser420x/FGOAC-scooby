# Releasing

A release is one zip and its checksum, published as a GitHub release on a `vX.Y.Z` tag. The updater
in the launcher reads `releases/latest` from the GitHub API, so nothing reaches players until the
release is published and not marked pre-release.

You need an FGO Arcade local platform install with the current English game files under `App\zh`,
because the zip carries those files - the repository does not.

## 1. Decide the version

Semantic versioning over the whole release: the launcher, the English game files and the installer.

- **Patch** - translation fixes, a launcher bug, a corrected error message.
- **Minor** - a new page, a new launcher feature, a batch of rebuilt sprite archives.
- **Major** - a release that is not applied the same way, or that does not roll back onto the
  previous one.

Set it in three places and make sure they agree:

| Where | What to change |
| --- | --- |
| `src\FGOLocalPlatform.csproj` | `<Version>` to `X.Y.Z` - the one place a version number is written |
| `CHANGELOG.md` | move the entries under `[Unreleased]` into a new `[X.Y.Z]` section with today's date |
| the tag | `vX.Y.Z` |

The About page, the updater and the packager all read the version back from the built assembly, so
the `.csproj` is the one that has to be right; passing `-Version` to `package.ps1` only overrides
the name of the folder and the zip.

## 2. Build and package

```
publish.cmd
package.ps1 -GameRoot <install root>
```

`package.ps1` publishes first if `dist\FGOAC scooby.exe` is missing, or always with `-Publish`. It
writes, under `release\`:

```
FGOAC-scooby-vX.Y.Z\        the package folder
FGOAC-scooby-vX.Y.Z.zip     the release asset
FGOAC-scooby-vX.Y.Z.zip.sha256
```

The zip has no base directory: it is meant to be unzipped straight into the game folder, so its
files land beside `App` and `Server`.

`package\README.md` is the short read-me that goes inside the zip; `{{VERSION}}` and `{{DATE}}` are
filled in during the run. There is only one changelog: the root `CHANGELOG.md` is copied into the
package as it stands.

## 3. Check the package before you publish

- `manifest.json` exists and its file count matches what the run reported.
- `SHA256SUMS.txt` lists the seven top-level files and `compat\fgoglcompat.dll`.
- Unzip into a scratch copy of a V1.01 install and run `Apply-EN-Patch.ps1 -InstallRoot <copy>`.
  A good run reports every file verified against the manifest with zero mismatches, and a second run
  is a no-op. Then `-Rollback` and confirm the install is back as it was.
- Run the published exe once and open each of the five pages.

Test against a copy, not the install you play on.

## 4. Tag and publish

```
git tag -a vX.Y.Z -m "X.Y.Z"
git push origin master --follow-tags

gh release create vX.Y.Z ^
  "release\FGOAC-scooby-vX.Y.Z.zip" ^
  "release\FGOAC-scooby-vX.Y.Z.zip.sha256" ^
  --title "X.Y.Z" ^
  --notes-file <the new section of CHANGELOG.md>
```

Both assets have to be on the release: the updater downloads the zip and checks it against the
`.zip.sha256` beside it before applying anything.

Do not use `--prerelease` or `--draft` for a release you want players to get - `releases/latest`
skips both, and the updater will not see it.

## 5. After publishing

- Open the release page and confirm both assets are attached and the zip's size matches the local one.
- Start a launcher from the previous version and confirm it offers the update, fetches it and
  applies it.
- Add a new empty `[Unreleased]` section at the top of `CHANGELOG.md`.
