#!/usr/bin/env python3
"""FGO Arcade (SDEJ 11.00) local account management CLI.

Subcommands:
  list    List local player profiles from state/fgo-players.json.
  create  Insert aime_user/aime_card rows and register a fresh profile.
  use     Point DEVICE/aime.txt at an account's access code.
  delete  Remove one profile and its Aime database identity transactionally.
  repair-card-codes
          Replace legacy card IDs that AMDaemon mistakes for Banapass cards.
  reset   Rebuild an account as normal/original (empty servant inventory,
          zeroed currencies/materials).
  catalog List grantable currencies and installed synthesis materials.
  grant   Atomically add selected benefits to one local account.

All subcommands accept --json: a single JSON object is written to stdout
(UTF-8, ensure_ascii=False) while human-readable output goes to stderr.

Run with the server virtualenv interpreter:
  G:\\FGO\\Server\\venv\\Scripts\\python.exe fgo_account.py list --json
"""

import argparse
from copy import deepcopy
import json
import os
from pathlib import Path
import re
import secrets
import shutil
import socket
import subprocess
import sys
import time
from tempfile import TemporaryDirectory
from datetime import datetime

TOOLS_DIR = Path(__file__).resolve().parent
# The packaged Python uses an isolated ._pth and omits the script directory.
if str(TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIR))
SERVER_DIR = TOOLS_DIR.parent
ARTEMIS_DIR = SERVER_DIR / "artemis"
PROFILES_PATH = SERVER_DIR / "state" / "fgo-players.json"
CORE_CONFIG_PATH = ARTEMIS_DIR / "config" / "core.yaml"
AIME_TXT_PATH = SERVER_DIR.parent / "DEVICE" / "aime.txt"
CARD_MANIFEST_PATH = (
    SERVER_DIR.parent
    / "DEVICE"
    / "print"
    / "FGO11_AllServants"
    / "library-manifest.json"
)
MASTER_LEVEL_PATH = SERVER_DIR.parent / "App" / "rom" / "multi" / "reward_master_level.txt"
CAPTURE_REQUESTS_PATH = SERVER_DIR.parent / "logs" / "fgo_capture" / "requests.jsonl"
MARIADB_ROOT = SERVER_DIR / "mariadb-10.11.16-winx64"
MARIADB_DAEMON = MARIADB_ROOT / "bin" / "mariadbd.exe"
MARIADB_INI = SERVER_DIR / "mariadb.ini"
SERVER_PROBE_HOST = "127.0.0.1"
SERVER_PROBE_PORT = int(json.loads((SERVER_DIR.parent / 'App' / 'fgo-launcher.json').read_text(encoding='utf-8')).get('serverPorts', {}).get('http', 80))

SERVER_RUNNING_MESSAGE = (
    "Stop the local server (Stop Server in the launcher, or Stop-FGOLocalServer.ps1), then try again"
)


def configure_stdio() -> None:
    for stream in (sys.stdout, sys.stderr):
        reconfigure = getattr(stream, "reconfigure", None)
        if callable(reconfigure):
            reconfigure(encoding="utf-8")


def emit(result: dict, use_json: bool, human_lines=None) -> int:
    if use_json:
        sys.stdout.write(json.dumps(result, ensure_ascii=False) + "\n")
        sys.stdout.flush()
    else:
        lines = human_lines
        if lines is None:
            lines = [json.dumps(result, ensure_ascii=False, indent=2)]
        for line in lines:
            print(line, file=sys.stderr)
    return 0 if result.get("ok") else 1


def server_running() -> bool:
    try:
        with socket.create_connection(
            (SERVER_PROBE_HOST, SERVER_PROBE_PORT), timeout=1.0
        ):
            return True
    except OSError:
        return False


def load_profiles_file() -> dict:
    try:
        with open(PROFILES_PATH, "r", encoding="utf-8") as profiles_file:
            data = json.load(profiles_file)
        if isinstance(data, dict):
            return data
    except (OSError, ValueError):
        pass
    return {}


def read_current_access_code() -> str:
    try:
        return AIME_TXT_PATH.read_text(encoding="utf-8").strip()
    except OSError:
        return ""


def write_current_access_code(access_code: str) -> None:
    """Atomically write the exact 10-byte BCD card ID format Segatools reads.

    ``Path.write_text`` performs universal-newline translation on Windows.  If
    an existing CRLF suffix is preserved and passed back to it, ``\r\n`` is
    silently expanded to ``\r\r\n``.  Keep this file byte-exact instead: 20
    decimal digits followed by one CRLF, or an empty file when no account is
    selected.
    """

    normalized = str(access_code).strip()
    if normalized and not is_scannable_access_code(normalized):
        raise ValueError(
            "An Aime access_code must be 20 decimal digits and must not start with 3"
        )

    payload = (normalized + "\r\n").encode("ascii") if normalized else b""
    AIME_TXT_PATH.parent.mkdir(parents=True, exist_ok=True)
    temporary_path = AIME_TXT_PATH.with_name(
        f".{AIME_TXT_PATH.name}.{os.getpid()}.tmp"
    )
    try:
        temporary_path.write_bytes(payload)
        os.replace(temporary_path, AIME_TXT_PATH)
    finally:
        try:
            temporary_path.unlink()
        except FileNotFoundError:
            pass


def smallest_available_positive_id(used_ids) -> int:
    """Return the smallest positive integer not present in *used_ids*."""

    used = {int(value) for value in used_ids if int(value) > 0}
    candidate = 1
    while candidate in used:
        candidate += 1
    return candidate


def is_scannable_access_code(access_code: str) -> bool:
    """Return whether Segatools/AMDaemon will treat the code as an AiMe card.

    The virtual reader parses the 20 characters as ten packed-BCD bytes.
    AMDaemon reserves a high nibble of ``3`` for Banapass-style cards; such a
    value can be read successfully by the reader but is rejected before any
    AimeDB ``lookup_ex`` request is sent.
    """

    normalized = str(access_code).strip()
    return bool(re.fullmatch(r"\d{20}", normalized)) and normalized[0] != "3"


def active_profile_aime_ids(profiles: dict) -> set:
    result = set()
    for key, profile in profiles.items():
        if not isinstance(profile, dict):
            continue
        aime_id = _profile_int(profile, "aime_id", 0)
        if aime_id <= 0 and isinstance(key, str) and key.startswith("aime:"):
            try:
                aime_id = int(key.split(":", 1)[1])
            except (TypeError, ValueError):
                aime_id = 0
        if aime_id > 0:
            result.add(aime_id)
    return result


def purge_inactive_account_backup_remnants(
    active_aime_ids: set, active_access_codes: set
) -> dict:
    """Remove deleted identities from account/card switch backups.

    Backups remain useful for surviving accounts, but a confirmed permanent
    deletion must not leave a restorable copy of the deleted profile or card
    number behind.  Profile backups are rewritten without inactive Aime rows;
    card-file backups that no longer belong to an active account are removed.
    """

    active_ids = {int(value) for value in active_aime_ids if int(value) > 0}
    active_codes = {
        str(value).strip()
        for value in active_access_codes
        if re.fullmatch(r"\d{20}", str(value).strip()) is not None
    }
    profiles_scrubbed = 0
    profile_rows_removed = 0
    card_backups_removed = 0
    errors = []

    for path in PROFILES_PATH.parent.glob(f"{PROFILES_PATH.name}.bak-*"):
        try:
            payload = json.loads(path.read_text(encoding="utf-8"))
            if not isinstance(payload, dict):
                continue
            removed = []
            for key, profile in list(payload.items()):
                if not isinstance(profile, dict):
                    continue
                aime_id = _profile_int(profile, "aime_id", 0)
                if aime_id <= 0 and isinstance(key, str) and key.startswith("aime:"):
                    try:
                        aime_id = int(key.split(":", 1)[1])
                    except (TypeError, ValueError):
                        aime_id = 0
                if aime_id > 0 and aime_id not in active_ids:
                    removed.append(key)
            if not removed:
                continue
            for key in removed:
                del payload[key]
            temporary_path = path.with_name(f".{path.name}.{os.getpid()}.tmp")
            temporary_path.write_text(
                json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
                encoding="utf-8",
                newline="\n",
            )
            os.replace(temporary_path, path)
            profiles_scrubbed += 1
            profile_rows_removed += len(removed)
        except (OSError, ValueError, TypeError) as exc:
            errors.append(f"{path}: {exc}")

    for path in AIME_TXT_PATH.parent.glob(f"{AIME_TXT_PATH.name}.bak-*"):
        try:
            code = path.read_text(encoding="utf-8").strip()
            if re.fullmatch(r"\d{20}", code) is None or code in active_codes:
                continue
            path.unlink()
            card_backups_removed += 1
        except OSError as exc:
            errors.append(f"{path}: {exc}")

    return {
        "profiles_scrubbed": profiles_scrubbed,
        "profile_rows_removed": profile_rows_removed,
        "card_backups_removed": card_backups_removed,
        "errors": errors,
    }


def backup_file(path: Path) -> str:
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    backup_path = path.with_name(f"{path.name}.bak-{stamp}")
    shutil.copy2(path, backup_path)
    return str(backup_path)


def _profile_int(profile: dict, field: str, default: int = 0) -> int:
    try:
        return int(profile.get(field, default))
    except (TypeError, ValueError):
        return default


