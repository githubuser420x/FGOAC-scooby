# compat

`amd-shim\` is the OpenGL compatibility layer for AMD and Intel graphics: fluphus's
[fgo-arcade-amd-shim](https://github.com/fluphus/fgo-arcade-amd-shim), MIT licence (the LICENSE
file beside it), taken verbatim from commit `1fbf3e4` of that repository. The game asks the driver
for NVIDIA-only extensions; `opengl32.dll` sits in `App\` in front of the system DLL, which it
forwards to as `opengl32real.dll`, and translates those calls onto what AMD and Intel drivers
provide. `amdcfg\amdOglpSettings.cfg` is the driver profile his installer writes beside it.

The launcher installs the three files fluphus's installer writes (`App\opengl32.dll`,
`App\opengl32real.dll` copied from `System32`, `App\amdcfg\amdOglpSettings.cfg`) when the AMD and
Intel compatibility layer switch on the Display page is on, and removes them when it is off. A
fresh install on a PC with no NVIDIA adapter turns it on by itself on the first run. An
`App\opengl32.dll` that his own installer placed is recognised by its hash and left alone.

His limits, as published: tested only on an RX 7900 XTX, 60 fps guaranteed only in PvP battles on
that card, 1920x1080 only, with a built-in 60 fps cap.

Installs from before 1.1.1 carry the older layer, `compat\fgoglcompat.dll`, and keep running it as
they are; the Display page offers the switch to this one and back.

| File | SHA-256 |
| --- | --- |
| `amd-shim\opengl32.dll` (458,240 bytes) | `a85042d91ea60a3108f28dfad4673265f2e1f2a402b252436e5d5757c368a0f0` |
| `amd-shim\amdcfg\amdOglpSettings.cfg` | `9cfe518d2ed064c856e0a2a8f0f8686f10ebbe63d0d5995117067bd8276066ae` |
