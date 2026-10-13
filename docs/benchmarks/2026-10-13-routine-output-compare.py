"""Compare full decoded captures and unchanged native fixture evidence; no image edits."""
import hashlib
import json
import re
from pathlib import Path
from PIL import Image, ImageChops

repo = Path(r"C:\Users\17917\Desktop\Card")
root = Path(r"C:\Users\17917\AppData\Local\Temp\CardGame-Startup-01a13324\renders")
prefix = repo / "docs/benchmarks/2026-10-13-routine-output-"

def evidence(name):
    return Path(str(prefix) + name)

def samples(label):
    return root / ("routine-output-" + label)

def files(directory, suffix):
    return {p.relative_to(directory).as_posix(): p for p in directory.rglob("*" + suffix) if p.name != ".owner.json"}

def pixels(a, b):
    with Image.open(a) as first, Image.open(b) as second:
        first, second = first.convert("RGBA"), second.convert("RGBA")
        if first.size != second.size:
            return {"equal": False, "firstSize": first.size, "secondSize": second.size}
        delta = ImageChops.difference(first, second)
        mask = Image.new("L", first.size, 0)
        maximum = 0
        for channel in delta.split():
            maximum = max(maximum, channel.getextrema()[1])
            mask = ImageChops.lighter(mask, channel.point(lambda value: 255 if value else 0))
        changed = mask.histogram()[255]
        return {"equal": changed == 0, "size": first.size, "changedPixels": changed, "maxChannelDelta": maximum,
                "firstRgbaSha256": hashlib.sha256(first.tobytes()).hexdigest(),
                "secondRgbaSha256": hashlib.sha256(second.tobytes()).hexdigest()}

def log_lines(label):
    lines = evidence(label + "-wpf.log").read_text(encoding="utf-8-sig").splitlines()
    return [line.replace(str(samples(label)), "<sample-output>") for line in lines if not line.startswith("[TIME]")]

def times(label):
    result = {}
    for line in evidence(label + "-wpf.log").read_text(encoding="utf-8-sig").splitlines():
        match = re.fullmatch(r"\[TIME\] (.+): ([\d.]+) ms", line)
        if match:
            result[match[1]] = float(match[2])
    if len(result) != 5:
        raise RuntimeError(f"Expected five measured checks in {label}, got {len(result)}")
    return result

comparisons = []
for first, second in [("before1", "before2"), ("before1", "after1"), ("after1", "after2")]:
    before, after = samples(first), samples(second)
    left, right = files(before, ".png"), files(after, ".png")
    if left.keys() != right.keys():
        raise RuntimeError(f"Capture set changed: {first}/{second}: {left.keys() ^ right.keys()}")
    images = [{"file": name, **pixels(left[name], right[name])} for name in sorted(left)]
    left, right = files(before, ".json"), files(after, ".json")
    if left.keys() != right.keys():
        raise RuntimeError(f"Native JSON evidence set changed: {first}/{second}")
    native = []
    for name in sorted(left):
        a = json.loads(left[name].read_text(encoding="utf-8-sig"))
        b = json.loads(right[name].read_text(encoding="utf-8-sig"))
        native.append({"file": name, "equal": a == b})
    comparisons.append({"first": first, "second": second, "images": images, "nativeJson": native,
                        "semanticLogLinesEqual": log_lines(first) == log_lines(second)})

measurements = {label: times(label) for label in ["before1", "before2", "after1", "after2"]}
rows = []
for name in measurements["before1"]:
    old = [measurements[label][name] for label in ["before1", "before2"]]
    new = [measurements[label][name] for label in ["after1", "after2"]]
    a, b = sum(old) / 2, sum(new) / 2
    rows.append({"name": name, "beforeSamplesMs": old, "afterSamplesMs": new,
                 "beforeMeanMs": a, "afterMeanMs": b, "observedSavedMs": a - b})
old, new = sum(row["beforeMeanMs"] for row in rows), sum(row["afterMeanMs"] for row in rows)
report = {"checks": rows, "beforeMeanMeasuredChecksMs": old, "afterMeanMeasuredChecksMs": new,
          "observedSavedMs": old - new, "observedSavedPercent": 100 * (old - new) / old,
          "comparisons": comparisons,
          "limits": "Two processes per variant, BB then AA, small samples and host load; no statistical, FPS or whole-routine savings claim. Full RGBA including alpha; complete native JSON semantics without field exclusions; logs exclude only timings and sample path."}
evidence("comparison.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
summary = {key: report[key] for key in ["beforeMeanMeasuredChecksMs", "afterMeanMeasuredChecksMs", "observedSavedMs", "observedSavedPercent"]}
summary.update({"pixelPairs": sum(len(row["images"]) for row in comparisons),
                "unequalPixels": sum(not value["equal"] for row in comparisons for value in row["images"]),
                "nativeJsonPairs": sum(len(row["nativeJson"]) for row in comparisons),
                "unequalNativeJson": sum(not value["equal"] for row in comparisons for value in row["nativeJson"]),
                "semanticLogsEqual": all(row["semanticLogLinesEqual"] for row in comparisons)})
print(json.dumps(summary, ensure_ascii=False, indent=2))