def _is_completed_print_confirmation(row: dict) -> bool:
    # The gameplay ledger can auto-confirm a summon before its physical card
    # is printed. Do not expose those digital acquisitions as printed copies.
    if not isinstance(row, dict):
        return False
    if "physical_report_prn_cmplt" in row:
        complete = _profile_int(row, "physical_report_prn_cmplt", 0) > 0
    elif row.get("virtual_confirmation"):
        complete = bool(row.get("printed_at"))
    else:
        complete = _profile_int(row, "prn_cmplt", 0) > 0
    return (
        _profile_int(row, "purchase_status", 0) == 2
        and complete
    )


def pending_print_count(profile: dict) -> int:
    """Count unique drawn/accepted cards still awaiting a completed print."""

    pending_keys = set()
    pending = profile.get("pending_summon_results", {})
    if isinstance(pending, dict):
        pending_keys.update(
            str(key) for key, row in pending.items() if isinstance(row, dict)
        )
    confirmations = profile.get("tc_confirmations", {})
    if isinstance(confirmations, dict):
        pending_keys.update(
            str(key)
            for key, row in confirmations.items()
            if isinstance(row, dict)
            and not _is_completed_print_confirmation(row)
        )
    return len(pending_keys)


def find_profile_by_aime_id(profiles: dict, aime_id: int):
    key = f"aime:{aime_id}"
    profile = profiles.get(key)
    if isinstance(profile, dict):
        return key, profile
    for candidate_key, candidate in profiles.items():
        if not isinstance(candidate, dict):
            continue
        if _profile_int(candidate, "aime_id", 0) == aime_id:
            return candidate_key, candidate
    return None, None


def load_card_manifest_by_id() -> dict:
    try:
        manifest = json.loads(CARD_MANIFEST_PATH.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError):
        manifest = {}
    result = {}
    for card in manifest.get("Cards", []):
        if not isinstance(card, dict):
            continue
        try:
            result[int(card["TradingCardId"])] = card
        except (KeyError, TypeError, ValueError):
            continue
    master_path = SERVER_DIR / 'data/fgo-master/trc/arms_mst_trc.bin'
    master_rows = {}
    for line in master_path.read_text(encoding='utf-8-sig').splitlines():
        match = re.match(r'^trc\.(\d+)\.([^.]+)=(.*)$', line)
        if match:
            master_rows.setdefault(match[1], {})[match[2]] = match[3]
    for row in master_rows.values():
        card_id = _profile_int(row, 'trading_card_id', 0)
        kind = _profile_int(row, 'trc_type_id', 0)
        entity_id = _profile_int(row, 'trc_type_unique_id', 0)
        if card_id > 0 and kind in (1, 2) and entity_id > 0 and card_id not in result:
            result[card_id] = {
                'TradingCardId': card_id, 'CardTypeId': kind,
                'ServantId': entity_id if kind == 1 else 0,
                'CraftEssenceId': entity_id if kind == 2 else 0,
                'DisplayName': row.get('disp_name', f'Trading Card {card_id}'),
                'ArtworkId': _profile_int(row, 'artwork_id', 0),
                'HoloTypeId': _profile_int(row, 'holo_type_id', 0),
            }
    return result


def load_master_level_requirements() -> list:
    """Load per-level EXP costs from the exact table used by the cabinet."""

    try:
        lines = MASTER_LEVEL_PATH.read_text(
            encoding="utf-8-sig", errors="replace"
        ).splitlines()
    except OSError:
        return []
    requirements = {}
    in_table = False
    for line in lines:
        stripped = line.split("#", 1)[0].strip()
        if stripped == "master_level :":
            in_table = True
            continue
        if not in_table:
            continue
        if stripped == "End :":
            break
        match = re.fullmatch(r"(\d+)\s*,\s*(\d+)", stripped)
        if match is not None:
            requirements[int(match.group(1))] = int(match.group(2))
    return [requirements[level] for level in sorted(requirements)]


def master_progress(total_exp: int, requirements: list) -> dict:
    total_exp = max(0, int(total_exp))
    remaining = total_exp
    level = 1
    for required in requirements:
        required = max(1, int(required))
        if remaining < required:
            return {
                "master_level": level,
                "master_exp": total_exp,
                "master_level_exp": remaining,
                "master_next_level_exp": required,
                "master_exp_to_next": required - remaining,
            }
        remaining -= required
        level += 1
    return {
        "master_level": max(1, level),
        "master_exp": total_exp,
        "master_level_exp": remaining,
        "master_next_level_exp": 0,
        "master_exp_to_next": 0,
    }


def quest_progress_counts(profile: dict) -> tuple[int, int]:
    quest_state = profile.get("quest_progress_by_singularity", {})
    if not isinstance(quest_state, dict):
        return 0, 0
    total = 0
    cleared = 0
    seen = set()
    for rows in quest_state.values():
        if not isinstance(rows, list):
            continue
        for row in rows:
            if not isinstance(row, dict):
                continue
            singularity_id = _profile_int(row, "singularity_id", 0)
            quest_id = _profile_int(row, "quest_id", 0)
            identity = (singularity_id, quest_id)
            if quest_id <= 0 or identity in seen:
                continue
            seen.add(identity)
            total += 1
            if (
                bool(row.get("is_get_first_clear_reward", False))
                or _profile_int(row, "best_result", 0) > 0
                or _profile_int(row, "quest_clear_for_mission", 0) > 0
            ):
                cleared += 1
    return cleared, total


def summon_history(profile: dict, cards_by_id: dict, limit: int = 100) -> list:
    confirmations = profile.get("tc_confirmations", {})
    if not isinstance(confirmations, dict):
        return []
    rows = []
    for key, confirmation in confirmations.items():
        if not isinstance(confirmation, dict):
            continue
        trading_card_id = _profile_int(confirmation, "tc_id", 0)
        if trading_card_id <= 0 or not _is_completed_print_confirmation(
            confirmation
        ):
            continue
        card = cards_by_id.get(trading_card_id, {})
        rows.append(
            {
                "key": str(key),
                "sid": str(confirmation.get("sid", "")),
                "tc_id": trading_card_id,
                "servant_id": _profile_int(card, "ServantId", 0),
                "craft_essence_id": _profile_int(
                    card, "CraftEssenceId", 0
                ),
                "card_type_id": _profile_int(card, "CardTypeId", 1),
                "display_name": str(
                    card.get("DisplayName", f"Trading Card {trading_card_id}")
                ),
                "artwork_id": _profile_int(card, "ArtworkId", 0),
                "holo_type_id": _profile_int(card, "HoloTypeId", 0),
                "lineup_id": _profile_int(confirmation, "lineup_id", 0),
                "tc_lottery_type": _profile_int(
                    confirmation, "tc_lottery_type", 0
                ),
                "summon_batch_id": str(
                    confirmation.get("summon_batch_id", "")
                ),
                "draw_index": _profile_int(confirmation, "draw_index", 1),
                "draw_count": _profile_int(confirmation, "draw_count", 1),
                "confirmed_at": str(confirmation.get("confirmed_at", "")),
            }
        )
    rows.sort(
        key=lambda row: (row["confirmed_at"], row["key"]), reverse=True
    )
    return rows[: max(0, int(limit))]


def owned_card_counts(profile: dict) -> dict:
    """Return confirmed/business card ownership using server ledger priority."""

    counts = {}
    confirmations = profile.get("tc_confirmations", {})
    legacy = profile.get("legacy_confirmed_summon_card_counts", {})
    business = profile.get("business_owned_card_counts", {})
    fields = []
    if not (
        isinstance(confirmations, dict)
        and confirmations
        and isinstance(legacy, dict)
        and isinstance(business, dict)
        and legacy == business
    ):
        fields.append("legacy_confirmed_summon_card_counts")
    fields.append("business_owned_card_counts")
    for field in fields:
        stored = profile.get(field, {})
        if not isinstance(stored, dict):
            continue
        for raw_card_id, raw_count in stored.items():
            try:
                card_id = int(raw_card_id)
                count = max(0, int(raw_count))
            except (TypeError, ValueError):
                continue
            if card_id > 0 and count > 0:
                counts[card_id] = counts.get(card_id, 0) + count

    if isinstance(confirmations, dict):
        for confirmation in confirmations.values():
            if not isinstance(confirmation, dict):
                continue
            if not _is_completed_print_confirmation(confirmation):
                continue
            card_id = _profile_int(confirmation, "tc_id", 0)
            if card_id > 0:
                counts[card_id] = counts.get(card_id, 0) + 1
    has_authoritative_ledger = (
        isinstance(confirmations, dict) and bool(confirmations)
    )
    if counts or has_authoritative_ledger:
        return counts

    legacy = profile.get("summoned_card_counts", {})
    if isinstance(legacy, dict):
        for raw_card_id, raw_count in legacy.items():
            try:
                card_id = int(raw_card_id)
                count = max(0, int(raw_count))
            except (TypeError, ValueError):
                continue
            if card_id > 0 and count > 0:
                counts[card_id] = count
    if counts:
        return counts

    for raw_card_id in profile.get("summoned_cards", []):
        try:
            card_id = int(raw_card_id)
        except (TypeError, ValueError):
            continue
        if card_id > 0:
            counts[card_id] = counts.get(card_id, 0) + 1
    return counts


