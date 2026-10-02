"""Sync original PNG portraits from the public official hero pages.

From the repository root, run these phases in order:
  python tools/sync_general_art.py --phase classic
  python tools/sync_general_art.py --phase skins
  python tools/sync_general_art.py --phase ol
  python tools/sync_general_art.py --phase finalize

Run --phase verify at any time to check the catalog and local PNGs offline.
Existing portraits and default selections are preserved on repeat runs.
Use repeated --key KEY options to sync only selected keys in a phase.
"""

from __future__ import annotations

import argparse
import concurrent.futures
import hashlib
import json
import re
import struct
import time
from pathlib import Path
from urllib.parse import urlparse

import requests
from bs4 import BeautifulSoup


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "src/CardGame.Wpf/Assets"
CATALOG = ROOT / "docs/content/general-art-catalog.json"
FAILURES = ROOT / "docs/content/general-art-download-failures.json"
HEROES = ROOT / ".artifacts/portrait-refresh/heroes.json"

# Explicit hero IDs avoid accidentally using an SP, boundary, god, or mou variant.
CLASSIC_HEROES = {
    "liu-bei": 1, "guan-yu": 2, "zhang-fei": 3,
    "zhuge-liang": 4, "zhao-yun": 5, "ma-chao": 6,
    "huang-yueying": 7, "sun-quan": 8, "gan-ning": 9,
    "lu-meng": 10, "huang-gai": 11, "zhou-yu": 12,
    "da-qiao": 13, "lu-xun": 14, "sun-shangxiang": 15,
    "cao-cao": 16, "sima-yi": 17, "xiahou-dun": 18,
    "zhang-liao": 19, "xu-chu": 20, "guo-jia": 21,
    "zhen-ji": 22, "hua-tuo": 23, "lu-bu": 24,
    "diao-chan": 25, "hua-xiong": 26, "huang-zhong": 27,
    "wei-yan": 28, "xiahou-yuan": 29, "cao-ren": 30,
    "xiao-qiao": 31, "zhou-tai": 32, "zhang-jiao": 33,
    "dian-wei": 35, "xun-yu": 36, "pang-tong": 37,
    "taishi-ci": 39, "yuan-shao": 42, "xu-huang": 43,
    "sun-jian": 45, "zhu-rong": 47, "meng-huo": 48,
    "xu-shu": 290, "yu-jin": 296, "gongsun-zan": 447,
    "ma-dai": 301,
    "wolong-zhuge-liang": 38, "pang-de": 40,
    "yan-liang-wen-chou": 41,
    "zhu-zhi": 339, "cao-pi": 44,
    "cao-rui": 332,
    "cao-xiu": 333,
    "zhong-yao": 334,
    "liu-chen": 335,
    "xiahou-shi": 336,
    "zhang-ni": 337,
    "sun-xiu": 338,
    "quan-cong": 340,
    "gongsun-yuan": 341,
    "guo-tu-feng-ji": 342,
    "sun-ce": 55, "cai-wen-ji": 58,
    "deng-ai": 52, "sha-mo-ke": 650, "lu-su": 50, "zhang-he": 51, "jiang-wei": 53,
    "dong-zhuo": 46,
    "liu-shan": 54,
    "jia-xu": 49,
    "zhang-zhao-zhang-hong": 56, "cao-zhi": 294,
}

# These variants have independent GeneralArt IDs and independent official pages.
OTHER_HEROES = {
    "sp-zhao-yun": 524, "sp-guan-yu": 532,
    "mou-lu-meng": 1651,
    "yan-yan": 90, "cao-zhang": 307, "gao-shun": 298,
    "liu-biao": 309, "wang-yi": 306, "zhong-hui": 308,
    "xun-you": 305, "liao-hua": 300,
    "guan-xing-zhang-bao": 299, "bu-lian-shi": 302,
    "cheng-pu": 303, "han-dang": 304, "cao-chong": 310,
    "guo-huai": 311, "man-chong": 312, "guan-ping": 313,
    "gu-yong": 329, "li-dian": 411,
    "zhu-huan": 328, "cao-ang": 404, "qu-yi": 1053,
    "shen-sima-yi": 208,
    "shen-zhou-yu": 203,
    "shen-lu-bu": 206,
    "fu-huanghou": 319,
}

OL_HEROES = {
    "boundary-sima-yi": (312, "司马懿"),
    "boundary-guo-jia": (316, "郭嘉"),
    "boundary-diao-chan": (445, "貂蝉"),
    "boundary-zhang-jiao": (448, "张角"),
    "boundary-cao-cao": (311, "曹操"),
    "boundary-zhang-liao": (314, "张辽"),
    "boundary-gan-ning": (305, "甘宁"),
    "boundary-xu-chu": (315, "许褚"),
}

