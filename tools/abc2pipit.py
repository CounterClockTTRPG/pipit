#!/usr/bin/env python3
"""Convert an ABC tune into Pipit's music string, with its chord symbols as a band.

    tools/abc2pipit.py tunes.abc [--tune N] [--transpose SEMITONES] [--flats] [--json]

ABC (abcnotation.com, The Session, Playford and Susato collections) is a plain-text melody
format. Keeps bars, repeats |: :| and first/second endings, ties, triplets (3abc and broken
rhythms a>b. Chords [ceg] keep their top note; grace notes, decorations and lyrics are
dropped. Chord symbols ("Em", "D7", "G/B") become three band parts: a bass on the root
(or the given bass note) and two held chord tones.

--halve halves every note value (4/2 becomes 4/4), as modern editions of early music do.
--voice N reads the Nth voice of a multi-voice tune (a consort part).
--part Bass reads a named P: section instead (a ground or bass written after the tune).
--tune picks the Nth tune in the file (from 1, default 1). --json prints title, time, key,
pickup, music and the band parts; otherwise the music and a summary on stderr.
"""
import argparse, json, re, sys
from fractions import Fraction as Fr

STEP = {'C': 0, 'D': 2, 'E': 4, 'F': 5, 'G': 7, 'A': 9, 'B': 11}
SHARPS = ['C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#', 'A', 'Bb', 'B']
FLATS = ['C', 'Db', 'D', 'Eb', 'E', 'F', 'Gb', 'G', 'Ab', 'A', 'Bb', 'B']
# Keys with one to three flats keep the leading notes F# and C# (G minor, D minor).
FEW_FLATS = ['C', 'C#', 'D', 'Eb', 'E', 'F', 'F#', 'G', 'Ab', 'A', 'Bb', 'B']
ORDER_SHARPS, ORDER_FLATS = 'FCGDAEB', 'BEADGCF'
MODES = {'': 0, 'maj': 0, 'ion': 0, 'm': -3, 'min': -3, 'aeo': -3, 'dor': -2, 'mix': -1, 'lyd': 1, 'phr': -4, 'loc': -5}
MAJOR_FIFTHS = {'C': 0, 'G': 1, 'D': 2, 'A': 3, 'E': 4, 'B': 5, 'F#': 6, 'C#': 7, 'F': -1, 'Bb': -2, 'Eb': -3, 'Ab': -4, 'Db': -5, 'Gb': -6, 'Cb': -7}
# Pipit durations in quarter-note beats, longest first.
DURS = [(Fr(6), 'w.'), (Fr(4), 'w'), (Fr(3), 'h.'), (Fr(2), 'h'), (Fr(3, 2), 'q.'), (Fr(1), 'q'), (Fr(3, 4), 'e.'),
        (Fr(1, 2), 'e'), (Fr(3, 8), 's.'), (Fr(1, 4), 's'), (Fr(1, 8), 't')]
NAME_OF = {v: n for v, n in DURS}

def key_signature(k):
    """Fifths of an ABC key ("G", "Edor", "Bbm", "F#mix") and the accidental per letter."""
    m = re.match(r'\s*([A-G])([#b]?)\s*([A-Za-z]*)', k)
    if not m or k.strip().lower() in ('none', ''):
        return 0, {}
    tonic, mode = m.group(1) + m.group(2), m.group(3).lower()[:3]
    fifths = MAJOR_FIFTHS[tonic] + MODES.get(mode if mode in MODES else ('m' if mode.startswith('m') and mode != 'mix' else ''), 0)
    alter = {}
    for i in range(abs(fifths)):
        alter[(ORDER_SHARPS if fifths > 0 else ORDER_FLATS)[i]] = 1 if fifths > 0 else -1
    return fifths, alter

def tunes(text):
    out, cur = [], None
    for line in text.splitlines():
        if line.startswith('X:'):
            cur = [line]; out.append(cur)
        elif cur is not None:
            cur.append(line)
    return out or [text.splitlines()]

def pieces(length):
    """A length in beats as Pipit durations, longest first; None if it does not fit the grid."""
    out = []
    while length > 0:
        fit = next((v for v, _ in DURS if v <= length), None)
        if fit is None:
            return None
        out.append(fit); length -= fit
    return out

