#!/usr/bin/env python3
"""Fetch the rules-oriented BWIKI Sanguosha reference corpus.

Only directly related general and skill category pages are queried.  Wikitext is
treated as data: this module never executes or expands templates.
"""

from __future__ import annotations

import argparse
import hashlib
import html
import json
import random
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable

API = "https://wiki.biligame.com/sgs/api.php"
PAGE_BASE = "https://wiki.biligame.com/sgs/"
GENERAL_CATEGORY = "分类:武将图鉴测试"
SKILL_CATEGORY = "分类:技能"
USER_AGENT = "CardGame-BWIKI-reference-sync/1.0 (rules research; low-rate batch client)"
GENERAL_TEMPLATE = "武将"
SKILL_TEMPLATE = "技能/武将"
IGNORED_FIELD_PARTS = ("台词", "配音", "故事", "传记", "画师", "获取方式", "上线时间", "品质")
GENERAL_RULE_PARTS = (
    "武将名", "势力", "性别", "别称", "称号", "出现模式", "武将包", "体力", "技能",
    "兵种", "卡牌", "定位", "规则", "模式", "版本", "阵亡", "主公",
)


class ApiError(RuntimeError):
    pass


@dataclass
class Template:
    name: str
    fields: dict[str, str]
    raw: str
    anomalies: list[str]


def _masked_regions(text: str) -> list[tuple[int, int]]:
    regions: list[tuple[int, int]] = []
    pattern = re.compile(r"<!--.*?-->|<nowiki\b[^>]*>.*?</nowiki\s*>", re.I | re.S)
    regions.extend((m.start(), m.end()) for m in pattern.finditer(text))
    return regions


def _in_region(pos: int, regions: list[tuple[int, int]], cursor: int) -> tuple[bool, int]:
    while cursor < len(regions) and pos >= regions[cursor][1]:
        cursor += 1
    return (cursor < len(regions) and regions[cursor][0] <= pos < regions[cursor][1], cursor)


def extract_templates(text: str) -> tuple[list[str], list[str]]:
    """Return top-level balanced templates and structural anomalies."""
    regions = _masked_regions(text)
    stack: list[int] = []
    result: list[str] = []
    anomalies: list[str] = []
    i = cursor = 0
    while i < len(text) - 1:
        masked, cursor = _in_region(i, regions, cursor)
        if masked:
            i = regions[cursor][1]
            continue
        pair = text[i : i + 2]
        if pair == "{{":
            stack.append(i)
            i += 2
            continue
        if pair == "}}":
            if not stack:
                anomalies.append(f"unmatched template close at offset {i}")
            else:
                start = stack.pop()
                if not stack:
                    result.append(text[start : i + 2])
            i += 2
            continue
        i += 1
    if stack:
        anomalies.append(f"unclosed template at offset {stack[0]}")
    return result, anomalies


def split_top_level(text: str, separator: str) -> list[str]:
    regions = _masked_regions(text)
    braces = brackets = 0
    parts: list[str] = []
    start = i = cursor = 0
    while i < len(text):
        masked, cursor = _in_region(i, regions, cursor)
        if masked:
            i = regions[cursor][1]
            continue
        pair = text[i : i + 2]
        if pair == "{{":
            braces += 1; i += 2; continue
        if pair == "}}" and braces:
            braces -= 1; i += 2; continue
        if pair == "[[":
            brackets += 1; i += 2; continue
        if pair == "]]" and brackets:
            brackets -= 1; i += 2; continue
        if text[i] == separator and braces == 0 and brackets == 0:
            parts.append(text[start:i]); start = i + 1
        i += 1
    parts.append(text[start:])
    return parts


def parse_template(raw: str) -> Template:
    body = raw[2:-2]
    parts = split_top_level(body, "|")
    name = parts[0].strip()
    fields: dict[str, str] = {}
    anomalies: list[str] = []
    positional = 1
    for part in parts[1:]:
        eq = split_top_level(part, "=")
        if len(eq) == 1:
            key, value = str(positional), part.strip(); positional += 1
        else:
            key, value = eq[0].strip(), "=".join(eq[1:]).strip()
        if key in fields:
            anomalies.append(f"duplicate field {key!r}")
            suffix = 2
            while f"{key}#{suffix}" in fields:
                suffix += 1
            key = f"{key}#{suffix}"
        fields[key] = value
    return Template(name=name, fields=fields, raw=raw, anomalies=anomalies)


def parse_templates(text: str) -> tuple[list[Template], list[str]]:
    raws, anomalies = extract_templates(text)
    templates = [parse_template(raw) for raw in raws]
    return templates, anomalies


def page_url(title: str) -> str:
    return PAGE_BASE + urllib.parse.quote(title.replace(" ", "_"), safe="/:()")


