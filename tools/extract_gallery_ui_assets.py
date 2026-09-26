"""Extract the few gallery UI sprites used by the WPF general detail view.

Requires Pillow (``python -m pip install Pillow``). Run from the repository:
``python tools/extract_gallery_ui_assets.py`` with source atlases already in
``.artifacts/portrait-refresh/cache-extracted``, or pass ``--download-missing``
to fetch only absent public CDN sources. This script never reads browser data,
overwrites existing source files, or scales sprites.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
from pathlib import Path
from urllib.request import Request, urlopen

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / ".artifacts/portrait-refresh/cache-extracted"
OUTPUT = ROOT / "src/CardGame.Wpf/Assets"
MANIFEST = ROOT / "docs/content/gallery-ui-assets.json"
CDN = "https://web.sanguosha.com/220/h5_2/res/"

# Output name, atlas base path, exact frame key.
SPRITES = [
    ("gallery-detail-background.png", "assets/window/generalinfoBg", "GeneralInfoLifeBg.png"),
    ("gallery-skill-background.png", "assets/window/generalinfoBg", "GeneralInfo_skill_bg.png"),
    ("gallery-skill-title.png", "assets/window/generalinfoBg", "GeneralInfo_skill_title.png"),
    ("gallery-skin-title.png", "assets/window/generalInfo", "GeneralInfo_skin_titile.png"),
    ("gallery-tab-normal.png", "assets/window/generals", "general_tab_bg.png"),
    ("gallery-tab-selected.png", "assets/window/generals", "general_tabBtn_selected.png"),
    ("gallery-close-normal.png", "assets/window/generals", "general_closeBtn_up.png"),
    ("gallery-close-hover.png", "assets/window/generals", "general_closeBtn_over.png"),
    ("gallery-faction-wei.png", "assets/bigSkin/bigSkin", "bigSkin_icon_wei.png"),
    ("gallery-faction-shu.png", "assets/bigSkin/bigSkin", "bigSkin_icon_shu.png"),
    ("gallery-faction-wu.png", "assets/bigSkin/bigSkin", "bigSkin_icon_wu.png"),
    ("gallery-faction-qun.png", "assets/bigSkin/bigSkin", "bigSkin_icon_qun.png"),
    ("gallery-faction-shen.png", "assets/bigSkin/bigSkin", "bigSkin_icon_shen.png"),
    ("gallery-faction-jin.png", "assets/bigSkin/bigSkin", "bigSkin_icon_jin.png"),
    ("gallery-hp-empty.png", "assets/bigSkin/bigSkin", "bigSkin_blood_empty_hp.png"),
    ("gallery-hp-wei.png", "assets/bigSkin/bigSkin", "bigSkin_wei_hp.png"),
    ("gallery-hp-shu.png", "assets/bigSkin/bigSkin", "bigSkin_shu_hp.png"),
    ("gallery-hp-wu.png", "assets/bigSkin/bigSkin", "bigSkin_wu_hp.png"),
    ("gallery-hp-qun.png", "assets/bigSkin/bigSkin", "bigSkin_qun_hp.png"),
    ("gallery-hp-jin.png", "assets/bigSkin/bigSkin", "bigSkin_jin_hp.png"),
]

QUERIES = {
    "assets/window/generalinfoBg": ("v=9239552217", "v=12946231637"),
    "assets/window/generalInfo": ("v=7669219857", "v=17376085897"),
    "assets/window/generals": ("v=-16547105947", "v=-4321015377"),
    "assets/bigSkin/bigSkin": ("v=-20110378407", "v=-13325342087"),
}


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def ensure_sources(download_missing: bool) -> None:
    required = {
        (f"{base}.atlas", f"{CDN}{base}.atlas?{QUERIES[base][0]}")
        for _, base, _ in SPRITES
    }
    required.update({
        (f"{base}.webp", f"{CDN}{base}.webp?{QUERIES[base][1]}")
        for _, base, _ in SPRITES
    })
    required.add((
        "assets/bigSkin/bigSkin_frame_level3.png",
        CDN + "assets/bigSkin/bigSkin_frame_level3.png",
    ))
    for relative, url in sorted(required):
        path = SOURCE / relative
        if path.exists():
            continue
        if not download_missing:
            raise FileNotFoundError(
                f"Missing source {path}; pass --download-missing to fetch it from the public CDN"
            )
        with urlopen(Request(url, headers={"User-Agent": "Mozilla/5.0"}), timeout=30) as response:
            data = response.read()
        if relative.endswith(".png") and not data.startswith(b"\x89PNG\r\n\x1a\n"):
            raise ValueError(f"Unexpected PNG response from {url}")
        if relative.endswith(".webp") and not (data[:4] == b"RIFF" and data[8:12] == b"WEBP"):
            raise ValueError(f"Unexpected WebP response from {url}")
        if relative.endswith(".atlas"):
            json.loads(data.decode("utf-8"))
        path.parent.mkdir(parents=True, exist_ok=True)
        try:
            with path.open("xb") as target:
                target.write(data)
        except FileExistsError:
            pass


def extract(download_missing: bool = False) -> None:
    ensure_sources(download_missing)
    OUTPUT.mkdir(parents=True, exist_ok=True)
    MANIFEST.parent.mkdir(parents=True, exist_ok=True)
    entries = []
    atlases: dict[str, dict] = {}
    textures: dict[str, Image.Image] = {}
    for filename, base, key in SPRITES:
        atlas_path = SOURCE / f"{base}.atlas"
        texture_path = SOURCE / f"{base}.webp"
        atlas = atlases.setdefault(base, json.loads(atlas_path.read_text(encoding="utf-8")))
        texture = textures.setdefault(base, Image.open(texture_path).convert("RGBA"))
        sprite = atlas["frames"][key]
        frame = sprite["frame"]
        source_size = sprite["sourceSize"]
        sprite_source = sprite["spriteSourceSize"]
        assert frame["idx"] == 0, (base, key, "unexpected texture index")
        assert (frame["w"], frame["h"]) == (sprite_source["w"], sprite_source["h"])
        x, y, w, h = (frame[n] for n in ("x", "y", "w", "h"))
        assert x >= 0 and y >= 0 and x + w <= texture.width and y + h <= texture.height
        cropped = texture.crop((x, y, x + w, y + h))
        restored = Image.new("RGBA", (source_size["w"], source_size["h"]))
        restored.paste(cropped, (sprite_source["x"], sprite_source["y"]))
        target = OUTPUT / filename
        restored.save(target)
        atlas_query, texture_query = QUERIES[base]
        entries.append({
            "file": target.relative_to(ROOT).as_posix(),
            "width": restored.width,
            "height": restored.height,
            "sha256": sha256(target),
            "sourceTextureUrl": f"{CDN}{base}.webp?{texture_query}",
            "sourceAtlasUrl": f"{CDN}{base}.atlas?{atlas_query}",
            "sourceTextureFile": texture_path.relative_to(ROOT).as_posix(),
            "sourceAtlasFile": atlas_path.relative_to(ROOT).as_posix(),
            "frameKey": key,
            "frame": frame,
            "sourceSize": source_size,
            "spriteSourceSize": sprite_source,
        })

    frame_source = SOURCE / "assets/bigSkin/bigSkin_frame_level3.png"
    frame_target = OUTPUT / "gallery-portrait-frame.png"
    shutil.copyfile(frame_source, frame_target)
    with Image.open(frame_target) as frame_image:
        frame_size = frame_image.size
    entries.append({
        "file": frame_target.relative_to(ROOT).as_posix(),
        "width": frame_size[0],
        "height": frame_size[1],
        "sha256": sha256(frame_target),
        "sourceTextureUrl": CDN + "assets/bigSkin/bigSkin_frame_level3.png",
        "sourceTextureFile": frame_source.relative_to(ROOT).as_posix(),
        "copy": "Byte-for-byte copy of the standalone transparent PNG; no atlas frame.",
    })
    MANIFEST.write_text(json.dumps({
        "description": "Original game UI pixels; atlas frame coordinates are in texture pixels. "
                       "Where an atlas sprite is trimmed, transparent padding is restored to sourceSize "
                       "at spriteSourceSize x/y. No scaling or repainting is performed. "
                       "sha256 is the SHA-256 of each output PNG.",
        "sourceDiscovery": "4399iw2.com game resource URLs in Chrome Default/Cache/Cache_Data/data_4; "
                           "public CDN copies saved to .artifacts/portrait-refresh/cache-extracted.",
        "items": entries,
    }, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    for item in entries:
        print(f"{item['file']}: {item['width']}x{item['height']}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--download-missing",
        action="store_true",
        help="Fetch absent atlas/WebP/frame sources from the recorded public CDN URLs",
    )
    extract(parser.parse_args().download_missing)
