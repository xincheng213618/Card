"""Import the public OL single-duel shop catalogue with exact source identities.

Prices and qualities come from hd_1v1_rogue, never from the shared Shanhe
catalogue. Shared rows supply descriptions and versioned spell identities.
The client implements this public resource decoder in aCt.InitOfb/pR.WorkParse.
"""
from __future__ import annotations

import gzip
import hashlib
import io
import json
import re
import zipfile
from datetime import date
from pathlib import Path

import requests
from Crypto.Cipher import AES

REPO = Path(__file__).resolve().parents[1]
BASE = "https://web.sanguosha.com/220/h5_2/"


def fetch_catalogue() -> dict:
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
        tactics = {row["plot"]: row for row in shared["Tactics"]}
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
            "verifiedOn": date(2026, 10, 21).isoformat(),
            "sourceUrl": BASE + "res/config/Config.sgs",
            "sourceSha256": hashlib.sha256(config).hexdigest(),
            "clientBundleSha256": hashlib.sha256(bundle).hexdigest(),
            "decodedMemberSha256": members,
            "tables": ["hd_1v1_rogue.Tactics", "hd_1v1_rogue.Spell", "hd_roguelike.Root.Tactics", "hd_roguelike.Root.Spell", "cha_spell.GameSpells.spell"],
            "note": "Public client catalogue; not evidence of live server availability or random weights. Shared catalogue prices are not single-duel prices.",
            "shopSeconds": mode["Root"]["Label"][0]["buyTime"], "timeLimitSeconds": mode["Root"]["Label"][0]["overtime"],
            "skills": rows,
            "tactics": [{"id": row["CitationID"], "name": tactics[row["CitationID"]]["plotname"],
                         "description": tactics[row["CitationID"]]["plotdesc"], "tokens": row["money"],
                         "quality": row["level"], "sourceQuality": tactics[row["CitationID"]]["level"],
                         "relatedTacticIds": tactics[row["CitationID"]].get("relel", "")}
                        for row in mode["Tactics"]],
        }


if __name__ == "__main__":
    catalogue = fetch_catalogue()
    target = REPO / "docs/content/wushuang-reference.json"
    target.write_text(json.dumps(catalogue, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Imported {len(catalogue['skills'])} skills and {len(catalogue['tactics'])} tactics.")
    for row in catalogue["skills"]:
        if not row["name"]:
            print("Unresolved spell:", row["id"], row["spellId"])
