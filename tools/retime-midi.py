#!/usr/bin/env python3
"""Put a frame-timed MIDI track (a SID rip, a live recording) on a clean grid.

    tools/retime-midi.py rip.mid TRACK out.mid [--unit TICKS] [--unit-is 16th|32nd] [--melody-only] [--insert UNIT:N ...] [--list]
        [--voice TRACK:other.mid ...]

C64 SID rips (Sid2Midi and the like) time notes in screen frames, so a sixteenth may be
152 ticks here and 156 there, and nothing lines up with the MIDI beat. This finds the
track's base unit (the smallest step the tune moves in), counts every note in units, and
writes a one-track MIDI at 96 ticks per quarter where that unit is a sixteenth (or a 32nd
when odd multiples are rare). Feed the result to tools/midi2pipit.py for bars and spelling.

Positions are measured from the last onset (in any track, unless --melody-only) that sat
cleanly on the grid, so the player's pitch-slide ornaments (clusters of very short notes) cannot
shift the rest of the tune: the other voices keep steady time. Notes
that land on the same unit as the next one are dropped (the ornament goes, the main note stays).
When a slide has swallowed a unit anyway (the bar lines drift by one step from some point on),
--insert UNIT:N shifts everything from that unit on by N units; --list prints the units to find it.
--voice writes another track (a band part) on the same grid, so it stays in time with the melody.
TRACK counts note-bearing tracks only. Prints the unit, the fit and the tempo (quarter = N) on stderr.
"""
import argparse, collections, struct, sys

def read_var(data, i):
    value = 0
    while True:
        b = data[i]; i += 1
        value = (value << 7) | (b & 0x7F)
        if not b & 0x80:
            return value, i