def owned_card_catalog(profile: dict, cards_by_id: dict) -> list:
    result = []
    for trading_card_id, count in sorted(owned_card_counts(profile).items()):
        card = cards_by_id.get(trading_card_id, {})
        card_type_id = _profile_int(card, "CardTypeId", 1)
        result.append(
            {
                "tc_id": trading_card_id,
                "count": count,
                "card_type_id": card_type_id,
                "servant_id": _profile_int(card, "ServantId", 0),
                "craft_essence_id": _profile_int(
                    card, "CraftEssenceId", 0
                ),
                "display_name": str(
                    card.get("DisplayName", f"Trading Card {trading_card_id}")
                ),
                "file_name": str(card.get("FileName", "")),
            }
        )
    return result


def account_entry(
    aime_id: int,
    profile: dict,
    current_code: str,
    cards_by_id: dict = None,
    master_requirements: list = None,
) -> dict:
    servant_state = profile.get("svt_state", {})
    servant_count = len(servant_state) if isinstance(servant_state, dict) else 0
    access_code = str(profile.get("auth_access_code", ""))
    confirmations = profile.get("tc_confirmations", {})
    result_count = (
        sum(
            1
            for row in confirmations.values()
            if isinstance(row, dict)
            and _is_completed_print_confirmation(row)
        )
        if isinstance(confirmations, dict)
        else 0
    )
    progress = master_progress(
        _profile_int(profile, "mstr_exp", 0), master_requirements or []
    )
    cleared_quests, tracked_quests = quest_progress_counts(profile)
    owned_cards = owned_card_catalog(profile, cards_by_id or {})
    return {
        "aime_id": int(aime_id),
        "pd_id": _profile_int(profile, "pd_id", 0),
        "master_name": str(profile.get("master_name", "")),
        "account_mode": str(profile.get("account_mode", "normal")),
        "resource_preset": str(profile.get("resource_preset", "original")),
        "material_preset": str(profile.get("material_preset", "original")),
        "servant_count": servant_count,
        "access_code": access_code,
        "is_current": bool(access_code) and access_code == current_code,
        "summon_result_count": result_count,
        "pending_print_count": pending_print_count(profile),
        "summon_history": summon_history(profile, cards_by_id or {}),
        "owned_card_count": len(owned_cards),
        "owned_card_copy_count": sum(row["count"] for row in owned_cards),
        "owned_cards": owned_cards,
        **progress,
        "qp_amnt": _profile_int(profile, "qp_amnt", 0),
        "fp_amnt": _profile_int(profile, "fp_amnt", 0),
        "mana_prism_amnt": _profile_int(profile, "mana_prism_amnt", 0),
        "summon_point_amnt": _profile_int(profile, "summon_point_amnt", 0),
        "cleared_quest_count": cleared_quests,
        "tracked_quest_count": tracked_quests,
        "last_battle": profile.get("last_cleared_single_battle", {}),
    }


def merge_captured_quest_progress(profile: dict, result_row: dict) -> int:
    """Merge quest rows echoed by a previously captured battle result."""

    incoming_rows = result_row.get("quest_progress_list", [])
    if not isinstance(incoming_rows, list):
        return 0
    state = profile.get("quest_progress_by_singularity", {})
    if not isinstance(state, dict):
        state = {}
        profile["quest_progress_by_singularity"] = state
    merged_count = 0
    for incoming in incoming_rows:
        if not isinstance(incoming, dict):
            continue
        singularity_id = _profile_int(incoming, "singularity_id", 0)
        quest_id = _profile_int(incoming, "quest_id", 0)
        if quest_id <= 0:
            continue
        key = str(singularity_id)
        rows = state.get(key, [])
        if not isinstance(rows, list):
            rows = []
            state[key] = rows
        existing = next(
            (
                row
                for row in rows
                if isinstance(row, dict)
                and _profile_int(row, "quest_id", 0) == quest_id
            ),
            None,
        )
        if existing is None:
            rows.append(deepcopy(incoming))
            merged_count += 1
            continue
        previous_clear_time = _profile_int(existing, "best_clear_time", 0)
        incoming_clear_time = _profile_int(incoming, "best_clear_time", 0)
        if incoming_clear_time <= 0:
            incoming_clear_time = _profile_int(incoming, "clear_time", 0)
        existing.update(deepcopy(incoming))
        positive_times = [
            value
            for value in (previous_clear_time, incoming_clear_time)
            if value > 0
        ]
        existing["best_clear_time"] = min(positive_times) if positive_times else 0
        merged_count += 1
    return merged_count


def replay_captured_battle_settlements(servlet, profile: dict) -> dict:
    """Recover monotonic progress without resurrecting spent balances."""

    pd_id = _profile_int(profile, "pd_id", 0)
    candidates = []
    previous_totals = {}
    captures = 0
    quest_rows = 0
    sync_indexes = []
    try:
        capture_file = CAPTURE_REQUESTS_PATH.open(encoding="utf-8")
    except OSError:
        return {
            "captures": 0,
            "quest_rows": 0,
            "sync_indexes": [],
            "spendable_rows_skipped": 0,
        }
    with capture_file:
        for line in capture_file:
            if "single_battle_result" not in line:
                continue
            try:
                record = json.loads(line)
            except ValueError:
                continue
            decoded = record.get("decoded", {})
            if not isinstance(decoded, dict):
                continue
            header = decoded.get("header", {})
            request_body = decoded.get("body", {})
            if (
                not isinstance(header, dict)
                or header.get("cmd") != "single_battle_result"
                or not isinstance(request_body, dict)
                or _profile_int(request_body, "pd_id", 0) != pd_id
                or request_body.get("_local_probe_no_persist", 0)
            ):
                continue
            result_row = servlet._single_battle_result_row(request_body)
            if not result_row:
                continue

            # Captures can span an explicit account reset while retaining the
            # same local pd_id.  A single QP/material decrease is ordinary
            # spending, not a reset.  Use master EXP (strictly monotonic during
            # play) or a coordinated collapse of at least three independent
            # totals as the reset boundary.
            common = result_row.get("cmn_btl_rslt", {})
            if not isinstance(common, dict):
                common = {}
            totals = {}
            if "mstr_exp" in common:
                totals["mstr_exp"] = max(
                    0, _profile_int(common, "mstr_exp", 0)
                )
            for change_field in ("qp_amnt_chngst", "fp_amnt_chngst"):
                change = common.get(change_field, {})
                if isinstance(change, dict) and "crrnt" in change:
                    totals[change_field] = max(
                        0, _profile_int(change, "crrnt", 0)
                    )
            for material_field in (
                "exp_mtrl_chngst_lst",
                "cmn_mtrl_chngst_lst",
                "ascnsn_mtrl_chngst_lst",
                "skl_mtrl_chngst_lst",
                "mtrl_ticket_chngst_lst",
                "svt_ticket_chngst_lst",
                "tlf_item_chngst_lst",
            ):
                rows = common.get(material_field, [])
                if not isinstance(rows, list):
                    continue
                for row in rows:
                    if not isinstance(row, dict) or "crrnt" not in row:
                        continue
                    item_id = _profile_int(row, "id", 0)
                    if item_id > 0:
                        totals[f"{material_field}:{item_id}"] = max(
                            0, _profile_int(row, "crrnt", 0)
                        )
            decreased_keys = {
                key
                for key, value in totals.items()
                if key in previous_totals and value < previous_totals[key]
            }
            if "mstr_exp" in decreased_keys or len(decreased_keys) >= 3:
                candidates.clear()
                previous_totals.clear()
            previous_totals.update(totals)
            candidates.append((record, request_body, result_row))

    spendable_fields = (
        "qp_amnt_chngst",
        "fp_amnt_chngst",
        "exp_mtrl_chngst_lst",
        "cmn_mtrl_chngst_lst",
        "ascnsn_mtrl_chngst_lst",
        "skl_mtrl_chngst_lst",
        "mtrl_ticket_chngst_lst",
        "svt_ticket_chngst_lst",
        "tlf_item_chngst_lst",
    )
    spendable_rows_skipped = 0
    for record, request_body, result_row in candidates:
        # Captured result values are authoritative only at the instant of the
        # battle.  QP, FP, materials and tickets may legitimately have been
        # spent afterwards, so max-merging them during a later repair duplicates
        # value.  Replay only master/servant/quest progression; live settlement
        # still consumes the complete unmodified result in the title server.
        safe_result = deepcopy(result_row)
        common = safe_result.get("cmn_btl_rslt", {})
        if not isinstance(common, dict):
            common = {}
            safe_result["cmn_btl_rslt"] = common
        for field in spendable_fields:
            value = common.pop(field, None)
            if isinstance(value, list):
                spendable_rows_skipped += len(value)
            elif value is not None:
                spendable_rows_skipped += 1
        settlement = servlet._apply_single_battle_common_result(
            profile, safe_result
        )
        quest_rows += merge_captured_quest_progress(profile, result_row)
        sync_index = _profile_int(result_row, "btl_rslt_cntnr_idx", 0)
        if sync_index > 0:
            sync_indexes.append(sync_index)
        captures += 1
        if (
            _profile_int(result_row, "is_success", 1) > 0
            and _profile_int(result_row, "is_withdraw", 0) == 0
        ):
            previous_battle = profile.get("last_cleared_single_battle", {})
            if not isinstance(previous_battle, dict):
                previous_battle = {}
            profile["last_cleared_single_battle"] = {
                "singularity_id": _profile_int(
                    request_body, "current_singularity_id", 0
                ),
                "quest_id": _profile_int(result_row, "quest_id", 0),
                "session_id": str(previous_battle.get("session_id", "")),
                "duplicate": False,
                "reward": deepcopy(previous_battle.get("reward", {})),
                "settlement": settlement,
                "cleared_at": str(record.get("received_at", "")),
            }
    if captures:
        profile["singularity_progress_list"] = (
            servlet._singularity_progress_for_profile(profile)
        )
        repairs = profile.get("account_repair_history", [])
        if not isinstance(repairs, list):
            repairs = []
        repairs.append(
            {
                "kind": "captured_battle_settlement",
                "captures": captures,
                "quest_rows": quest_rows,
                "spendable_rows_skipped": spendable_rows_skipped,
                "sync_indexes": sorted(set(sync_indexes)),
                "repaired_at": datetime.now().astimezone().isoformat(
                    timespec="seconds"
                ),
            }
        )
        profile["account_repair_history"] = repairs[-20:]
    return {
        "captures": captures,
        "quest_rows": quest_rows,
        "sync_indexes": sorted(set(sync_indexes)),
        "spendable_rows_skipped": spendable_rows_skipped,
    }


