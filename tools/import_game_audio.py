"""Scan observed public game audio URLs, sync assets, and map official config.

The default --sync-cache workflow reads only matching game-CDN URLs from the
selected Chrome Cache_Data directory. The initial index rebuild uses the two
previously verified source indexes under .artifacts/portrait-refresh.
"""

from __future__ import annotations

import argparse
from concurrent.futures import ThreadPoolExecutor, as_completed
import ctypes
import csv
import gzip
import hashlib
import io
import json
import os
import re
import shutil
import subprocess
import tempfile
import time
import zipfile
from pathlib import Path
from urllib.parse import urlsplit

import requests
from Crypto.Cipher import AES


REPO = Path(__file__).resolve().parents[1]
OUTPUT = REPO / "src/CardGame.Wpf/Assets/Audio/Official"
CATALOG = REPO / "docs/content/game-audio-catalog.json"
RESEARCH = REPO / ".artifacts/portrait-refresh/audio-research/source-index.json"
CACHE = REPO / ".artifacts/portrait-refresh/audio-cache-refresh/2026-09-26-user-listening/source-index.json"
SYNC = REPO / ".artifacts/portrait-refresh/audio-sync"
CSV_INDEX = REPO / "docs/content/game-audio-index.csv"
CONFIG_URL = "https://web.sanguosha.com/220/h5_2/res/config/Config.sgs"
GAME_URL = "https://web.sanguosha.com/220/h5_2/sgsGame_a.sgs"
AUDIO_URL = re.compile(rb"https://web\.sanguosha\.com/[A-Za-z0-9_.~%/\-]+?\.(?:mp3|ogg|wav)(?:\?[A-Za-z0-9_.~%&=+\-]*)?", re.I)
# Exact client Class values only. JiaXu/SunCe are retained for future local
# registration; no Po/Shen/Mou class is ever folded into their classic key.
CLASS_LOCAL_KEYS = {"JiaXu": ("jia-xu", "贾诩"), "SunCe": ("sun-ce", "孙策"),
                    "DaQiao": ("da-qiao", "大乔")}