# These IDs belong to the current OL site, whose roster is independent of x.sanguosha.com.
CURRENT_HEROES = {
    "guo-huanghou": (380, "郭皇后"), "li-yan": (381, "李严"),
    "sun-deng": (382, "孙登"), "liu-yu": (383, "刘虞"),
    "cen-hun": (384, "岑昏"), "sun-zi-liu-fang": (385, "孙资刘放"),
    "huang-hao": (386, "黄皓"), "zhang-rang": (387, "张让"),
    "xin-xianying": (388, "辛宪英"), "wu-xian": (389, "吴苋"),
    "xu-shi": (390, "徐氏"), "cao-jie": (391, "曹节"),
    "ji-kang": (392, "嵇康"), "qin-mi": (393, "秦宓"),
    "xue-zong": (394, "薛综"), "cai-yong": (395, "蔡邕"),
    "wang-ping": (401, "王平"), "lu-ji": (402, "陆绩"),
    "hao-zhao": (408, "郝昭"),
    "zhuge-zhan": (410, "诸葛瞻"), "chen-dao": (409, "陈到"),
    "sun-liang": (403, "孙亮"),
    "guanqiu-jian": (413, "毌丘俭"), "lu-kang": (414, "陆抗"),
    "xu-you": (406, "许攸"),
    "wang-ji": (362, "王基"), "kuai-yue-kuai-liang": (404, "蒯越蒯良"),
    "lu-zhi": (407, "卢植"),
    "yuan-shu": (100, "袁术"), "zhou-fei": (411, "周妃"),
    "zhang-xiu": (415, "张绣"),
    "yu-ji": (211, "于吉"), "zuo-ci": (56, "左慈"),
    "boundary-lu-meng": (306, "界吕蒙"),
    "boundary-huang-gai": (307, "界黄盖"),
    "boundary-lu-xun": (310, "界陆逊"),
    "boundary-liu-bei": (299, "界刘备"),
    "boundary-da-qiao": (309, "界大乔"),
    "boundary-hua-tuo": (318, "界华佗"),
    "boundary-guan-yu": (300, "界关羽"),
    "boundary-zhang-fei": (301, "界张飞"),
    "boundary-ma-chao": (303, "界马超"),
}

NEW_OFFICIAL_DEFAULTS = {"boundary-gan-ning", "boundary-xu-chu", "qu-yi", "shen-zhou-yu", "shen-lu-bu", "fu-huanghou"}

EXISTING_SOURCES = {
    "gu-yong": ("https://www.sanguosha.com/hero/329", "https://web.sanguosha.com/220/miniGame/release/laya2/res/runtime/m/general/big/static/32900.png"),
    "li-dian": ("https://www.sanguosha.com/hero/317", "https://web.sanguosha.com/220/miniGame/release/laya2/res/runtime/m/general/big/static/31700.png"),
    "yan-yan": ("https://www.sanguosha.com/hero/400", "https://web.sanguosha.com/220/miniGame/release/laya2/res/runtime/m/general/big/static/40000.png"),
    "sp-guan-yu": ("https://www.sanguosha.com/hero/106", "https://web.sanguosha.com/220/miniGame/release/laya2/res/runtime/m/general/big/static/10600.png"),
    "boundary-sima-yi": ("https://www.sanguosha.com/hero/312", "https://web.sanguosha.com/220/miniGame/release/laya2/res/runtime/m/general/big/static/31200.png"),
    "boundary-guo-jia": ("https://www.sanguosha.com/hero/316", "https://web.sanguosha.com/220/miniGame/release/laya2/res/runtime/m/general/big/static/31600.png"),
    "boundary-diao-chan": ("https://www.sanguosha.com/hero/445", "https://web.sanguosha.com/220/h5_2/res/runtime/pc/activity/shequshow/xingxiang/44501.png"),
    "boundary-cao-cao": ("https://www.sanguosha.com/hero/311", "https://web.sanguosha.com/220/miniGame/release/laya2/res/runtime/m/general/big/static/31100.png"),
    "boundary-zhang-liao": ("https://www.sanguosha.com/hero/314", "https://web.sanguosha.com/220/miniGame/release/laya2/res/runtime/m/general/big/static/31400.png"),
    "zhu-huan": ("https://www.sanguosha.com/news/20160429_4284_0716", "https://www.sanguosha.com/uploads/201604/57231faba311d.jpg"),
}


