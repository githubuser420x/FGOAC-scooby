#!/usr/bin/env python3
"""
FGO Arcade Linux PowerShell Shim
Intercepts PowerShell calls from FGOAC scooby / FGOLocalPlatform and handles server lifecycle & game launching.
"""
import sys
import os
import re
import socket
import time
import subprocess
import json
import shutil
from pathlib import Path
from datetime import datetime

# Root directory of FGOA installation
SCRIPT_DIR = Path(__file__).resolve().parent
PROJECT_ROOT = SCRIPT_DIR.parent
APP_DIR = PROJECT_ROOT / "App"
SERVER_DIR = PROJECT_ROOT / "Server"
ARTEMIS_DIR = SERVER_DIR / "artemis"
LOG_DIR = PROJECT_ROOT / "logs"
STATE_DIR = SERVER_DIR / "state"
DEVICE_DIR = PROJECT_ROOT / "DEVICE"

LOG_DIR.mkdir(exist_ok=True)
STATE_DIR.mkdir(exist_ok=True)
DEVICE_DIR.mkdir(exist_ok=True)

SHIM_LOG = LOG_DIR / "server-control.log"

def log(msg: str):
    timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    formatted = f"[{timestamp}] {msg}\n"
    try:
        with open(SHIM_LOG, "a", encoding="utf-8") as f:
            f.write(formatted)
    except Exception:
        pass


def is_port_open(port: int, host: str = "127.0.0.1", timeout: float = 0.5) -> bool:
    try:
        with socket.create_connection((host, port), timeout=timeout):
            return True
    except OSError:
        return False


def wait_for_ports(ports: list[int], timeout: float = 20.0) -> bool:
    deadline = time.time() + timeout
    while time.time() < deadline:
        if all(is_port_open(p) for p in ports):
            return True
        time.sleep(0.3)
    return False


def get_server_settings():
    try:
        config_tool = SERVER_DIR / "tools" / "fgo_server_config.py"
        if config_tool.exists():
            import yaml
            core_yaml = ARTEMIS_DIR / "config" / "core.yaml"
            launcher_json = APP_DIR / "fgo-launcher.json"
            core = yaml.safe_load(core_yaml.read_text(encoding="utf-8")) if core_yaml.exists() else {}
            launcher = json.loads(launcher_json.read_text(encoding="utf-8")) if launcher_json.exists() else {}
            
            data = {
                "host": launcher.get("serverHost", "auto"),
                "address": core.get("server", {}).get("hostname", "192.168.100.1"),
                "http": int(core.get("server", {}).get("port", 777)),
                "billing": int(core.get("billing", {}).get("port", 9999)),
                "aime": int(core.get("aimedb", {}).get("port", 7777)),
                "database": int(core.get("database", {}).get("port", 8888)),
            }
            return json.dumps(data)
    except Exception as e:
        log(f"Error loading server settings: {e}")
    return '{"host": "auto", "address": "192.168.100.1", "http": 777, "billing": 9999, "aime": 7777, "database": 8888}'


def set_ini_value(text: str, section: str, key: str, value: str) -> str:
    header_pattern = re.compile(rf"(?m)^\[{re.escape(section)}\]\s*$")
    match = header_pattern.search(text)
    if not match:
        return text.rstrip() + f"\n\n[{section}]\n{key}={value}\n"

    section_start = match.end()
    next_header = re.compile(r"(?m)^\[[^\]]+\]\s*$").search(text, section_start)
    section_end = next_header.start() if next_header else len(text)
    section_body = text[section_start:section_end]

    key_pattern = re.compile(rf"(?m)^(\s*{re.escape(key)}\s*=).*$")
    if key_pattern.search(section_body):
        new_body = key_pattern.sub(rf"\g<1>{value}", section_body)
    else:
        new_body = section_body.rstrip() + f"\n{key}={value}\n"

    return text[:section_start] + new_body + text[section_end:]


