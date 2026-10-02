#!/usr/bin/env python3
"""Splits a one-part MusicXML collection into pieces and converts them to Pipit music strings.

    tools/musicxml2pipit.py collection.xml --list            # one line per piece: bars, time, problems
    tools/musicxml2pipit.py collection.xml --piece N --json  # piece N as {title, time, key, music, ...}

A piece starts at a text direction (its title) at the start of a bar. Repeats and first/second
endings are kept as |: :| [1 [2; ties as ~. Pieces with tuplets or notes shorter than a
sixteenth cannot be written in the song format yet and are reported, not converted.
"""
import argparse, json, sys, xml.etree.ElementTree as ET
from fractions import Fraction as Fr

STEPS = 'CDEFGAB'
NAT = {'C': 0, 'D': 2, 'E': 4, 'F': 5, 'G': 7, 'A': 9, 'B': 11}
DUR = {Fr(4): 'w', Fr(3): 'h.', Fr(2): 'h', Fr(3, 2): 'q.', Fr(1): 'q', Fr(3, 4): 'e.', Fr(1, 2): 'e', Fr(3, 8): 's.', Fr(1, 4): 's'}
MAJOR = {-3: 'Eb', -2: 'Bb', -1: 'F', 0: 'C', 1: 'G', 2: 'D', 3: 'A'}

def text(e):
    return ''.join(e.itertext()).strip() if e is not None else ''

def pieces(root):
    part = root.find('part')
    divisions, time, fifths = 1, (4, 4), 0
    current = None
    out = []
    for m in part.findall('measure'):
        title = None
        for d in m.findall('direction'):
            w = d.find('direction-type/words')
            if w is not None and text(w) and title is None:
                title = text(w)
        attrs = m.find('attributes')
        if attrs is not None:
            if attrs.find('divisions') is not None:
                divisions = int(text(attrs.find('divisions')))
            if attrs.find('key/fifths') is not None:
                fifths = int(text(attrs.find('key/fifths')))
            if attrs.find('time/beats') is not None:
                time = (int(text(attrs.find('time/beats'))), int(text(attrs.find('time/beat-type'))))
        # Titles that are really performance notes ("re", "met Twee-en-derigh noten...") do not
        # start a piece; a real title comes with a new system or page in this collection.
        starts = title is not None and (current is None or m.find('print') is not None)
        if current is None and not starts:
            starts, title = True, text(root.find('work/work-title')) or 'Untitled'
        if starts:
            current = {'title': title, 'measures': [], 'notes': []}
            out.append(current)
        if current is None:
            continue
        current['measures'].append((m, divisions, time, fifths))
    return out

def split(d):
    """A duration as representable pieces, longest first (tied); None if it cannot be written."""
    out = []
    while d > 0:
        fit = [k for k in DUR if k <= d]
        if not fit:
            return None
        out.append(max(fit)); d -= max(fit)
    return out

