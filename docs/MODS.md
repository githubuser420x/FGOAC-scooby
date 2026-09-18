# Writing a mod

A mod is a folder with a `mod.json` in it, dropped into `Mods\`. The launcher's Mods page lists it,
applies it and takes it off again. `overlay\Mods\` holds the mods that ship with the launcher, so a
mod written there reaches players through the English patch; a mod a player writes sits in their own
`Mods\` folder and is never overwritten by an update.

This is the manifest reference. `docs\DEVELOPING.md` covers the engine underneath - the baseline
store, the journal, drift and repair. `docs\mods\examples\` has a worked folder for each shape
below; none of it ships, and copying a folder into `Mods\` is how to try one.

## Where things are

The install root is found, not configured. `GamePaths.ResolveGameRoot` starts at the folder the
launcher was launched from and uses its `App` subfolder when `<that>\App\ago.exe` exists, so the
install root is the folder holding `App\`, `Server\`, `DEVICE\` and the launcher together. From
there the manager uses fixed names:

| Path | What it is |
| --- | --- |
| `<install>\Mods\<folder>\mod.json` | a mod. Only immediate subfolders of `Mods\` are read |
| `<install>\Mods\<folder>\files\` | only for a mod that uses `overlay`: the whole files it ships |
| `<install>\Mods\tools\farc.py` | the archive reader/writer, run by the game's own Python |
| `<install>\Server\python\python.exe` | the Python that `farc.py` runs under |
| `<install>\Mods\.state\` | the manager's own store. Never edit it by hand |

**A path in a manifest is relative to the install root, not to the mod's folder.**
`App\zh\rom\gm_param.properties` is right; `Mods\my-mod\gm_param.properties` is not.

Only the file name part of a path may contain wildcards. `App/zh/rom/single/quest_prop_*.txt`
matches every quest file in that one folder; `App/*/rom/single/quest_prop_*.txt` matches nothing,
because the folder part has to be a real folder.

`Mods\.state` is the launcher's own store and is separate from the `mods\` folder and `.state` of
the standalone PowerShell manager. Pointing both at one install loses the true original, because
whichever ran first keeps it.

## The manifest

| Field | Required | What it is |
| --- | --- | --- |
| `id` | no | the name the journal records. Falls back to the folder name |
| `name` | no | the title the Mods page shows. Falls back to the folder name |
| `version` | no | free text, recorded when the mod is applied |
| `description` | no | shown when the row is selected |
| `targets` | no | the files to edit and the rules that edit them |
| `overlay` | no | whole files the mod ships under its own `files\`, as install-root relative paths |
| `parameters` | no | the knobs a mod exposes. Only the mod whose id is `battle-tuner` has a page that draws them; see "Knobs" below |

Write the `description` for the player who is about to click Apply. The ones the launcher ships run
to a paragraph or three: what is being changed, the evidence for the value chosen, what is
deliberately left alone, and what has not been verified in game. That is the difference between a
reviewable mod and a magic number.

Keep the folder name and the `id` the same. The journal records the `id`, so changing it after a mod
has been applied reads as a different mod, and the next write restores the original instead.

### Targets

| Field | What it is |
| --- | --- |
| `files` | install-root relative path globs, one to many. `/` works as a separator |
| `farc.member` | when set, the rules edit this member inside a packed archive instead of the file |
| `rules` | the rewrites to run on the text |
| `patches` | raw byte writes, for a file that is neither text nor an archive |

`files` plus `farc.member` is how a master table is edited. `patches` is a target of its own and is
not combined with `rules`. One target is one way of editing, so a mod that edits two tables uses two
targets, and a mod that edits the same table in two different ways uses two targets with the same
`files`.

### Rules

Every rule matches every line its pattern matches, and rebuilds each of those lines as
`head + new value + tail`. The pattern **must** capture `(?<value>...)`, and should capture
`(?<head>...)` and `(?<tail>...)` as well - a pattern that captures only `value` has the rest of the
match thrown away, which is why the head and tail exist.

| Field | What it is |
| --- | --- |
| `id` | free text, for the reader. Nothing reads it - a refusal names the pattern, not the id |
| `pattern` | a .NET regular expression, run multi-line, against the whole file |
| `operation` | `set`, `scale`, `shift`, `clamp` or `replace` |
| `value` | `set`: the number to write. `replace`: the text to write |
| `factor` | `scale`: the multiplier |
| `amount` | `shift`: the number added |
| `min`, `max` | clamp the computed number, under every operation |
| `when` | knob id to expected value. The rule is skipped unless every pair matches |

| `operation` | Writes | Reads |
| --- | --- | --- |
| `set` | the captured number becomes `value` | `value` |
| `scale` | the captured number times `factor` | `factor` |
| `shift` | the captured number plus `amount` | `amount` |
| `clamp` | the captured number, bounded by `min` and `max` | `min`, `max` |
| `replace` | `value`, taken as text | `value` |

`set`, `scale`, `shift` and `clamp` parse what they captured as a number. A capture that is not a
number is left alone, so one rule can sweep a table where a few rows are blank. `replace` does no
parsing, which is how a field that is not a number - a YAML boolean, a line of Python - is changed.

`scale` rounds half to even, so 5 at a factor of 0.5 is 2, not 3.

### Writing the pattern

Almost every rule wants the same shape:

```
^(?<head>[ \t]*key\.\d+[ \t]*=[ \t]*)(?<value>\d+)(?<tail>[ \t]*(?:\r?\n|$))
```

- `[ \t]*` on both sides of the `=` takes space or tab indentation and either spacing style.
- `\d+` is the value. `-?\d+` for a number that can be negative, `[^\r\n]*` for one that is not a
  plain integer.
- `(?:\r?\n|$)` at the end stops the newline being swallowed, and takes the last line of a file that
  has no trailing newline.
- `\d` matches one digit. `skill_reload_\d` hits `skill_reload_1` and stops at `skill_reload_10`;
  use `\d+` unless that is what you meant.

Two gotchas that cost time:

- In JSON a backslash doubles. The pattern above is written in `mod.json` as
  `^(?<head>[ \\t]*key\\.\\d+...`.
- .NET has no `\Q...\E`. To match a literal with punctuation in it - `[printer]`, a dotted path - use
  `Regex.Escape` on paper and write the escaped form out, or match each character with its own
  escape.

### Anchoring a short literal

`enable=1` appears twelve times in `App\segatools.ini`. A pattern that matches the bare line edits
the first one, which is not the one you meant, and nothing reports which line it was: the engine
counts how many lines it changed, not where they were. Two ways out. Match the key as well as the
value, which is enough whenever the key is unique to the line; or put a lookbehind in `head` so the
match still starts on the line that changes:

```json
"pattern": "(?<head>(?<=\\[printer\\]\\r?\\n; Sinfonia CHC-C330 printer emulation setting\\.\\r?\\n))(?<value>enable=1)(?<tail>[ \\t]*(?:\\r?\\n|$))"
```

That is the printer block and no other, so the other eleven `enable=1` lines survive. The anchor is
the comment text, which is the trade a lookbehind makes: the pattern breaks if that comment is
reworded. Keep the lookbehind inside `head` rather than wrapping the section header into the match,
or the match starts on the header line and stops starting on the line that changed.

## The shapes

Five of them, decided by what the target file is rather than by what the mod does.

### 1. A text file, one field per line

The ordinary case. A properties file or a quest table, one `key = value` per line, value is a
number. `docs\mods\examples\text-field\` makes every command-skill cooldown half again as long.

```json
{
  "id": "text-field",
  "name": "Text field example - longer command-skill cooldowns",
  "version": "1.0.0",
  "description": "Multiplies every command-skill cooldown by 1.5, so a skill spends half again as long before it can be used again. The fields are the gm_param.skill_reload_N rows of App/zh/rom/gm_param.properties, which the client reads; the server does not. min stops a cooldown reaching zero, which the game would treat as ready. Not verified in game.",
  "targets": [
    {
      "files": [ "App/zh/rom/gm_param.properties" ],
      "rules": [
        {
          "id": "skill-reload",
          "pattern": "^(?<head>[ \\t]*gm_param\\.skill_reload_\\d+[ \\t]*=[ \\t]*)(?<value>\\d+)(?<tail>[ \\t]*(?:\\r?\\n|$))",
          "operation": "scale",
          "factor": 1.5,
          "min": 1
        }
      ]
    }
  ]
}
```

The launcher's `battle-tuner` scales these same rows off a knob.

### 2. A text file, a value that is not a number

A YAML boolean, a line of Python, a config string. `replace` writes the text as given and does no
arithmetic. `docs\mods\examples\text-literal\` lifts the platform's currency limit.

```json
{
  "id": "text-literal",
  "name": "Text literal example - unlimited currency balances",
  "version": "1.0.0",
  "description": "Sets unlimited_balances to True in Server/artemis/config/fgo.yaml, so an account is no longer held to the amount of currency the platform hands out. One literal line is replaced and the line count is unchanged. The server reads this file at startup, so the platform has to be restarted before the change reaches anyone. Uninstalling puts the original bytes back.",
  "targets": [
    {
      "files": [ "Server/artemis/config/fgo.yaml" ],
      "rules": [
        {
          "id": "unlimited-balances",
          "pattern": "^(?<head>[ \\t]*)(?<value>unlimited_balances:[ \\t]*False)(?<tail>[ \\t]*(?:\\r?\\n|$))",
          "operation": "replace",
          "value": "unlimited_balances: True"
        }
      ]
    }
  ]
}
```

Two things about that pattern. A `replace` rule still rebuilds the line as head + value + tail, so
the key has to sit inside the `head` capture, or inside `value` along with the text being written.
A pattern that captures the bare word as `value` and leaves the key outside the match drops the key
and writes one word where a setting was.

And the pattern matches the value the file carries now, `False`, not the one the mod wants, `True`,
because a rule that matched `True` would find nothing to do the second time it ran.

### 3. A member inside a packed archive

The master tables under `App\zh\rom\mst_data\` are `farc` archives holding one or more members. Name
the member in `farc` and the rules act on it as text; `Mods\tools\farc.py` unpacks and repacks it
under the game's own Python, which already carries the zstandard module. `docs\mods\examples\
farc-member\` halves every Craft Essence's deck cost.

```json
{
  "id": "farc-member",
  "name": "Packed archive example - cheaper Craft Essences",
  "version": "1.0.0",
  "description": "Halves the cost of every Craft Essence, so a deck an 84 limit would refuse fits inside the limit the game actually enforces. The fields are craft_essence.N.cost in member arms_mst_craft_essence.bin of App/zh/rom/mst_data/arms_mst_craft_essence.farc: the rules edit the member, and the archive is repacked around it. The whole archive is the unit that gets a baseline and gets restored, so removing the mod puts the original archive back. Not verified in game.",
  "targets": [
    {
      "files": [ "App/zh/rom/mst_data/arms_mst_craft_essence.farc" ],
      "farc": { "member": "arms_mst_craft_essence.bin" },
      "rules": [
        {
          "id": "ce-cost",
          "pattern": "^(?<head>[ \\t]*craft_essence\\.\\d+\\.cost[ \\t]*=[ \\t]*)(?<value>\\d+)(?<tail>[ \\t]*(?:\\r?\\n|$))",
          "operation": "scale",
          "factor": 0.5,
          "min": 0
        }
      ]
    }
  ]
}
```

The mod needs `Server\python\python.exe` and `Mods\tools\farc.py`, and says so plainly if either is
missing. Only the zstd-packed `FARc` containers go through the tool; the uppercase `FARC` sound
archive is a different format and the tool refuses it, which is why the bundled mute mod is a byte
patch instead.

### 4. A raw byte patch

For a file that is neither text nor a `FARc` archive - the sound archive, an executable. `offset`
is decimal and measured from the start of the file, `bytes` is hex, and `size` is the file length
the offsets were measured against. `docs\mods\examples\byte-patch\` sets two sound-cue volumes to
`'0'`.

```json
{
  "id": "byte-patch",
  "name": "Byte patch example - drop two sound cues to silence",
  "version": "1.0.0",
  "description": "Sets the volume digit of two cues in App/rom/sound/se_quest.farc to '0', which silences them without touching the audio. A patch carries the offsets and the new bytes rather than a copy of the 3 MB archive, so no game file ships with the mod. The size guard means a build the offsets were not measured against is left alone instead of being written at the wrong place.",
  "targets": [
    {
      "files": [ "App/rom/sound/se_quest.farc" ],
      "patches": [
        {
          "id": "mute-que-time-ext-01",
          "offset": 3013098,
          "bytes": "30",
          "size": 3013643
        },
        {
          "id": "mute-que-warning-01",
          "offset": 3013168,
          "bytes": "30",
          "size": 3013643
        }
      ]
    }
  ]
}
```

A patch is skipped, not failed, when the file length does not match `size`, when the offset runs past
the end of the file, or when the bytes there are already what the patch writes. So a mod whose
patches all skip leaves the file byte-identical rather than rewriting it.

`bytes` is a hex string and can be any length. `30` is the ASCII `'0'`. Use `size` on anything whose
offsets you measured by hand: without it a patch lands at that offset in a build it was never
measured against, which is the one way a mod here can corrupt a file.

The `id` on a patch is documentation, and so is the `id` on a rule: neither is read anywhere, so a
refusal names the pattern it could not use rather than the id you gave it.

### 5. A whole file: overlay

When the change cannot be written as a rule - a binary asset, or a file the mod author prepared with
another tool - the mod ships the finished file instead. `overlay` lists its install-root relative
paths, and the bytes come from `files\` inside the mod's own folder.

```
Mods\overlay\
	mod.json
	files\
		App\audio-volume.ini
```

```json
{
  "id": "overlay",
  "name": "Whole-file example - a prepared volume profile",
  "version": "1.0.0",
  "description": "Copies a prepared App/audio-volume.ini over the game's, setting background music to 80 and leaving voice and effects at 100. An overlay ships the whole file rather than rewriting a line, which is the only way to change an asset that is not text at all: the bytes come from files\\App\\audio-volume.ini inside this mod's own folder and are copied over the install's copy. The file has to already exist in the install, because an overlay copies over a file and cannot create one. The original is captured before the first write and restored on removal. Worth knowing when reading this one: the launcher's Audio page writes this same file, so a change made there appears on Verify as drift and Repair puts this copy back.",
  "overlay": [ "App/audio-volume.ini" ]
}
```

The file has to already exist in the install - an overlay cannot create one, and applying the mod
fails with a message saying so. No mod that ships with the launcher uses `overlay`; it is here for
the case a rule cannot reach, and the reason none of them needs it is that text and archive members
cover the tables they touch.

Two things to know before writing one:

- An overlay is applied after the rules for the same file, so a target and an overlay on one path
  means the overlay wins.
- The repository has `* text=auto eol=crlf` in `.gitattributes`, which rewrites line endings on
  checkout. A file whose exact bytes matter has to be marked `-text` in a `.gitattributes` beside
  it, or it will not be the file you tested. `docs\mods\examples\.gitattributes` does that for the
  examples.

## Knobs

A mod declares the numbers a player may change in `parameters`. Two things to be plain about, since
the sample folder implies otherwise:

- The code that builds the controls is generic, but the **Battle Tuner tab is pinned to the mod id
  `battle-tuner`** (`ModsView.TunerModId`). Another mod's `parameters` are parsed and stored, and no
  page shows them, so a new parameterised mod needs an edit in `ModsView.cs` before anyone can turn
  its knobs.
- That tab is disabled until the bundle is applied, because a number written while the mod is off
  would have nothing to attach to, and it says so when `battle-tuner` is not in the Mods folder at
  all.

`docs\mods\examples\parameters\` is therefore a manifest reference rather than a working tab. It is
written to be safe to run and to show the shape - a cooldown factor, a wave timer that is a mode
plus the two numbers that mode uses, a drop switch, and a loot multiplier, six knobs over two files,
every default chosen to change nothing - but those knobs only become visible if the folder is
renamed to `battle-tuner`.

A mod that declares none of this stays portable to the standalone PowerShell manager.

| Field | What it is |
| --- | --- |
| `id` | the name a rule refers to it by |
| `label` | the label on the tab |
| `help` | the sentence under the control |
| `type` | `number`, `switch` or `choice` |
| `value` | the default |
| `unit` | `number` only: `x`, `s`, `%`, drawn beside the box |
| `min`, `max`, `step` | `number` only: the range, and the slider's interval |
| `options` | `choice` only: `{ "value": ..., "label": ... }`, the first one is the default |

`switch` stores `true` or `false`. `choice` stores the chosen `value`. `number` stores text and is
clamped to `min` and `max` before use, so a hand-edited store cannot put a multiplier out of bounds.

A rule names a knob by wrapping its id in braces, in place of a literal `value`, `factor`, `amount`,
`min` or `max`:

```json
{ "id": "skill-reload", "pattern": "...", "operation": "scale", "factor": "{skillCooldown}", "min": 1 }
```

A rule may also carry `when`, a map of knob id to expected value. Every pair has to match or the rule
is skipped, which is how one mod offers several mutually exclusive shapes without them all firing:

```json
{ "id": "drops-on",  "when": { "drops": "true"  }, "operation": "scale", "factor": "{loot}" }
{ "id": "drops-off", "when": { "drops": "false" }, "operation": "set",   "value": 0 }
```

There is no rule for a mode that should change nothing - leave the rule out and that mode is the
untouched file. `when` is checked against rules only: it has no effect on `patches`.

The values chosen live in `Mods\.state\params\<id>.json`, and anything not stored falls back to the
mod's own default, so the store only ever holds what the player changed. Saving writes that store and
nothing else; applying the mod is what rewrites the game file, which is what lets the tab collect
several changes and write once.

Changing a knob re-derives the file from the baseline rather than stacking the new factor on the old
result, so raising a multiplier twice gives the second value, not the product of the two.

## Trying a mod

1. Copy the folder into `<install>\Mods\` and press Refresh. Import takes a `.zip` holding a mod
   folder instead. The count on the row is how many files the manifest's paths resolved against, so
   a zero there means the globs found nothing and the mod would do nothing.
2. Close the game and the local server. Every button refuses while any of the four platform ports
   (777, 9999, 7777, 8888) answers.
3. Tick the row and press Apply Selected, then look at the file by hand. Nothing is written until
   the original is in the baseline store, so the first apply is the safe one to experiment with.
4. Verify names any managed file that no longer matches the journal, because a platform update or a
   re-run of the English patch puts files back. Repair writes the mod's edit there again.
5. Remove Selected puts the original bytes back. Deleting the folder instead revokes the mod the
   same way on the next apply, remove or repair - the row goes and the files it wrote are put back.

Restore Originals puts every managed file back and clears the journal, which is the way out of a mod
that has gone wrong. The baseline copies are kept, so it stays repeatable.

## Things that will bite

- A rule that matches nothing is not an error. Apply reports the mod applied either way, so read the
  file before believing one took.
- A zero-length capture is skipped rather than written, so `(?<value>\d*)` will not blank a field.
  To write 0, make the pattern match a digit and use `set` with `value: 0`.
- Rules run in the order written and each sees the previous one's output. Two rules on the same
  pattern both fire unless `when` separates them.
- The targets of one mod are applied in the order of its `targets` array, and two mods applied in
  turn both rewrite from the same baseline, so the later mod's edit is what lands.
- `min` and `max` clamp but do not reject. A factor that would produce a value the game refuses
  produces the clamp instead, silently.
- The engine writes only inside the install root, never into `Mods\.state`, and rewrites a file
  atomically, so a crash mid-apply leaves either the old file or the new one.