def start_server():
    log("Request: Start FGO Local Server")
    
    # 1. Start MariaDB if port 8888 is not open
    if not is_port_open(8888):
        log("Starting MariaDB (port 8888)...")
        maria_exe = SERVER_DIR / "mariadb-10.11.16-winx64" / "bin" / "mariadbd.exe"
        maria_ini = SERVER_DIR / "mariadb.ini"
        maria_root = SERVER_DIR / "mariadb-10.11.16-winx64"
        maria_data = SERVER_DIR / "data" / "mariadb"
        maria_pid = STATE_DIR / "mariadb-engine.pid"
        maria_log = LOG_DIR / "mariadb.log"
        
        args = [
            str(maria_exe),
            f"--defaults-file={maria_ini}",
            f"--basedir={maria_root}",
            f"--datadir={maria_data}",
            f"--pid-file={maria_pid}",
            f"--log-error={maria_log}",
            "--console"
        ]
        
        stdout_file = open(LOG_DIR / "mariadb-stdout.log", "a", encoding="utf-8")
        stderr_file = open(LOG_DIR / "mariadb-stderr.log", "a", encoding="utf-8")
        
        try:
            proc = subprocess.Popen(
                args,
                cwd=str(SERVER_DIR),
                stdout=stdout_file,
                stderr=stderr_file,
                creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0)
            )
            (STATE_DIR / "mariadb.pid").write_text(str(proc.pid), encoding="utf-8")
            log(f"MariaDB spawned with PID {proc.pid}")
        except Exception as e:
            log(f"Failed to spawn MariaDB: {e}")

    # Wait for MariaDB to be ready
    if not wait_for_ports([8888], timeout=15.0):
        log("ERROR: MariaDB did not open port 8888 in time.")
        print("Error: MariaDB did not open port 8888.", file=sys.stderr)
        return False

    # 2. Start Artemis (HTTP :777, Billing :9999, Aimedb :7777)
    if not is_port_open(777):
        log("Starting Artemis (ports 777, 9999, 7777)...")
        python_exe = SERVER_DIR / "python" / "python.exe"
        if not python_exe.exists():
            python_exe = sys.executable

        stdout_file = open(LOG_DIR / "artemis-stdout.log", "a", encoding="utf-8")
        stderr_file = open(LOG_DIR / "artemis-stderr.log", "a", encoding="utf-8")
        
        args = [
            str(python_exe),
            "index.py",
            "--config",
            "config"
        ]
        
        try:
            proc = subprocess.Popen(
                args,
                cwd=str(ARTEMIS_DIR),
                stdout=stdout_file,
                stderr=stderr_file,
                creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0)
            )
            (STATE_DIR / "artemis.pid").write_text(str(proc.pid), encoding="utf-8")
            log(f"Artemis spawned with PID {proc.pid}")
        except Exception as e:
            log(f"Failed to spawn Artemis: {e}")

    # Wait for all server ports
    if not wait_for_ports([777, 9999, 7777], timeout=20.0):
        log("ERROR: Artemis services did not open ports 777, 9999, 7777 in time.")
        print("Error: Artemis services did not open all required ports.", file=sys.stderr)
        return False

    log("FGO local server is ready.")
    print("FGO local server is ready at 192.168.100.1 (777/9999/7777).")
    return True


def stop_server():
    log("Request: Stop FGO Local Server")
    artemis_pid_file = STATE_DIR / "artemis.pid"
    if artemis_pid_file.exists():
        try:
            pid = int(artemis_pid_file.read_text(encoding="utf-8").strip())
            if os.name == "nt":
                subprocess.run(["taskkill", "/F", "/T", "/PID", str(pid)], capture_output=True)
            else:
                subprocess.run(["kill", "-9", str(pid)], capture_output=True)
            artemis_pid_file.unlink(missing_ok=True)
            log(f"Stopped Artemis PID {pid}")
        except Exception as e:
            log(f"Error stopping Artemis PID: {e}")

    mariadb_pid_file = STATE_DIR / "mariadb.pid"
    if mariadb_pid_file.exists():
        try:
            pid = int(mariadb_pid_file.read_text(encoding="utf-8").strip())
            if os.name == "nt":
                subprocess.run(["taskkill", "/F", "/T", "/PID", str(pid)], capture_output=True)
            else:
                subprocess.run(["kill", "-TERM", str(pid)], capture_output=True)
            mariadb_pid_file.unlink(missing_ok=True)
            log(f"Stopped MariaDB PID {pid}")
        except Exception as e:
            log(f"Error stopping MariaDB PID: {e}")

    log("FGO local server stopped.")
    print("FGO local server stopped.")


