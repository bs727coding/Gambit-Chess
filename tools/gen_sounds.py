"""Synthesizes Gambit's sound effects (16-bit mono WAV) with the Python standard library only.

Run:  python tools/gen_sounds.py
Writes src/Gambit.App/Assets/Sounds/*.wav. Deterministic (fixed random seed), no downloads.
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


def write(name, samples, peak=0.8):
    samples = fade_out(normalize(samples, peak))
    OUT.mkdir(parents=True, exist_ok=True)
    with wave.open(str(OUT / f"{name}.wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(max(-1, min(1, s)) * 32767)) for s in samples))
    print(f"wrote {name}.wav ({len(samples) / RATE * 1000:.0f} ms)")


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