class Writer:
    """Collects notes into bars; splits them at bar lines and into writable durations."""
    def __init__(self, bar_beats, names):
        self.bar, self.names = bar_beats, names
        self.bars, self.cur, self.pos = [], [], Fr(0)
        self.marks_before = []  # repeat/ending marks waiting for the next bar
        self.first_len = None

    def note(self, midi, length, tie=False, triplet=False):
        if triplet:
            base = length * Fr(3, 2)
            if base not in NAME_OF:
                raise SystemExit(f"cannot write a triplet of {base} beats")
            self.cur.append(self.token(midi, NAME_OF[base] + '3', tie)); self.pos += length
            return
        parts = pieces(length)
        if parts is None:
            raise SystemExit(f"cannot write a length of {length} beats")
        for i, p in enumerate(parts):
            self.cur.append(self.token(midi, NAME_OF[p], tie or (midi is not None and i < len(parts) - 1)))
        self.pos += length

    def token(self, midi, dur, tie):
        name = 'R' if midi is None else f"{self.names[midi % 12]}{midi // 12 - 1}"
        return f"{name}/{dur}" + ('~' if tie else '')

    def barline(self, sign):
        if self.cur:
            if self.first_len is None:
                self.first_len = self.pos
            self.bars.append((self.marks_before, self.cur, sign))
            self.marks_before, self.cur, self.pos = [], [], Fr(0)
        elif sign != '|' and self.bars:
            # A repeat sign right after a bar line belongs to that bar line.
            m, c, s = self.bars[-1]
            self.bars[-1] = (m, c, sign if s == '|' else s + ' ' + sign)

    def music(self):
        out = []
        for i, (before, notes, sign) in enumerate(self.bars):
            out.append(' '.join(before + notes))
            if i < len(self.bars) - 1:
                out.append(sign)
            elif ':|' in sign:
                out.append(':|')
        return ' '.join(out).replace(' |: ', ' |: ').strip()