def build_servlet(isolated_profile_path=None):
    """Construct an offline FgoServlet, optionally isolated before startup."""

    os.chdir(ARTEMIS_DIR)
    if str(ARTEMIS_DIR) not in sys.path:
        sys.path.insert(0, str(ARTEMIS_DIR))
    import yaml

    from core.config import CoreConfig
    from titles.fgo.index import FgoServlet

    cfg = CoreConfig()
    with open(CORE_CONFIG_PATH, "r", encoding="utf-8") as config_file:
        cfg.update(yaml.safe_load(config_file))
    config_dir = ARTEMIS_DIR / "config"
    if isolated_profile_path is not None:
        isolated_path = Path(isolated_profile_path).resolve()
        isolated_path.parent.mkdir(parents=True, exist_ok=True)
        with open(
            config_dir / "fgo.yaml", "r", encoding="utf-8"
        ) as config_file:
            isolated_config = yaml.safe_load(config_file) or {}
        isolated_server = isolated_config.setdefault("server", {})
        isolated_server["profile_path"] = str(isolated_path)
        isolated_server["capture_dir"] = str(
            ARTEMIS_DIR / "logs" / "fgo_capture"
        )
        with open(
            isolated_path.parent / "fgo.yaml", "w", encoding="utf-8"
        ) as config_file:
            yaml.safe_dump(isolated_config, config_file, sort_keys=False)
        config_dir = isolated_path.parent
    return FgoServlet(cfg, str(config_dir))


def connect_aime_db():
    """Connect to the Aime MariaDB using config/core.yaml (not hard-coded).

    Stopping the local server also stops MariaDB, while account operations
    only need the title server (port 80) to be offline.  Start the bundled
    daemon on demand so create/use/reset work right after "Stop Server";
    Start-FGOLocalServer.ps1 already reuses a live MariaDB, so the daemon
    is intentionally left running.
    """

    import pymysql
    import yaml

    with open(CORE_CONFIG_PATH, "r", encoding="utf-8") as config_file:
        core_cfg = yaml.safe_load(config_file)
    database = core_cfg.get("database", {})
    port = int(database.get("port", 3306))
    options = dict(
        host=str(database.get("host", "127.0.0.1")),
        port=port,
        user=str(database.get("username", "aime")),
        password=str(database.get("password", "")),
        database=str(database.get("name", "aime")),
        charset="utf8mb4",
        autocommit=False,
    )
    try:
        return pymysql.connect(**options)
    except pymysql.err.OperationalError:
        ensure_mariadb_running(port)
        return pymysql.connect(**options)


def _tcp_port_open(port: int) -> bool:
    try:
        with socket.create_connection((SERVER_PROBE_HOST, port), timeout=1.0):
            return True
    except OSError:
        return False


def ensure_mariadb_running(port: int) -> None:
    """Start the bundled MariaDB if its port is closed; wait until ready."""

    if _tcp_port_open(port):
        return
    if not (MARIADB_DAEMON.exists() and MARIADB_INI.exists()):
        return
    subprocess.Popen(
        [str(MARIADB_DAEMON), f"--defaults-file={MARIADB_INI}",
         f"--basedir={MARIADB_ROOT}", f"--datadir={SERVER_DIR / 'data' / 'mariadb'}",
         f"--pid-file={SERVER_DIR / 'state' / 'mariadb-engine.pid'}",
         f"--log-error={SERVER_DIR.parent / 'logs' / 'mariadb.log'}", "--console"],
        cwd=str(SERVER_DIR),
        creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )
    deadline = time.monotonic() + 20
    while time.monotonic() < deadline and not _tcp_port_open(port):
        time.sleep(0.25)


def generate_access_code(taken_codes: set) -> str:
    """Return a fresh 20-digit AiMe code (never a Banapass-prefixed code)."""

    while True:
        code = secrets.choice("012456789") + "".join(
            str(secrets.randbelow(10)) for _ in range(19)
        )
        if code not in taken_codes:
            return code


def pick_unique_value(cursor, table: str, column: str, base: str, max_len: int) -> str:
    base = base[:max_len].strip() or "master"
    candidate = base
    suffix = 2
    while True:
        cursor.execute(
            f"SELECT 1 FROM {table} WHERE {column}=%s LIMIT 1", (candidate,)
        )
        if cursor.fetchone() is None:
            return candidate
        tail = f"_{suffix}"
        candidate = f"{base[: max_len - len(tail)]}{tail}"
        suffix += 1


def collect_profile_access_codes(profiles: dict) -> set:
    codes = set()
    for profile in profiles.values():
        if not isinstance(profile, dict):
            continue
        code = str(profile.get("auth_access_code", "")).strip()
        if code:
            codes.add(code)
    return codes


def resolve_access_code(aime_id: int, profile: dict) -> str:
    """Profile auth_access_code first, then the aime_card row as fallback."""

    code = str(profile.get("auth_access_code", "")).strip()
    if code:
        return code
    try:
        connection = connect_aime_db()
    except Exception:
        return ""
    try:
        with connection.cursor() as cursor:
            cursor.execute(
                "SELECT access_code FROM aime_card WHERE user=%s"
                " ORDER BY id LIMIT 1",
                (int(aime_id),),
            )
            row = cursor.fetchone()
            if row and row[0]:
                return str(row[0]).strip()
    finally:
        connection.close()
    return ""


def command_list(args) -> dict:
    profiles = load_profiles_file()
    current_code = read_current_access_code()
    cards_by_id = load_card_manifest_by_id()
    master_requirements = load_master_level_requirements()
    accounts = []
    for key, profile in profiles.items():
        if not isinstance(profile, dict):
            continue
        if isinstance(key, str) and key.startswith("aime:"):
            try:
                aime_id = int(key.split(":", 1)[1])
            except (TypeError, ValueError):
                aime_id = _profile_int(profile, "aime_id", 0)
        else:
            continue
        accounts.append(
            account_entry(
                aime_id,
                profile,
                current_code,
                cards_by_id,
                master_requirements,
            )
        )
    accounts.sort(key=lambda entry: entry["aime_id"])
    return {
        "ok": True,
        "server_running": server_running(),
        "current_access_code": current_code,
        "accounts": accounts,
    }


def command_create(args) -> dict:
    if server_running() and not args.force:
        return {"ok": False, "error": "server_running", "message": SERVER_RUNNING_MESSAGE}

    name = str(args.name).strip()
    if not name:
        return {"ok": False, "error": "invalid_name", "message": "--name cannot be empty"}

    try:
        connection = connect_aime_db()
    except Exception as exc:
        return {
            "ok": False,
            "error": "db_unavailable",
            "message": f"Cannot connect to the Aime database: {exc}",
        }

    backup_name = ""
    profile = None
    try:
        profiles = load_profiles_file()
        taken_codes = collect_profile_access_codes(profiles)
        with connection.cursor() as cursor:
            cursor.execute("SELECT id FROM aime_user ORDER BY id FOR UPDATE")
            database_aime_ids = {int(row[0]) for row in cursor.fetchall()}
            used_aime_ids = database_aime_ids | active_profile_aime_ids(profiles)
            aime_id = smallest_available_positive_id(used_aime_ids)

            cursor.execute("SELECT access_code FROM aime_card")
            database_access_codes = set()
            for (code,) in cursor.fetchall():
                if code:
                    normalized_code = str(code).strip()
                    taken_codes.add(normalized_code)
                    database_access_codes.add(normalized_code)
            # Remove remnants of accounts that were deleted before ID reuse was
            # introduced.  In particular, an old aime:<id> backup must never be
            # mistaken for the newly-created account that reuses that ID.
            purge_inactive_account_backup_remnants(
                active_profile_aime_ids(profiles),
                collect_profile_access_codes(profiles) | database_access_codes,
            )
            username = pick_unique_value(cursor, "aime_user", "username", name, 25)
            email_base = f"{username}@fgo.local"
            email = pick_unique_value(
                cursor, "aime_user", "email", email_base, 255
            )
            cursor.execute(
                "INSERT INTO aime_user (id, username, email, password, permissions)"
                " VALUES (%s, %s, %s, %s, %s)",
                (aime_id, username, email, "", 1),
            )
            access_code = generate_access_code(taken_codes)
            cursor.execute(
                "INSERT INTO aime_card (user, access_code) VALUES (%s, %s)",
                (aime_id, access_code),
            )
        # Keep the SQL transaction open until the profile has been written.
        # Otherwise a profile-generation failure leaves an aime_card that the
        # front end lists as an account but the title server cannot load.
        backup_name = backup_file(PROFILES_PATH)
        servlet = build_servlet()
        profile = servlet._register_profile(
            {
                "aime_id": aime_id,
                "access_code": access_code,
                "master_name": name,
                "mst_gender_type": 1,
                "account_mode": args.mode,
                "resource_preset": "original",
                "material_preset": "original",
            }
        )
        connection.commit()
    except Exception as exc:
        connection.rollback()
        restore_error = ""
        if backup_name:
            try:
                shutil.copy2(backup_name, PROFILES_PATH)
            except OSError as restore_exc:
                restore_error = f"; restoring the account file failed: {restore_exc}"
        return {
            "ok": False,
            "error": "create_failed",
            "message": f"Account creation was rolled back: {exc}{restore_error}",
        }
    finally:
        connection.close()

    return {
        "ok": True,
        "aime_id": aime_id,
        "access_code": access_code,
        "account": account_entry(aime_id, profile, read_current_access_code()),
    }