def parse(path):
    """Note-bearing tracks as lists of (start, end, pitch), plus the division and first tempo."""
    data = open(path, 'rb').read()
    i = data.find(b'MThd')
    _, ntracks, division = struct.unpack('>HHH', data[i+8:i+14]); i += 14
    tracks, tempo = [], 500000
    for _ in range(ntracks):
        k = data.find(b'MTrk', i)
        if k < 0: break
        length = struct.unpack('>I', data[k+4:k+8])[0]
        chunk, i = data[k+8:k+8+length], k + 8 + length
        t = j = status = 0; open_, notes = {}, []
        while j < len(chunk):
            delta, j = read_var(chunk, j); t += delta
            b = chunk[j]
            if b == 0xFF:
                kind = chunk[j+1]; n, j = read_var(chunk, j + 2)
                if kind == 0x51 and tempo == 500000: tempo = int.from_bytes(chunk[j:j+n], 'big')
                j += n; continue
            if b in (0xF0, 0xF7):
                n, j = read_var(chunk, j + 1); j += n; continue
            if b & 0x80: status, j = b, j + 1
            hi, ch = status & 0xF0, status & 0x0F
            if hi in (0xC0, 0xD0): j += 1; continue
            a, v = chunk[j], chunk[j+1]; j += 2
            if hi == 0x90 and v > 0: open_[(a, ch)] = t
            elif (hi == 0x80 or hi == 0x90) and (a, ch) in open_: notes.append((open_.pop((a, ch)), t, a))
        if notes: tracks.append(sorted(notes))
    return division, tracks, tempo

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('midi'); ap.add_argument('track', type=int); ap.add_argument('out')
    ap.add_argument('--unit', type=float, help='base unit in ticks (default: found from the gaps between notes)')
    ap.add_argument('--unit-is', choices=['16th', '32nd'], help='what the unit is written as (default: guessed)')
    ap.add_argument('--insert', action='append', default=[], help='UNIT:N, shift notes from UNIT on by N units')
    ap.add_argument('--list', action='store_true', help='print each note as unit, length, pitch')
    ap.add_argument('--voice', action='append', default=[], help='TRACK:out.mid, another track on the same grid')
    ap.add_argument('--melody-only', action='store_true', help="anchor on the melody's own onsets only")
    a = ap.parse_args()
    division, tracks, tempo = parse(a.midi)
    notes = tracks[a.track]
    onsets = sorted({s for s, _, _ in notes})
    gaps = [b - a_ for a_, b in zip(onsets, onsets[1:]) if b - a_ > division / 40]

    def fit(u):
        errs = [abs(x / u - round(x / u)) for x in gaps if round(x / u) > 0]
        return sum(e < 0.15 for e in errs) / len(errs) if errs else 0
    u = a.unit
    if u is None:
        common = collections.Counter(gaps).most_common(1)[0][0]
        candidates = [common / k for k in (1, 2, 3, 4, 6)] + [common * 2, common * 3]
        good = [c for c in candidates if fit(c) >= 0.9 and c > division / 20]
        u = max(good) if good else max(candidates, key=fit)
        for _ in range(2):  # refine: the median of each clean gap divided by its unit count
            r = sorted(x / round(x / u) for x in gaps if 1 <= round(x / u) <= 8 and abs(x / u - round(x / u)) < 0.15)
            u = r[len(r) // 2]

    # Count onsets in units from the last clean anchor.
    grid = onsets if a.melody_only else sorted({s for t in tracks for s, _, _ in t if s >= onsets[0]})
    anchor_t, anchor_k, unit_of = onsets[0], 0, {}
    for s in grid:
        pos = anchor_k + (s - anchor_t) / u
        k = round(pos)
        unit_of[s] = k
        if abs(pos - k) < 0.12: anchor_t, anchor_k = s, k
    def place(notes):
        """One note per unit (the last to start there: ornaments come before the main note), with
        the inserts applied; returns {unit: (start, end, pitch)}. Notes before the melody are dropped."""
        by_unit = {}
        for s, e, p in notes:
            if s in unit_of: by_unit[unit_of[s]] = (s, e, p)
        for spec in a.insert:
            at, n = (int(x) for x in spec.split(':'))
            by_unit = {k + n if k >= at else k: v for k, v in by_unit.items()}
        return by_unit
    melody = place(notes)
    odd = sum(k % 2 for k in melody) / len(melody)
    unit_is = a.unit_is or ('32nd' if odd < 0.12 else '16th')
    tick = 12 if unit_is == '32nd' else 24

    def write(by_unit, path, show=False):
        starts = sorted(by_unit)
        following = dict(zip(starts, starts[1:]))
        events = []
        for k in starts:
            s, e, p = by_unit[k]
            end = k + max(1, round((e - s) / u))
            if k in following:
                nxt = following[k]
                if end > nxt or 0 < nxt - end <= 1: end = nxt  # overlaps and one-unit staccato gaps: legato
            events += [(k * tick, 1, p), (end * tick, 0, p)]
            if show: print(f"{k:5d} {end - k:3d} {p}", file=sys.stderr)
        events.sort(key=lambda x: (x[0], x[1]))

        def var(v):
            out = [v & 0x7F]; v >>= 7
            while v: out.insert(0, (v & 0x7F) | 0x80); v >>= 7
            return bytes(out)
        body, last = b'', 0
        for t, on, p in events:
            body += var(t - last) + bytes([0x90, p, 80] if on else [0x80, p, 0]); last = t
        body += b'\x00\xff\x2f\x00'
        with open(path, 'wb') as f:
            f.write(b'MThd' + struct.pack('>IHHH', 6, 0, 1, 96) + b'MTrk' + struct.pack('>I', len(body)) + body)
    write(melody, a.out, a.list)
    for spec in a.voice:
        track, path = spec.split(':', 1)
        write(place(tracks[int(track)]), path)
    seconds = u / division * tempo / 1e6
    quarter = 60 / (seconds * (8 if unit_is == '32nd' else 4))
    print(f"unit {u:.2f} ticks = a {unit_is}, fit {fit(u):.0%}, {len(notes) - len(melody)} ornament notes dropped, "
          f"tempo quarter = {quarter:.0f}", file=sys.stderr)

main()
