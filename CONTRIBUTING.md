# Contributing

Thanks for looking. This is a small fan project, so the bar is simple: the change has to be
explainable in one sentence, and it has to still build.

## Build it

You need the .NET SDK (10.x is what this is developed on; it builds the `net6.0-windows` target and
restores the 6.0 reference and runtime packs from nuget.org on first run) and Windows 10 or 11 x64.

```
build.cmd      compile check only
publish.cmd    self-contained single file, written to dist\FGOAC scooby.exe
deploy.cmd D:\path\to\FGOA   copy the published exe into an install
```

Expect around 165 `CS8632` warnings and zero errors. Those warnings come from the decompiled source
and are not worth silencing one file at a time.

To try a build against a real install, publish and then run `deploy.cmd` with your install root.
The launcher needs administrator rights because the game does.

## Where things live

| Path | Contents |
| --- | --- |
| `src\` | the launcher: C# and XAML, one WPF project |
| `overlay\` | English replacements for files that live outside the assembly, laid out by their path relative to the install root |
| `patch\` | the installer players run, and the manifest builder it checks against |
| `docs\` | player guide, developer notes, design, release steps, project history |
| `package\` | the README and changelog that ship inside the release zip |

`docs\DEVELOPING.md` says where `src\` comes from, the compile fixes the decompile needs and what
the translation must never change. Read it before your first change to `src\`.

`docs\DESIGN.md` is the plan the interface is built from. If you are changing how something looks,
read it first: it says what each colour and each component is for, and a change that contradicts it
should say so out loud.

`docs\HISTORY.md` records what was done and why, newest last.

## Report a problem

Open an issue with the bug report template. The two things that make a report usable are the screen
you were on and the log files; the template lists which ones.

## House style

- **Plain English.** Short sentences. An error message says what failed, then what to do about it.
- **ASCII only** in source, scripts, commit messages and documentation. The one place non-ASCII text
  is expected is Japanese card and quest names in data files that are deliberately left untranslated.
- **No attribution lines** of any kind in commits, code comments or documentation - no trailers, no
  tool credits, no signatures. Commit messages are factual and short, present tense, and say what
  the change does rather than what you did.
- **One logical change per commit.** A rename and a behaviour change are two commits.
- **Match the file you are in.** The C# is decompiler output with the translation applied on top;
  it uses tabs and it is not going to be reformatted. `.editorconfig` has the rest.
- **Do not change** anything listed under "Things the translation must not change" in
  `docs\DEVELOPING.md` without saying why: XAML `Tag` values, combo box item order where the index
  is persisted, the `1920x1080` caption form, the JSON keys of the card and craft-essence tables,
  and the config keys shared with the PowerShell scripts.

## Translation changes

Card names follow the English release: the FGO Arcade wiki and Atlas Academy. Craft Essence effect
text follows FGO NA phrasing, and every number in it has to match the Japanese original - the
arcade's values differ from the mobile game's, so NA sentences cannot be copied wholesale.