def command_use(args) -> dict:
    profiles = load_profiles_file()
    _key, profile = find_profile_by_aime_id(profiles, args.aime_id)
    if profile is None:
        return {
            "ok": False,
            "error": "account_not_found",
            "message": f"No account found with aime_id={args.aime_id}",
        }
    access_code = resolve_access_code(args.aime_id, profile)
    if not access_code:
        return {
            "ok": False,
            "error": "access_code_missing",
            "message": (
                f"Account aime_id={args.aime_id} has no access_code in its profile or in the aime_card table"
            ),
        }
    if not is_scannable_access_code(access_code):
        # Old profile backups can reintroduce a card number generated before
        # the Banapass-prefix rule was understood.  Selecting such an account
        # should repair all three stores, not crash in the card-file writer.
        repair = command_repair_card_codes(argparse.Namespace(force=False))
        if not repair.get("ok"):
            return {
                "ok": False,
                "error": "legacy_access_code_repair_failed",
                "message": repair.get(
                    "message", "Automatic repair of the legacy Aime card number failed"
                ),
            }
        profiles = load_profiles_file()
        _key, profile = find_profile_by_aime_id(profiles, args.aime_id)
        access_code = (
            resolve_access_code(args.aime_id, profile)
            if profile is not None
            else ""
        )
        if not is_scannable_access_code(access_code):
            return {
                "ok": False,
                "error": "legacy_access_code_not_repaired",
                "message": (
                    f"The legacy card number for account aime_id={args.aime_id} is still not scannable"
                ),
            }

    backup_name = ""
    if AIME_TXT_PATH.exists():
        backup_name = backup_file(AIME_TXT_PATH)
    write_current_access_code(access_code)
    return {
        "ok": True,
        "aime_id": int(args.aime_id),
        "access_code": access_code,
        "backup": backup_name,
    }


def purge_player_prints(aime_id: int) -> int:
    """Remove only this account's print identifiers, never the shared artwork."""
    if int(aime_id) <= 0:
        raise ValueError("Invalid account id for print cleanup")
    root = AIME_TXT_PATH.parent / 'print' / 'players'
    target = root / f'aime-{int(aime_id)}'
    if not target.exists():
        return 0
    if target.is_symlink() or target.resolve().parent != root.resolve():
        raise ValueError("Unsafe player print directory")
    entries = list(target.rglob('*'))
    if any(p.is_symlink() or bool(p.stat().st_file_attributes & 0x400) for p in [target, *entries]):
        raise ValueError("Linked entry in player print directory")
    count = sum(p.is_file() for p in entries)
    shutil.rmtree(target)
    return count


def command_delete(args) -> dict:
    """Delete one account across profile, Aime DB and current-card stores."""

    if server_running() and not args.force:
        return {
            "ok": False,
            "error": "server_running",
            "message": SERVER_RUNNING_MESSAGE,
        }
    if not args.yes:
        return {
            "ok": False,
            "error": "confirmation_required",
            "message": "Deleting an account requires --yes",
        }

    profiles = load_profiles_file()
    profile_key, profile = find_profile_by_aime_id(profiles, args.aime_id)
    if profile is None or profile_key is None:
        return {
            "ok": False,
            "error": "account_not_found",
            "message": f"No account found with aime_id={args.aime_id}",
        }

    aime_id = _profile_int(profile, "aime_id", args.aime_id)
    master_name = str(profile.get("master_name", "MASTER"))
    access_code = resolve_access_code(aime_id, profile)
    current_access_code = read_current_access_code()
    deleting_current = bool(
        access_code and current_access_code == access_code
    )
    remaining_accounts = []
    for key, candidate in profiles.items():
        if key == profile_key or not isinstance(candidate, dict):
            continue
        candidate_aime_id = _profile_int(candidate, "aime_id", 0)
        if candidate_aime_id > 0:
            remaining_accounts.append((candidate_aime_id, candidate))
    remaining_accounts.sort(key=lambda item: item[0])

    replacement_aime_id = 0
    replacement_access_code = ""
    if deleting_current and remaining_accounts:
        replacement_aime_id, replacement_profile = remaining_accounts[0]
        replacement_access_code = resolve_access_code(
            replacement_aime_id, replacement_profile
        )
        if not replacement_access_code:
            return {
                "ok": False,
                "error": "replacement_access_code_missing",
                "message": (
                    f"Cannot read the card number of the fallback account aime_id={replacement_aime_id}, "
                    "so the deletion was cancelled"
                ),
            }

    try:
        connection = connect_aime_db()
    except Exception as exc:
        return {
            "ok": False,
            "error": "db_unavailable",
            "message": f"Cannot connect to the Aime database: {exc}",
        }

    profile_backup = ""
    aime_backup = ""
    remaining_database_codes = set()
    try:
        profile_backup = backup_file(PROFILES_PATH)
        if deleting_current and AIME_TXT_PATH.exists():
            aime_backup = backup_file(AIME_TXT_PATH)

        servlet = build_servlet()
        stored_key, stored_profile = find_profile_by_aime_id(
            servlet._profiles, aime_id
        )
        if stored_profile is None or stored_key is None:
            raise RuntimeError("The account profile changed before the delete transaction started")
        del servlet._profiles[stored_key]
        servlet._save_profiles()

        with connection.cursor() as cursor:
            cursor.execute("DELETE FROM aime_card WHERE user=%s", (aime_id,))
            cursor.execute("DELETE FROM aime_user WHERE id=%s", (aime_id,))
            if cursor.rowcount != 1:
                raise RuntimeError("The Aime identity is missing or changed during the deletion")

            cursor.execute("SELECT COUNT(*) FROM aime_card WHERE user=%s", (aime_id,))
            if int(cursor.fetchone()[0]) != 0:
                raise RuntimeError("The Aime card mappings were not fully deleted")
            cursor.execute("SELECT COUNT(*) FROM aime_user WHERE id=%s", (aime_id,))
            if int(cursor.fetchone()[0]) != 0:
                raise RuntimeError("The Aime identity was not fully deleted")
            cursor.execute("SELECT access_code FROM aime_card")
            remaining_database_codes = {
                str(row[0]).strip() for row in cursor.fetchall() if row[0]
            }

        if deleting_current:
            next_code = replacement_access_code
            write_current_access_code(next_code)
        connection.commit()
    except Exception as exc:
        connection.rollback()
        restore_errors = []
        for backup, destination, label in (
            (profile_backup, PROFILES_PATH, "the account profile"),
            (aime_backup, AIME_TXT_PATH, "the current card number"),
        ):
            if not backup:
                continue
            try:
                shutil.copy2(backup, destination)
            except OSError as restore_exc:
                restore_errors.append(f"restoring {label} failed: {restore_exc}")
        suffix = "; " + "; ".join(restore_errors) if restore_errors else ""
        return {
            "ok": False,
            "error": "delete_failed",
            "message": f"Account deletion was rolled back: {exc}{suffix}",
        }
    finally:
        connection.close()

    removed_print_files = purge_player_prints(aime_id)
    remaining_profiles = load_profiles_file()
    remaining_codes = (
        collect_profile_access_codes(remaining_profiles) | remaining_database_codes
    )
    cleanup = purge_inactive_account_backup_remnants(
        active_profile_aime_ids(remaining_profiles), remaining_codes
    )

    return {
        "ok": True,
        "aime_id": aime_id,
        "master_name": master_name,
        "deleted_current": deleting_current,
        "replacement_aime_id": replacement_aime_id,
        "replacement_access_code": replacement_access_code,
        "profile_backup": profile_backup,
        "aime_backup": aime_backup,
        "backup_cleanup": cleanup,
        "removed_print_files": removed_print_files,
    }


