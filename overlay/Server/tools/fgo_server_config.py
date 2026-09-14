"""Local server configuration; no player or database content is rewritten."""
import argparse
import ipaddress
import json
from pathlib import Path
import socket
import sys
import yaml

ROOT = Path(__file__).resolve().parents[2]


def load(root=ROOT):
    core = yaml.safe_load((root / 'Server/artemis/config/core.yaml').read_text(encoding='utf-8'))
    launcher = json.loads((root / 'App/fgo-launcher.json').read_text(encoding='utf-8'))
    return dict(host=launcher.get('serverHost', 'auto'), address=core['server']['hostname'],
                http=int(core['server']['port']), billing=int(core['billing']['port']),
                aime=int(core['aimedb']['port']), database=int(core['database']['port']))


def validate(values):
    ports = [values[k] for k in ('http', 'billing', 'aime', 'database')]
    if any(type(p) is not int or not 1 <= p <= 65535 for p in ports):
        raise ValueError('Ports must be whole numbers between 1 and 65535.')
    if len(set(ports)) != len(ports):
        raise ValueError('The four services cannot share a port.')
    defaults = {'http':80, 'billing':8443, 'aime':22345}
    for key, original in defaults.items():
        if values[key] in set(defaults.values()) - {original}:
            raise ValueError('The game, billing and Aime ports cannot be swapped with each other, or the game port mapping breaks.')
    if values['host'] not in ('auto', 'local', 'localhost', '127.0.0.1'):
        address = ipaddress.IPv4Address(values['host'])
        if address.is_loopback or address.is_unspecified or address.is_multicast:
            raise ValueError('Use auto for a local offline server, or an IPv4 address for a remote one.')


def open_port(port):
    try:
        with socket.create_connection(('127.0.0.1', port), timeout=.2):
            return True
    except OSError:
        return False


def set_ini(text, section, key, value):
    import re
    pattern = re.compile(r'(?ms)(^\[' + re.escape(section) + r'\][^\n]*\n)(.*?)(?=^\[|\Z)')
    match = pattern.search(text)
    if not match:
        return text.rstrip() + f'\n\n[{section}]\n{key}={value}\n'
    body = match[2]
    entry = re.compile(r'(?m)^' + re.escape(key) + r'\s*=.*$')
    body = entry.sub(f'{key}={value}', body) if entry.search(body) else body.rstrip() + f'\n{key}={value}\n\n'
    return text[:match.start(2)] + body + text[match.end(2):]


def apply(values, root=ROOT, check_running=True):
    validate(values)
    current = load(root)
    if check_running:
        if any(open_port(current[k]) for k in ('http', 'billing', 'aime', 'database')):
            raise ValueError('Stop the local server before saving port settings.')
        for key in ('http', 'billing', 'aime', 'database'):
            if open_port(values[key]):
                raise ValueError(f"Port {values[key]} is already in use by another program.")
    core_path = root / 'Server/artemis/config/core.yaml'
    launcher_path = root / 'App/fgo-launcher.json'
    ini_path = root / 'App/segatools.ini'
    db_path = root / 'Server/mariadb.ini'
    core = yaml.safe_load(core_path.read_text(encoding='utf-8'))
    for section, key in (('server','http'), ('allnet','http'), ('billing','billing'), ('aimedb','aime'), ('database','database')):
        core[section]['port'] = values[key]
    core['server']['hostname'] = ('192.168.100.1' if values['host'] in ('auto','local','localhost','127.0.0.1') else values['host'])
    launcher = json.loads(launcher_path.read_text(encoding='utf-8'))
    launcher['serverHost'] = values['host']
    launcher['serverPorts'] = {k: values[k] for k in ('http','billing','aime','database')}
    launcher['requiredPorts'] = [values[k] for k in ('http','billing','aime')]
    ini = ini_path.read_text(encoding='utf-8')
    for setting, key in (('startupPort','http'), ('billingPort','billing'), ('aimedbPort','aime')):
        ini = set_ini(ini, 'dns', setting, values[key])
    db = db_path.read_text(encoding='utf-8')
    db = set_ini(set_ini(db, 'mariadbd', 'port', values['database']), 'client', 'port', values['database'])
    changes = {core_path: yaml.safe_dump(core, allow_unicode=True, sort_keys=False),
               launcher_path: json.dumps(launcher, ensure_ascii=False, indent=2)+'\n', ini_path: ini, db_path: db}
    originals = {p: p.read_bytes() for p in changes}
    try:
        for p, text in changes.items():
            temporary = p.with_name(p.name + '.config-tmp')
            temporary.write_text(text, encoding='utf-8')
            temporary.replace(p)
    except Exception:
        for p, data in originals.items():
            p.write_bytes(data)
        raise
    finally:
        for p in changes:
            p.with_name(p.name + '.config-tmp').unlink(missing_ok=True)
    return load(root)


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    parser = argparse.ArgumentParser()
    parser.add_argument('command', choices=('show','validate','apply'))
    parser.add_argument('--host', default='auto')
    for key, default in (('http',80),('billing',8443),('aime',22345),('database',3307)):
        parser.add_argument('--'+key, type=int, default=default)
    args = parser.parse_args()
    try:
        values = {k:getattr(args,k) for k in ('host','http','billing','aime','database')}
        if args.command == 'validate':
            validate(values)
            result = values
        else:
            result = load() if args.command == 'show' else apply(values)
        print(json.dumps(result, ensure_ascii=False))
    except Exception as exc:
        print(json.dumps({'error':str(exc)}, ensure_ascii=False))
        sys.exit(1)