def parse(lines, transpose, names, part=None, voice_no=1, halve=False):
    """part: the label of a P: section to read instead of the tune (e.g. "Bass");
    voice_no: which V: voice to read, counted in order of appearance (1 = the first)."""
    head, body = {}, []
    in_body = False
    voices, voice = [], None
    selecting = part is None
    in_text = False
    for line in lines:
        if line.startswith('%%begintext'):
            in_text = True
        if in_text:  # a block of words (verses) between %%begintext and %%endtext
            in_text = not line.startswith('%%endtext')
            continue
        if line.startswith('%') or re.match(r'^[wWNHZSOBDFGIR]:', line):
            continue
        m = re.match(r'^([A-Za-z]):(.*)$', line)
        if m and not in_body:
            head[m.group(1)] = m.group(2).strip()
            if m.group(1) == 'K':
                in_body = True
            continue
        if not in_body:
            continue
        if not line.strip():
            break  # a blank line ends the tune (verses often follow)
        if m and m.group(1) == 'V':
            voice = m.group(2).split()[0] if m.group(2).split() else ''
            if voice not in voices:
                voices.append(voice)
            continue
        if m and m.group(1) == 'P':
            label = m.group(2).strip()
            if part is not None:
                selecting = label.lower() == part.lower()
            elif not re.fullmatch(r'[A-Z]\d*\.?', label):
                break  # another part (a bass, a descant): the tune ends here
            continue
        if voice is not None and voices.index(voice) != voice_no - 1:
            continue  # another voice
        if m and m.group(1) in 'KLM':
            line = f'[{m.group(1)}:{m.group(2).strip()}]'
        if selecting:
            body.append(line)
    meter = head.get('M', '4/4').replace('C|', '2/2').replace('C', '4/4')
    if not re.match(r'\s*\d+/\d+', meter):
        raise SystemExit(f"no time signature (M:{head.get('M')}): free rhythm cannot be written")
    up, low = (int(x) for x in meter.split()[0].split('/'))
    unit = Fr(head['L']) * 4 if 'L' in head else (Fr(1, 16) if up / low < 0.75 else Fr(1, 8)) * 4
    if halve:  # early music in halves (4/2, 3/2) read in quarters, as modern editions do
        low, unit = low * 2, unit / 2
        head['M'] = f'{up}/{low}' 
    fifths, keysig = key_signature(head.get('K', 'C'))
    bar_beats = Fr(up * 4, low)
    w = Writer(bar_beats, names)
    chords = []  # (beat from the start, symbol)
    beat = Fr(0)
    text = ' '.join(body)
    i, accidentals, broken, tuplet = 0, {}, None, 0
    pending_tie = False
    last = None  # (midi, length) of the last note, for broken rhythm
    while i < len(text):
        c = text[i]
        if c == '"':
            j = text.find('"', i + 1)
            if j < 0:  # an unclosed annotation: skip it
                i += 1; continue
            sym = text[i+1:j]; i = j + 1
            if sym and sym[0] not in '^_<>@':
                chords.append((beat, sym))
            continue
        if c == '!' or c == '+':
            j = text.find(c, i + 1); i = (j + 1) if j > 0 else i + 1; continue
        if c == '{':
            i = text.index('}', i) + 1; continue
        if c == '[' and re.match(r'\[[A-Za-z]:', text[i:]):
            j = text.index(']', i); field = text[i+1:j]; i = j + 1
            if field.startswith('K:'):
                fifths, keysig = key_signature(field[2:])
            continue
        m = re.match(r'(:*\|[\]|:]*\d?|\[\||::|\[\d|\|\d)', text[i:])
        if m and not (c == '[' and not re.match(r'\[[|\d]', text[i:])):
            sign = m.group(1); i += len(sign)
            accidentals = {}
            ending = re.search(r'(\d)', sign)
            core = re.sub(r'[\d\[]', '', sign)
            if core in ('', '|', '|]', '||', '[|'):
                out = '||' if core in ('||', '|]', '[|') else '|'
                if core == '':
                    out = None
            elif core.startswith(':') and core.endswith(':'):
                out = ':| |:'
            elif core.startswith(':'):
                out = ':|'
            elif core.endswith(':'):
                out = '|:'
            else:
                out = '|'
            if out is not None:
                w.barline(out)
            if ending:
                w.marks_before.append('[' + ending.group(1))
            continue
        if c == '(' and i + 1 < len(text) and text[i+1].isdigit():
            tuplet = int(text[i+1]); i += 2
            if tuplet != 3:
                raise SystemExit(f"only triplets are supported, not ({tuplet}")
            continue
        if c in '>' or c == '<':
            n = 1
            while i + n < len(text) and text[i+n] == c: n += 1
            broken = (c, n); i += n; continue
        nm = re.match(r"(\^\^|\^|__|_|=)?([A-Ga-g])([,']*)(\d*)(/*)(\d*)", text[i:])
        cm = re.match(r"\[([^\]]+)\](\d*)(/*)(\d*)", text[i:]) if c == '[' else None
        rm = re.match(r"([zxZ])(\d*)(/*)(\d*)", text[i:])
        if cm or nm or rm:
            if cm:
                inner = re.findall(r"(\^\^|\^|__|_|=)?([A-Ga-g])([,']*)", cm.group(1))
                acc, letter, octv = max(inner, key=lambda t: (STEP[t[1].upper()] + (12 if t[1].islower() else 0) + 12 * t[2].count("'") - 12 * t[2].count(',')))
                num, slashes, den = cm.group(2), cm.group(3), cm.group(4)
                # a length on the first inner note counts when the chord itself has none
                inner_len = re.match(r"[\^_=]*[A-Ga-g][,']*(\d*)(/*)(\d*)", cm.group(1))
                if not (num or slashes) and inner_len:
                    num, slashes, den = inner_len.group(1), inner_len.group(2), inner_len.group(3)
                i += len(cm.group(0)); rest = False
            elif rm:
                letter = rm.group(1); num, slashes, den = rm.group(2), rm.group(3), rm.group(4)
                i += len(rm.group(0)); rest = True
            else:
                acc, letter, octv, num, slashes, den = nm.groups(); i += len(nm.group(0)); rest = False
            mult = Fr(int(num)) if num else Fr(1)
            if slashes:
                mult /= Fr(int(den)) if den else Fr(2 ** len(slashes))
            length = unit * mult
            if rest and letter == 'Z':
                length = bar_beats * (int(num) if num else 1)
            midi = None
            if not rest:
                step = letter.upper()
                octave = 4 + (1 if letter.islower() else 0) + octv.count("'") - octv.count(',')
                key = (step, octave)
                if acc:
                    accidentals[key] = {'^': 1, '^^': 2, '_': -1, '__': -2, '=': 0}[acc]
                alter = accidentals.get(key, keysig.get(step, 0))
                midi = 12 * (octave + 1) + STEP[step] + alter + transpose
            if broken:
                kind, n = broken
                short = Fr(1, 2 ** n)
                factor_prev, factor_this = (2 - short, short) if kind == '>' else (short, 2 - short)
                # lengthen or shorten the previous note: rewrite its tokens
                pm, plen, pstate = last
                w.cur = w.cur[:pstate]; w.pos -= plen; beat -= plen
                w.note(pm, plen * factor_prev, tie=False)
                beat += plen * factor_prev
                length = length * factor_this
                broken = None
            tie = False
            if i < len(text) and text[i] == '-':
                tie = True; i += 1
            state = len(w.cur)
            trip = tuplet > 0
            w.note(midi, length * Fr(2, 3) if trip else length, tie=tie, triplet=trip)
            if trip:
                tuplet -= 1
            beat += length * Fr(2, 3) if trip else length
            last = (midi, length, state)
            continue
        i += 1
    w.barline('|')
    pickup = w.first_len if w.first_len is not None and w.first_len < bar_beats else Fr(0)
    return head, w, chords, pickup, fifths, bar_beats

