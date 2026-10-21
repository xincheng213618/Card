"""Import the public OL single-duel shop catalogue with exact source identities.

Prices and qualities come from hd_1v1_rogue, never from the shared Shanhe
catalogue. Shared rows supply descriptions and versioned spell identities.
The client implements this public resource decoder in aCt.InitOfb/pR.WorkParse.
"""
from __future__ import annotations

import argparse
import gzip
import hashlib
import io
import json
import re
import zipfile
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timedelta, timezone
from pathlib import Path

import requests
from Crypto.Cipher import AES

REPO = Path(__file__).resolve().parents[1]
BASE = "https://web.sanguosha.com/220/h5_2/"


def sync_tactic_artwork(tactics: dict, offers: list[dict]) -> None:
    """Fill missing shared local icons using the client's exact plotpic mapping."""
    pending = []
    for offer in offers:
        tactic_id = offer["CitationID"]
        target = REPO / f"src/CardGame.Wpf/Assets/Shanhe/{tactic_id}.png"
        if target.exists():
            continue
        picture = tactics[tactic_id]["plotpic"]
        if not picture.endswith(".png") or Path(picture).name != picture:
            raise ValueError(f"Invalid tactic image name: {picture!r}")
        pending.append((tactic_id, target, "res/runtime/pc/pattern/roguelike/zhanfa/" + picture))
    if not pending:
        print("All single-duel tactic icons are already present.")
        return

    response = requests.get(BASE + "version.json", timeout=(10, 45))
    response.raise_for_status()
    versions = response.json()
    urls = {key: BASE + key + ("?v=" + str(versions[key]) if key in versions else "")
            for _, _, key in pending}

    def download(key: str) -> tuple[str, bytes]:
        response = requests.get(urls[key], timeout=(10, 45))
        response.raise_for_status()
        data = response.content
        if not data.startswith(b"\x89PNG\r\n\x1a\n"):
            raise ValueError(f"Expected PNG artwork: {urls[key]}")
        return key, data

    # Fetch each shared picture once; validate the whole batch before writing assets.
    with ThreadPoolExecutor(max_workers=8) as pool:
        images = dict(pool.map(download, sorted(urls)))
    manifest_path = REPO / "docs/content/wushuang-ui-assets.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    existing = {asset["file"] for asset in manifest["assets"]}
    for tactic_id, target, key in pending:
        data = images[key]
        target.write_bytes(data)
        file = target.relative_to(REPO).as_posix()
        if file not in existing:
            manifest["assets"].append({"file": file, "sourceUrl": urls[key],
                                       "sha256": hashlib.sha256(data).hexdigest(),
                                       "sourcePlotId": tactic_id})
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Imported {len(pending)} missing tactic icons from {len(images)} source pictures.")