def digest(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as file:
        for block in iter(lambda: file.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def write_json_atomic(path: Path, value: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp_name = None
    try:
        with tempfile.NamedTemporaryFile("w", encoding="utf-8", newline="\n",
                                         dir=path.parent, prefix=".audio-catalog-", suffix=".tmp",
                                         delete=False) as stream:
            temp_name = stream.name
            json.dump(value, stream, ensure_ascii=False, indent=2)
            stream.write("\n")
        os.replace(temp_name, path)
    finally:
        if temp_name and Path(temp_name).exists():
            Path(temp_name).unlink()


def write_csv_index(assets: list[dict]) -> None:
    """Human-readable, one row per local binding (or source-only asset)."""
    CSV_INDEX.parent.mkdir(parents=True, exist_ok=True)
    columns = ["assetId", "productLine", "assignmentStatus", "generalName", "sourceGeneralClasses",
               "generalKey", "skinName", "localSkinId", "localSkinMatched", "role", "skillName",
               "lineText", "sourceGeneralIds", "sourceSkinIds", "sourceSkillIds", "localPath", "sourceUrl"]
    temp_name = None
    try:
        with tempfile.NamedTemporaryFile("w", encoding="utf-8-sig", newline="",
                                         dir=CSV_INDEX.parent, prefix=".audio-index-", suffix=".tmp",
                                         delete=False) as stream:
            temp_name = stream.name
            writer = csv.DictWriter(stream, fieldnames=columns)
            writer.writeheader()
            for asset in sorted(assets, key=lambda item: item["id"]):
                primary = {key: asset.get(key) for key in ("generalKey", "generalName", "skinId", "skinName",
                                                            "role", "skillName", "lineText")}
                bindings = [primary, *(asset.get("bindings") or [])]
                seen = set()
                for raw in bindings:
                    binding = {key: raw.get(key) or primary.get(key) for key in primary}
                    identity = tuple(binding.get(key) for key in ("generalKey", "skinId", "role", "skillName", "lineText"))
                    if identity in seen:
                        continue
                    seen.add(identity)
                    row = {
                        "assetId": asset["id"], "productLine": asset.get("productLine", ""),
                        "assignmentStatus": asset.get("assignmentStatus", ""),
                        "generalName": binding.get("generalName") or "",
                        "sourceGeneralClasses": ";".join(asset.get("sourceGeneralClasses", [])),
                        "generalKey": binding.get("generalKey") or "",
                        "skinName": binding.get("skinName") or "",
                        "localSkinId": binding.get("skinId") or "",
                        "localSkinMatched": "yes" if binding.get("generalKey") and binding.get("skinId") else "no",
                        "role": binding.get("role") or "", "skillName": binding.get("skillName") or "",
                        "lineText": binding.get("lineText") or "",
                        "sourceGeneralIds": ";".join(asset.get("sourceGeneralIds", [])),
                        "sourceSkinIds": ";".join(asset.get("sourceSkinIds", [])),
                        "sourceSkillIds": ";".join(asset.get("sourceSkillIds", [])),
                        "localPath": asset["localPath"], "sourceUrl": asset.get("sourceUrl", ""),
                    }
                    writer.writerow(row)
        os.replace(temp_name, CSV_INDEX)
    finally:
        if temp_name and Path(temp_name).exists():
            Path(temp_name).unlink()


def probe(path: Path) -> dict:
    run = subprocess.run(
        ["ffprobe", "-v", "error", "-select_streams", "a:0", "-show_entries",
         "format=format_name,duration:stream=codec_name,sample_rate,channels", "-of", "json", str(path)],
        capture_output=True, text=True, check=True,
    )
    data = json.loads(run.stdout)
    stream = data["streams"][0]
    subprocess.run(["ffmpeg", "-v", "error", "-i", str(path), "-f", "null", "NUL"],
                   capture_output=True, text=True, check=True)
    return {
        "format": data["format"]["format_name"],
        "codec": stream["codec_name"],
        "durationSeconds": float(data["format"]["duration"]),
        "sampleRateHz": int(stream["sample_rate"]),
        "channels": int(stream["channels"]),
    }


def import_file(source: Path, target: Path, source_sha: str) -> tuple[str, dict | None]:
    if digest(source) != source_sha:
        raise ValueError(f"Source SHA-256 mismatch: {source}")
    target.parent.mkdir(parents=True, exist_ok=True)
    conversion = None
    temp_name = None
    if source.suffix.lower() == ".ogg":
        target = target.with_suffix(".wav")
        conversion = {
            "tool": "ffmpeg",
            "commandTemplate": "ffmpeg -y -v error -i <verified-source.ogg> -c:a pcm_s16le <delivered.wav>",
            "targetCodec": "pcm_s16le",
        }
    try:
        with tempfile.NamedTemporaryFile(dir=target.parent, prefix=".audio-import-",
                                         suffix=target.suffix, delete=False) as stream:
            temp_name = stream.name
        temp = Path(temp_name)
        if source.suffix.lower() == ".ogg":
            command = ["ffmpeg", "-y", "-v", "error", "-i", str(source),
                       "-c:a", "pcm_s16le", str(temp)]
            subprocess.run(command, capture_output=True, text=True, check=True)
        else:
            shutil.copyfile(source, temp)
        if target.exists():
            if digest(target) != digest(temp):
                raise ValueError(f"Existing delivered asset differs from verified source: {target}")
        else:
            os.replace(temp, target)
    finally:
        if temp_name and Path(temp_name).exists():
            Path(temp_name).unlink()
    delivered = probe(target)
    if source.suffix.lower() == ".ogg" and delivered["codec"] != "pcm_s16le":
        raise ValueError(f"Converted file is not PCM WAV: {target}")
    return target.relative_to(REPO).as_posix(), conversion


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cache-index", type=Path, default=CACHE)
    parser.add_argument("--research-index", type=Path, default=RESEARCH)
    args = parser.parse_args()
    cache = json.loads(args.cache_index.read_text(encoding="utf-8"))
    research = json.loads(args.research_index.read_text(encoding="utf-8"))
    assets: list[dict] = []
    seen_sha: set[str] = set()

    for row in cache["items"]:
        sha = row["sha256"]
        if sha in seen_sha:
            continue
        seen_sha.add(sha)
        source = args.cache_index.parent / row["file"]
        path = urlsplit(row["canonicalUrl"]).path
        prefix = "/220/h5_2/res/runtime/pc/voice/"
        if not path.startswith(prefix):
            raise ValueError(f"Unexpected OL resource path: {path}")
        relative = Path(*path.removeprefix(prefix).split("/"))
        if ".." in relative.parts:
            raise ValueError(f"Unsafe OL path: {path}")
        target = OUTPUT / "OL" / relative
        local, conversion = import_file(source, target, sha)
        delivered_path = REPO / local
        resource_directory = relative.parts[0] if len(relative.parts) > 1 else "voice-root"
        asset: dict = {
            "id": "ol-" + sha,
            "productLine": "sanguosha-ol-web",
            "kind": "bgm" if resource_directory == "bgm" else "voice-or-effect",
            "role": "unmapped",
            "assignmentStatus": "unmapped",
            "generalKey": None,
            "generalName": None,
            "skinId": None,
            "skinName": None,
            "skillId": None,
            "skillName": None,
            "lineText": None,
            "resourceDirectory": resource_directory,
            "sourceUrl": row["canonicalUrl"],
            "sourceEvidence": "Chrome game-cache URL observation; see audio-cache-refresh source-index.json",
            "localPath": local,
            "sourceSha256": sha,
            "deliveredSha256": digest(delivered_path),
            "sourceFormat": Path(path).suffix.lstrip(".").lower(),
            "deliveredFormat": delivered_path.suffix.lstrip(".").lower(),
            "sourceBytes": row["bytes"],
            "deliveredBytes": delivered_path.stat().st_size,
            "durationSeconds": row["durationSeconds"],
        }
        if conversion:
            asset["conversion"] = conversion
        if relative.as_posix() == "bgm/BackGroundMusic01.mp3":
            asset.update(role="lobby", assignmentStatus="verified",
                         sourceEvidence="sgsGame_a.sgs: JB.MainBgmUrl fallback")
        elif relative.as_posix() == "bgm/BackGroundMusic02.mp3":
            asset.update(role="battle", assignmentStatus="verified",
                         sourceEvidence="sgsGame_a.sgs: JB.GameBgmUrl fallback after SoundSets.BgMusicName lookup")
        elif relative.as_posix() in ("spell/122caoang/1skill1_1.mp3", "spell/122caoang/1skill1_2.mp3"):
            line = "典将军，比比看谁杀敌更多！" if relative.name == "1skill1_1.mp3" else "父亲快走，有我殿后！"
            asset.update(
                kind="voice", role="skill", assignmentStatus="verified",
                generalKey="cao-ang", generalName="曹昂", skillName="慷忾", lineText=line,
                sourcePage="https://x.sanguosha.com/hero/404.html",
                sourceEvidence="The official hero page identifies Cao Ang/Kangkai and its voice URL; decoded 16 kHz mono PCM matches this OL file at zero lag (correlation >0.999997). OL skin ID is not established.",
            )
        assets.append(asset)

    for row in research["items"]:
        sha = row["sha256"]
        if sha in seen_sha:
            continue
        seen_sha.add(sha)
        source = REPO / row["file"]
        url_path = urlsplit(row["url"]).path
        filename = Path(url_path).name
        if row.get("generalKey") == "ma-dai":
            branch = "YJCM/ma-dai/" + row["skinResourceId"]
        elif row.get("generalKey") == "cao-ang":
            branch = "YJCM/cao-ang/shared-classic-and-lin-wei-bu-luan"
        elif row.get("generalKey") == "liu-bei":
            branch = "OL/verified-samples/liu-bei"
        else:
            raise ValueError(f"Unexpected research sample: {row['file']}")
        target = OUTPUT / branch / filename
        local, conversion = import_file(source, target, sha)
        delivered_path = REPO / local
        official_hero = row.get("generalKey") in ("ma-dai", "cao-ang")
        asset = {
            "id": ("yjcm-" if official_hero else "ol-") + sha,
            "productLine": "yijianchengming-hero-site" if official_hero else "sanguosha-ol-web",
            "kind": "voice",
            "role": "skill",
            "assignmentStatus": "verified" if official_hero else "corroborated",
            "generalKey": row["generalKey"],
            "generalName": {"ma-dai": "马岱", "cao-ang": "曹昂", "liu-bei": "刘备"}[row["generalKey"]],
            "skinId": "240401" if row.get("generalKey") == "cao-ang" else row.get("skinResourceId"),
            "skillId": None,
            "skinName": ("经典形象*马岱" if row.get("skinResourceId") == "130101" else "界限突破*马岱") if row.get("generalKey") == "ma-dai" else ("临危不乱*曹昂" if row.get("generalKey") == "cao-ang" else "经典形象*刘备"),
            "skillName": "潜袭" if row.get("generalKey") == "ma-dai" else ("慷忾" if row.get("generalKey") == "cao-ang" else "仁德"),
            "lineText": ("喊什么喊，我敢杀你。" if row.get("skinResourceId") == "130101" else "暗隐深处，袭敌斩首！") if row.get("generalKey") == "ma-dai" else (row["lineText"] if row.get("generalKey") == "cao-ang" else "惟贤惟德，能服于人。"),
            "sourceUrl": row["url"],
            "sourcePage": row.get("sourcePage") or row.get("mappingSource"),
            "sourceEvidence": row.get("mappingEvidence") or row.get("mappingConfidence"),
            "localPath": local,
            "sourceSha256": sha,
            "deliveredSha256": digest(delivered_path),
            "sourceFormat": source.suffix.lstrip(".").lower(),
            "deliveredFormat": delivered_path.suffix.lstrip(".").lower(),
            "sourceBytes": row["bytes"],
            "deliveredBytes": delivered_path.stat().st_size,
            "durationSeconds": row["durationSeconds"],
        }
        if conversion:
            asset["conversion"] = conversion
        if row.get("generalKey") == "cao-ang":
            asset["bindings"] = [
                {"generalKey": "cao-ang", "skinId": "140401", "skinName": "经典形象*曹昂", "skillName": "慷忾", "lineText": row["lineText"]},
                {"generalKey": "cao-ang", "skinId": "240401", "skinName": "临危不乱*曹昂", "skillName": "慷忾", "lineText": row["lineText"]},
            ]
        assets.append(asset)

    if len(assets) != 206 or len({x["sourceSha256"] for x in assets}) != 206:
        raise ValueError(f"Expected 206 distinct source SHA-256 values (204 original plus two verified Cao Ang voices); got {len(assets)}")
    result = {
        "schemaVersion": 1,
        "description": "Public game audio; unmapped resources must never be selected as character/skill voice by inference.",
        "sourceIndexes": [
            ".artifacts/portrait-refresh/audio-cache-refresh/2026-09-26-user-listening/source-index.json",
            ".artifacts/portrait-refresh/audio-research/source-index.json",
        ],
        "assets": sorted(assets, key=lambda x: x["id"]),
    }
    write_json_atomic(CATALOG, result)
    write_csv_index(result["assets"])
    print(f"Imported {len(assets)} distinct audio files; catalog: {CATALOG}")


def read_shared(path: Path) -> bytes:
    """Read a live Chrome cache file without asking Chrome to close or unlock it."""
    if os.name != "nt":
        return path.read_bytes()
    import msvcrt
    kernel = ctypes.windll.kernel32
    kernel.CreateFileW.argtypes = [ctypes.c_wchar_p, ctypes.c_uint32, ctypes.c_uint32,
                                   ctypes.c_void_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p]
    kernel.CreateFileW.restype = ctypes.c_void_p
    invalid = ctypes.c_void_p(-1).value
    handle = kernel.CreateFileW(str(path), 0x80000000, 0x1 | 0x2 | 0x4, None, 3, 0x80, None)
    if handle == invalid:
        raise OSError(ctypes.get_last_error(), f"Cannot read live cache file: {path}")
    fd = msvcrt.open_osfhandle(handle, os.O_RDONLY)
    with os.fdopen(fd, "rb") as stream:
        return stream.read()


def scan_cache(cache_dir: Path) -> tuple[list[dict], int]:
    SYNC.mkdir(parents=True, exist_ok=True)
    state_file = SYNC / "cache-scan-state.json"
    observations_file = SYNC / "cache-observations.json"
    state = json.loads(state_file.read_text(encoding="utf-8")) if state_file.exists() else {}
    observations = json.loads(observations_file.read_text(encoding="utf-8")) if observations_file.exists() else []
    seen = {(x["url"], x["cacheFile"], x["offset"]) for x in observations}
    changed = 0
    if not cache_dir.is_dir():
        raise FileNotFoundError(f"Chrome Cache_Data directory missing: {cache_dir}")
    for path in sorted(cache_dir.iterdir()):
        if not path.is_file() or not (path.name.startswith("data_") or path.name.startswith("f_")):
            continue
        stat = path.stat()
        stamp = [stat.st_size, stat.st_mtime_ns]
        if state.get(path.name) == stamp:
            continue
        changed += 1
        try:
            data = read_shared(path)
        except OSError as exc:
            print(f"Cache read skipped: {path.name}: {exc}", flush=True)
            continue
        for match in AUDIO_URL.finditer(data):
            url = match.group().decode("ascii")
            parsed = urlsplit(url)
            if parsed.scheme != "https" or parsed.netloc != "web.sanguosha.com" or not parsed.path.startswith("/220/h5_2/res/runtime/pc/voice/"):
                continue
            key = (url, path.name, match.start())
            if key not in seen:
                observations.append({"cacheFile": path.name, "offset": match.start(), "url": url,
                                     "canonicalUrl": parsed._replace(query="", fragment="").geturl()})
                seen.add(key)
        state[path.name] = stamp
    state_file.write_text(json.dumps(state, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    observations_file.write_text(json.dumps(observations, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return observations, changed


def fetch_public(url: str, target: Path) -> tuple[bytes, str]:
    response = requests.get(url, timeout=(10, 45), headers={"User-Agent": "Mozilla/5.0"})
    response.raise_for_status()
    data = response.content
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(data)
    return data, hashlib.sha256(data).hexdigest()


def public_config() -> tuple[dict, dict, dict, dict]:
    """Decode public client resources exactly as pR.WorkParse/aCt.InitOfb do."""
    bundle_path = SYNC / "config/sgsGame_a.sgs"
    config_path = SYNC / "config/Config.sgs"
    bundle, bundle_sha = fetch_public(GAME_URL, bundle_path)
    config, config_sha = fetch_public(CONFIG_URL, config_path)
    with zipfile.ZipFile(io.BytesIO(bundle)) as archive:
        script = archive.read("sgsGame_a.sgs").decode("utf-8", "replace")
    code = re.search(r"InitOfb\(\)\{var t=new Uint8Array\(\[([0-9,]+)\]\);this\.jofb=new aesjs\.ModeOfOperation\.cfb\(\[([0-9,]+)\],t,16\)", script)
    if not code:
        raise ValueError("The public client OFB-named CFB resource decoder changed; refusing to guess a key")
    iv = bytes(int(n) for n in code.group(1).split(","))
    key = bytes(int(n) for n in code.group(2).split(","))
    if len(key) != 16 or len(iv) != 16:
        raise ValueError("Unexpected public client resource decoder key/IV size")
    decoded = {}
    members = {}
    with zipfile.ZipFile(io.BytesIO(config)) as archive:
        for member in ("sys_h5_music.sgs", "character.sgs", "cha_gs_dbs_fs_skininfo.sgs"):
            source = archive.read(member)
            plain = AES.new(key, AES.MODE_CFB, iv=iv, segment_size=128).decrypt(source)
            data = gzip.decompress(plain)
            decoded[member] = json.loads(data)
            members[member] = {"sha256": hashlib.sha256(source).hexdigest(),
                               "decodedSha256": hashlib.sha256(data).hexdigest()}
    evidence = {"clientBundleUrl": GAME_URL, "clientBundleSha256": bundle_sha,
                "configUrl": CONFIG_URL, "configSha256": config_sha,
                "decoder": "sgsGame_a.sgs pR.WorkParse + aCt.InitOfb: AES-CFB128 then gzip",
                "members": members}
    (SYNC / "config/source-index.json").write_text(json.dumps(evidence, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return (decoded["sys_h5_music.sgs"], decoded["character.sgs"],
            decoded["cha_gs_dbs_fs_skininfo.sgs"], evidence)


def unique(values: list[str]) -> list[str]:
    return list(dict.fromkeys(x for x in values if x))


def apply_config_mapping(assets: list[dict], music: dict, characters: dict, skins: dict, evidence: dict) -> dict:
    root = music["root"]
    by_resource: dict[str, list[tuple[dict, str]]] = {}
    for row in root["heromusic"]:
        for path_field, word_field in zip("fghijk", "mnopqr"):
            if row.get(path_field):
                by_resource.setdefault(row[path_field], []).append((row, row.get(word_field, "")))
    card_resource: dict[str, list[dict]] = {}
    for row in root["cardmusic"]:
        for field in ("musicboy", "musicgirl"):
            if row.get(field):
                card_resource.setdefault(row[field], []).append(row)
    characters_by_id = {str(row["a"]): row for row in characters["GameCharacters"]["character"] if "a" in row}
    skin_by_id = {str(row["skinID"]): row for row in skins["root"]["manu"]["item"]}
    art = json.loads((REPO / "docs/content/general-art-catalog.json").read_text(encoding="utf-8-sig"))
    art_by_skin: dict[str, list[tuple[dict, dict]]] = {}
    for entry in art["entries"]:
        for skin in entry["skins"]:
            art_by_skin.setdefault(skin["name"], []).append((entry, skin))
    stats = {"heroVoiceMatched": 0, "cardVoiceMatched": 0, "unmatchedVoice": 0,
             "localSkinBindingsAdded": 0}
    for asset in assets:
        url = asset.get("sourceUrl", "")
        if "/220/h5_2/res/runtime/pc/voice/spell/" not in url:
            continue
        resource = urlsplit(url).path.split("/voice/spell/", 1)[1].removesuffix(".mp3")
        matches = by_resource.get(resource, [])
        if not matches:
            cards = card_resource.get(resource, [])
            if cards:
                asset.update(kind="voice", role="card", assignmentStatus="verified",
                             sourceSkillIds=unique([str(row.get("skillID", "")) for row in cards]),
                             sourceEvidence=f"{CONFIG_URL}#sys_h5_music.sgs cardmusic: exact resource path {resource}")
                stats["cardVoiceMatched"] += 1
            else:
                stats["unmatchedVoice"] += 1
            continue
        stats["heroVoiceMatched"] += 1
        # Recompute derived OL bindings on refresh. Earlier import versions
        # matched only a personal name and could conflate variant identities.
        independently_verified = resource in ("1liubei/2skill1_1", "2guanyu/2skill1_1")
        if not independently_verified and asset.get("mappingOrigin") != "manual":
            asset["generalKey"] = None
            asset["skinId"] = None
            asset["skinName"] = None
            asset.pop("bindings", None)
        rows = [row for row, _ in matches]
        source_ids = unique([x for row in rows for x in str(row.get("a", "")).split(",")])
        source_skins = unique([x for row in rows for x in str(row.get("b", "")).split(";")])
        source_skill_ids = unique([x for row in rows for x in str(row.get("c", "")).split(",") if x != "None"])
        source_names = unique([characters_by_id[id]["b"] for id in source_ids if id in characters_by_id and characters_by_id[id].get("b")])
        source_classes = unique([characters_by_id[id]["d"] for id in source_ids if id in characters_by_id and characters_by_id[id].get("d")])
        names = unique([str(row.get("d", "")) for row in rows])
        words = unique([word for _, word in matches])
        role = "death" if all("e" in row for row in rows) else "skill" if all("c" in row for row in rows) else "unmapped"
        asset.update(kind="voice", role=role, assignmentStatus="verified",
                     sourceGeneralIds=source_ids, sourceSkinIds=source_skins,
                     sourceSkillIds=source_skill_ids, sourceGeneralClasses=source_classes,
                     sourceSkinNames=unique([skin_by_id[id]["name"] for id in source_skins if id in skin_by_id]),
                     sourceEvidence=f"{CONFIG_URL}#sys_h5_music.sgs heromusic: exact resource path {resource}; {evidence['configSha256']}")
        asset["sourceGeneralId"] = source_ids[0] if len(source_ids) == 1 else None
        asset["sourceSkinId"] = source_skins[0] if len(source_skins) == 1 else None
        asset["sourceSkillId"] = source_skill_ids[0] if len(source_skill_ids) == 1 else None
        if len(source_names) == 1:
            asset["generalName"] = source_names[0]
        if not asset.get("skinName"):
            ol_skin_names = asset["sourceSkinNames"]
            asset["skinName"] = " / ".join(ol_skin_names) if ol_skin_names else "OL 默认配音（皮肤未对应）"
        if len(names) == 1 and role == "skill":
            asset["skillName"] = names[0]
        if len(words) == 1:
            asset["lineText"] = words[0]
        # Local keys require a version-aware identity; matching only a plain
        # Chinese name would collapse 神/界/SP/谋 into the classic character.
        if not asset.get("generalKey") and len(source_ids) == 1:
            known = {"1": ("liu-bei", "LiuBei"), "2": ("guan-yu", "GuanYu"),
                     "122": ("cao-ang", "CaoAng"), "152": ("gao-shun", "GaoShun"),
                     "201": ("shen-guan-yu", "GuanYuShen")}
            identity = known.get(source_ids[0])
            if identity and source_classes == [identity[1]]:
                asset["generalKey"] = identity[0]
            if asset.get("generalKey") == "shen-guan-yu" and source_classes == ["GuanYuShen"]:
                asset["generalName"] = "神关羽"
        class_bindings = []
        for source_id in source_ids:
            source_character = characters_by_id.get(source_id, {})
            exact_class = source_character.get("d")
            if exact_class in CLASS_LOCAL_KEYS:
                local_key, display_name = CLASS_LOCAL_KEYS[exact_class]
                if local_key not in {candidate["generalKey"] for candidate in class_bindings}:
                    class_bindings.append({"generalKey": local_key, "generalName": display_name,
                                           "skinId": None, "skinName": asset.get("skinName"),
                                           "skillName": asset.get("skillName"), "lineText": asset.get("lineText"),
                                           "role": role, "sourceGeneralId": source_id,
                                           "sourceGeneralClass": exact_class})
        if class_bindings:
            if not asset.get("generalKey"):
                primary = class_bindings[0]
                asset["generalKey"] = primary["generalKey"]
                asset["generalName"] = primary["generalName"]
            asset["sourceEvidence"] += "; character.sgs exact Class-to-local-key: " + ", ".join(
                f"{item['sourceGeneralId']} {item['sourceGeneralClass']}→{item['generalKey']}" for item in class_bindings)
            for candidate in class_bindings:
                if candidate["generalKey"] != asset.get("generalKey"):
                    asset.setdefault("bindings", []).append(candidate)
        # OL and hero-site numeric skin IDs differ. Bind only exact official
        # skin names that also occur in our art catalog for the same person.
        local_bindings = []
        for sid in source_skins:
            source_skin = skin_by_id.get(sid)
            if not source_skin:
                continue
            for art_entry, art_skin in art_by_skin.get(source_skin["name"], []):
                if not asset.get("generalKey") or asset["generalKey"] != art_entry["key"]:
                    continue
                local_bindings.append({"generalKey": art_entry["key"], "generalName": art_entry["characterName"],
                                       "skinId": art_skin["id"], "skinName": art_skin["name"],
                                       "skillName": asset.get("skillName"), "lineText": asset.get("lineText"), "role": role})
        existing = asset.get("bindings", [])
        existing_ids = {(b.get("generalKey"), b.get("skinId"), b.get("role", role)) for b in existing}
        for binding in local_bindings:
            ident = (binding["generalKey"], binding["skinId"], binding["role"])
            if ident not in existing_ids:
                existing.append(binding)
                existing_ids.add(ident)
                stats["localSkinBindingsAdded"] += 1
        if existing:
            asset["bindings"] = existing
        if not asset.get("skinId") and local_bindings:
            first = local_bindings[0]
            asset.update(generalKey=first["generalKey"], skinId=first["skinId"], skinName=first["skinName"])
    # These three default-skin alignments have independent official-page PCM
    # checks; a matching personal name alone is not sufficient.
    classic = {
        "spell/1liubei/2skill1_1.mp3": ("liu-bei", "刘备", "100101", "经典形象*刘备", "official hero/1 spell31_1 PCM correlation 0.9999016"),
        "spell/2guanyu/2skill1_1.mp3": ("guan-yu", "关羽", "100201", "经典形象*关羽", "official hero/2 spell33_1 PCM correlation 0.9997345"),
    }
    for asset in assets:
        rel = asset.get("sourceUrl", "").split("/voice/", 1)[-1]
        if rel in classic:
            key, name, skin_id, skin_name, proof = classic[rel]
            asset.update(assignmentStatus="verified", role="skill", generalKey=key, generalName=name,
                         skinId=skin_id, skinName=skin_name,
                         sourceEvidence=asset.get("sourceEvidence", "") + "; " + proof)
        elif rel in ("spell/122caoang/1skill1_1.mp3", "spell/122caoang/1skill1_2.mp3"):
            asset["generalKey"] = "cao-ang"
            asset["generalName"] = "曹昂"
            # OL 12207 (临危不乱) has no separate audio row, so the public
            # client getSoundVOsByGIDAndSID fallback selects the default row.
            asset["sourceEvidence"] += "; hero/404 PCM match >0.999997; OL skin 12207 fallback to -1"
    return stats


def sync_cache(cache_dir: Path, refresh_mapping: bool, retry_failures: bool) -> None:
    if not CATALOG.is_file():
        raise FileNotFoundError(f"First import must supply the verified source indexes: {CATALOG}")
    catalog = json.loads(CATALOG.read_text(encoding="utf-8"))
    assets = catalog["assets"]
    observations, changed_files = scan_cache(cache_dir)
    grouped: dict[str, list[dict]] = {}
    for row in observations:
        grouped.setdefault(row["canonicalUrl"], []).append(row)
    known = {urlsplit(url)._replace(query="", fragment="").geturl()
             for asset in assets for url in [asset["sourceUrl"], *(asset.get("sourceUrlAliases") or [])]}
    failure_file = SYNC / "failed-urls.json"
    failed_history = json.loads(failure_file.read_text(encoding="utf-8")) if failure_file.exists() else {}
    if not failed_history and (SYNC / "last-run.json").exists():
        previous = json.loads((SYNC / "last-run.json").read_text(encoding="utf-8"))
        failed_history = {x["sourceUrl"]: x for x in previous.get("failures", [])}
    pending = [(url, rows) for url, rows in sorted(grouped.items())
               if url not in known and (retry_failures or url not in failed_history)]
    if not pending and not refresh_mapping and catalog.get("configSource"):
        if not CSV_INDEX.exists():
            write_csv_index(assets)
        print(f"Scanned {changed_files} changed cache files; 0 new downloadable game audio URLs. "
              f"{len(assets)} catalog assets unchanged; {len(failed_history)} known failed URL(s) retained.")
        return
    print(f"Scanned {changed_files} changed cache files; {len(grouped)} observed game audio URLs; "
          f"{len(pending)} URLs outside catalog.", flush=True)
    # Resolve public config before copying any new distributable asset. If
    # mapping retrieval fails, the existing catalog and asset tree stay intact.
    config_data = public_config() if pending or refresh_mapping or not catalog.get("configSource") else None
    failures = []
    added = []
    reused_sha = 0
    by_sha_asset = {asset["sourceSha256"]: asset for asset in assets}
    succeeded_urls = []

    def get_one(url: str, rows: list[dict]) -> dict:
        parsed = urlsplit(url)
        prefix = "/220/h5_2/res/runtime/pc/voice/"
        if parsed.scheme != "https" or parsed.netloc != "web.sanguosha.com" or not parsed.path.startswith(prefix):
            raise ValueError(f"Outside approved public game CDN: {url}")
        relative = Path(*parsed.path.removeprefix(prefix).split("/"))
        if ".." in relative.parts:
            raise ValueError(f"Unsafe game resource path: {url}")
        source = SYNC / "downloads" / relative
        if not source.exists():
            last = None
            for attempt in range(3):
                try:
                    data, _ = fetch_public(rows[0]["url"], source)
                    break
                except requests.RequestException as exc:
                    last = exc
                    time.sleep(attempt + 1)
            else:
                raise ValueError(f"CDN request failed after three attempts: {last}")
        source_info = probe(source)
        source_sha = digest(source)
        directory = relative.parts[0] if len(relative.parts) > 1 else "voice-root"
        result = {
            "id": "ol-" + source_sha,
            "productLine": "sanguosha-ol-web",
            "kind": "bgm" if directory == "bgm" else "voice-or-effect",
            "role": "unmapped", "assignmentStatus": "unmapped",
            "generalKey": None, "generalName": None, "skinId": None, "skinName": None,
            "skillId": None, "skillName": None, "lineText": None,
            "sourceUrl": url,
            "sourceEvidence": "Chrome game-cache URL observation; public CDN download",
            "cacheObservations": [{"file": x["cacheFile"], "offset": x["offset"]} for x in rows],
            "resourceDirectory": directory,
            "sourcePathForImport": str(source),
            "relativePathForImport": relative.as_posix(),
            "sourceSha256": source_sha,
            "sourceFormat": source.suffix.lstrip(".").lower(),
            "sourceBytes": source.stat().st_size,
            "durationSeconds": source_info["durationSeconds"],
        }
        return result

    if pending:
        with ThreadPoolExecutor(max_workers=4) as executor:
            futures = {executor.submit(get_one, url, rows): url for url, rows in pending}
            for done, future in enumerate(as_completed(futures), 1):
                url = futures[future]
                try:
                    result = future.result()
                    prior = by_sha_asset.get(result["sourceSha256"])
                    if prior:
                        if result["sourceUrl"] != prior["sourceUrl"]:
                            aliases = prior.setdefault("sourceUrlAliases", [])
                            if result["sourceUrl"] not in aliases:
                                aliases.append(result["sourceUrl"])
                        reused_sha += 1
                        succeeded_urls.append(result["sourceUrl"])
                    else:
                        source = Path(result.pop("sourcePathForImport"))
                        relative = Path(*result.pop("relativePathForImport").split("/"))
                        local, conversion = import_file(source, OUTPUT / "OL" / relative,
                                                        result["sourceSha256"])
                        delivered = REPO / local
                        result.update(localPath=local, deliveredSha256=digest(delivered),
                                      deliveredFormat=delivered.suffix.lstrip(".").lower(),
                                      deliveredBytes=delivered.stat().st_size)
                        if conversion:
                            result["conversion"] = conversion
                        added.append(result)
                        by_sha_asset[result["sourceSha256"]] = result
                        succeeded_urls.append(result["sourceUrl"])
                except Exception as exc:
                    failures.append({"sourceUrl": url, "error": str(exc)})
                if done % 20 == 0 or done == len(pending):
                    print(f"Download progress {done}/{len(pending)}; {len(added)} new SHA; "
                          f"{reused_sha} reused SHA; {len(failures)} failed", flush=True)
    for row in failures:
        failed_history[row["sourceUrl"]] = row
    for url in succeeded_urls:
        failed_history.pop(url, None)
    failure_file.write_text(json.dumps(failed_history, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    assets.extend(added)
    # A first sync also upgrades pre-existing assets with official config
    # provenance; later unchanged-cache runs return before any network call.
    if config_data:
        music, characters, skins, evidence = config_data
        mapping_stats = apply_config_mapping(assets, music, characters, skins, evidence)
    else:
        mapping_stats = {}
    catalog["assets"] = sorted(assets, key=lambda asset: asset["id"])
    catalog["configSource"] = evidence if mapping_stats else catalog.get("configSource")
    write_json_atomic(CATALOG, catalog)
    write_csv_index(catalog["assets"])
    report = {"changedCacheFiles": changed_files, "observedGameAudioUrls": len(grouped),
              "newUrlCandidates": len(pending), "downloadedValid": len(added) + reused_sha,
              "newDistinctSha256": len(added), "reusedSha256Aliases": reused_sha,
              "failures": failures, "totalAssets": len(assets), "mapping": mapping_stats}
    (SYNC / "last-run.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Imported {len(added)} distinct audio files; {reused_sha} URL alias(es); {len(failures)} failed; "
          f"{len(assets)} total catalog assets; "
          f"mapped {mapping_stats.get('heroVoiceMatched', 0)} hero voices, "
          f"{mapping_stats.get('cardVoiceMatched', 0)} card voices.")


if __name__ == "__main__":
    if "--rebuild-from-indexes" in os.sys.argv:
        os.sys.argv.remove("--rebuild-from-indexes")
        main()
    else:
        cli = argparse.ArgumentParser(description="Sync only public web.sanguosha.com audio observed in Chrome game cache")
        cli.add_argument("--sync-cache", action="store_true", help="Compatibility alias; sync is the default")
        cli.add_argument("--cache-dir", type=Path,
                         default=Path(os.environ.get("LOCALAPPDATA", "")) / "Google/Chrome/User Data/Default/Cache/Cache_Data")
        cli.add_argument("--refresh-mapping", action="store_true")
        cli.add_argument("--retry-failures", action="store_true")
        options = cli.parse_args()
        sync_cache(options.cache_dir, options.refresh_mapping, options.retry_failures)