def command_repair_card_codes(args) -> dict:
    """Reconcile profile, Aime DB and active-reader card numbers atomically.

    Early local-account builds allowed a generated 20-digit code to start with
    ``3``.  Segatools can read that packed-BCD value, but AMDaemon treats the
    prefix as a Banapass card and drops it before AimeDB ``lookup_ex``.  Repair
    every legacy database row and then ensure every local profile names a card
    that really maps back to that profile's ``aime_user`` row.
    """

    if server_running() and not args.force:
        return {
            "ok": False,
            "error": "server_running",
            "message": SERVER_RUNNING_MESSAGE,
        }

    try:
        connection = connect_aime_db()
    except Exception as exc:
        return {
            "ok": False,
            "error": "db_unavailable",
            "message": f"Cannot connect to the Aime database: {exc}",
        }

    profile_backup = ""
    aime_backup = ""
    repaired_cards = []
    repaired_profiles = []
    inserted_cards = []
    current_before = read_current_access_code()
    current_after = current_before
    try:
        servlet = build_servlet()
        profiles = servlet._profiles

        with connection.cursor() as cursor:
            cursor.execute("SELECT id FROM aime_user ORDER BY id FOR UPDATE")
            valid_users = {int(row[0]) for row in cursor.fetchall()}
            cursor.execute(
                "SELECT id, user, access_code FROM aime_card"
                " ORDER BY id FOR UPDATE"
            )
            database_cards = [
                {
                    "id": int(row[0]),
                    "user": int(row[1]),
                    "code": str(row[2]).strip() if row[2] else "",
                }
                for row in cursor.fetchall()
            ]

            taken_codes = {
                card["code"] for card in database_cards if card["code"]
            }
            taken_codes.update(collect_profile_access_codes(profiles))
            replacement_by_user_and_code = {}

            # First make every existing AimeDB mapping acceptable to AMDaemon.
            for card in database_cards:
                if is_scannable_access_code(card["code"]):
                    continue
                old_code = card["code"]
                new_code = generate_access_code(taken_codes)
                taken_codes.add(new_code)
                cursor.execute(
                    "UPDATE aime_card SET access_code=%s"
                    " WHERE id=%s AND user=%s",
                    (new_code, card["id"], card["user"]),
                )
                if cursor.rowcount != 1:
                    raise RuntimeError(
                        f"aime_card id={card['id']} changed during the repair"
                    )
                card["code"] = new_code
                replacement_by_user_and_code[(card["user"], old_code)] = (
                    new_code
                )
                repaired_cards.append(
                    {
                        "aime_id": card["user"],
                        "card_id": card["id"],
                        "old_access_code": old_code,
                        "new_access_code": new_code,
                    }
                )

            cards_by_user = {}
            code_owner = {}
            for card in database_cards:
                cards_by_user.setdefault(card["user"], []).append(card)
                code_owner[card["code"]] = card["user"]

            # Profiles are the title-server identity source.  Their card must
            # both be scannable and belong to the same Aime user in the DB.
            profile_targets = []
            for profile_key, profile in profiles.items():
                if not isinstance(profile, dict):
                    continue
                aime_id = _profile_int(profile, "aime_id", 0)
                if aime_id <= 0 and isinstance(profile_key, str):
                    if profile_key.startswith("aime:"):
                        try:
                            aime_id = int(profile_key.split(":", 1)[1])
                        except (TypeError, ValueError):
                            aime_id = 0
                if aime_id <= 0:
                    continue
                if aime_id not in valid_users:
                    raise RuntimeError(
                        f"Account profile aime_id={aime_id} has no matching aime_user identity"
                    )

                old_code = str(profile.get("auth_access_code", "")).strip()
                user_cards = cards_by_user.setdefault(aime_id, [])
                user_codes = {card["code"] for card in user_cards}
                target_code = replacement_by_user_and_code.get(
                    (aime_id, old_code), ""
                )
                if not target_code and is_scannable_access_code(old_code):
                    if old_code in user_codes:
                        target_code = old_code
                    elif old_code not in code_owner:
                        cursor.execute(
                            "INSERT INTO aime_card (user, access_code)"
                            " VALUES (%s, %s)",
                            (aime_id, old_code),
                        )
                        new_card = {
                            "id": int(cursor.lastrowid),
                            "user": aime_id,
                            "code": old_code,
                        }
                        user_cards.append(new_card)
                        code_owner[old_code] = aime_id
                        target_code = old_code
                        inserted_cards.append(
                            {
                                "aime_id": aime_id,
                                "card_id": new_card["id"],
                                "access_code": old_code,
                            }
                        )
                if not target_code:
                    target_code = next(
                        (
                            card["code"]
                            for card in user_cards
                            if is_scannable_access_code(card["code"])
                        ),
                        "",
                    )
                if not target_code:
                    target_code = generate_access_code(taken_codes)
                    taken_codes.add(target_code)
                    cursor.execute(
                        "INSERT INTO aime_card (user, access_code)"
                        " VALUES (%s, %s)",
                        (aime_id, target_code),
                    )
                    new_card = {
                        "id": int(cursor.lastrowid),
                        "user": aime_id,
                        "code": target_code,
                    }
                    user_cards.append(new_card)
                    code_owner[target_code] = aime_id
                    inserted_cards.append(
                        {
                            "aime_id": aime_id,
                            "card_id": new_card["id"],
                            "access_code": target_code,
                        }
                    )

                if old_code != target_code:
                    profile["auth_access_code"] = target_code
                    repaired_profiles.append(
                        {
                            "aime_id": aime_id,
                            "old_access_code": old_code,
                            "new_access_code": target_code,
                        }
                    )
                profile_targets.append((aime_id, target_code))

            known_codes = {
                card["code"]
                for cards in cards_by_user.values()
                for card in cards
                if is_scannable_access_code(card["code"])
            }
            for row in repaired_profiles:
                if current_before == row["old_access_code"]:
                    current_after = row["new_access_code"]
                    break
            if current_after not in known_codes:
                current_after = (
                    sorted(profile_targets, key=lambda row: row[0])[0][1]
                    if profile_targets
                    else ""
                )

        changed_profiles = bool(repaired_profiles)
        changed_aime = current_after != current_before
        if changed_profiles:
            profile_backup = backup_file(PROFILES_PATH)
            servlet._save_profiles()
        if changed_aime:
            if AIME_TXT_PATH.exists():
                aime_backup = backup_file(AIME_TXT_PATH)
            write_current_access_code(current_after)
        connection.commit()
    except Exception as exc:
        connection.rollback()
        restore_errors = []
        for backup, destination, label in (
            (profile_backup, PROFILES_PATH, "the account profile"),
            (aime_backup, AIME_TXT_PATH, "the current card number"),
        ):
            if not backup:
                continue
            try:
                shutil.copy2(backup, destination)
            except OSError as restore_exc:
                restore_errors.append(f"restoring {label} failed: {restore_exc}")
        suffix = "; " + "; ".join(restore_errors) if restore_errors else ""
        return {
            "ok": False,
            "error": "repair_card_codes_failed",
            "message": f"Card number repair was rolled back: {exc}{suffix}",
        }
    finally:
        connection.close()

    return {
        "ok": True,
        "repaired_cards": repaired_cards,
        "repaired_profiles": repaired_profiles,
        "inserted_cards": inserted_cards,
        "current_before": current_before,
        "current_after": current_after,
        "profile_backup": profile_backup,
        "aime_backup": aime_backup,
    }


def command_reset(args) -> dict:
    if server_running() and not args.force:
        return {"ok": False, "error": "server_running", "message": SERVER_RUNNING_MESSAGE}

    profiles = load_profiles_file()
    _key, existing = find_profile_by_aime_id(profiles, args.aime_id)
    if existing is None:
        return {
            "ok": False,
            "error": "account_not_found",
            "message": f"No account found with aime_id={args.aime_id}",
        }

    aime_id = _profile_int(existing, "aime_id", args.aime_id)
    access_code = resolve_access_code(aime_id, existing)
    master_name = str(args.name).strip() if args.name else str(
        existing.get("master_name", "MASTER")
    )
    gender_type = _profile_int(existing, "mst_gender_type", 1)
    old_pd_id = _profile_int(existing, "pd_id", 0)

    backup_file(PROFILES_PATH)
    servlet = build_servlet()
    for profile_key, stored in list(servlet._profiles.items()):
        if not isinstance(stored, dict):
            continue
        if profile_key == _key or (
            old_pd_id > 0 and _profile_int(stored, "pd_id", 0) == old_pd_id
        ):
            del servlet._profiles[profile_key]
    profile = servlet._register_profile(
        {
            "aime_id": aime_id,
            "access_code": access_code,
            "master_name": master_name,
            "mst_gender_type": gender_type,
            "account_mode": "normal",
            "resource_preset": "original",
            "material_preset": "original",
        }
    )
    removed_print_files = purge_player_prints(aime_id)
    return {
        "ok": True,
        "aime_id": aime_id,
        "access_code": access_code,
        "removed_print_files": removed_print_files,
        "account": account_entry(aime_id, profile, read_current_access_code()),
    }


def command_repair(args) -> dict:
    if server_running() and not args.force:
        return {
            "ok": False,
            "error": "server_running",
            "message": SERVER_RUNNING_MESSAGE,
        }

    backup_name = backup_file(PROFILES_PATH)
    servlet = build_servlet()
    _key, profile = find_profile_by_aime_id(servlet._profiles, args.aime_id)
    if profile is None:
        return {
            "ok": False,
            "error": "account_not_found",
            "message": f"No account found with aime_id={args.aime_id}",
        }
    repair = replay_captured_battle_settlements(servlet, profile)
    if repair["captures"]:
        servlet._save_profiles(profile)
    return {
        "ok": True,
        "aime_id": int(args.aime_id),
        "backup": backup_name,
        "repair": repair,
        "account": account_entry(
            int(args.aime_id),
            profile,
            read_current_access_code(),
            load_card_manifest_by_id(),
            load_master_level_requirements(),
        ),
    }


