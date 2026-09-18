#!/usr/bin/env python3
"""Apply the two Wine-compatibility byte patches to an FGO Arcade install.

These patch the game executable (App\\ago.exe) of the Cloud23333 local platform.
They are only needed to run ago.exe under Wine; on Windows they are not applied
and the game is left untouched. Both patches are idempotent and a backup of the
original executable is kept beside it as ago.exe.wine-orig.

Usage:
    python3 apply-wine-fixes.py <install root>      # the folder holding App and Server
    python3 apply-wine-fixes.py --revert <root>

Why each patch exists
---------------------
1. WGL_CONTEXT_FLAGS_ARB = ROBUST_ACCESS (offset 0x27af7d)

   At startup the game creates a second OpenGL context that requests the robust
   access flag. Windows/NVIDIA grants it. Wine's EGL backend cannot match the
   request and returns EGL_BAD_MATCH (0x3009), so the game drops its current
   context and then resolves its 280-entry OpenGL entry-point table with no
   context current. Wine's wglGetProcAddress returns NULL for every OpenGL 1.2+
   symbol without a current context, so the loader aborts at the first such
   symbol and leaves the whole table zero. The game later calls glCreateBuffers
   through that table and dies on a NULL function pointer.

   Clearing the flag lets the context be created, so the table is resolved and
   the game boots far enough to render. This is the single change that turns the
   immediate startup crash into a running game.

2. SetWindowFeedbackSetting (offset 0x27CB8D)

   ago.exe imports USER32!SetWindowFeedbackSetting, which Wine implements as a
   stub that aborts the process. The call only provides pen/touch feedback and
   its return value is checked, so the call is replaced with "mov eax, 1"
   (success) plus a NOP.
"""

import os
import shutil
import sys

PATCHES = [
    # (name, file offset, expected bytes, replacement bytes)
    (
        "clear WGL_CONTEXT_FLAGS_ARB robust-access bit",
        0x27AF7D,
        bytes.fromhex("94200000"),
        bytes.fromhex("00000000"),
    ),
    (
        "stub USER32!SetWindowFeedbackSetting",
        0x27CB8D,
        bytes.fromhex("ff150df40301"),
        bytes.fromhex("b80100000090"),
    ),
]


def patch(path: str) -> int:
    exe = os.path.join(path, "App", "ago.exe")
    if not os.path.isfile(exe):
        print("not found: " + exe)
        return 1
    backup = exe + ".wine-orig"
    if not os.path.isfile(backup):
        shutil.copy2(exe, backup)
        print("backup written: " + backup)

    with open(exe, "rb") as handle:
        data = bytearray(handle.read())

    changed = 0
    for name, offset, expected, replacement in PATCHES:
        current = bytes(data[offset:offset + len(expected)])
        if current == replacement:
            print("already applied: " + name)
        elif current == expected:
            data[offset:offset + len(expected)] = replacement
            changed += 1
            print("applied: " + name)
        else:
            print("unexpected bytes at 0x%x for %s (found %s) - skipping"
                  % (offset, name, current.hex()))
    if changed:
        with open(exe, "wb") as handle:
            handle.write(data)
        print("wrote %d patch(es) to %s" % (changed, exe))
    return 0


def revert(path: str) -> int:
    exe = os.path.join(path, "App", "ago.exe")
    backup = exe + ".wine-orig"
    if not os.path.isfile(backup):
        print("no backup to revert from: " + backup)
        return 1
    shutil.copy2(backup, exe)
    print("restored " + exe + " from backup")
    return 0


def main() -> int:
    args = sys.argv[1:]
    if not args:
        print(__doc__)
        return 2
    if args[0] == "--revert":
        return revert(args[1]) if len(args) > 1 else 2
    return patch(args[0])


if __name__ == "__main__":
    sys.exit(main())
