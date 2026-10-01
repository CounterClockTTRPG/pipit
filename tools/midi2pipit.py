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
DURS = {16: 'w', 12: 'h.', 8: 'h', 6: 'q.', 4: 'q', 3: 'e.', 2: 'e', 1: 's'}

def name(midi):
    return f"{NAMES[midi % 12]}{midi // 12 - 1}"

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('midi'); ap.add_argument('--track', type=int, default=None)
    ap.add_argument('--transpose', type=int, default=0); ap.add_argument('--bars', default=None)
    ap.add_argument('--time', default='4/4')
    ap.add_argument('--flats', action='store_true', help='spell black keys as flats (flat keys)')
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
    notes.sort(key=lambda n: (n[0], -n[2]))
    mono = []
    for s_, e_, p_ in notes:
        if mono and mono[-1][0] == s_:
            continue
        if mono and mono[-1][1] > s_:
            if p_ < mono[-1][2]:
                continue  # an inner voice moving under a held melody note
            mono[-1] = (mono[-1][0], s_, mono[-1][2])
        mono.append((s_, e_, p_))
    notes = mono
    sixteenth = division / 4
    up, low = map(int, a.time.split('/'))
    bar16 = up * 16 // low
    # With a pickup, shift so the first full bar starts at a bar line (the pickup sits in "bar 0").
    shift = (bar16 - a.pickup) % bar16 if a.pickup else 0
    q = [(round(s / sixteenth) + shift, max(1, round((e - s) / sixteenth)), p + a.transpose) for s, e, p in notes]
    # Fill gaps with rests; split notes and rests at bar lines with ties.
    out, pos, first, last = [], 0, None, None
    if a.bars:
        first, last = (int(x) for x in a.bars.split('-'))
    def emit(pitch, start, length):
        while length > 0:
            room = bar16 - start % bar16
            take = min(length, room)
            # Split into representable durations.
            while take > 0:
                d = max(k for k in DURS if k <= take)
                bar = start // bar16 + 1
                tie = '~' if pitch is not None and (take - d > 0 or length - take > 0) else ''
                out.append((bar, (name(pitch) if pitch is not None else 'R') + '/' + DURS[d] + tie))
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
    print(f"bars {keys[0]}-{keys[-1]}, {len(sel)} notes, range {name(min(sel))}-{name(max(sel))}, shortest {min(d for _, d, _ in q)}/16", file=sys.stderr)

main()
