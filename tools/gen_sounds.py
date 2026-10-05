"""Synthesizes Gambit's sound effects (16-bit mono WAV) with the Python standard library only.

Run:  python tools/gen_sounds.py
Writes src/Gambit.App/Assets/Sounds/*.wav (the "classic" pack) and the other packs of Settings >
Sound pack into subfolders (soft/, retro/). Deterministic (fixed random seeds), no downloads.
"""
import math
import random
import struct
import wave
from pathlib import Path

RATE = 44100
OUT = Path(__file__).resolve().parent.parent / "src" / "Gambit.App" / "Assets" / "Sounds"
rng = random.Random(20261002)


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def mix(*tracks):
    n = max(len(t) for t in tracks)
    out = [0.0] * n
    for t in tracks:
        for i, v in enumerate(t):
            out[i] += v
    return out


def delayed(track, seconds):
    return silence(seconds) + track


def lowpass(samples, cutoff_hz):
    # One-pole low-pass filter.
    dt = 1.0 / RATE
    rc = 1.0 / (2 * math.pi * cutoff_hz)
    a = dt / (rc + dt)
    out, y = [], 0.0
    for x in samples:
        y += a * (x - y)
        out.append(y)
    return out


def highpass(samples, cutoff_hz):
    dt = 1.0 / RATE
    rc = 1.0 / (2 * math.pi * cutoff_hz)
    a = rc / (rc + dt)
    out, y, prev = [], 0.0, 0.0
    for x in samples:
        y = a * (y + x - prev)
        prev = x
        out.append(y)
    return out


def knock(freq=190.0, length=0.09, decay=55.0, noise=0.6, body=0.9, brightness=2600.0):
    """A wooden 'thock': a short filtered noise transient over a fast-decaying low tone."""
    n = int(RATE * length)
    tone = [math.sin(2 * math.pi * freq * i / RATE + 0.6 * math.sin(2 * math.pi * freq * 2.01 * i / RATE)) *
            math.exp(-decay * i / RATE) for i in range(n)]
    hiss = [rng.uniform(-1, 1) * math.exp(-decay * 2.2 * i / RATE) for i in range(n)]
    hiss = lowpass(highpass(hiss, 300), brightness)
    return [body * t + noise * h for t, h in zip(tone, hiss)]


def chime(freqs, length=0.55, decay=5.5, spacing=0.0, volume=0.35):
    tracks = []
    for k, f in enumerate(freqs):
        n = int(RATE * length)
        tone = [volume * (math.sin(2 * math.pi * f * i / RATE) + 0.25 * math.sin(2 * math.pi * f * 2 * i / RATE))
                * math.exp(-decay * i / RATE) * min(1.0, i / (RATE * 0.004)) for i in range(n)]
        tracks.append(delayed(tone, spacing * k))
    return mix(*tracks)


def normalize(samples, peak=0.8):
    m = max(1e-9, max(abs(s) for s in samples))
    return [s * peak / m for s in samples]


def fade_out(samples, seconds=0.01):
    n = min(len(samples), int(RATE * seconds))
    for i in range(n):
        samples[-1 - i] *= i / n
    return samples