def convert(piece, staff=None, transpose=0):
    bars, problems, times, keys = [], set(), [], []
    pitches, shortest = [], Fr(4)
    for m, divisions, time, fifths in piece['measures']:
        if time not in times: times.append(time)
        if fifths not in keys: keys.append(fifths)
        toks = []
        for b in m.findall('barline'):
            if b.find('repeat') is not None and b.find('repeat').get('direction') == 'forward':
                toks.append('|:')
            e = b.find('ending')
            if e is not None and e.get('type') == 'start':
                toks.append('[' + e.get('number', '1').split(',')[0].strip())
        for n in m.findall('note'):
            if staff is not None and (n.findtext('staff') or '1') != str(staff):
                continue
            if n.find('grace') is not None:
                problems.add('grace'); continue
            if n.find('chord') is not None:
                problems.add('chord'); continue
            if n.find('time-modification') is not None:
                problems.add('tuplets')
            d = Fr(int(text(n.find('duration'))), divisions)
            shortest = min(shortest, d)
            pieces_ = split(d)
            if pieces_ is None:
                problems.add(f'duration {d}'); pieces_ = [Fr(1)]
            if n.find('rest') is not None:
                toks.extend('R/' + DUR[x] for x in pieces_)
                continue
            p = n.find('pitch')
            step, octave = text(p.find('step')), int(text(p.find('octave')))
            alter = int(float(text(p.find('alter')))) if p.find('alter') is not None else 0
            octave += transpose // 12
            pitches.append(12 * (octave + 1) + NAT[step] + alter)
            tie = any(t.get('type') == 'start' for t in n.findall('tie'))
            name = f"{step}{ {1: '#', -1: 'b', 0: ''}[alter] }{octave}".replace(' ', '')
            for k, x in enumerate(pieces_):
                last = k == len(pieces_) - 1
                toks.append(f"{name}/{DUR[x]}" + ('~' if tie or not last else ''))
        for b in m.findall('barline'):
            if b.find('repeat') is not None and b.find('repeat').get('direction') == 'backward':
                toks.append(':|')
        bars.append(' '.join(toks))
    music = ' | '.join(bars).replace('|: |', '|:').replace(':| |', ':|')
    return {'bar_list': bars,
        'title': piece['title'], 'bars': len(bars), 'times': [f'{a}/{b}' for a, b in times],
        'keys': keys, 'problems': sorted(problems), 'shortest': str(shortest),
        'range': [min(pitches), max(pitches)] if pitches else None, 'music': music,
    }

def load(path):
    return pieces(ET.parse(path).getroot())

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('xml'); ap.add_argument('--list', action='store_true')
    ap.add_argument('--piece', type=int); ap.add_argument('--json', action='store_true')
    a = ap.parse_args()
    ps = [convert(p) for p in pieces(ET.parse(a.xml).getroot())]
    if a.list:
        for i, p in enumerate(ps):
            print(f"{i:3} {p['title'][:40]:40} bars {p['bars']:4} {','.join(p['times']):12} key {p['keys']} short {p['shortest']:5} range {p['range']} {' '.join(p['problems'])}")
    elif a.piece is not None:
        p = ps[a.piece]
        print(json.dumps(p, ensure_ascii=False) if a.json else p['music'])

if __name__ == '__main__':
    main()


# --- Playing order and re-barring -------------------------------------------------------------
# Van Eyck's prints join bars and start each repeated half with an upbeat, so the bars of the
# transcription are irregular. A piece is turned into one stream of notes in playing order
# (repeats and endings written out) and cut into regular bars again.

def events(measure, divisions, staff=None, transpose=0):
    out = []
    for n in measure.findall('note'):
        if staff is not None and (n.findtext('staff') or '1') != str(staff):
            continue
        if n.find('grace') is not None or n.find('chord') is not None:
            continue
        d = Fr(int(text(n.find('duration'))), divisions)
        if n.find('rest') is not None:
            out.append([None, d, False])
            continue
        p = n.find('pitch')
        step, octave = text(p.find('step')), int(text(p.find('octave'))) + transpose // 12
        alter = int(float(text(p.find('alter')))) if p.find('alter') is not None else 0
        name = f"{step}{ {1: '#', -1: 'b', 0: ''}[alter] }{octave}".replace(' ', '')
        out.append([name, d, any(t.get('type') == 'start' for t in n.findall('tie'))])
    return out

