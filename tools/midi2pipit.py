#!/usr/bin/env python3
"""Convert a monophonic MIDI track into Pipit's music string.

    tools/midi2pipit.py piece.mid [--track N] [--transpose SEMITONES] [--bars FROM-TO] [--time 4/4]

Used for public-domain sources (e.g. the Mutopia Project) so long pieces are not
transcribed from memory. Durations are quantised to sixteenth notes; notes are
spelled with sharps except B-flat (as in the app). Prints the music string with bar
lines, and a summary (range, shortest note) on stderr.
"""
import argparse, struct, sys

def read_var(data, i):
    value = 0
    while True:
        b = data[i]; i += 1
        value = (value << 7) | (b & 0x7F)
        if not b & 0x80:
            return value, i

def parse(path):
    data = open(path, 'rb').read()
    assert data[:4] == b'MThd'
    fmt, ntracks, division = struct.unpack('>HHH', data[8:14])
    i, tracks = 14, []
    for _ in range(ntracks):
        assert data[i:i+4] == b'MTrk'
        length = struct.unpack('>I', data[i+4:i+8])[0]
        chunk, i = data[i+8:i+8+length], i + 8 + length
        events, t, j, status = [], 0, 0, 0
        while j < len(chunk):
            delta, j = read_var(chunk, j); t += delta
            b = chunk[j]
            if b == 0xFF:
                kind, j = chunk[j+1], j + 2
                n, j = read_var(chunk, j); j += n
                continue
            if b in (0xF0, 0xF7):
                n, j = read_var(chunk, j + 1); j += n
                continue
            if b & 0x80:
                status, j = b, j + 1
            hi = status & 0xF0
            if hi in (0x80, 0x90, 0xA0, 0xB0, 0xE0):
                a, v = chunk[j], chunk[j+1]; j += 2
                if hi == 0x90 and v > 0: events.append(('on', t, a))
                elif hi == 0x80 or (hi == 0x90 and v == 0): events.append(('off', t, a))
            else:
                j += 1
        tracks.append(events)
    return division, tracks

NAMES = ['C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#', 'A', 'Bb', 'B']
# Durations in units of 1/24 beat, so 32nd notes (3) and triplets (e3 = 8, s3 = 4) fit the grid.
UNIT = 24
DURS = {96: 'w', 72: 'h.', 48: 'h', 36: 'q.', 24: 'q', 18: 'e.', 12: 'e', 9: 's.', 6: 's', 3: 't'}
TRIPLETS = {32: 'h3', 16: 'q3', 8: 'e3', 4: 's3', 2: 't3'}

def pieces(length):
    """A length in units as written durations, longest first: plain values when it fits the
    32nd grid, triplets otherwise."""
    table = DURS if length % 3 == 0 else TRIPLETS
    out = []
    while length > 0:
        fit = [k for k in table if k <= length]
        if not fit:
            raise SystemExit(f"cannot write a length of {length}/{UNIT} beat")
        out.append(max(fit)); length -= max(fit)
    return out

