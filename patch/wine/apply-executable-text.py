#!/usr/bin/env python3
"""Apply the English in-executable text to App\\ago.exe without the hook.

The English patch ships App\\zh\\executable-text.json, a list of 2774 entries of
the form

    { "id": "ago.exe||19695408", "原文": "...", "译文": "..." }

where the id carries the exact file offset of the original string inside
ago.exe. At runtime the content hook (fgozh.dll) applies these replacements in
memory. That hook does not initialise under Wine, so this script applies the
same replacements to the file instead. Every replacement is shorter than or the
same length as the original and is NUL-padded, so no offset in the image moves.

Run after patch\\wine\\apply-wine-fixes.py (that script makes the one-time
pristine backup, ago.exe.wine-orig). Re-run after a platform update.

Usage:
    python3 apply-executable-text.py <install root>
"""

import json
import os
import sys


def main() -> int:
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    include_empty = "--include-empty" in sys.argv[1:]
    if not args:
        print(__doc__)
        return 2
    app = os.path.join(args[0], "App")
    exe = os.path.join(app, "ago.exe")
    table = os.path.join(app, "zh", "executable-text.json")
    if not os.path.isfile(exe):
        print("not found: " + exe)
        return 1
    if not os.path.isfile(table):
        print("not found: " + table)
        return 1

    entries = json.load(open(table, encoding="utf-8-sig"))
    data = bytearray(open(exe, "rb").read())

    applied = already = skipped = 0
    for entry in entries:
        ident = entry.get("id", "")
        if "||" not in ident:
            continue
        name, _, offset = ident.rpartition("||")
        if not name.lower().endswith("ago.exe"):
            continue
        try:
            offset = int(offset)
        except ValueError:
            continue
        source = entry.get("原文", "").encode("utf-8")
        target = entry.get("译文", "").encode("utf-8")
        if not target and not include_empty:
            # 550 entries in the shipped table translate to the empty string.
            # They are log/debug text that never reaches the player. Blanking
            # them in the image makes the game spin during startup under Wine,
            # so they are left as they are unless --include-empty is given.
            skipped += 1
            continue
        if len(target) > len(source):
            skipped += 1
            continue
        current = bytes(data[offset:offset + len(source)])
        if current == target + b"\x00" * (len(source) - len(target)):
            already += 1
            continue
        if current != source:
            skipped += 1
            continue
        data[offset:offset + len(source)] = target + b"\x00" * (len(source) - len(target))
        applied += 1

    if applied:
        with open(exe, "wb") as handle:
            handle.write(data)
    print("applied %d string(s); %d already English; %d skipped" % (applied, already, skipped))
    return 0


if __name__ == "__main__":
    sys.exit(main())