def get(url: str) -> bytes:
    for attempt in range(4):
        try:
            result = requests.get(url, timeout=30, headers={"User-Agent": "Mozilla/5.0"})
            result.raise_for_status()
            return result.content
        except (requests.RequestException, TimeoutError):
            if attempt == 3:
                raise
            time.sleep(1.5 * (attempt + 1))
    raise AssertionError("unreachable")


def parse_hero_index(html: str) -> list[dict]:
    match = re.search(r"\bconst\s+(?:heros|Listdata)\s*=\s*", html)
    if match is None:
        raise ValueError("Official hero page has no const heros/Listdata index")
    heroes, _ = json.JSONDecoder().raw_decode(html[match.end():])
    if not isinstance(heroes, list) or not heroes or any(
        not isinstance(item, dict) or not isinstance(item.get("gid"), int)
        or not isinstance(item.get("name"), str) or not item["name"]
        for item in heroes
    ):
        raise ValueError("Official hero index has an unexpected schema")
    return heroes


def load_heroes() -> dict[int, dict]:
    if HEROES.exists():
        items = json.loads(HEROES.read_text(encoding="utf-8"))
    else:
        items = parse_hero_index(get("https://x.sanguosha.com/hero").decode("utf-8"))
        HEROES.parent.mkdir(parents=True, exist_ok=True)
        HEROES.write_text(json.dumps(items, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    heroes = {item["gid"]: item for item in items}
    expected = set(CLASSIC_HEROES.values()) | set(OTHER_HEROES.values())
    missing = expected - heroes.keys()
    if missing:
        raise ValueError(f"Official hero index is missing IDs: {sorted(missing)}")
    return heroes


def png_info(data: bytes) -> tuple[str, int, int]:
    if len(data) < 33 or data[:8] != b"\x89PNG\r\n\x1a\n" or data[12:16] != b"IHDR":
        raise ValueError("Response is not a PNG")
    width, height = struct.unpack(">II", data[16:24])
    if not (100 <= width <= 5000 and 100 <= height <= 5000) or b"IEND" not in data[-64:]:
        raise ValueError(f"Incomplete or implausible PNG: {width}x{height}")
    return hashlib.sha256(data).hexdigest(), width, height


def page_skins(gid: int) -> list[dict]:
    page = f"https://x.sanguosha.com/hero/{gid}.html"
    soup = BeautifulSoup(get(page).decode("utf-8"), "html.parser")
    skins = []
    for block in soup.select(".skin"):
        img = block.select_one("img.yang-img")
        title = block.select_one(".skin-name")
        if img is None or title is None or not img.get("src"):
            continue
        url = img["src"].strip()
        resource_id = Path(urlparse(url).path).stem
        if not re.fullmatch(r"\d+", resource_id):
            continue
        skins.append({"id": resource_id, "name": title.get_text(" ", strip=True),
                      "sourcePage": page, "imageUrl": url})
    if not skins:
        raise ValueError(f"No official skin images: {page}")
    return skins


def ol_page_skins(gid: int, expected_name: str | None = None) -> list[dict]:
    page = f"https://www.sanguosha.com/hero/{gid}"
    soup = BeautifulSoup(get(page).decode("utf-8"), "html.parser")
    actual_name = soup.select_one(".hero-card-name")
    if expected_name is not None and (
        actual_name is None or actual_name.get_text(strip=True) != expected_name
    ):
        raise ValueError(f"Unexpected hero identity at {page}: {actual_name.get_text(strip=True) if actual_name else '(missing)'}")
    images = [soup.select_one(".hero-detail-card img")]
    images += soup.select(".hero-detail-nav img[data-url]")
    skins = []
    for index, img in enumerate(images):
        if img is None:
            continue
        url = (img.get("data-url") or img.get("src") or "").replace("web.sanguosha.com//", "web.sanguosha.com/")
        resource_id = Path(urlparse(url).path).stem
        if not re.fullmatch(r"\d+", resource_id):
            continue
        skins.append({"id": resource_id,
                      "name": "经典形象" if index == 0 else f"官方皮肤 {resource_id}（官网未标原名）",
                      "sourcePage": page, "imageUrl": url})
    if not skins:
        raise ValueError(f"No official skin images: {page}")
    return skins


def save_image(path: Path, skin: dict) -> dict:
    data = path.read_bytes() if path.exists() else get(skin["imageUrl"])
    sha, width, height = png_info(data)
    if not path.exists():
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
    return {**skin, "localPath": path.relative_to(ROOT).as_posix(),
            "sha256": sha, "pixelWidth": width, "pixelHeight": height}


def verify_catalog(catalog: dict) -> None:
    if catalog.get("schemaVersion") != 1 or not isinstance(catalog.get("entries"), list):
        raise ValueError("Invalid catalog schema")
    if not catalog["entries"]:
        raise ValueError("Catalog has no portrait entries")
    entry_keys = set()
    image_count = 0
    root = ROOT.resolve()
    for entry in catalog["entries"]:
        key = entry.get("key")
        if not isinstance(key, str) or not key or key in entry_keys:
            raise ValueError(f"Missing or duplicate catalog key: {key!r}")
        entry_keys.add(key)
        skins = entry.get("skins")
        if not isinstance(skins, list) or not skins:
            raise ValueError(f"No skins for {key}")
        skin_ids = set()
        for skin in skins:
            skin_id = skin.get("id")
            if not isinstance(skin_id, str) or not skin_id or skin_id in skin_ids:
                raise ValueError(f"Missing or duplicate skin ID for {key}: {skin_id!r}")
            skin_ids.add(skin_id)
            local_path = skin.get("localPath")
            if not isinstance(local_path, str):
                raise ValueError(f"Missing localPath for {key}/{skin_id}")
            path = (ROOT / local_path).resolve()
            if not path.is_relative_to(root) or not path.is_file():
                raise ValueError(f"Missing or invalid PNG path for {key}/{skin_id}: {local_path}")
            digest, width, height = png_info(path.read_bytes())
            if (digest, width, height) != (
                skin.get("sha256"), skin.get("pixelWidth"), skin.get("pixelHeight")
            ):
                raise ValueError(f"PNG hash or dimensions changed for {key}/{skin_id}: {local_path}")
            image_count += 1
        if entry.get("defaultSkinId") not in skin_ids:
            raise ValueError(f"Invalid defaultSkinId for {key}")
    print(f"Verified {len(entry_keys)} generals and {image_count} PNG records offline.")


def finalize(entries: dict[str, dict], only_keys: set[str] | None = None) -> None:
    wiki = {item["portraitKey"]: item for item in json.loads((ROOT / "docs/content/bwiki-portraits.json").read_text(encoding="utf-8"))}
    for key, entry in entries.items():
        if only_keys is not None and key not in only_keys:
            continue
        official = ASSETS / f"official-{key}.png"
        if not official.exists():
            continue
        digest, width, height = png_info(official.read_bytes())
        default = next((skin for skin in entry["skins"] if skin["id"] == entry["defaultSkinId"]), None)
        if default is not None and default["sha256"] == digest:
            default["localPath"] = official.relative_to(ROOT).as_posix()
        elif key in EXISTING_SOURCES:
            page, url = EXISTING_SOURCES[key]
            entry["skins"].insert(0, {
                "id": "current", "name": "当前默认形象", "localPath": official.relative_to(ROOT).as_posix(),
                "sourcePage": page, "imageUrl": url, "sha256": digest,
                "pixelWidth": width, "pixelHeight": height,
            })
            entry["defaultSkinId"] = "current"
        else:
            raise ValueError(f"Cannot verify existing default source for {key}")

    for key in ("sp-zhao-yun", "shen-guan-yu"):
        if only_keys is not None and key not in only_keys:
            continue
        item = wiki[key]
        art = item["artwork"]
        path = ASSETS / art["localFilename"]
        digest, width, height = png_info(path.read_bytes())
        entry = entries.setdefault(key, {"key": key, "characterName": "SP赵云" if key == "sp-zhao-yun" else "神关羽", "defaultSkinId": "current", "skins": []})
        entry["skins"] = [skin for skin in entry["skins"] if skin["id"] != "current"]
        entry["skins"].insert(0, {
            "id": "current", "name": "当前默认形象",
            "localPath": path.relative_to(ROOT).as_posix(),
            "sourcePage": art["sourcePage"], "imageUrl": art["imageUrl"],
            "sha256": digest, "pixelWidth": width, "pixelHeight": height,
        })
        entry["defaultSkinId"] = "current"

    for key in OL_HEROES:
        if only_keys is not None and key not in only_keys:
            continue
        if key not in entries:
            raise ValueError(f"Missing OL portrait {key}; run --phase ol")
        entries[key]["characterName"] = "界" + OL_HEROES[key][1]
        if key in NEW_OL_DEFAULTS:
            for skin in entries[key]["skins"]:
                if skin["name"].startswith("官方皮肤 ") and "官网未标原名" not in skin["name"]:
                    skin["name"] += "（官网未标原名）"
    for key, name in {"sp-zhao-yun": "SP赵云", "sp-guan-yu": "SP关羽", "mou-lu-meng": "谋吕蒙"}.items():
        if only_keys is not None and key not in only_keys:
            continue
        if key in entries:
            entries[key]["characterName"] = name

    for key, entry in entries.items():
        if only_keys is not None and key not in only_keys:
            continue
        seen = set()
        ordered = sorted(entry["skins"], key=lambda skin: skin["id"] != entry["defaultSkinId"])
        unique = []
        for skin in ordered:
            if skin["sha256"] in seen:
                continue
            seen.add(skin["sha256"])
            unique.append(skin)
        entry["skins"] = unique


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--phase", choices=("classic", "skins", "ol", "finalize", "verify"), required=True)
    parser.add_argument("--key", action="append", dest="keys", metavar="KEY",
                        help="Only sync this general key; repeat to select more")
    args = parser.parse_args()
    only_keys = set(args.keys) if args.keys else None
    if args.phase == "verify" and only_keys is not None:
        parser.error("--phase verify always checks the entire catalog")
    catalog = json.loads(CATALOG.read_text(encoding="utf-8")) if CATALOG.exists() else {"schemaVersion": 1, "entries": []}
    if args.phase == "verify":
        verify_catalog(catalog)
        return
    entries = {entry["key"]: entry for entry in catalog["entries"]}
    if args.phase == "finalize":
        if only_keys is not None:
            missing = only_keys - entries.keys()
            if missing:
                parser.error(f"Unknown catalog keys: {sorted(missing)}")
        finalize(entries, only_keys)
        catalog["entries"] = [entries[k] for k in sorted(entries)]
        CATALOG.write_text(json.dumps(catalog, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        return
    heroes = load_heroes() if args.phase in ("classic", "skins") else {}
    targets = dict(CLASSIC_HEROES)
    if args.phase == "skins":
        targets.update({key: gid for key, gid in OTHER_HEROES.items() if gid is not None})
    if args.phase in ("classic", "skins"):
        targets.update({key: gid for key, (gid, _) in CURRENT_HEROES.items()})
    if args.phase == "ol":
        targets = {key: gid for key, (gid, _) in OL_HEROES.items()}
    if only_keys is not None:
        missing = only_keys - targets.keys()
        if missing:
            parser.error(f"Keys unavailable in {args.phase} phase: {sorted(missing)}")
        targets = {key: gid for key, gid in targets.items() if key in only_keys}
    def process(target: tuple[str, int]) -> tuple[str, dict, list[dict]]:
        key, gid = target
        page_items = (ol_page_skins(gid, CURRENT_HEROES[key][1]) if key in CURRENT_HEROES else
                      ol_page_skins(gid, "界" + OL_HEROES[key][1]) if args.phase == "ol" else page_skins(gid))
        canonical = next((x for x in page_items if x["name"].startswith("经典形象")), page_items[0])
        character_name = (CURRENT_HEROES[key][1] if key in CURRENT_HEROES else
                          OL_HEROES[key][1] if args.phase == "ol" else heroes[gid]["name"])
        entry = entries.get(key, {"key": key, "characterName": character_name, "defaultSkinId": canonical["id"], "skins": []})
        known = {skin["id"]: skin for skin in entry["skins"]}
        errors = []
        selected = [canonical] if args.phase == "classic" else page_items
        for skin in selected:
            existing_default = ASSETS / f"official-{key}.png"
            if args.phase == "classic" or (skin["id"] == entry["defaultSkinId"] and (key in CLASSIC_HEROES or key in CURRENT_HEROES or key == "boundary-zhang-jiao" or key in NEW_OFFICIAL_DEFAULTS)):
                path = existing_default
            else:
                path = ASSETS / "Skins" / key / f"{skin['id']}.png"
            if skin["id"] not in known:
                try:
                    record = save_image(path, skin)
                    known[skin["id"]] = record
                except (requests.RequestException, ValueError, OSError) as exc:
                    errors.append({"key": key, **skin, "error": str(exc)})
        entry["skins"] = list(known.values())
        return key, entry, errors

    failures = json.loads(FAILURES.read_text(encoding="utf-8")) if FAILURES.exists() else []
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        futures = {pool.submit(process, target): target[0] for target in targets.items()}
        for future in concurrent.futures.as_completed(futures):
            try:
                key, entry, errors = future.result()
            except Exception as exc:
                failures.append({"key": futures[future], "error": str(exc)})
                continue
            entries[key] = entry
            failures.extend(error for error in errors if error not in failures)
            print(f"{key}: {len(entry['skins'])} skin(s)", flush=True)
            catalog["entries"] = [entries[k] for k in sorted(entries)]
            CATALOG.parent.mkdir(parents=True, exist_ok=True)
            CATALOG.write_text(json.dumps(catalog, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
            FAILURES.write_text(json.dumps(failures, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