class Client:
    def __init__(self, cache: Path, delay_min: float, delay_max: float, retries: int, refresh: bool):
        self.cache = cache
        self.cache.mkdir(parents=True, exist_ok=True)
        self.delay_min = delay_min
        self.delay_max = delay_max
        self.retries = retries
        self.refresh = refresh
        self.request_count = 0
        self.cache_hits = 0
        self.cache_timestamps: list[str] = []

    def get(self, params: dict[str, str], cache_key: str) -> dict[str, Any]:
        request_hash = hashlib.sha256(json.dumps(params, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode()).hexdigest()[:20]
        path = self.cache / f"{cache_key}-{request_hash}.json"
        if path.exists() and not self.refresh:
            self.cache_hits += 1
            cached = json.loads(path.read_text(encoding="utf-8"))
            if "data" in cached and "cachedAt" in cached:
                self.cache_timestamps.append(cached["cachedAt"])
                return cached["data"]
            return cached
        url = API + "?" + urllib.parse.urlencode(params)
        last: Exception | None = None
        for attempt in range(self.retries + 1):
            try:
                if self.request_count:
                    time.sleep(random.uniform(self.delay_min, self.delay_max))
                request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT, "Accept": "application/json"})
                with urllib.request.urlopen(request, timeout=40) as response:
                    data = json.load(response)
                self.request_count += 1
                if "error" in data:
                    raise ApiError(json.dumps(data["error"], ensure_ascii=False))
                cached_at = datetime.now(timezone.utc).isoformat()
                self.cache_timestamps.append(cached_at)
                path.write_text(json.dumps({"cachedAt": cached_at, "requestParams": params, "data": data}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
                return data
            except (OSError, TimeoutError, json.JSONDecodeError, ApiError) as exc:
                last = exc
                code = getattr(exc, "code", None)
                if attempt >= self.retries:
                    break
                time.sleep((3 if code == 429 else 1) * (2 ** attempt) + random.random())
        raise ApiError(f"API request failed after {self.retries + 1} attempts: {last}")


def get_category(client: Client, title: str, slug: str) -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
    members: list[dict[str, Any]] = []
    pagination: list[dict[str, Any]] = []
    cont: dict[str, str] = {}
    page_no = 1
    while True:
        params = {"action": "query", "format": "json", "formatversion": "2", "list": "categorymembers",
                  "cmtitle": title, "cmtype": "page", "cmlimit": "500", **cont}
        data = client.get(params, f"category-{slug}-{page_no}")
        batch = data.get("query", {}).get("categorymembers", [])
        members.extend(batch)
        pagination.append({"page": page_no, "count": len(batch), "continue": data.get("continue")})
        if "continue" not in data:
            break
        cont = {str(k): str(v) for k, v in data["continue"].items()}
        page_no += 1
    return members, pagination


def chunks(items: list[dict[str, Any]], size: int) -> Iterable[list[dict[str, Any]]]:
    for offset in range(0, len(items), size):
        yield items[offset : offset + size]


def get_pages(client: Client, members: list[dict[str, Any]], slug: str) -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
    pages: list[dict[str, Any]] = []
    failures: list[dict[str, Any]] = []
    for number, batch in enumerate(chunks(members, 50), 1):
        titles = "|".join(item["title"] for item in batch)
        digest = hashlib.sha256(titles.encode()).hexdigest()[:16]
        params = {"action": "query", "format": "json", "formatversion": "2", "prop": "revisions|pageimages",
                  "rvprop": "ids|timestamp|content", "rvslots": "main", "piprop": "name|original", "titles": titles}
        try:
            data = client.get(params, f"pages-{slug}-{number:03d}-{digest}")
            returned = data.get("query", {}).get("pages", [])
            pages.extend(returned)
            expected = {item["pageid"]: item for item in batch}
            returned_ids = {page.get("pageid") for page in returned if page.get("pageid") is not None}
            returned_titles = {page.get("title") for page in returned}
            for pageid, item in expected.items():
                if pageid not in returned_ids and item["title"] not in returned_titles:
                    failures.append({"title": item["title"], "pageid": pageid, "reason": "page omitted from API batch response"})
            for page in returned:
                if page.get("missing") or not page.get("revisions"):
                    failures.append({"title": page.get("title"), "pageid": page.get("pageid"), "reason": "missing page or revision"})
        except ApiError as exc:
            failures.extend({"title": item["title"], "pageid": item["pageid"], "reason": str(exc)} for item in batch)
    return pages, failures


def revision_content(page: dict[str, Any]) -> tuple[dict[str, Any], str, str]:
    revision = page["revisions"][0]
    slot = revision.get("slots", {}).get("main", {})
    source = slot.get("content", "")
    return revision, source, slot.get("sourceSha256") or hashlib.sha256(source.encode("utf-8")).hexdigest()


def cleaned_fields(template: Template, kind: str) -> dict[str, str]:
    result: dict[str, str] = {}
    for key, value in template.fields.items():
        if any(part in key for part in IGNORED_FIELD_PARTS):
            continue
        if kind == "general" and not any(part in key for part in GENERAL_RULE_PARTS):
            continue
        result[key] = html.unescape(value)
    return result


def raw_from_fields(name: str, fields: dict[str, str]) -> str:
    lines = ["{{" + name]
    lines.extend(f"|{key}={value}" for key, value in fields.items())
    lines.append("}}")
    return "\n".join(lines)


def make_common(page: dict[str, Any], revision: dict[str, Any], source_sha256: str) -> dict[str, Any]:
    return {"pageid": page["pageid"], "namespace": page.get("ns"), "title": page["title"], "url": page_url(page["title"]),
            "revid": revision["revid"], "parentid": revision.get("parentid"), "timestamp": revision["timestamp"],
            "sourceSha256": source_sha256,
            "image": {k: page[k] for k in ("pageimage", "original") if k in page}}


def process_pages(pages: list[dict[str, Any]], kind: str, page_dir: Path) -> tuple[list[dict[str, Any]], dict[str, Any]]:
    records: list[dict[str, Any]] = []
    stats: dict[str, Any] = {"pagesFetched": len(pages), "pagesParsed": 0, "records": 0, "unparsedPages": [],
                             "parseAnomalies": [], "unsupportedTemplates": [], "excludedNamespacePages": []}
    wanted = GENERAL_TEMPLATE if kind == "general" else SKILL_TEMPLATE
    for page in pages:
        if page.get("missing") or not page.get("revisions"):
            continue
        if page.get("ns", 0) != 0:
            stats["excludedNamespacePages"].append({"pageid": page["pageid"], "namespace": page.get("ns"), "title": page["title"]})
            continue
        revision, source, source_sha256 = revision_content(page)
        templates, structural = parse_templates(source)
        selected = [template for template in templates if template.name.strip() == wanted]
        common = make_common(page, revision, source_sha256)
        if structural:
            stats["parseAnomalies"].append({"pageid": page["pageid"], "title": page["title"], "details": structural})
        for template in templates:
            if template.anomalies:
                stats["parseAnomalies"].append({"pageid": page["pageid"], "title": page["title"], "template": template.name,
                                                "details": template.anomalies})
        if not selected:
            names = [template.name for template in templates]
            stats["unparsedPages"].append({"pageid": page["pageid"], "title": page["title"], "templates": names})
            for name in names:
                if name not in stats["unsupportedTemplates"]:
                    stats["unsupportedTemplates"].append(name)
        else:
            stats["pagesParsed"] += 1
        page_records: list[dict[str, Any]] = []
        for ordinal, template in enumerate(selected, 1):
            fields = cleaned_fields(template, kind)
            record = dict(common)
            record.update({"templateOrdinal": ordinal, "fields": fields, "source": raw_from_fields(template.name, fields)})
            if kind == "skill":
                record.update({"name": fields.get("技能名", page["title"]), "generals": [x.strip() for x in re.split(r"[,，、/]", fields.get("所属武将", "")) if x.strip()],
                               "mode": fields.get("技能版本"), "version": fields.get("历史版本") or fields.get("技能版本"),
                               "descriptions": {k: v for k, v in fields.items() if "技能描述" in k}})
            else:
                record.update({"name": fields.get("武将名", page["title"]), "modes": [x.strip() for x in re.split(r"[,，、]", fields.get("出现模式", "")) if x.strip()]})
            records.append(record); page_records.append(record)
        page_doc = {**common, "kind": kind, "records": page_records,
                    "unparsedTemplateNames": [] if selected else [template.name for template in templates]}
        (page_dir / f"{page['pageid']}.json").write_text(json.dumps(page_doc, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    stats["records"] = len(records)
    stats["unsupportedTemplates"].sort()
    return records, stats


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=Path("data/reference/bwiki"))
    parser.add_argument("--refresh", action="store_true", help="ignore cached API responses")
    parser.add_argument("--delay-min", type=float, default=0.4)
    parser.add_argument("--delay-max", type=float, default=0.8)
    parser.add_argument("--retries", type=int, default=2)
    args = parser.parse_args(argv)
    if args.delay_min < 0 or args.delay_max < args.delay_min:
        parser.error("invalid delay range")
    if args.retries < 0:
        parser.error("retries must be non-negative")
    output = args.output.resolve()
    staging = output / ".staging"
    page_dir = staging / "pages"
    page_dir.mkdir(parents=True, exist_ok=True)
    client = Client(output / ".cache", args.delay_min, args.delay_max, args.retries, args.refresh)
    started = datetime.now(timezone.utc)
    failures: list[dict[str, Any]] = []
    publication_started = False
    try:
        general_members, general_paging = get_category(client, GENERAL_CATEGORY, "generals")
        skill_members, skill_paging = get_category(client, SKILL_CATEGORY, "skills")
        write_json(staging / "category-generals.json", general_members)
        write_json(staging / "category-skills.json", skill_members)
        general_pages, general_failures = get_pages(client, general_members, "generals")
        skill_pages, skill_failures = get_pages(client, skill_members, "skills")
        failures.extend(general_failures + skill_failures)
        general_records, general_stats = process_pages(general_pages, "general", page_dir)
        skill_records, skill_stats = process_pages(skill_pages, "skill", page_dir)
        write_json(staging / "generals.json", general_records)
        write_json(staging / "skills.json", skill_records)
        known_generals = {record["name"] for record in general_records}
        missing_relations = [{"pageid": record["pageid"], "title": record["title"], "templateOrdinal": record["templateOrdinal"],
                              "reason": "missing 所属武将"} for record in skill_records if not record["generals"]]
        unknown_relations = [{"pageid": record["pageid"], "title": record["title"], "general": general}
                             for record in skill_records for general in record["generals"] if general not in known_generals]
        manifest = {
            "schemaVersion": 1, "collectedAt": datetime.now(timezone.utc).isoformat(), "collectedAtMeaning": "local corpus assembly time",
            "sourceResponseTimeRange": ([min(client.cache_timestamps), max(client.cache_timestamps)] if client.cache_timestamps else None),
            "startedAt": started.isoformat(),
            "api": API, "indexSource": page_url("武将图鉴"),
            "categories": {
                "generals": {"title": GENERAL_CATEGORY, "total": len(general_members), "apiPages": general_paging},
                "skills": {"title": SKILL_CATEGORY, "total": len(skill_members), "apiPages": skill_paging},
            },
            "requests": {"network": client.request_count, "cacheHits": client.cache_hits, "batchLimit": 50,
                         "delaySeconds": [args.delay_min, args.delay_max], "retries": args.retries},
            "generals": general_stats, "skills": skill_stats, "failures": failures,
            "relationWarnings": {"missingGeneralField": missing_relations, "generalNotInIndex": unknown_relations},
            "currentPageids": {"generals": [item["pageid"] for item in general_members], "skills": [item["pageid"] for item in skill_members]},
            "complete": not failures and len(general_pages) == len(general_members) and len(skill_pages) == len(skill_members),
            "rulesTemplateCoverageComplete": not general_stats["unparsedPages"] and not skill_stats["unparsedPages"] and
                                             not general_stats["parseAnomalies"] and not skill_stats["parseAnomalies"],
            "scope": "rules templates and image-link metadata only; biographies, stories, flavor text, voice lines and audio excluded",
        }
        write_json(staging / "manifest.json", manifest)
        if failures or not manifest["complete"]:
            write_json(output / "last-attempt.json", {
                "attemptedAt": datetime.now(timezone.utc).isoformat(), "api": API,
                "publishedSnapshotPreserved": True, "reason": "incomplete acquisition was not published",
                "failures": failures,
            })
            print(json.dumps({"failures": len(failures), "complete": False, "published": False}, ensure_ascii=False), file=sys.stderr)
            return 2
        output.mkdir(parents=True, exist_ok=True)
        published_pages = output / "pages"
        published_pages.mkdir(parents=True, exist_ok=True)
        publication_started = True
        for staged_page in page_dir.glob("*.json"):
            staged_page.replace(published_pages / staged_page.name)
        for name in ("category-generals.json", "category-skills.json", "generals.json", "skills.json"):
            (staging / name).replace(output / name)
        (staging / "manifest.json").replace(output / "manifest.json")
        write_json(output / "last-attempt.json", {
            "attemptedAt": manifest["collectedAt"], "api": API,
            "status": "succeeded", "published": True, "complete": True, "failures": [],
        })
        print(json.dumps({"generals": len(general_members), "generalRecords": len(general_records), "skills": len(skill_members),
                          "skillRecords": len(skill_records), "failures": len(failures), "complete": manifest["complete"]}, ensure_ascii=False))
        return 0 if manifest["complete"] else 2
    except Exception as exc:
        failure_manifest = {"attemptedAt": datetime.now(timezone.utc).isoformat(), "api": API,
                            "publishedSnapshotPreserved": not publication_started,
                            "publicationStarted": publication_started,
                            "fatalError": f"{type(exc).__name__}: {exc}", "failures": failures}
        write_json(output / "last-attempt.json", failure_manifest)
        print(f"fatal: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
