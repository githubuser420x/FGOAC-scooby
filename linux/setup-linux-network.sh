#!/usr/bin/env bash
# ==============================================================================
# FGO Arcade Linux Host Network Configuration Script
# Configures privileged port binding (<1024), ptrace scope & 192.168.100.1 loopback IP
# ==============================================================================
set -euo pipefail

if [[ $EUID -ne 0 ]]; then
   echo "[ERROR] This script must be run as root (use sudo)." 
   exit 1
fi

echo "[*] Configuring Linux host network for FGO Arcade Offline Build..."

# 1. Allow unprivileged processes to bind to port 777 (and all ports >= 0)
echo "[*] Setting net.ipv4.ip_unprivileged_port_start=0..."
sysctl -w net.ipv4.ip_unprivileged_port_start=0 >/dev/null
sysctl -w kernel.yama.ptrace_scope=0 >/dev/null

SYSCTL_CONF="/etc/sysctl.d/50-fgoa-unprivileged-ports.conf"
cat <<EOF > "$SYSCTL_CONF"
# FGO Arcade Offline Build Network Permissions
net.ipv4.ip_unprivileged_port_start = 0
kernel.yama.ptrace_scope = 0
EOF
echo "[+] Saved sysctl configuration to $SYSCTL_CONF"

# 2. Add 192.168.100.1/32 to loopback interface if not already present
echo "[*] Adding 192.168.100.1/32 to lo interface..."
if ! ip addr show lo | grep -q "192.168.100.1"; then
    ip addr add 192.168.100.1/32 dev lo
    echo "[+] Added 192.168.100.1/32 to lo."
else
    echo "[+] 192.168.100.1/32 is already present on lo."
fi

# 3. Create persistent systemd service for virtual bridge IP
SERVICE_FILE="/etc/systemd/system/fgoa-vnet.service"
echo "[*] Installing $SERVICE_FILE..."
cat <<EOF > "$SERVICE_FILE"
[Unit]
Description=FGO Arcade Virtual Network Bridge (192.168.100.1/32)
After=network.target

[Service]
Type=oneshot
RemainAfterExit=yes
ExecStart=/usr/bin/ip addr replace 192.168.100.1/32 dev lo
ExecStop=/usr/bin/ip addr del 192.168.100.1/32 dev lo

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
systemctl enable --now fgoa-vnet.service
echo "[+] fgoa-vnet.service enabled and started."

echo "[✓] Linux network configuration complete! You can now start the FGO launcher."