def launch_game(cmd_str: str):
    log(f"Request: Launch Game ({cmd_str})")
    print("Starting/checking the local ALL.Net, billing, AimeDB services...")
    
    # 1. Ensure local servers are running
    start_server()

    # 2. Parse display / resolution arguments
    width = 1280
    height = 720
    windowed = True
    
    m_w = re.search(r"-ResolutionWidth\s+(\d+)", cmd_str, re.IGNORECASE)
    if m_w:
        width = int(m_w.group(1))
    m_h = re.search(r"-ResolutionHeight\s+(\d+)", cmd_str, re.IGNORECASE)
    if m_h:
        height = int(m_h.group(1))
    if "-DisplayMode exclusive" in cmd_str:
        windowed = False

    render_arg = "-hdtv720" if (width <= 1280 and height <= 720) else "-hdtv1080"

    # 3. Synchronize INI settings for fgohook.dll
    base_ini = APP_DIR / "segatools.ini"
    runtime_dir = DEVICE_DIR / "runtime"
    runtime_dir.mkdir(parents=True, exist_ok=True)
    runtime_ini = runtime_dir / "segatools.runtime.ini"

    ini_content = base_ini.read_text(encoding="utf-8", errors="replace") if base_ini.exists() else ""
    
    # Update resolution & surface parameters
    ini_content = set_ini_value(ini_content, "amvideo", "enable", "1")
    ini_content = set_ini_value(ini_content, "amvideo", "resolutionWidth", str(width))
    ini_content = set_ini_value(ini_content, "amvideo", "resolutionHeight", str(height))
    
    ini_content = set_ini_value(ini_content, "gfx", "enable", "1")
    ini_content = set_ini_value(ini_content, "gfx", "windowed", "1" if windowed else "0")
    ini_content = set_ini_value(ini_content, "gfx", "framed", "1" if windowed else "0")
    ini_content = set_ini_value(ini_content, "gfx", "width", str(width))
    ini_content = set_ini_value(ini_content, "gfx", "height", str(height))
    ini_content = set_ini_value(ini_content, "gfx", "logicalWidth", str(width))
    ini_content = set_ini_value(ini_content, "gfx", "logicalHeight", str(height))
    ini_content = set_ini_value(ini_content, "gfx", "preserveAspect", "1")

    ini_content = set_ini_value(ini_content, "dns", "default", "192.168.100.1")
    ini_content = set_ini_value(ini_content, "dns", "startupPort", "777")
    ini_content = set_ini_value(ini_content, "dns", "billingPort", "9999")
    ini_content = set_ini_value(ini_content, "dns", "aimedbPort", "7777")

    # Write both UTF-8 base INI and UTF-16 runtime INI
    try:
        base_ini.write_text(ini_content, encoding="utf-8")
        runtime_ini.write_text(ini_content, encoding="utf-16")
    except Exception as e:
        log(f"Error saving updated segatools ini: {e}")

    # 4. Prepare injection arguments
    inject_exe = str((APP_DIR / "inject.exe").resolve())
    
    # Ensure zh translation files exist
    if (APP_DIR / "zh" / "fgozh.dll").exists() and not (APP_DIR / "fgozh.dll").exists():
        try:
            shutil.copy2(APP_DIR / "zh" / "fgozh.dll", APP_DIR / "fgozh.dll")
        except Exception:
            pass

    args = [inject_exe, "-d", "-k", "fgohook.dll"]
    if (APP_DIR / "fgozh.dll").exists():
        args.extend(["-k", "fgozh.dll"])
    
    args.append("ago.exe")
    args.append(render_arg)
    if windowed:
        args.append("-w")

    env = os.environ.copy()
    env["__NV_PRIME_RENDER_OFFLOAD"] = "1"
    env["__GLX_VENDOR_LIBRARY_NAME"] = "nvidia"
    env["__VK_LAYER_NV_optimus"] = "NVIDIA_only"
    env["DXVK_FILTER_DEVICE_NAME"] = "GeForce"
    env["SEGATOOLS_CONFIG_PATH"] = str(runtime_ini)
    env["FGO_ZH_ENABLED"] = "1"
    env["FGO_TARGET_FPS"] = "60"
    env["FGO_FULL_SURFACE_FBO"] = "1"
    env["FGO_SMAA"] = "0"
    env["FGO_RENDER_SCALE"] = "100"
    env["FGO_SHADOW_RESOLUTION"] = "1024"

    log_file_path = LOG_DIR / "fgo-last-launch.log"
    log(f"Spawning inject in {APP_DIR}: {' '.join(args)}")
    print(f"Virtual LAN: 192.168.100.1 (local bridge=True)")
    print(f"Server     : 192.168.100.1")
    print(f"Display    : {'windowed' if windowed else 'fullscreen'} {width}x{height}")
    print(f"[inject] starting; stdout and stderr are live.")

    def run_proc(current_args):
        with open(log_file_path, "w", encoding="utf-8", errors="replace") as lf:
            proc = subprocess.Popen(
                current_args,
                cwd=str(APP_DIR),
                env=env,
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                text=True,
                bufsize=1
            )
            output_lines = []
            for line in proc.stdout:
                sys.stdout.write(line)
                sys.stdout.flush()
                lf.write(line)
                lf.flush()
                output_lines.append(line)
            
            proc.wait()
            return proc.returncode, "".join(output_lines)

    try:
        rc, output = run_proc(args)
        if rc != 0 and "fgozh.dll: DLL failed to load" in output:
            log("Warning: fgozh.dll failed to load, falling back to fgohook only...")
            fallback_args = [inject_exe, "-d", "-k", "fgohook.dll", "ago.exe", render_arg]
            if windowed:
                fallback_args.append("-w")
            rc, output = run_proc(fallback_args)
            
        log(f"Game process exited with code {rc}")
        sys.exit(rc)
    except Exception as e:
        log(f"Failed to launch game: {e}")
        print(f"Failed to launch game: {e}", file=sys.stderr)
        sys.exit(1)