def name(midi):
    return f"{NAMES[midi % 12]}{midi // 12 - 1}"

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('midi'); ap.add_argument('--track', type=int, default=None)
    ap.add_argument('--transpose', type=int, default=0); ap.add_argument('--bars', default=None)
    ap.add_argument('--time', default='4/4')
    ap.add_argument('--flats', action='store_true', help='spell black keys as flats (flat keys)')
    ap.add_argument('--voice', choices=['top', 'bottom'], default='top', help='which note of a chord to keep (bottom for bass lines)')
    ap.add_argument('--pickup', type=int, default=0, help='length of the pickup bar in sixteenths')
    a = ap.parse_args()
    if a.flats:
        NAMES[1], NAMES[3], NAMES[6], NAMES[8] = 'Db', 'Eb', 'Gb', 'Ab'
    division, tracks = parse(a.midi)
    # Default: the track with the highest average pitch (the melody / right hand).
    def mean_pitch(t):
        ps = [e[2] for e in t if e[0] == 'on']
        return sum(ps) / len(ps) if ps else -1
    track = tracks[a.track] if a.track is not None else max(tracks, key=mean_pitch)
    notes, open_ = [], {}
    for kind, t, p in track:
        if kind == 'on': open_[p] = t
        elif p in open_: notes.append((open_.pop(p), t, p))
    # Keep one line: at equal starts keep the highest note; a note that starts while another
    # sounds cuts the earlier one short (the top voice of keyboard chords).
    # With --voice bottom the same rules apply upside down (the bass of left-hand chords).
    sign = 1 if a.voice == 'top' else -1
    notes.sort(key=lambda n: (n[0], -sign * n[2]))
    mono = []
    for s_, e_, p_ in notes:
        if mono and mono[-1][0] == s_:
            continue
        if mono and mono[-1][1] > s_:
            if sign * p_ < sign * mono[-1][2]:
                continue  # an inner voice moving under (or over) a held note
            mono[-1] = (mono[-1][0], s_, mono[-1][2])
        mono.append((s_, e_, p_))
    notes = mono
    unit = division / UNIT
    up, low = map(int, a.time.split('/'))
    bar16 = up * 4 * UNIT // low  # units per bar
    pickup = a.pickup * UNIT // 4  # --pickup is in sixteenths
    # With a pickup, shift so the first full bar starts at a bar line (the pickup sits in "bar 0").
    shift = (bar16 - pickup) % bar16 if pickup else 0
    # Note edges sit on the 32nd grid, or on the triplet-sixteenth grid; anything else is timing
    # noise in the file and goes to the nearest 32nd.
    def snap(t):
        t = round(t / unit)
        return t if t % 3 == 0 or t % 4 == 0 else 3 * round(t / 3)
    q = [(snap(s) + shift, max(1, snap(e) - snap(s)), p + a.transpose) for s, e, p in notes]
    # Grace notes (LilyPond plays them as slivers) are shorter than any written note: their
    # time goes to the note before, so the main note keeps its place.
    merged = []
    for s_, d_, p_ in q:
        if d_ < 3 and merged and merged[-1][0] + merged[-1][1] == s_:
            merged[-1] = (merged[-1][0], merged[-1][1] + d_, merged[-1][2])
        elif d_ >= 3:
            merged.append((s_, d_, p_))
    # Gaps shorter than a 32nd are articulation, not rests: the note before lasts until the next.
    q = [(s_, d_ + (gap if 0 < (gap := merged[i + 1][0] - s_ - d_) < 3 else 0), p_) if i + 1 < len(merged) else (s_, d_, p_)
         for i, (s_, d_, p_) in enumerate(merged)]
    # Fill gaps with rests; split notes and rests at bar lines with ties.
    out, pos, first, last = [], 0, None, None
    if a.bars:
        first, last = (int(x) for x in a.bars.split('-'))
    def emit(pitch, start, length):
        while length > 0:
            room = bar16 - start % bar16
            take = min(length, room)
            # Split into representable durations.
            for d in pieces(take):
                bar = start // bar16 + 1
                tie = '~' if pitch is not None and (take - d > 0 or length - take > 0) else ''
                out.append((bar, (name(pitch) if pitch is not None else 'R') + '/' + (DURS.get(d) or TRIPLETS[d]) + tie))
                start += d; take -= d; length -= d
    pos = shift
    for s, d, p in q:
        if s > pos: emit(None, pos, s - pos)
        if s < pos: d -= pos - s; s = pos
        if d > 0: emit(p, s, d); pos = s + d
    bars = {}
    for bar, tok in out:
        bars.setdefault(bar, []).append(tok)
    keys = [b for b in sorted(bars) if (first is None or first <= b <= last)]
    print(' | '.join(' '.join(bars[b]) for b in keys))
    sel = [p for (s, d, p) in q if first is None or first <= s // bar16 + 1 <= last]
    print(f"bars {keys[0]}-{keys[-1]}, {len(sel)} notes, range {name(min(sel))}-{name(max(sel))}, shortest {min(d for _, d, _ in q)}/{UNIT} beat", file=sys.stderr)

main()