CHORD_STEPS = {'': (4, 7), 'm': (3, 7), 'min': (3, 7), 'dim': (3, 6), 'aug': (4, 8), '+': (4, 8), 'sus4': (5, 7), 'sus2': (2, 7),
               '7': (4, 10), 'maj7': (4, 11), 'm7': (3, 10), 'dim7': (3, 9), '6': (4, 9), 'm6': (3, 9)}

def band(chords, total, pickup, bar_beats, names, transpose):
    """Bass (root or slash bass, around E2-D#3) and two held chord tones (around G3-F#4)."""
    # Only real chord names count ("d" or "g g" are accordion bass buttons in some books).
    chords = [c for c in chords if re.match(r'[A-G]', c[1])]
    if not chords:
        return []
    events = []
    for k, (start, sym) in enumerate(chords):
        m = re.match(r'([A-G])([#b]?)([^/]*)(?:/([A-G][#b]?))?', sym)
        if not m:
            continue
        root = (STEP[m.group(1)] + {'#': 1, 'b': -1, '': 0}[m.group(2)] + transpose) % 12
        quality = m.group(3).strip()
        third, fifth = CHORD_STEPS.get(quality, CHORD_STEPS.get(quality[:1] if quality.startswith('m') and not quality.startswith('maj') else '', (4, 7)))
        bass = root if not m.group(4) else (STEP[m.group(4)[0]] + {'#': 1, 'b': -1, '': 0}[m.group(4)[1:] or ''] + transpose) % 12
        end = chords[k + 1][0] if k + 1 < len(chords) else total
        events.append((start, end, 40 + (bass - 4) % 12, 55 + (root + third - 7) % 12, 55 + (root + fifth - 7) % 12))
    parts = []
    for idx, (pid, name, sound) in enumerate([('bass', 'Bass', 'cello'), ('chord1', 'Chords', 'strings'), ('chord2', 'Chords 2', 'strings')]):
        w = Writer(bar_beats, names)
        pos = Fr(0)
        # Bars of the band are counted from the start of the song (the pickup is a short first bar).
        boundary = pickup if pickup else bar_beats
        def emit(midi, start, end):
            nonlocal pos, boundary
            t = start
            while t < end:
                take = min(end, boundary) - t
                if take > 0:
                    w.note(midi, take, tie=midi is not None and t + take < end)
                    t += take
                if t >= boundary:
                    w.barline('|'); boundary += bar_beats
        at = Fr(0)
        for e in events:
            if e[0] > at:
                emit(None, at, e[0])
            emit(e[2 + idx], e[0], e[1])
            at = e[1]
        w.barline('|')
        parts.append({"id": pid, "name": name, "sound": sound, "music": w.music()})
    return parts

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('abc'); ap.add_argument('--tune', type=int, default=1)
    ap.add_argument('--transpose', type=int, default=0); ap.add_argument('--flats', action='store_true')
    ap.add_argument('--json', action='store_true')
    ap.add_argument('--part', help='read this P: section (e.g. Bass) instead of the tune')
    ap.add_argument('--halve', action='store_true', help='halve all note values (4/2 becomes 4/4)')
    ap.add_argument('--voice', type=int, default=1, help='read the Nth V: voice (1 = the first, the default)')
    a = ap.parse_args()
    lines = tunes(open(a.abc, encoding='utf-8', errors='replace').read())[a.tune - 1]
    fifths0, _ = key_signature(next((l[2:] for l in lines if l.startswith('K:')), 'C'))
    target = ((fifths0 + 7 * a.transpose + 6) % 12) - 6  # the key signature after transposing
    names = (FLATS if target <= -4 else FEW_FLATS) if a.flats or target < 0 else SHARPS
    head, w, chords, pickup, fifths, bar_beats = parse(lines, a.transpose, names, a.part, a.voice, a.halve)
    music = w.music()
    total = sum(sum(pieces_len(t) for t in notes) for _, notes, _ in w.bars)
    pick = NAME_OF.get(pickup, None) if pickup else None
    if pickup and pick is None:
        raise SystemExit(f"pickup of {pickup} beats is not a single duration")
    # Band length: the performed length (repeats expanded) is what the band must cover.
    performed = performed_beats(music)
    parts = band(performed_chords(music, chords), performed, pickup, bar_beats, names, a.transpose)
    midis = [int(m.group(2)) * 12 + 12 + SHARPS.index(m.group(1)) if m.group(1) in SHARPS else int(m.group(2)) * 12 + 12 + FLATS.index(m.group(1))
             for m in re.finditer(r'\b([A-G][#b]?)(\d)/', music)]
    info = {"title": head.get('T', ''), "time": head.get('M', '4/4').split()[0].replace('C|', '2/2').replace('C', '4/4'), "key": head.get('K', ''),
            "pickup": pick, "music": music, "accompaniment": parts,
            "range": [min(midis), max(midis)] if midis else None, "source": {k: head[k] for k in ('S', 'Z', 'O', 'C', 'B') if k in head}}
    if a.json:
        print(json.dumps(info, ensure_ascii=False, indent=1))
    else:
        print(music)
        print(f"{info['title']}: {info['time']}, key {info['key']}, pickup {pick}, range {info['range']}, {len(chords)} chord symbols", file=sys.stderr)