def benefit_catalog(servlet, profile: dict = None) -> list:
    """Return the finite resources exposed by the launcher grant editor."""

    if not isinstance(profile, dict):
        profile = {}
    result = [{"key": "currency:sp_cos_point", "category": "Costume Fragments",
               "name": "Costume Fragment (generic, current version)", "item_id": "sp_cos_point",
               "current": servlet._clamp_resource_amount("sp_cos_point", profile.get("sp_cos_point", 0)),
               "max_amount": servlet.GENERIC_AMOUNT_CAP}]
    currency_specs = (
        ("currency:qp", "QP", "qp_amnt"),
        ("currency:fp", "Friend Point", "fp_amnt"),
        ("currency:mana_prism", "Mana Prism", "mana_prism_amnt"),
        ("currency:summon_point", "Summon Point", "summon_point_amnt"),
    )
    for key, name, field in currency_specs:
        result.append(
            {
                "key": key,
                "category": "Currency",
                "name": name,
                "item_id": field,
                "current": servlet._clamp_resource_amount(
                    field, profile.get(field, 0)
                ),
                "max_amount": servlet.RESOURCE_BALANCE_CAPS[field],
            }
        )
    result.append(
        {
            "key": "currency:general_fatal_coin",
            "category": "Currency",
            "name": "Generic Fatal Servant Coin",
            "item_id": "general_fatal_svt_tc_coin_amount",
            "current": min(
                servlet.FATAL_COIN_AMOUNT_CAP,
                max(
                    0,
                    _profile_int(
                        profile, "general_fatal_svt_tc_coin_amount", 0
                    ),
                ),
            ),
            "max_amount": servlet.FATAL_COIN_AMOUNT_CAP,
        }
    )

    master_root = SERVER_DIR / "data" / "fgo-master" / "material"
    material_specs = (
        (1, "EXP Materials", "arms_mst_exp_material.bin", "exp_material"),
        (
            2,
            "Common Materials",
            "arms_mst_synthesis_cmn_material.bin",
            "synthesis_cmn_material",
        ),
        (
            3,
            "Ascension Materials",
            "arms_mst_svt_ascension_material.bin",
            "svt_ascension_material",
        ),
        (
            4,
            "Skill Materials",
            "arms_mst_skill_synthesis_material.bin",
            "skill_synthesis_material",
        ),
    )
    stored_materials = profile.get("material_amounts", {})
    if not isinstance(stored_materials, dict):
        stored_materials = {}
    for category_id, category_name, file_name, table_name in material_specs:
        rows = servlet._load_property_rows(
            str(master_root / file_name), table_name
        )
        seen_ids = set()
        stored_category = stored_materials.get(str(category_id), {})
        if not isinstance(stored_category, dict):
            stored_category = {}
        for row in rows:
            item_id = _profile_int(row, "item_idx_in_category", 0)
            if item_id <= 0 or item_id in seen_ids:
                continue
            seen_ids.add(item_id)
            name = str(row.get("item_name", "")).strip()
            result.append(
                {
                    "key": f"material:{category_id}:{item_id}",
                    "category": category_name,
                    "name": name or f"Material {category_id}:{item_id}",
                    "item_id": f"{category_id}:{item_id}",
                    "current": min(
                        servlet.MATERIAL_AMOUNT_CAP,
                        max(0, _profile_int(stored_category, str(item_id), 0)),
                    ),
                    "max_amount": servlet.MATERIAL_AMOUNT_CAP,
                }
            )

    # Retired aggregate-event counters are still enforced by the installed
    # quest scripts.  Unlike synthesis materials, these values can no longer
    # be earned from a live cabinet-wide service, so expose the exact IDs
    # recovered from quest_prop to the finite grant editor.  Do not fabricate
    # balances automatically: normal accounts receive only what the operator
    # explicitly grants.
    stored_tlf_items = profile.get("tlf_item_amounts", {})
    if not isinstance(stored_tlf_items, dict):
        stored_tlf_items = {}
    for item_id, gate_amount in sorted(servlet._tlf_item_gate_amounts().items()):
        result.append(
            {
                "key": f"tlf_item:{item_id}",
                "category": "Event Items",
                "name": f"Event item {item_id} (max required {gate_amount})",
                "item_id": str(item_id),
                "current": min(
                    servlet.GENERIC_AMOUNT_CAP,
                    max(0, _profile_int(stored_tlf_items, str(item_id), 0)),
                ),
                "max_amount": servlet.GENERIC_AMOUNT_CAP,
            }
        )
    return result


def command_catalog(args) -> dict:
    servlet = build_servlet()
    _key, profile = find_profile_by_aime_id(servlet._profiles, args.aime_id)
    if profile is None:
        return {
            "ok": False,
            "error": "account_not_found",
            "message": f"No account found with aime_id={args.aime_id}",
        }
    return {
        "ok": True,
        "aime_id": int(args.aime_id),
        "server_running": server_running(),
        "items": benefit_catalog(servlet, profile),
    }


def command_gift(args) -> dict:
    # Construct catalogue/migrations against a disposable snapshot, never the
    # live server's player file. Online writes are append-only inbox batches.
    snapshot = load_profiles_file()
    with TemporaryDirectory(prefix="fgo-benefit-") as temporary:
        snapshot_path = Path(temporary) / "players.json"
        snapshot_path.write_text(json.dumps(snapshot), encoding="utf-8")
        servlet = build_servlet(snapshot_path)
        _key, profile = find_profile_by_aime_id(servlet._profiles, args.aime_id)
        if profile is None:
            return {"ok": False, "error": "account_not_found"}
        from titles.fgo.benefit_queue import descriptor
        rows = []
        for row in benefit_catalog(servlet, profile):
            try:
                descriptor(row["key"])
            except ValueError:
                continue
            rows.append(row)
        if not getattr(args, "item", None):
            return {"ok": True, "aime_id": args.aime_id, "items": rows}
        requested, error = _parse_grant_specs(args.item)
        if error:
            return {"ok": False, "error": "invalid_grant", "message": error}
        catalog = {row["key"]: row for row in rows}
        if set(requested) - catalog.keys():
            return {"ok": False, "error": "unsupported_present_item"}
        batch_id = secrets.token_hex(16)
        batch = {"version": 1, "id": batch_id, "aime_id": args.aime_id, "items": [
            {"key": key, "amount": min(amount, catalog[key]["max_amount"]),
             "cap": catalog[key]["max_amount"], "name": catalog[key]["name"]}
            for key, amount in requested.items()]}
    folder = PROFILES_PATH.parent / "fgo-benefit-inbox" / str(args.aime_id)
    folder.mkdir(parents=True, exist_ok=True)
    temporary_file = folder / (batch_id + ".tmp")
    temporary_file.write_text(json.dumps(batch, ensure_ascii=False), encoding="utf-8")
    os.replace(temporary_file, folder / (batch_id + ".json"))
    return {"ok": True, "queued": True, "batch_id": batch_id, "grants": [
        {"name": row["name"], "applied": row["amount"]} for row in batch["items"]]}


def _parse_grant_specs(raw_specs: list) -> tuple[dict, str]:
    grants = {}
    for raw_spec in raw_specs or []:
        key, separator, raw_amount = str(raw_spec).rpartition("=")
        key = key.strip()
        if not separator or not key:
            return {}, f"Invalid grant entry: {raw_spec} (expected key=amount)"
        try:
            amount = int(raw_amount)
        except (TypeError, ValueError):
            return {}, f"Invalid grant amount: {raw_spec}"
        if amount <= 0:
            return {}, f"Grant amounts must be greater than 0: {raw_spec}"
        grants[key] = grants.get(key, 0) + amount
    if not grants:
        return {}, "Pick at least one item and enter an amount above zero"
    return grants, ""


def command_grant(args) -> dict:
    if server_running() and not args.force:
        return {
            "ok": False,
            "error": "server_running",
            "message": SERVER_RUNNING_MESSAGE,
        }
    requested, error = _parse_grant_specs(args.item)
    if error:
        return {"ok": False, "error": "invalid_grant", "message": error}

    servlet = build_servlet()
    _key, profile = find_profile_by_aime_id(servlet._profiles, args.aime_id)
    if profile is None:
        return {
            "ok": False,
            "error": "account_not_found",
            "message": f"No account found with aime_id={args.aime_id}",
        }
    catalog_by_key = {
        row["key"]: row for row in benefit_catalog(servlet, profile)
    }
    unknown = sorted(set(requested) - set(catalog_by_key))
    if unknown:
        return {
            "ok": False,
            "error": "unknown_grant_item",
            "message": "Unknown grant item: " + ", ".join(unknown),
        }

    backup_name = backup_file(PROFILES_PATH)
    applied_rows = []
    for key, amount in requested.items():
        catalog_row = catalog_by_key[key]
        before = int(catalog_row["current"])
        if key.startswith("currency:"):
            currency_name = key.split(":", 1)[1]
            field_by_name = {
                "sp_cos_point": "sp_cos_point",
                "qp": "qp_amnt",
                "fp": "fp_amnt",
                "mana_prism": "mana_prism_amnt",
                "summon_point": "summon_point_amnt",
            }
            if currency_name == "general_fatal_coin":
                after = min(
                    servlet.FATAL_COIN_AMOUNT_CAP,
                    before + amount,
                )
                profile["general_fatal_svt_tc_coin_amount"] = after
            else:
                field = field_by_name[currency_name]
                after = servlet._change_profile_balance(profile, field, amount)
        elif key.startswith("material:"):
            _kind, raw_category, raw_item_id = key.split(":", 2)
            after = servlet._change_material_amount(
                profile, int(raw_category), int(raw_item_id), amount
            )
        else:
            _kind, raw_item_id = key.split(":", 1)
            after = servlet._change_profile_id_amount(
                profile, "tlf_item_amounts", int(raw_item_id), amount
            )
        applied_rows.append(
            {
                "key": key,
                "name": catalog_row["name"],
                "requested": amount,
                "applied": after - before,
                "before": before,
                "after": after,
                "max_amount": int(catalog_row["max_amount"]),
                "clamped": after - before < amount,
            }
        )

    servlet._save_profiles(profile)
    return {
        "ok": True,
        "aime_id": int(args.aime_id),
        "backup": backup_name,
        "grants": applied_rows,
        "account": account_entry(
            int(args.aime_id),
            profile,
            read_current_access_code(),
            load_card_manifest_by_id(),
            load_master_level_requirements(),
        ),
    }