def handle_get_net_tcp_connection(cmd: str):
    m = re.search(r"-LocalPort\s+(\d+)", cmd, re.IGNORECASE)
    port = int(m.group(1)) if m else 0
    if port > 0 and is_port_open(port):
        print(f"LocalAddress LocalPort RemoteAddress RemotePort State       OwningProcess")
        print(f"------------ --------- ------------- ---------- -----       -------------")
        print(f"0.0.0.0      {port:<9} 0.0.0.0       0          Listen      1000         ")


def main():
    raw_args = sys.argv[1:]
    cmd_str = " ".join(raw_args)
    log(f"PowerShell Shim invoked: {cmd_str}")

    # Check for runtime version query
    if "PSVersionTable" in cmd_str:
        sys.exit(0)

    # 1. Start Server
    if "Start-FGOLocalServer" in cmd_str and "FGO_Launcher.ps1" not in cmd_str:
        start_server()
        sys.exit(0)

    # 2. Stop Server
    if "Stop-FGOLocalServer" in cmd_str:
        stop_server()
        sys.exit(0)

    # 3. Server Settings
    if "ServerSettings.ps1" in cmd_str or "Get-FgoServerSettings" in cmd_str:
        print(get_server_settings())
        sys.exit(0)

    # 4. Get-NetTCPConnection
    if "Get-NetTCPConnection" in cmd_str:
        handle_get_net_tcp_connection(cmd_str)
        sys.exit(0)

    # 5. Launch Game
    if "FGO_Launcher.ps1" in cmd_str:
        launch_game(cmd_str)
        sys.exit(0)

    # 6. Patch & Environment / Startup checks
    if "Apply-EN-Patch.ps1" in cmd_str or "FGO_EnvironmentCheck" in cmd_str or "FGO_StartupChecks" in cmd_str or "Test-FgoWritableLayout" in cmd_str:
        if "-AsJson" in cmd_str:
            print("[]")
        sys.exit(0)

    log(f"Unhandled command (ignoring safely): {cmd_str}")
    sys.exit(0)


if __name__ == "__main__":
    main()
