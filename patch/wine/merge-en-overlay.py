#!/usr/bin/env python3
"""Materialise the English overlay into App\\rom so the game reads it directly.

The English patch deliberately does not touch App\\rom. It installs the
translated resources under App\\zh\\rom and relies on the content hook
(fgozh.dll) to redirect the game's reads of App\\rom\\... to App\\zh\\rom\\...
That hook does not initialise under Wine, so the game stays in Japanese.

This script performs the redirection ahead of time: every file under
App\\zh\\rom is copied over the matching file under App\\rom, so the game reads
the translation without any hook. It is the same substitution the hook makes at
runtime; files that only exist in App\\rom are untouched, which matches the
hook's "missing resources use original files" behaviour.

Originals are kept under App\\rom.wine-orig\\<relative path> so the change can
be undone. Run again after a platform update to re-apply.

Usage:
    python3 merge-en-overlay.py <install root>     # the folder holding App
    python3 merge-en-overlay.py --revert <root>
"""

import os
import shutil
import sys


def app_dir(root: str) -> str:
    return os.path.join(root, "App")


def merge(root: str) -> int:
    app = app_dir(root)
    zh_rom = os.path.join(app, "zh", "rom")
    rom = os.path.join(app, "rom")
    backup = os.path.join(app, "rom.wine-orig")
    if not os.path.isdir(zh_rom):
        print("no English overlay at " + zh_rom)
        return 1
    if not os.path.isdir(rom):
        print("no game resources at " + rom)
        return 1

    copied = 0
    backed_up = 0
    for base, _dirs, files in os.walk(zh_rom):
        for name in files:
            src = os.path.join(base, name)
            rel = os.path.relpath(src, zh_rom)
            dst = os.path.join(rom, rel)
            keep = os.path.join(backup, rel)
            if os.path.isfile(dst) and not os.path.isfile(keep):
                os.makedirs(os.path.dirname(keep), exist_ok=True)
                shutil.copy2(dst, keep)
                backed_up += 1
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            shutil.copy2(src, dst)
            copied += 1
    print("copied %d file(s) from zh\\rom into rom; backed up %d original(s) to %s"
          % (copied, backed_up, backup))
    return 0


def revert(root: str) -> int:
    app = app_dir(root)
    backup = os.path.join(app, "rom.wine-orig")
    rom = os.path.join(app, "rom")
    if not os.path.isdir(backup):
        print("nothing to revert from: " + backup)
        return 1
    restored = 0
    for base, _dirs, files in os.walk(backup):
        for name in files:
            src = os.path.join(base, name)
            rel = os.path.relpath(src, backup)
            shutil.copy2(src, os.path.join(rom, rel))
            restored += 1
    print("restored %d original file(s)" % restored)
    return 0


def main() -> int:
    args = sys.argv[1:]
    if not args:
        print(__doc__)
        return 2
    if args[0] == "--revert":
        return revert(args[1]) if len(args) > 1 else 2
    return merge(args[0])


if __name__ == "__main__":
    sys.exit(main())
