"""Original short PCM effects for this project; Python standard library only.

Run without arguments to regenerate Assets/Audio. --preview writes a listening reel;
--timeline reads the actual UI test's audio-preview-events.json and mixes a soundtrack.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import random
import struct
import wave

RATE = 44100
BANK = Path(__file__).resolve().parents[1] / "src/CardGame.Wpf/Assets/Audio"
LENGTHS = {"card": .23, "response": .28, "hit": .34, "fire": .5, "thunder": .65,
           "recover": .72, "yourturn": .58, "prompt": .38, "dying": .8, "death": .9,
           "victory": 1.35, "defeat": 1.15, "draw": .8}


def pluck(t, frequency, decay=9, metallic=False):
    if t < 0:
        return 0.0
    ratios = (1, 2.13, 3.83) if metallic else (1, 2, 3)
    return min(1, t / .004) * sum(
        weight * math.sin(math.tau * frequency * ratio * t) * math.exp(-decay * (1 + index * .3) * t)
        for index, (ratio, weight) in enumerate(zip(ratios, (1, .3, .12))))


def synth(name, duration):
    random_source = random.Random(20260908 + list(LENGTHS).index(name))
    result = []
    low_noise = 0.0
    for index in range(round(duration * RATE)):
        t = index / RATE
        noise = random_source.uniform(-1, 1)
        low_noise = .83 * low_noise + .17 * noise
        if name == "card":
            value = .7 * (noise - low_noise) * math.sin(math.pi * t / duration) ** 2 * math.exp(-11 * t)
            value += .25 * pluck(t - .055, 760, 44)
        elif name == "response":
            value = .6 * pluck(t, 1020, 21, True) + .08 * noise * math.exp(-60 * t)
        elif name == "hit":
            value = .7 * math.sin(math.tau * (130 * t - 90 * t * t)) * math.exp(-17 * t) + .38 * low_noise * math.exp(-20 * t)
        elif name == "fire":
            value = .6 * low_noise * math.exp(-6 * t) * (1 + .4 * math.sin(330 * t)) + .18 * noise * math.exp(-12 * t)
            value += .13 * pluck(t, 145, 13)
        elif name == "thunder":
            value = .65 * low_noise * math.exp(-5 * t) + .22 * math.sin(math.tau * (65 * t - 15 * t * t)) * math.exp(-8 * t)
            value += .18 * noise * math.exp(-45 * t)
        elif name == "recover":
            value = sum(.4 * pluck(t - offset, frequency, 8) for offset, frequency in [(0, 440), (.13, 587.33), (.26, 880)])
        elif name == "yourturn":
            value = .5 * pluck(t, 587.33, 11) + .45 * pluck(t - .15, 880, 10)
        elif name == "prompt":
            value = .35 * pluck(t, 880, 22) + .3 * pluck(t - .12, 1174.66, 20)
        elif name == "dying":
            value = .6 * pluck(t, 98, 11) + .45 * pluck(t - .27, 98, 12)
        elif name == "death":
            value = .6 * pluck(t, 130.81, 5, True) + .18 * low_noise * math.exp(-10 * t)
        elif name == "victory":
            value = sum(.35 * pluck(t - offset, frequency, 6) for offset, frequency in [(0, 587.33), (.16, 739.99), (.32, 880), (.53, 1174.66)])
            value += .25 * pluck(t, 73.42, 5, True)
        elif name == "defeat":
            value = sum(.4 * pluck(t - offset, frequency, 7) for offset, frequency in [(0, 293.66), (.2, 261.63), (.42, 220)])
            value += .22 * pluck(t, 65.41, 5, True)
        else:
            value = .45 * pluck(t, 293.66, 7) + .35 * pluck(t - .18, 220, 8)
        edge = min(1, t / .003, max(0, (duration - t - 1 / RATE) / .016))
        result.append(value * edge)
    scale = .68 / max(abs(value) for value in result)
    return [value * scale for value in result]


def write_wav(path, samples):
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as output:
        output.setnchannels(1)
        output.setsampwidth(2)
        output.setframerate(RATE)
        output.writeframes(struct.pack(f"<{len(samples)}h", *(round(max(-1, min(1, value)) * 32767) for value in samples)))


def load_wav(name):
    with wave.open(str(BANK / f"{name.lower()}.wav"), "rb") as source:
        assert source.getframerate() == RATE and source.getnchannels() == 1 and source.getsampwidth() == 2
        frames = source.readframes(source.getnframes())
    return [value / 32768 for value in struct.unpack(f"<{len(frames) // 2}h", frames)]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--preview", type=Path)
    parser.add_argument("--timeline", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    if args.timeline:
        timeline = json.loads(args.timeline.read_text(encoding="utf-8-sig"))
        mixed = [0.0] * round(timeline["Duration"] * RATE)
        for event in timeline["Events"]:
            offset = round(event["Seconds"] * RATE)
            for index, sample in enumerate(load_wav(event["Sound"])):
                if offset + index >= len(mixed):
                    break
                mixed[offset + index] += sample * .4
        assert max(abs(value) for value in mixed) < 1, "Preview mix clipped"
        write_wav(args.output, mixed)
        print(f"Mixed {len(timeline['Events'])} actual UI sound events into {args.output}")
    elif args.preview:
        samples = []
        markers = []
        for name in LENGTHS:
            markers.append({"sound": name, "seconds": round(len(samples) / RATE, 3)})
            samples.extend(value * .65 for value in load_wav(name))
            samples.extend([0] * round(.24 * RATE))
        write_wav(args.preview, samples)
        args.preview.with_suffix(".json").write_text(json.dumps(markers, indent=2), encoding="utf-8")
        print(f"Created {len(markers)}-sound reel: {args.preview}")
    else:
        manifest = []
        for name, duration in LENGTHS.items():
            samples = synth(name, duration)
            path = BANK / f"{name}.wav"
            write_wav(path, samples)
            manifest.append({"name": name, "seconds": duration, "peak": round(max(abs(value) for value in samples), 4),
                             "rms": round(math.sqrt(sum(value * value for value in samples) / len(samples)), 4),
                             "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
        (BANK / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
        print(f"Generated {len(manifest)} original PCM effects in {BANK}")


if __name__ == "__main__":
    main()