def pieces_len(token):
    d = token.split('/')[1].rstrip('~,')
    base = dict((n, v) for v, n in DURS)[d.rstrip('3')] if d.endswith('3') else dict((n, v) for v, n in DURS)[d]
    return base * Fr(2, 3) if d.endswith('3') else base

def written_bars(music):
    """The written bars (token lists) with their marks, as SongParser reads them."""
    bars, start, ending = [], False, 0
    for part in re.split(r'(\|:|:\||\|\||\||\[1|\[2)', music):
        part = part.strip()
        if part == '|:': start = True
        elif part == ':|':
            if bars: bars[-1]['end'] = True
            if ending == 1: ending = 0
        elif part == '[1': ending = 1
        elif part == '[2': ending = 2
        elif part in ('|', '||') or not part: pass
        else:
            bars.append({'notes': part.split(), 'start': start, 'end': False, 'ending': ending}); start = False
            if ending == 2: ending = 0
    return bars

def performance_order(music, indices=False):
    """Bars in played order, expanding |: :| and [1 [2 like SongParser (token lists, or written indices)."""
    bars = written_bars(music)
    order, i, rs, taken = [], 0, 0, set()
    while i < len(bars):
        b = bars[i]
        if b['start']: rs = i
        if b['ending'] == 1 and i in taken:
            i += 1; continue
        order.append(i if indices else b['notes'])
        if b['end'] and i not in taken:
            taken.update(range(rs, i + 1)); i = rs; continue
        i += 1
    return order

def performed_beats(music):
    """Where the last note ends, repeats expanded (trailing rests do not count: the band stops with the tune)."""
    t, end = Fr(0), Fr(0)
    for bar in performance_order(music):
        for tok in bar:
            t += pieces_len(tok)
            if not tok.startswith('R/'):
                end = t
    return end

def performed_chords(music, chords):
    """Chord symbols are at written beats; repeats replay them at the performed beats."""
    bars = written_bars(music)
    starts, t = [], Fr(0)
    for b in bars:
        starts.append(t); t += sum(pieces_len(x) for x in b['notes'])
    out, pos = [], Fr(0)
    for k in performance_order(music, indices=True):
        length = sum(pieces_len(x) for x in bars[k]['notes'])
        out += [(pos + s - starts[k], sym) for s, sym in chords if starts[k] <= s < starts[k] + length]
        pos += length
    return out

main()