def playing_order(measures):
    """Indexes of the measures in the order they are played (each repeat taken once more)."""
    order, i, start, done, pass_ = [], 0, 0, set(), 1
    while i < len(measures):
        m = measures[i][0]
        for b in m.findall('barline'):
            if b.find('repeat') is not None and b.find('repeat').get('direction') == 'forward' and i not in done:
                start = i
        ending = next((b.find('ending') for b in m.findall('barline') if b.find('ending') is not None and b.find('ending').get('type') == 'start'), None)
        if ending is not None:
            numbers = [int(x) for x in ending.get('number', '1').replace(' ', '').split(',') if x.isdigit()]
            if pass_ not in numbers:
                # Skip this ending: up to and including the measure that ends it.
                while i < len(measures):
                    stop = any(b.find('ending') is not None and b.find('ending').get('type') in ('stop', 'discontinue') for b in measures[i][0].findall('barline'))
                    i += 1
                    if stop: break
                continue
        order.append(i)
        back = any(b.find('repeat') is not None and b.find('repeat').get('direction') == 'backward' for b in m.findall('barline'))
        if back and i not in done:
            done.add(i); pass_ = 2; i = start
            continue
        if back:
            pass_ = 1
            start = i + 1
        i += 1
    return order

def rebar(piece, staff=None, transpose=0):
    """Regular bars for a piece: (time, pickup, bars) or raises ValueError when it cannot be done."""
    measures = piece['measures']
    lengths = {Fr(t[0] * 4, t[1]) for _, _, t, _ in measures}
    bar = min(lengths)
    if any(l % bar for l in lengths):
        raise ValueError(f"time signatures {sorted(lengths)} do not share a bar")
    t = next(t for _, _, t, _ in measures if Fr(t[0] * 4, t[1]) == bar)
    if bar == 8:
        bar, t = Fr(4), (4, 4)  # the long bars of a pavan: written as two bars of four
    stream = []
    for i in playing_order(measures):
        m, divisions, _, _ = measures[i]
        stream.extend(events(m, divisions, staff, transpose))
    if not stream:
        raise ValueError('no notes')
    for name, d, _ in stream:
        if split(d) is None:
            raise ValueError(f"duration {d}")
    # Merge tied notes, so a note can be split again at the new bar lines.
    merged = []
    for name, d, tie in stream:
        if merged and merged[-1][2] and merged[-1][0] == name:
            merged[-1][1] += d; merged[-1][2] = tie
        else:
            merged.append([name, d, tie])
    first = Fr(sum(e[1] for e in events(measures[0][0], measures[0][1], staff, transpose)))
    pickup = first % bar
    bars, cur, pos = [], [], (bar - pickup) % bar
    for name, d, _ in merged:
        while d > 0:
            take = min(d, bar - pos)
            parts = split(take)
            for k, x in enumerate(parts):
                tie = name is not None and (k < len(parts) - 1 or d - take > 0)
                cur.append(('R' if name is None else name) + '/' + DUR[x] + ('~' if tie else ''))
            d -= take; pos += take
            if pos == bar:
                bars.append(' '.join(cur)); cur, pos = [], Fr(0)
    if cur:
        rest = split(bar - pos)
        bars.append(' '.join(cur + ['R/' + DUR[x] for x in rest]))
    return f"{t[0]}/{t[1]}", pickup, bars

def load_mxl(path):
    """The pieces of a compressed MusicXML (.mxl) file."""
    import zipfile
    with zipfile.ZipFile(path) as z:
        container = ET.fromstring(z.read('META-INF/container.xml'))
        root = container.find('.//rootfile').get('full-path')
        return pieces(ET.fromstring(z.read(root)))

def theme_end(piece):
    """Index of the last measure of the theme: where its second repeat closes (AABB), or the
    first double bar, whichever comes first; None when the piece has no such sections."""
    backs = 0
    for k, (m, *_) in enumerate(piece['measures']):
        for b in m.findall('barline'):
            rep = b.find('repeat')
            if rep is not None and rep.get('direction') == 'backward':
                backs += 1
                if backs == 2:
                    return k
            elif b.findtext('bar-style') in ('light-heavy', 'light-light') and k < len(piece['measures']) - 1:
                return k
    return None

def theme(piece):
    end = theme_end(piece)
    return None if end is None else {**piece, 'measures': piece['measures'][:end + 1]}