def command_upgrade(args) -> dict:
    if server_running():
        return {"ok": False, "error": "server_running", "message": SERVER_RUNNING_MESSAGE}
    from fgo_account_actions import apply_action
    servlet = build_servlet()
    _key, original = find_profile_by_aime_id(servlet._profiles, args.aime_id)
    if original is None:
        return {"ok": False, "error": "account_not_found", "message": "The selected account was not found"}
    profile = deepcopy(original)
    try:
        count = apply_action(servlet, profile, args.action, benefit_catalog(servlet, profile) if args.action == 'materials' else [])
        backup_name = backup_file(PROFILES_PATH)
        servlet._save_profiles(profile)
    except Exception as exc:
        return {"ok": False, "error": "upgrade_failed", "message": str(exc)}
    return {"ok": True, "action": args.action, "changed": count, "backup": backup_name}


def human_lines_for(command: str, result: dict):
    if not result.get("ok"):
        return [f"Error: {result.get('error')}: {result.get('message', '')}"]
    if command == "list":
        lines = [
            f"Server running: {result['server_running']}",
            f"Current Aime code: {result['current_access_code'] or '(none)'}",
        ]
        for account in result["accounts"]:
            marker = "*" if account["is_current"] else " "
            lines.append(
                "{marker} aime_id={aime_id} pd_id={pd_id} \"{master_name}\" "
                "{account_mode}/{resource_preset}/{material_preset} "
                "servants={servant_count} code={access_code}".format(
                    marker=marker, **account
                )
            )
        if not result["accounts"]:
            lines.append("(no accounts)")
        return lines
    if command == "create":
        account = result["account"]
        return [
            f"Created account aime_id={result['aime_id']} access_code={result['access_code']}",
            f"  name={account['master_name']} mode={account['account_mode']} "
            f"servants={account['servant_count']}",
            "Run `use --aime-id {0}` to switch to this account".format(result["aime_id"]),
        ]
    if command == "use":
        return [
            f"Switched aime.txt -> {result['access_code']} (aime_id={result['aime_id']})",
            f"Backup: {result['backup'] or '(none)'}",
        ]
    if command == "delete":
        replacement = (
            f"; the current account is now aime_id={result['replacement_aime_id']}"
            if result["replacement_aime_id"]
            else "; the current card number was cleared"
            if result["deleted_current"]
            else ""
        )
        return [
            f"Deleted account aime_id={result['aime_id']} "
            f"\"{result['master_name']}\"{replacement}",
            "  Event saves, the Aime identity, card mappings and leftover account backups were cleaned up",
        ]
    if command == "repair-card-codes":
        return [
            "Aime card numbers are consistent again: "
            f"replaced {len(result['repaired_cards'])} old cards, "
            f"updated {len(result['repaired_profiles'])} account profiles, "
            f"added {len(result['inserted_cards'])} card mappings",
            f"  Current card number: {result['current_after'] or '(none)'}",
        ]
    if command == "reset":
        account = result["account"]
        return [
            f"Reset account aime_id={result['aime_id']} \"{account['master_name']}\"",
            f"  mode={account['account_mode']}/{account['resource_preset']}"
            f"/{account['material_preset']} servants={account['servant_count']}",
        ]
    if command == "repair":
        account = result["account"]
        repair = result["repair"]
        return [
            f"Repaired account aime_id={result['aime_id']} \"{account['master_name']}\"",
            f"  captured results={repair['captures']} quest rows={repair['quest_rows']} "
            f"skipped spendable balances={repair['spendable_rows_skipped']} "
            f"Lv.{account['master_level']} EXP={account['master_exp']}",
            f"  Backup: {result['backup']}",
        ]
    if command == "catalog":
        return [
            f"Grantable items: {len(result['items'])} "
            f"(aime_id={result['aime_id']})"
        ]
    if command == "grant":
        lines = [f"Granted items to aime_id={result['aime_id']}:"]
        for row in result["grants"]:
            suffix = " (capped at the maximum)" if row["clamped"] else ""
            lines.append(
                f"  {row['name']}: +{row['applied']} -> {row['after']}{suffix}"
            )
        lines.append(f"  Backup: {result['backup']}")
        return lines
    return None


def main() -> int:
    configure_stdio()
    common = argparse.ArgumentParser(add_help=False)
    common.add_argument(
        "--json",
        action="store_true",
        help="write one JSON object to stdout (human-readable output goes to stderr)",
    )
    parser = argparse.ArgumentParser(
        description="FGO Arcade local account management (create/switch/delete/reset)"
    )
    subparsers = parser.add_subparsers(dest="command", required=True)

    subparsers.add_parser("list", parents=[common], help="list local accounts")

    create_parser = subparsers.add_parser(
        "create", parents=[common], help="create an account (Aime database + profile)"
    )
    create_parser.add_argument("--name", required=True, help="Master name")
    create_parser.add_argument(
        "--mode",
        choices=["test_full", "normal"],
        default="normal",
        help="account mode (default normal: a plain new account; test_full is an explicit all-Servants test account)",
    )
    create_parser.add_argument(
        "--force", action="store_true", help="run even while the server is running"
    )

    use_parser = subparsers.add_parser(
        "use", parents=[common], help="point aime.txt at the given account"
    )
    use_parser.add_argument("--aime-id", type=int, required=True)

    delete_parser = subparsers.add_parser(
        "delete", parents=[common], help="delete an account (profile + Aime database)"
    )
    delete_parser.add_argument("--aime-id", type=int, required=True)
    delete_parser.add_argument(
        "--yes", action="store_true", help="confirm this permanent deletion"
    )
    delete_parser.add_argument(
        "--force", action="store_true", help="run even while the server is running"
    )

    repair_card_codes_parser = subparsers.add_parser(
        "repair-card-codes",
        parents=[common],
        help="repair old card numbers that AMDaemon rejects, and check account card mappings",
    )
    repair_card_codes_parser.add_argument(
        "--force", action="store_true", help="run even while the server is running"
    )

    reset_parser = subparsers.add_parser(
        "reset",
        parents=[common],
        help="reset an account to normal/original (Servants, resources and progress are cleared)",
    )
    reset_parser.add_argument("--aime-id", type=int, required=True)
    reset_parser.add_argument("--name", default=None, help="optional new Master name")
    reset_parser.add_argument(
        "--force", action="store_true", help="run even while the server is running"
    )

    repair_parser = subparsers.add_parser(
        "repair",
        parents=[common],
        help="restore EXP, Bond and quest progress from captured cabinet results",
    )
    repair_parser.add_argument("--aime-id", type=int, required=True)
    repair_parser.add_argument(
        "--force", action="store_true", help="run even while the server is running"
    )

    catalog_parser = subparsers.add_parser(
        "catalog", parents=[common], help="list grantable items"
    )
    catalog_parser.add_argument("--aime-id", type=int, required=True)

    grant_parser = subparsers.add_parser(
        "grant", parents=[common], help="grant items to an account"
    )
    grant_parser.add_argument("--aime-id", type=int, required=True)
    grant_parser.add_argument(
        "--item",
        action="append",
        required=True,
        help="a grant entry key=amount; repeat for more items",
    )
    grant_parser.add_argument(
        "--force", action="store_true", help="run even while the server is running"
    )

    gift_parser = subparsers.add_parser("gift", parents=[common], help="check grants or queue items for the Present Box")
    gift_parser.add_argument("--aime-id", type=int, required=True)
    gift_parser.add_argument("--item", action="append", default=[])
    upgrade_parser = subparsers.add_parser("upgrade", parents=[common], help="one-click inventory and growth actions for the selected account")
    upgrade_parser.add_argument("--aime-id", type=int, required=True)
    from fgo_account_actions import ACTIONS
    upgrade_parser.add_argument("--action", choices=ACTIONS, required=True)
    args = parser.parse_args()
    handlers = {
        "list": command_list,
        "create": command_create,
        "use": command_use,
        "delete": command_delete,
        "repair-card-codes": command_repair_card_codes,
        "reset": command_reset,
        "repair": command_repair,
        "catalog": command_catalog,
        "grant": command_grant,
        "gift": command_gift,
        "upgrade": command_upgrade,
    }
    result = handlers[args.command](args)
    return emit(
        result,
        bool(getattr(args, "json", False)),
        human_lines=human_lines_for(args.command, result),
    )


if __name__ == "__main__":
    sys.exit(main())
