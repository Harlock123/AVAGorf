#!/usr/bin/env python3
"""Synthesises AVAGorf's sound effects and tunes into ../src/AVAGorf/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAGorf", "Sounds")
RNG = random.Random(1982)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def robot(seconds, syllables, base=95):
    """The Overlord's voice: a buzzy, robotic warble, its pitch and 'mouth' jumping syllable by syllable."""
    n = int(RATE * seconds)
    rng = random.Random(7)
    pitches = [base * rng.choice([1, 1.12, 1.26, 0.89, 1.5]) for _ in range(syllables)]
    out, phase = [], 0.0
    for i in range(n):
        t = i / RATE
        k = min(syllables - 1, int(t / seconds * syllables))
        within = (t / seconds * syllables) % 1
        f = pitches[k] * (1 + 0.04 * math.sin(2 * math.pi * 6 * t))
        phase += f / RATE
        saw = 2 * (phase % 1) - 1
        # A crude formant: the buzz ring-modulated by a vowel tone that moves each syllable.
        vowel = 500 + 700 * ((k * 0.37) % 1)
        env = math.sin(math.pi * within) ** 0.6
        out.append(env * (0.6 * saw + 0.4 * saw * math.sin(2 * math.pi * vowel * t)))
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The fighter's beam: a bright twang; the enemy's shots, a blip.
    write("Fire.wav", mix(tone(0.14, 1800, 900, "square", release=1.0), tone(0.14, 3600, 1800, "tri", release=1.0, gain=0.5)), 0.24)
    write("AlienFire.wav", tone(0.07, 1300, 800, "pulse", release=0.8), 0.16)
    write("Hit.wav", mix(tone(0.2, 3200, 200, "noise", release=0.9), tone(0.14, 1100, 260, "square", release=0.8, gain=0.5)), 0.38)
    write("BigHit.wav", cat(mix(tone(0.3, 3000, 150, "noise", release=0.7), tone(0.3, 650, 110, "saw", release=0.7, gain=0.7)),
                            tone(0.25, 1600, 1500, "tri", release=0.7, vibrato=0.05, vib_rate=20, gain=0.5)), 0.45)
    # The Armada's march: a low thud.
    write("Step.wav", tone(0.09, 90, 60, "square", release=0.9), 0.34)
    write("Dive.wav", tone(0.85, 1500, 350, "tri", release=0.4, vibrato=0.05, vib_rate=11), 0.22)
    # A Lancer charging up, then its beam.
    write("BeamWarn.wav", tone(0.75, 300, 1400, "saw", attack=0.05, release=0.2, vibrato=0.08, vib_rate=30), 0.22)
    write("Beam.wav", mix(tone(0.6, 180, 170, "saw", release=0.5, vibrato=0.04, vib_rate=50), tone(0.6, 4000, 3000, "noise", release=0.5, gain=0.4)), 0.34)
    write("ShieldChip.wav", tone(0.05, 2600, 2000, "tri", release=0.8), 0.2)
    # A ship out of the warp: a rising swirl.
    write("Warp.wav", tone(0.5, 200, 1600, "sine", attack=0.02, release=0.4, vibrato=0.2, vib_rate=16), 0.26)
    write("CoreHit.wav", cat(*[tone(0.06, f, kind="square", release=0.5) for f in [1800, 1200, 1800, 1200, 1800, 1200]]), 0.4)
    write("Explode.wav", mix(tone(2.4, 4000, 50, "noise", release=0.6), tone(2.4, 150, 25, "saw", release=0.5, gain=0.8),
                             cat(*[tone(0.2, f, f * 0.5, "square", release=0.6, gain=0.5) for f in [800, 670, 560, 470, 390, 330, 280, 240]])), 0.55)
    write("Death.wav", cat(mix(tone(0.9, 3500, 90, "noise", release=0.6), tone(0.9, 420, 40, "saw", release=0.6, gain=0.7)),
                           *[tone(0.14, hz(n), kind="tri", release=0.4) for n in ["E5", "C5", "A4", "E4"]]), 0.5)
    write("MissionStart.wav", tune(170, [("E4", 0.5), ("B4", 0.5), ("E5", 0.5), ("D5", 0.5), ("B4", 0.5), ("E5", 1.5)],
                                   [("E2", 1), ("B2", 1), ("E2", 2)]), 0.42)
    write("Clear.wav", tune(190, [("E5", 0.5), ("G5", 0.5), ("B5", 0.5), ("E6", 0.5), ("D6", 0.5), ("B5", 0.5), ("E6", 2)], [("E3", 1), ("B2", 1), ("E3", 2)]), 0.42)
    write("RankUp.wav", tune(150, [("E5", 0.5), ("E5", 0.5), ("E5", 0.5), ("G5", 1), ("B5", 1), ("E6", 2)], [("E3", 2), ("B2", 1), ("E3", 3)]), 0.42)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["E5", "G#5", "B5", "E6", "B5", "E6"]]), 0.4)
    write("Taunt.wav", robot(1.4, 7), 0.32)

    # The Overlord's engines: a deep, slow throb with a menacing whine. Loops (four seconds, whole cycles).
    n = int(RATE * 4)
    hum = []
    for i in range(n):
        t = i / RATE
        pulse = 0.55 + 0.45 * math.sin(2 * math.pi * 1 * t) ** 2
        hum.append(pulse * (math.sin(2 * math.pi * 55 * t) * 0.55 + math.sin(2 * math.pi * 82.5 * t) * 0.2)
                   + 0.05 * math.sin(2 * math.pi * (990 + 40 * math.sin(2 * math.pi * 0.5 * t)) * t))
    write("Music.wav", hum, 0.28)

    # The title: a menacing march in E minor over a stomping bass (an original tune).
    lead = [("E4", 1), ("E4", 0.5), ("E4", 0.5), ("G4", 1), ("E4", 1), ("B4", 2), ("A4", 1), ("G4", 1),
            ("F#4", 1), ("F#4", 0.5), ("F#4", 0.5), ("A4", 1), ("F#4", 1), ("C5", 2), ("B4", 2),
            ("E5", 1), ("D5", 1), ("C5", 1), ("B4", 1), ("A4", 1), ("G4", 1), ("F#4", 1), ("D#4", 1), ("E4", 4)]
    bass = [("E2", 1), ("B2", 1), ("E2", 1), ("B2", 1), ("E2", 1), ("B2", 1), ("E2", 1), ("B2", 1),
            ("D2", 1), ("A2", 1), ("D2", 1), ("A2", 1), ("C2", 1), ("G2", 1), ("B1", 1), ("F#2", 1),
            ("A2", 2), ("E2", 2), ("B1", 2), ("B2", 2), ("E2", 4)]
    write("TitleTune.wav", cat(tune(180, lead, bass, kind="pulse"), silence(1.2)), 0.42)
    write("StartTune.wav", tune(168, [("B4", 0.5), ("E5", 0.5), ("G5", 0.5), ("F#5", 0.5), ("E5", 0.5), ("B5", 2)], [("E2", 1), ("B2", 1), ("E2", 2)], kind="pulse"), 0.45)
    write("GameOver.wav", tune(80, [("B4", 1), ("G4", 1), ("F#4", 1), ("D#4", 1), ("E4", 4)], [("E3", 2), ("C3", 2), ("B2", 2), ("E2", 2)], kind="tri"), 0.45)


if __name__ == "__main__":
    main()