def write(name, samples, peak=0.8, folder=OUT):
    samples = fade_out(normalize(samples, peak))
    folder.mkdir(parents=True, exist_ok=True)
    with wave.open(str(folder / f"{name}.wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(max(-1, min(1, s)) * 32767)) for s in samples))
    print(f"wrote {folder.name}/{name}.wav ({len(samples) / RATE * 1000:.0f} ms)")


write("move", knock(), peak=0.55)
write("capture", mix(knock(freq=240, decay=65, noise=1.0, brightness=4200),
                     delayed(knock(freq=160, length=0.07, decay=80, noise=0.5), 0.028)), peak=0.75)
write("castle", mix(knock(), delayed(knock(freq=170), 0.11)), peak=0.6)
write("check", mix(knock(freq=260, noise=0.8, brightness=5000), delayed(chime([1046.5], length=0.25, decay=14, volume=0.5), 0.0)), peak=0.7)
write("promote", mix(knock(), delayed(chime([659.3, 880.0, 1318.5], length=0.4, decay=9, spacing=0.06), 0.03)), peak=0.65)
write("start", chime([523.3, 784.0], length=0.6, decay=6, spacing=0.12), peak=0.5)
write("win", chime([523.3, 659.3, 784.0, 1046.5], length=0.8, decay=4.5, spacing=0.11), peak=0.55)
write("end", chime([587.3, 440.0, 349.2], length=0.8, decay=4.5, spacing=0.14), peak=0.5)
write("illegal", lowpass([0.6 * math.sin(2 * math.pi * 110 * i / RATE) * math.exp(-18 * i / RATE) for i in range(int(RATE * 0.18))], 900), peak=0.35)
write("lowtime", chime([1760.0], length=0.12, decay=30, volume=0.6), peak=0.4)


# ------------------------------------------------------------------ other packs (Settings > Sound pack)

def soft_pack():
    """Felt taps and gentle bells: rounder and quieter, without the wooden click."""
    def tap(freq=150.0, length=0.12, decay=38.0):
        n = int(RATE * length)
        return lowpass([math.sin(2 * math.pi * freq * i / RATE) * math.exp(-decay * i / RATE) * min(1.0, i / (RATE * 0.003))
                        for i in range(n)], 1400)

    return {
        "move": (tap(), 0.45),
        "capture": (mix(tap(170, decay=45), delayed(tap(130, length=0.1), 0.035)), 0.6),
        "castle": (mix(tap(), delayed(tap(140), 0.12)), 0.5),
        "check": (mix(tap(190), chime([880.0], length=0.35, decay=10, volume=0.4)), 0.55),
        "promote": (mix(tap(), delayed(chime([587.3, 784.0, 1174.7], length=0.5, decay=7, spacing=0.07, volume=0.3), 0.03)), 0.55),
        "start": (chime([440.0, 659.3], length=0.7, decay=5, spacing=0.14, volume=0.3), 0.45),
        "win": (chime([440.0, 554.4, 659.3, 880.0], length=0.9, decay=4, spacing=0.12, volume=0.3), 0.5),
        "end": (chime([493.9, 392.0, 329.6], length=0.9, decay=4, spacing=0.15, volume=0.3), 0.45),
        "illegal": (lowpass([0.5 * math.sin(2 * math.pi * 98 * i / RATE) * math.exp(-14 * i / RATE) for i in range(int(RATE * 0.2))], 600), 0.3),
        "lowtime": (chime([1318.5], length=0.18, decay=22, volume=0.5), 0.35),
    }


def retro_pack():
    """8-bit blips from square waves, softened a little. Square waves are dense, so the peaks are lower
    than the other packs' to come out about as loud."""
    def square(freq, length, volume=0.5, decay=0.0):
        n = int(RATE * length)
        return [volume * (1 if math.sin(2 * math.pi * freq * i / RATE) >= 0 else -1) * math.exp(-decay * i / RATE) for i in range(n)]

    def sweep(f0, f1, length, volume=0.5):
        n, phase, out = int(RATE * length), 0.0, []
        for i in range(n):
            phase += 2 * math.pi * (f0 + (f1 - f0) * i / n) / RATE
            out.append(volume * (1 if math.sin(phase) >= 0 else -1))
        return out

    def notes(freqs, step=0.07, length=0.06):
        return mix(*[delayed(square(f, length, 0.4, decay=8), step * k) for k, f in enumerate(freqs)])

    def soft(samples):
        return lowpass(samples, 5000)

    return {
        "move": (soft(square(440, 0.045, decay=30)), 0.23),
        "capture": (soft(mix(square(330, 0.05, decay=25), delayed(square(220, 0.05, decay=25), 0.05))), 0.25),
        "castle": (soft(mix(square(392, 0.04, decay=30), delayed(square(523.3, 0.04, decay=30), 0.07))), 0.25),
        "check": (soft(sweep(600, 1200, 0.12)), 0.13),
        "promote": (soft(notes([523.3, 659.3, 784.0, 1046.5], step=0.06)), 0.15),
        "start": (soft(notes([392.0, 523.3, 659.3], step=0.08)), 0.16),
        "win": (soft(notes([523.3, 659.3, 784.0, 1046.5, 1318.5], step=0.09, length=0.08)), 0.18),
        "end": (soft(notes([659.3, 523.3, 392.0, 261.6], step=0.11, length=0.1)), 0.17),
        "illegal": (soft(square(110, 0.15, volume=0.4, decay=12)), 0.2),
        "lowtime": (soft(mix(square(1760, 0.05, decay=20), delayed(square(1760, 0.05, decay=20), 0.09))), 0.21),
    }


for pack, sounds in (("soft", soft_pack()), ("retro", retro_pack())):
    for name, (samples, peak) in sounds.items():
        write(name, samples, peak=peak, folder=OUT / pack)
