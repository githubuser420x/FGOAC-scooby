# compat

`fgoglcompat.dll` is the OpenGL compatibility layer for AMD and Intel graphics. The game asks the
driver for NVIDIA-only extensions (`GL_NV_shader_buffer_load`, `GL_NV_gpu_shader5`,
`GL_NV_shader_thread_group`, `GL_NV_shader_atomic_float` and NVIDIA bindless textures); this
library is loaded ahead of the game hook by the author's launch script, which reserves exactly
this file name, and translates those calls onto the ARB equivalents that AMD and Intel drivers
provide. It is a community build, shipped here as received; it is not built from this repository and is not
covered by this repository's licence. We do not know who wrote it - if it is yours, open an issue and
say how you want it credited, or whether you want it removed.

The launcher copies it into `App\` when the AMD and Intel compatibility layer switch on the
Display page is on, and removes it when the switch is off. On a PC with no NVIDIA adapter the
first run turns it on by itself.

SHA-256 `4932eebf73d715949b04c74b7cf1ee9dbabcc6887f666f3524850d374156f66c`, 659,456 bytes.
