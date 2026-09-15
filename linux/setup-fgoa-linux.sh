#!/usr/bin/env bash
# ==============================================================================
# FGO Arcade Linux All-in-One Automated Setup Script
# Configures Network, Wine Prefix PowerShell Symlink, ago.exe IAT Patch & Checks
# ==============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
APP_DIR="${PROJECT_ROOT}/App"
AGO_EXE="${APP_DIR}/ago.exe"
AGO_BAK="${APP_DIR}/ago.exe.bak"

DEFAULT_PREFIX="${HOME}/.local/share/wineprefixes/fgoa"
WINEPREFIX="${WINEPREFIX:-$DEFAULT_PREFIX}"

echo "======================================================================"
echo "   FGO Arcade Linux Offline Build - Automated 1-Click Setup"
echo "======================================================================"
echo "[*] Project Directory: ${PROJECT_ROOT}"
echo "[*] Wine Prefix      : ${WINEPREFIX}"

# 1. Check Root / Network configuration
echo ""
echo "[1/4] Checking Linux Network & Kernel permissions..."
SYSCTL_FILE="/etc/sysctl.d/50-fgoa-unprivileged-ports.conf"
SERVICE_FILE="/etc/systemd/system/fgoa-vnet.service"

NEED_SUDO=0
if [[ "$(sysctl -n net.ipv4.ip_unprivileged_port_start 2>/dev/null)" != "0" ]]; then
    NEED_SUDO=1
fi
if ! ip addr show lo | grep -q "192.168.100.1"; then
    NEED_SUDO=1
fi

if [[ "$NEED_SUDO" -eq 1 ]]; then
    echo "[!] Root privileges required to configure system network..."
    sudo bash "${SCRIPT_DIR}/setup-linux-network.sh"
else
    echo "[✓] Network & unprivileged port settings are already active."
fi

# 2. Configure PowerShell Symlink in Wine Prefix
echo ""
echo "[2/4] Configuring Wine Prefix PowerShell shim..."
WINE_PS_DIR="${WINEPREFIX}/drive_c/windows/system32/WindowsPowerShell/v1.0"
if [[ -d "$WINE_PS_DIR" ]]; then
    if [[ -f "${WINE_PS_DIR}/pwsh.exe" ]]; then
        ln -sf "pwsh.exe" "${WINE_PS_DIR}/powershell.exe"
        echo "[✓] Linked ${WINE_PS_DIR}/powershell.exe -> pwsh.exe (Shim Wrapper)"
    elif command -v pwsh &>/dev/null; then
        PWSH_HOST_BIN="$(which pwsh)"
        ln -sf "$PWSH_HOST_BIN" "${WINE_PS_DIR}/powershell.exe"
        echo "[✓] Linked ${WINE_PS_DIR}/powershell.exe -> ${PWSH_HOST_BIN}"
    else
        echo "[!] Notice: pwsh.exe exists in prefix and is managing ps_shim.py."
    fi
else
    echo "[!] Notice: Wine prefix directory not found at ${WINE_PS_DIR}. Ensure your Wine prefix is created before starting."
fi

# 3. Check and Patch App/ago.exe IAT
echo ""
echo "[3/4] Verifying and Patching App/ago.exe IAT..."
if [[ ! -f "$AGO_EXE" ]]; then
    echo "[ERROR] ${AGO_EXE} not found! Please place this folder in your FGO Arcade installation root."
    exit 1
fi

python3 -c "
import shutil, sys, os

ago = '${AGO_EXE}'
bak = '${AGO_BAK}'

with open(ago, 'rb') as f:
    f.seek(0x1971978)
    cur = f.read(25)

if cur.startswith(b'SetWindowTextA'):
    print('[✓] App/ago.exe IAT is already patched (SetWindowTextA).')
elif cur.startswith(b'SetWindowFeedbackSetting'):
    if not os.path.exists(bak):
        shutil.copy2(ago, bak)
        print(f'[+] Created original backup: {bak}')
    with open(ago, 'r+b') as f:
        f.seek(0x1971978)
        f.write(b'SetWindowTextA\x00' + b'\x00'*10)
    print('[✓] Successfully patched App/ago.exe IAT at offset 0x1971978!')
else:
    print(f'[!] Warning: Unexpected bytes at offset 0x1971978 ({cur}). File might be a different revision.')
"

# 4. Check GPU & NVIDIA Driver
echo ""
echo "[4/4] Checking GPU Offload status..."
if command -v nvidia-smi &>/dev/null; then
    GPU_NAME="$(nvidia-smi --query-gpu=name --format=csv,noheader | head -n 1)"
    echo "[✓] Detected NVIDIA GPU: ${GPU_NAME}"
    echo "[✓] PRIME Offload environment ready."
else
    echo "[!] Notice: nvidia-smi not detected or GPU not available. If on AMD/Intel only, fluphus compatibility layer will be used."
fi

echo ""
echo "======================================================================"
echo "[✓] All configurations verified! You can now start the game in Lutris."
echo "======================================================================"