def fetch_catalogue(*, sync_artwork: bool = False) -> dict:
    def fetch(url: str) -> bytes:
        response = requests.get(url, timeout=(10, 45))
        response.raise_for_status()
        return response.content

    bundle = fetch(BASE + "sgsGame_a.sgs")
    config = fetch(BASE + "res/config/Config.sgs")
    with zipfile.ZipFile(io.BytesIO(bundle)) as archive:
        script = archive.read("sgsGame_a.sgs").decode("utf-8")
    match = re.search(
        r"InitOfb\(\)\{var t=new Uint8Array\(\[([0-9,]+)\]\);this\.jofb="
        r"new aesjs\.ModeOfOperation\.cfb\(\[([0-9,]+)\],t,16\)", script)
    if not match:
        raise ValueError("Public client decoder changed; refusing to guess.")
    iv, key = (bytes(map(int, match.group(i).split(","))) for i in (1, 2))
    members = {}
    with zipfile.ZipFile(io.BytesIO(config)) as archive:
        def read(name: str) -> dict:
            raw = archive.read(name)
            plain = gzip.decompress(AES.new(key, AES.MODE_CFB, iv=iv, segment_size=128).decrypt(raw))
            members[name] = hashlib.sha256(plain).hexdigest()
            return json.loads(plain)

        mode = read("hd_1v1_rogue.sgs")
        shared = read("hd_roguelike.sgs")["Root"]
        spells = {row["a"]: row for row in read("cha_spell.sgs")["GameSpells"]["spell"]}
        card_source = read("sys_playcard.sgs")
        card_definitions = {row["a"]: row for row in card_source["GamePlayCards"]["card"]}
        card_descriptions = {row["res"]: row.get("dese", "") for row in card_source["CardsBook"] if "res" in row}
        card_offers = {row["id"]: row for row in shared["Card"]}
        cards = []
        for row in mode["Card"]:
            item = card_offers[row["CitationID"]]
            card = card_definitions[item["cardid"]]
            cards.append({"id": row["CitationID"], "cardId": item["cardid"], "class": card["p"],
                          "name": card["b"], "description": card_descriptions.get(card["p"], ""),
                          "suit": {1: "Diamond", 2: "Heart", 3: "Spade", 4: "Club"}[card["g"]],
                          "rank": card["f"], "tokens": row["money"], "quality": row["level"],
                          "isEquipment": bool(item.get("isequip")), "autoEquip": bool(item.get("autoequip"))})
        tactics = {row["plot"]: row for row in shared["Tactics"]}
        if sync_artwork:
            sync_tactic_artwork(tactics, mode["Tactics"])
        spell_offers = {row["id"]: row for row in shared["Spell"]}
        rows = []
        for row in mode["Spell"]:
            definition = spell_offers[row["CitationID"]]
            spell = spells.get(definition["spellid"], {})
            rows.append({
                "id": row["CitationID"], "spellId": definition["spellid"],
                "class": spell.get("d"), "name": spell.get("c"), "description": spell.get("o"),
                "tokens": row["money"], "quality": row["level"],
                "relatedSpellIds": definition.get("relel", ""),
            })
        return {
            "verifiedOn": datetime.now(timezone(timedelta(hours=8))).date().isoformat(),
            "sourceUrl": BASE + "res/config/Config.sgs",
            "sourceSha256": hashlib.sha256(config).hexdigest(),
            "clientBundleSha256": hashlib.sha256(bundle).hexdigest(),
            "decodedMemberSha256": members,
            "tables": ["hd_1v1_rogue.Tactics", "hd_1v1_rogue.Spell", "hd_1v1_rogue.Card", "hd_roguelike.Root.Tactics", "hd_roguelike.Root.Spell", "hd_roguelike.Root.Card", "cha_spell.GameSpells.spell", "sys_playcard.GamePlayCards.card"],
            "note": "Public client catalogue; not evidence of live server availability or random weights. Shared catalogue prices are not single-duel prices.",
            "shopSeconds": mode["Root"]["Label"][0]["buyTime"], "timeLimitSeconds": mode["Root"]["Label"][0]["overtime"],
            "skills": rows,
            "cards": cards,
            "tactics": [{"id": row["CitationID"], "name": tactics[row["CitationID"]]["plotname"],
                         "description": tactics[row["CitationID"]]["plotdesc"], "tokens": row["money"],
                         "quality": row["level"], "sourceQuality": tactics[row["CitationID"]]["level"],
                         "relatedTacticIds": tactics[row["CitationID"]].get("relel", "")}
                        for row in mode["Tactics"]],
        }


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--artwork-only", action="store_true",
                        help="Fill missing official tactic icons without rewriting the catalogue.")
    args = parser.parse_args()
    catalogue = fetch_catalogue(sync_artwork=args.artwork_only)
    if args.artwork_only:
        raise SystemExit(0)
    target = REPO / "docs/content/wushuang-reference.json"
    target.write_text(json.dumps(catalogue, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Imported {len(catalogue['skills'])} skills and {len(catalogue['tactics'])} tactics.")
    for row in catalogue["skills"]:
        if not row["name"]:
            print("Unresolved spell:", row["id"], row["spellId"])
