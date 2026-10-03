#!/usr/bin/env python3
"""Creates repertoire songs from multi-part MIDI sources (see SONGS below).

    tools/import-songs.py

Playable parts and accompaniment are converted with midi2pipit.py from the same source and cut
from the same bar (leading bars where no playable part plays are dropped), the key is chosen by
fewest printed accidentals, and short final bars are padded with rests. Existing about/tip texts
are kept when a song is regenerated.
"""
import json, os, re, subprocess
from fractions import Fraction

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CONV = os.path.join(ROOT, 'tools', 'midi2pipit.py')
M = os.path.join(ROOT, 'tools', 'sources', 'mutopia')
MAJOR = {0: 'C', 1: 'G', 2: 'D', 3: 'A', -1: 'F', -2: 'Bb', -3: 'Eb'}
SHARPS, FLATS = 'FCGDAEB', 'BEADGCF'
DUR = {'w': 4, 'h': 2, 'q': 1, 'e': 0.5, 's': 0.25, 't': 0.125}
REST = {6: 'R/w.', 4: 'R/w', 3: 'R/h.', 2: 'R/h', 1.5: 'R/q.', 1: 'R/q', 0.75: 'R/e.', 0.5: 'R/e', 0.25: 'R/s'}

HANDEL = 'Mutopia Project edition after Chrysander (Händel-Gesellschaft, 1879), CC BY-SA 3.0; converted with tools/import-songs.py'
TELEMANN = 'Mutopia Project edition after Der getreue Music-Meister (1728), Public Domain; converted with tools/import-songs.py'
HANDEL_ABOUT = {
    'en': "Handel wrote a handful of sonatas specifically for the alto recorder (\"flauto\") with basso continuo, in London in the 1720s. They were printed around 1730 as part of his Opus 1, and recorder players have loved them ever since.",
    'nl': "Händel schreef in de jaren 1720 in Londen een handvol sonates speciaal voor de altblokfluit (\"flauto\") met basso continuo. Ze werden rond 1730 gedrukt als deel van zijn Opus 1, en blokfluitisten zijn er sindsdien dol op.",
}
TELEMANN_ABOUT = {
    'en': "Telemann published \"Der getreue Music-Meister\" in Hamburg in 1728-29, a music magazine that came out every two weeks with new pieces for amateurs. This duet for two recorders appeared in it: both parts are equally interesting, so you can play either while the band plays the other.",
    'nl': "Telemann gaf in 1728-29 in Hamburg \"Der getreue Music-Meister\" uit, een muziektijdschrift dat om de twee weken verscheen met nieuwe stukken voor liefhebbers. Dit duet voor twee blokfluiten stond erin: beide partijen zijn even interessant, dus je kan er een spelen terwijl de band de andere speelt.",
}
PLAY_EITHER = {'en': "Pick a part: the band plays the other recorder.", 'nl': "Kies een partij: de band speelt de andere blokfluit."}
HANDEL_TIP = {'en': "Written for alto recorder; on a soprano Pipit moves it to fit. The bass line is the harpsichord's: let it carry you.",
              'nl': "Geschreven voor altblokfluit; op een sopraan verschuift Pipit het zodat het past. De baslijn is van het klavecimbel: laat je erdoor dragen."}

def handel(hwv, key, key_en, key_nl, movements):
    out = []
    for i, (mid, roman, name, time, tempo, *pickup) in enumerate(movements):
        out.append(dict(id=f'handel-hwv{hwv}-{i + 1}', midi=f'{M}/HWV{hwv}-Sonata-mids/{mid}', time=time, tempo=tempo, key=key,
            pickup=(pickup or [None])[0],
            title={'en': f'Sonata in {key_en}, HWV {hwv}: {roman}. {name}', 'nl': f'Sonate in {key_nl}, HWV {hwv}: {roman}. {name}'},
            composer='George Frideric Handel', source=HANDEL, about=HANDEL_ABOUT, tip=HANDEL_TIP,
            parts=[(1, 'Recorder', 'alto')], accompaniment=[(2, 'Basso continuo', 'harpsichord', 'bottom')]))
    return out

SONGS = [
    dict(id='brahms-lullaby', midi=f'{M}/LullabyBrahms-C-LullabyBrahms-C.mid', time='3/4', tempo=88, pickup='q',
         title={'en': "Brahms' Lullaby", 'nl': 'Wiegelied van Brahms', 'fr': 'Berceuse de Brahms'}, composer='Johannes Brahms',
         source='Mutopia Project arrangement (Public Domain); converted with tools/import-songs.py',
         about={'en': "Brahms wrote this \"Wiegenlied\" (cradle song) in 1868 as a present for a friend, Bertha Faber, when her second son was born. It became one of the best-known lullabies in the world.",
                'nl': "Brahms schreef dit \"Wiegenlied\" in 1868 als cadeau voor een vriendin, Bertha Faber, toen haar tweede zoon geboren werd. Het werd een van de bekendste slaapliedjes ter wereld."},
         tip={'en': "Soft and calm, in three. Most of it uses the low notes: very gentle air.", 'nl': "Zacht en rustig, in drie. Het meeste gebruikt de lage noten: heel zachte lucht."},
         parts=[(1, 'Melody', 'soprano')], accompaniment=[(2, 'Piano', 'piano', 'top')]),
    dict(id='japart-tmeiskin', midi=f'{M}/27-tmeiskin-27-tmeiskin.mid', time='2/2', tempo=120,
         title={'en': 'Tmeiskin was jonck', 'nl': 'Tmeiskin was jonck'}, composer='Jean Japart',
         source='Mutopia Project edition after the facsimile of the Odhecaton (Venice, 1501/1504), Public Domain; converted with tools/import-songs.py',
         about={'en': "\"The girl was young\": a Flemish song, set for four voices by the Franco-Flemish composer Jean Japart. It was printed in Venice in the Odhecaton (1501), the first book of polyphonic music ever printed with movable type. Recorder consorts played music like this.",
                'nl': "\"Het meisje was jong\": een Vlaams lied, voor vier stemmen gezet door de Franco-Vlaamse componist Jean Japart. Het werd in Venetië gedrukt in het Odhecaton (1501), het allereerste boek met meerstemmige muziek gedrukt met losse letters. Blokfluitconsorten speelden zulke muziek."},
         tip={'en': "With the band on, you play the top voice of a Renaissance consort.", 'nl': "Met de band aan speel je de bovenstem van een renaissanceconsort."},
         parts=[(1, 'Superius', 'soprano')], accompaniment=[(2, 'Altus', 'recorder', 'top'), (3, 'Tenor', 'recorder', 'top'), (4, 'Bassus', 'bassoon', 'top')]),
] + handel(360, 'Gm', 'G minor', 'g klein', [
    ('Sonata.mid', 'I', 'Larghetto', '4/4', 60), ('Sonata-1.mid', 'II', 'Andante', '3/4', 76),
    ('Sonata-2.mid', 'III', 'Adagio', '3/2', 60), ('Sonata-3.mid', 'IV', 'Presto', '4/4', 100)]) + handel(365, 'C', 'C major', 'C groot', [
    ('Sonata.mid', 'I', 'Larghetto', '4/4', 60), ('Sonata-1.mid', 'II', 'Allegro', '3/8', 90), ('Sonata-2.mid', 'III', 'Larghetto', '3/4', 66),
    ('Sonata-3.mid', 'IV', 'Tempo di Gavotta', '2/2', 112, 'h'), ('Sonata-4.mid', 'V', 'Allegro', '3/8', 90)]) + handel(369, 'F', 'F major', 'F groot', [
    ('Sonata.mid', 'I', 'Larghetto', '3/4', 66), ('Sonata-1.mid', 'II', 'Allegro', '4/4', 100),
    ('Sonata-2.mid', 'III', 'Siciliana', '12/8', 72), ('Sonata-3.mid', 'IV', 'Allegro', '12/8', 116)]) + [
    dict(id=f'telemann-twv40-107-{i + 1}', midi=f'{M}/TWV40-Duett-Telemann-mids/{mid}', time=time, tempo=tempo, key='Bb',
         title={'en': f'Duet TWV 40:107: {roman}. {name}', 'nl': f'Duet TWV 40:107: {roman}. {name}'}, composer='Georg Philipp Telemann',
         source=TELEMANN, about=TELEMANN_ABOUT, tip=PLAY_EITHER,
         parts=[(1, 'Recorder 1', 'alto'), (2, 'Recorder 2', 'alto')], accompaniment=[],
         # The grace note (\grace a4) before bar 27 of the second part took its time from the B flat.
         fix=[(1, 25, 'G5/h A5/q Bb5/e. A5/s', 'G5/h A5/q Bb5/q')] if i == 3 else [])
    for i, (mid, roman, name, time, tempo) in enumerate([
        ('Duett-Telemann.mid', 'I', 'Affettuoso', '4/4', 66), ('Duett-Telemann-1.mid', 'II', 'Allegro', '3/4', 120),
        ('Duett-Telemann-2.mid', 'III', 'Andante', '6/4', 108), ('Duett-Telemann-3.mid', 'IV', 'Presto', '2/2', 132)])
]

PICKUP = {'h': 8, 'q': 4, 'e': 2}
FIFTHS = {'C': 0, 'Am': 0, 'G': 1, 'D': 2, 'F': -1, 'Dm': -1, 'Bb': -2, 'Gm': -2, 'Eb': -3}

TELEMANN_SONATA_ABOUT = {
    'en': "Telemann wrote six sonatas for two flutes or recorders without any accompaniment; they were engraved in Paris in the 1730s. The two parts take turns with the tune and answer each other, so either player can lead.",
    'nl': "Telemann schreef zes sonates voor twee fluiten of blokfluiten zonder begeleiding; ze werden in de jaren 1730 in Parijs gegraveerd. De twee partijen wisselen de melodie af en antwoorden elkaar, zodat elke speler kan leiden.",
}
MORE_MUTOPIA = [
    dict(id='polonaise-bwv-anh-119', midi=f'{M}/BWV-119.mid', time='3/4', tempo=80, key='Gm', transpose=12,
         title={'en': 'Polonaise in G minor, BWV Anh. 119', 'nl': 'Polonaise in g klein, BWV Anh. 119'},
         composer='Anonymous (Notebook for Anna Magdalena Bach)',
         source='Mutopia Project edition after the Bach-Gesellschaft Ausgabe vol. 43 (1894), Public Domain; an octave up for the alto; converted with tools/import-songs.py',
         about={'en': "From the music notebook that J. S. Bach gave his wife Anna Magdalena in 1725. A polonaise is a stately Polish dance in three; nobody knows who wrote this one. An octave up, it fits the alto recorder from its lowest note to its highest.",
                'nl': "Uit het muziekboekje dat J. S. Bach in 1725 aan zijn vrouw Anna Magdalena gaf. Een polonaise is een statige Poolse dans in drie; niemand weet wie deze schreef. Een octaaf hoger past ze op de altblokfluit van de laagste tot de hoogste noot."},
         tip={'en': "Walk it like a proud dance: the first beat of every bar is a little heavier.", 'nl': "Speel ze als een fiere dans: de eerste tel van elke maat is iets zwaarder."},
         parts=[(1, 'Recorder', 'alto')], accompaniment=[(2, 'Bass', 'harpsichord', 'bottom')]),
    dict(id='marcello-ciaccona', midi=f'{M}/O2-Ciaccona.mid', time='3/4', tempo=88, key='F',
         title={'en': 'Ciaccona, Op. 2 No. 13', 'nl': 'Ciaccona, opus 2 nr. 13'}, composer='Benedetto Marcello',
         source='Mutopia Project edition after Gieddes Samling I,51 (Copenhagen), CC BY-SA 3.0; converted with tools/import-songs.py',
         about={'en': "Benedetto Marcello (1686-1739) was a Venetian nobleman, lawyer and composer. A ciaccona repeats the same short bass line over and over while the melody above it changes every time: here the recorder goes through more than forty variations.",
                'nl': "Benedetto Marcello (1686-1739) was een Venetiaans edelman, advocaat en componist. Een ciaccona herhaalt steeds dezelfde korte baslijn terwijl de melodie erboven telkens verandert: hier gaat de blokfluit door meer dan veertig variaties."},
         tip={'en': "An advanced alto piece. Listen for the bass: every four bars it starts again, and you can practise one variation at a time.",
              'nl': "Een gevorderd stuk voor alt. Luister naar de bas: om de vier maten begint ze opnieuw, en je kan een variatie per keer oefenen."},
         parts=[(1, 'Recorder', 'alto')], accompaniment=[(2, 'Basso continuo', 'harpsichord', 'bottom')]),
    dict(id='bach-contrapunctus-16', midi=f'{M}/BWV1080-rectus.mid', time='2/2', tempo=96, key='Dm', pickup='q',
         title={'en': 'The Art of Fugue: Contrapunctus XVI', 'nl': 'De kunst van de fuga: Contrapunctus XVI'}, composer='Johann Sebastian Bach',
         source='Mutopia Project edition (BWV 1080), set for two recorders and bass, Public Domain; converted with tools/import-songs.py',
         about={'en': "In \"The Art of Fugue\", Bach's last great work, every piece is built from one short theme. This is a mirror fugue: it also exists upside down, with every interval turned over. Here two recorders and a bass play it.",
                'nl': "In \"De kunst van de fuga\", Bachs laatste grote werk, is elk stuk gebouwd op één kort thema. Dit is een spiegelfuga: ze bestaat ook omgekeerd, met elk interval ondersteboven. Hier spelen twee blokfluiten en een bas ze."},
         tip={'en': "Pick a part. Each voice enters with the theme in turn: listen for it in the others too.",
              'nl': "Kies een partij. Elke stem valt beurtelings in met het thema: luister er ook naar bij de anderen."},
         parts=[(1, 'Recorder 1', 'alto'), (2, 'Recorder 2', 'alto')], accompaniment=[(3, 'Bass', 'bassoon', 'top')]),
] + [
    dict(id=f'telemann-sonata-two-recorders-1-{i + 1}', midi=f'{M}/Sonata_1-Sonata_1-mids/{mid}', time=time, tempo=tempo,
         title={'en': f'Sonata No. 1 for two recorders: {roman}. {name}', 'nl': f'Sonate nr. 1 voor twee blokfluiten: {roman}. {name}'},
         composer='Georg Philipp Telemann',
         source='Mutopia Project edition after the Paris edition engraved by J.-L. Renou (1730s), Public Domain; converted with tools/import-songs.py',
         about=TELEMANN_SONATA_ABOUT, tip=PLAY_EITHER,
         parts=[(2, 'Recorder 1', 'alto'), (1, 'Recorder 2', 'alto')], accompaniment=[])
    for i, (mid, roman, name, time, tempo) in enumerate([
        ('Sonata_1.mid', 'I', 'Dolce', '4/4', 50), ('Sonata_1-1.mid', 'II', 'Allegro', '4/4', 76),
        ('Sonata_1-2.mid', 'III', 'Largo', '3/4', 60), ('Sonata_1-3.mid', 'IV', 'Vivace', '3/8', 80)])
]
SONGS += MORE_MUTOPIA

B = os.path.join(ROOT, 'tools', 'sources', 'personal-bellugi')
BELLUGI = 'davidbellugi.com/midi.html: MIDI transcription by David Bellugi (bellugi@dada.it), used with acknowledgment as the files ask; PERSONAL USE ONLY (no licence stated)'
BELLUGI_DIM = BELLUGI.replace('MIDI transcription', 'MIDI transcription (diminutions)')
ENCINA_ABOUT = {
    'en': "Juan del Encina (1468-1529) was a poet, playwright and composer at the court of the Duke of Alba in Spain. His songs were collected in the \"Cancionero de Palacio\", the songbook of the royal palace. Here David Bellugi sets one for a recorder consort.",
    'nl': "Juan del Encina (1468-1529) was dichter, toneelschrijver en componist aan het hof van de hertog van Alba in Spanje. Zijn liederen staan in de \"Cancionero de Palacio\", het liedboek van het koninklijk paleis. David Bellugi zet er hier een voor een blokfluitconsort.",
}
DIM_TIP = {'en': "Diminutions split long notes into quick runs around the tune. Learn the plain tune first, then practise the runs slowly with the speed dial.",
           'nl': "Diminuties verdelen lange noten in snelle loopjes rond de melodie. Leer eerst de gewone melodie, en oefen de loopjes dan traag met de snelheidsknop."}
CONSORT_TIP = {'en': "Switch the band on: you play the top line of a whole recorder consort. Listen to the others and keep a steady beat.",
               'nl': "Zet de band aan: jij speelt de bovenstem van een heel blokfluitconsort. Luister naar de anderen en houd een gelijk tempo."}
def consort(tracks, first=1):
    names = ['Altus', 'Tenor', 'Bassus', 'Voice 5', 'Voice 6']
    return [(t, names[k] if k < len(names) else f'Voice {k + 2}', 'bassoon' if k == len(tracks) - 1 else 'recorder', 'top') for k, t in enumerate(tracks)]
HANDEL_CONCERTO_ABOUT = {
    'en': "Handel's Organ Concerto Op. 4 No. 6 was first a concerto for harp, played in London in 1736 between the acts of his ode \"Alexander's Feast\". This version for recorder, bassoon and strings is an arrangement by David Bellugi. With the band on, you are the soloist in front of a baroque orchestra.",
    'nl': "Händels Orgelconcerto opus 4 nr. 6 was eerst een concerto voor harp, gespeeld in Londen in 1736 tussen de bedrijven van zijn ode \"Alexander's Feast\". Deze versie voor blokfluit, fagot en strijkers is een bewerking van David Bellugi. Met de band aan ben jij de solist voor een barokorkest.",
}
HANDEL_CONCERTO_TIP = {'en': "Written for alto recorder. Listen to the orchestra's opening bars before you come in.",
                       'nl': "Geschreven voor altblokfluit. Luister naar de openingsmaten van het orkest voor je invalt."}
PERSONAL = [
    dict(id='procurans-odium', dir='personal', midi=f'{B}/procuran.mid', time='3/4', tempo=100,
         title={'en': 'Procurans odium', 'nl': 'Procurans odium'}, composer='Anonymous (14th century), set by David Bellugi', source=BELLUGI,
         about={'en': "A Latin song from the 14th century, more than 600 years old. Its words warn against people who stir up hatred. It moves in a gentle three, and the top voice uses only a handful of notes.",
                'nl': "Een Latijns lied uit de 14de eeuw, meer dan 600 jaar oud. De tekst waarschuwt tegen mensen die haat zaaien. Het beweegt in een rustige drie, en de bovenstem gebruikt maar een handvol noten."},
         tip=CONSORT_TIP, parts=[(1, 'Cantus', 'soprano')], accompaniment=consort([2, 3, 4, 5, 6])),
    dict(id='tobacco-fumes-away', dir='personal', midi=f'{B}/tobacco.mid', time='2/2', tempo=120, keep_intro=True,
         title={'en': 'Tobacco fumes away', 'nl': 'Tobacco fumes away'}, composer='Thomas Ravenscroft', source=BELLUGI,
         about={'en': "Thomas Ravenscroft (about 1582-1635) collected and wrote cheerful English songs for friends to sing together. This one, from 1614, jokes that tobacco makes your worries vanish like smoke. (Today we know it does the opposite!)",
                'nl': "Thomas Ravenscroft (rond 1582-1635) verzamelde en schreef vrolijke Engelse liederen om samen te zingen. Dit lied uit 1614 grapt dat tabak je zorgen doet verdwijnen als rook. (Vandaag weten we dat het omgekeerde waar is!)"},
         tip={'en': "The band starts first: count its two bars, then come in.", 'nl': "De band begint eerst: tel zijn twee maten mee, en val dan in."},
         parts=[(1, 'Cantus', 'soprano')], accompaniment=consort([2, 3, 4])),
    dict(id='encina-pues-que-ya', dir='personal', key='Dm', midi=f'{B}/encina3.mid', time='3/4', tempo=96,
         title={'en': 'Pues que ya nunca nos veis', 'nl': 'Pues que ya nunca nos veis'}, composer='Juan del Encina', source=BELLUGI,
         about=ENCINA_ABOUT, tip=CONSORT_TIP, parts=[(2, 'Cantus', 'soprano')], accompaniment=consort([3, 4, 5])),
    dict(id='encina-pues-que-ya-diminutions', dir='personal', key='Dm', midi=f'{B}/encina3.mid', time='3/4', tempo=96,
         title={'en': 'Pues que ya nunca nos veis (with diminutions)', 'nl': 'Pues que ya nunca nos veis (met diminuties)'},
         composer='Juan del Encina, diminutions by David Bellugi', source=BELLUGI_DIM,
         about=ENCINA_ABOUT, tip=DIM_TIP, parts=[(1, 'Diminutions', 'soprano')], accompaniment=consort([2, 3, 4, 5])),
    dict(id='encina-triste-espana', dir='personal', key='Gm', midi=f'{B}/encina2.mid', time='2/2', tempo=88,
         title={'en': 'Triste España sin ventura', 'nl': 'Triste España sin ventura'}, composer='Juan del Encina', source=BELLUGI,
         about=ENCINA_ABOUT, tip={'en': "\"Sad Spain, without fortune\": a lament, written in 1497 when the crown prince died. Play it slowly and sing through the long notes.",
                                  'nl': "\"Droevig Spanje, zonder geluk\": een klaagzang, geschreven in 1497 toen de kroonprins stierf. Speel ze traag en laat de lange noten zingen."},
         parts=[(2, 'Cantus', 'soprano')], accompaniment=consort([3, 4, 5])),
    dict(id='encina-triste-espana-diminutions', dir='personal', key='Gm', midi=f'{B}/encina2.mid', time='2/2', tempo=88, transpose=12,
         title={'en': 'Triste España sin ventura (with diminutions)', 'nl': 'Triste España sin ventura (met diminuties)'},
         composer='Juan del Encina, diminutions by David Bellugi', source=BELLUGI_DIM + '; an octave up to fit the soprano',
         about=ENCINA_ABOUT, tip=DIM_TIP, parts=[(1, 'Diminutions', 'soprano')], accompaniment=consort([2, 3, 4, 5])),
    dict(id='lasso-mais-qui-pourroit', dir='personal', midi=f'{B}/lasso2.mid', time='2/2', tempo=112,
         title={'en': 'Mais qui pourroit estre celuy', 'nl': 'Mais qui pourroit estre celuy'}, composer='Orlando di Lasso', source=BELLUGI,
         about={'en': "Orlando di Lasso (about 1532-1594) was born in Mons, in what is now Belgium, and became one of the most famous composers in Europe, working in Italy and Munich. He wrote more than 2000 pieces; this is a French chanson for three voices.",
                'nl': "Orlando di Lasso (rond 1532-1594) werd geboren in Bergen, in het huidige België, en werd een van de beroemdste componisten van Europa, in Italië en München. Hij schreef meer dan 2000 stukken; dit is een Franse chanson voor drie stemmen."},
         tip=CONSORT_TIP, parts=[(1, 'Cantus', 'soprano')], accompaniment=[(2, 'Altus', 'recorder', 'top'), (3, 'Bassus', 'bassoon', 'top')]),
] + [
    dict(id=f'handel-recorder-concerto-{i + 1}', dir='personal', midi=f'{B}/handel_{i + 1}.mid', time=time, tempo=tempo, key='Bb', keep_intro=True,
         title={'en': f'Concerto in B♭, after Op. 4 No. 6: {roman}. {name}', 'nl': f'Concerto in Bes, naar opus 4 nr. 6: {roman}. {name}'},
         composer='George Frideric Handel, arranged by David Bellugi', source=BELLUGI,
         about=HANDEL_CONCERTO_ABOUT, tip=HANDEL_CONCERTO_TIP, parts=[(1, 'Recorder', 'alto')], accompaniment=band)
    for i, (roman, name, time, tempo, band) in enumerate([
        ('I', 'Andante allegro', '4/4', 84, [(2, 'Bassoon', 'bassoon', 'top'), (3, 'Violin I', 'violin', 'top'), (4, 'Violin II', 'violin', 'top'), (5, 'Viola', 'viola', 'top'), (6, 'Cello', 'cello', 'top'), (8, 'Organ', 'organ', 'bottom')]),
        ('II', 'Larghetto', '3/4', 60, [(2, 'Violin I', 'violin', 'top'), (3, 'Violin II', 'violin', 'top'), (4, 'Viola', 'viola', 'top'), (5, 'Cello', 'cello', 'top'), (7, 'Organ', 'organ', 'bottom')]),
        ('III', 'Allegro moderato', '3/8', 96, [(2, 'Bassoon', 'bassoon', 'top'), (3, 'Violin I', 'violin', 'top'), (4, 'Violin II', 'violin', 'top'), (5, 'Viola', 'viola', 'top'), (6, 'Cello', 'cello', 'top'), (8, 'Organ', 'organ', 'bottom')]),
    ])
]
SONGS += PERSONAL

def convert(midi, track, time, flats, voice='top', pickup=None, transpose=0):
    args = ['python3', CONV, midi, '--track', str(track), '--time', time, '--voice', voice, '--transpose', str(transpose)] + (['--flats'] if flats else [])
    if pickup:
        args += ['--pickup', str(PICKUP[pickup])]
    return [b.strip() for b in subprocess.run(args, capture_output=True, text=True, check=True).stdout.strip().split(' | ')]

ENHARMONIC = {'C#': 'Db', 'D#': 'Eb', 'F#': 'Gb', 'G#': 'Ab', 'A#': 'Bb', 'Db': 'C#', 'Eb': 'D#', 'Gb': 'F#', 'Ab': 'G#', 'Bb': 'A#'}

def respell(bars, key):
    """Black keys as the key's signature has them plus the next flat (E flat in F, A flat in G minor);
    the other black keys are sharps, as leading notes mostly are."""
    f = FIFTHS[key]
    flats = set(FLATS[:max(0, -f) + 1]) if f <= 0 else set()
    flats = {n + 'b' for n in flats} | {'Bb'}
    def fix(m):
        n = m.group(1)
        if len(n) == 2:
            want = n if (n in flats) == n.endswith('b') else ENHARMONIC[n]
            n = want
        return n + m.group(2)
    return [re.sub(r'\b([A-G][#b]?)(-?\d/)', fix, b) for b in bars]

def beats(bar):
    total = 0
    for t in bar.split():
        if '/' not in t:
            continue
        d = t.split('/')[1].rstrip('~,')
        total += DUR[d[0]] * (1.5 if d.endswith('.') else 2 / 3 if d.endswith('3') else 1)
    return total

def is_rest(bar):
    return all(t.startswith('R/') for t in bar.split())

def key_for(bars):
    notes = [(m.group(1), {'#': 1, 'b': -1}.get(m.group(2), 0)) for b in bars for m in re.finditer(r'([A-G])(#|b)?-?\d', b)]
    def cost(f):
        alt = {s: (1 if f > 0 else -1) for s in (SHARPS[:f] if f > 0 else FLATS[:-f])}
        return sum(1 for s, a in notes if a != alt.get(s, 0))
    return min(range(-3, 4), key=lambda f: (cost(f), abs(f)))

idx_path = os.path.join(ROOT, 'data', 'songs', 'index.json')
index = json.load(open(idx_path))
for song in SONGS:
    up, low = map(int, song['time'].split('/'))
    full = up * 4 / low
    pickup = song.get('pickup')
    if 'key' in song:
        key = song['key']
    else:
        key = MAJOR[key_for(convert(song['midi'], song['parts'][0][0], song['time'], False, transpose=song.get('transpose', 0)))]
    flats = FIFTHS[key] < 0
    parts = [convert(song['midi'], t, song['time'], flats, pickup=pickup, transpose=song.get('transpose', 0)) for t, _, _ in song['parts']]
    skip = 0 if song.get('keep_intro') else min(next((i for i, b in enumerate(p) if not is_rest(b)), 0) for p in parts)
    length = max(len(p) for p in parts) - skip
    def finish(bars, part):
        bars = respell(bars[skip:skip + length], key)
        for p, i, was, now in song.get('fix', []):
            if p == part:
                assert bars[i] == was, (song['id'], i, bars[i])
                bars[i] = now
        gap = full - beats(bars[-1])
        while gap > 1e-9:
            r = max(k for k in REST if k <= gap + 1e-9)
            bars[-1] += ' ' + REST[r]
            gap -= r
        return ' | '.join(bars)
    rel = os.path.join(song.get('dir', ''), song['id'] + '.json').replace(os.sep, '/')
    path = os.path.join(ROOT, 'data', 'songs', rel)
    old = json.load(open(path, encoding='utf-8')) if os.path.exists(path) else {}
    out = {
        'id': song['id'], 'title': song['title'], 'composer': song['composer'], 'chapter': old.get('chapter', 0), 'tempo': song['tempo'],
        'time': song['time'], 'key': key, 'source': song['source'],
        'about': old.get('about', song['about']), 'tip': old.get('tip', song['tip']),
        **({'pickup': pickup} if pickup else {}),
        'parts': [{'id': f'part{i + 1}', 'name': name, 'instrument': instr, 'music': finish(p, i)} for i, ((t, name, instr), p) in enumerate(zip(song['parts'], parts))],
    }
    tune_last = out['parts'][0]['music'].split(' | ')[-1].split()
    while tune_last[-1].startswith('R/'):
        tune_last.pop()
    song_end = beats(' '.join(tune_last))
    acc = []
    for t, name, sound, voice in song['accompaniment']:
        bars = respell(convert(song['midi'], t, song['time'], flats, voice, pickup, song.get('transpose', 0))[skip:skip + length], key)
        # A band part ends with the song: its last note is held until the tune's last note ends.
        last = bars[-1].split()
        gap = song_end - beats(bars[-1])
        if gap > 1e-9 and not last[-1].startswith('R/'):
            pitch, d = last[-1].split('/')
            if held := REST.get(beats('X/' + d) + gap):
                last[-1] = pitch + held[1:]
                bars[-1] = ' '.join(last)
        if not all(is_rest(b) for b in bars):
            acc.append({'id': f'voice{t}', 'name': name, 'sound': sound, 'music': ' | '.join(bars)})
    if acc:
        out['accompaniment'] = acc
    json.dump(out, open(path, 'w', encoding='utf-8'), ensure_ascii=False, indent=2)
    open(path, 'a').write('\n')
    if rel not in index:
        index.append(rel)
    print(f"{song['id']:28} key {key:3} bars {length:3} parts {len(parts)} band {len(acc)}")
json.dump(index, open(idx_path, 'w'), indent=2)

# --- MusicXML sources (tools/musicxml2pipit.py): van Eyck and Dowland, personal use ------------
import importlib.util
_spec = importlib.util.spec_from_file_location('musicxml2pipit', os.path.join(ROOT, 'tools', 'musicxml2pipit.py'))
mx = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(mx)
MS = os.path.join(ROOT, 'tools', 'sources', 'personal-musescore')
EYCK_SOURCE = 'MuseScore transcription of the first edition (musescore.com/score/1608116), MusicXML converted with tools/import-songs.py; PERSONAL USE ONLY (no licence stated)'
EYCK_ABOUT = {
    'en': "Jacob van Eyck (about 1590-1657) was a blind carillon player in Utrecht. On summer evenings he played the recorder in the churchyard of the Janskerk for people strolling by, and the town paid him extra for it. His book \"Der Fluyten Lust-hof\" (The Flute's Pleasure Garden) holds about 150 pieces: the popular songs, dances and psalms of his day, each followed by his own variations.",
    'nl': "Jacob van Eyck (rond 1590-1657) was een blinde beiaardier in Utrecht. Op zomeravonden speelde hij blokfluit op het kerkhof van de Janskerk voor wandelaars, en de stad betaalde hem daar extra voor. Zijn boek \"Der Fluyten Lust-hof\" (De lusthof van de fluit) bevat zo'n 150 stukken: de populaire liedjes, dansen en psalmen van zijn tijd, elk gevolgd door zijn eigen variaties.",
}
EYCK_NOTES = {
    'wilhelmus': ("The Wilhelmus is now the Dutch national anthem, and one of the oldest in the world: its tune was already well known around 1570.",
                  "Het Wilhelmus is nu het Nederlandse volkslied, en een van de oudste ter wereld: de melodie was rond 1570 al bekend."),
    'lachrymae': ("This is John Dowland's \"Flow, my tears\", the greatest hit of its time, which van Eyck decorated in his own way.",
                  "Dit is \"Flow, my tears\" van John Dowland, de grootste hit van zijn tijd, die van Eyck op zijn eigen manier versierde."),
    'comagain': ("The tune is John Dowland's song \"Come again, sweet love doth now invite\" (1597).",
                 "De melodie is het lied \"Come again, sweet love doth now invite\" van John Dowland (1597)."),
    'amarilli': ("The tune is Giulio Caccini's famous song \"Amarilli, mia bella\" (1602). In this version van Eyck wrote two recorder parts.",
                 "De melodie is het beroemde lied \"Amarilli, mia bella\" van Giulio Caccini (1602). In deze versie schreef van Eyck twee blokfluitpartijen."),
    'bethlehem': ("A Christmas song about the stable in Bethlehem.", "Een kerstlied over de stal in Bethlehem."),
    'nachtegaeltje': ("\"The English Nightingale\": in the variations the recorder turns into a bird, with quick trills and runs like birdsong.",
                      "\"De Engelse nachtegaal\": in de variaties wordt de blokfluit een vogel, met snelle trillers en loopjes als vogelzang."),
    'echo': ("Each phrase is answered by its echo: play the first time out loud, the repeat softly, as if from far away.",
             "Elke frase krijgt een echo als antwoord: speel ze de eerste keer luid en de herhaling zacht, alsof van ver weg."),
    'engels-duet': ("An English song for two recorders: van Eyck wrote both parts.", "Een Engels lied voor twee blokfluiten: van Eyck schreef beide partijen."),
}
THEME_TIP = {'en': "This is the tune itself. In the book van Eyck follows it with variations; try the full version when this one goes well.",
             'nl': "Dit is de melodie zelf. In het boek laat van Eyck er variaties op volgen; probeer de volledige versie als deze goed lukt."}
FULL_TIP = {'en': "First the tune, then van Eyck's variations, each one busier than the last. Practise them one at a time with the speed dial.",
            'nl': "Eerst de melodie, dan de variaties van van Eyck, elk drukker dan de vorige. Oefen ze een voor een met de snelheidsknop."}
DUET_TIP = {'en': "A duet: pick a part, and the band plays the other recorder.", 'nl': "Een duet: kies een partij, en de band speelt de andere blokfluit."}
SHORT_TIP = {'en': "A short piece in van Eyck's style: keep the quick notes light and even.",
             'nl': "Een kort stuk in de stijl van van Eyck: houd de snelle noten licht en gelijk."}

def eyck(id, pieces_, en, nl, kind, tempo, note=None, key=None):
    about = {lang: EYCK_ABOUT[lang] + (' ' + EYCK_NOTES[note][k] if note else '') for k, lang in enumerate(['en', 'nl'])}
    suffix = {'theme': (' (tune)', ' (melodie)'), 'full': ('', ''), 'duet': ('', ''), 'short': ('', '')}[kind]
    tip = {'theme': THEME_TIP, 'full': FULL_TIP, 'duet': DUET_TIP, 'short': SHORT_TIP}[kind]
    return dict(id=id, dir='personal', xml=f'{MS}/der-fluyten-lust-hof-vol-1.mxl', pieces=pieces_, theme=kind == 'theme', tempo=tempo, key=key,
                title={'en': en + suffix[0], 'nl': nl + suffix[1]}, composer='Jacob van Eyck', source=EYCK_SOURCE, about=about, tip=tip)

XML_SONGS = [
    eyck('van-eyck-preludium', [0], 'Prelude', 'Preludium of Voorspel', 'short', 72),
    eyck('van-eyck-wilhelmus-tune', [41], 'Wilhelmus', 'Wilhelmus van Nassouwe', 'theme', 76, 'wilhelmus'),
    eyck('van-eyck-wilhelmus', [41], 'Wilhelmus', 'Wilhelmus van Nassouwe', 'full', 72, 'wilhelmus'),
    eyck('van-eyck-wat-zalmen-tune', [49], 'What shall we do this evening', 'Wat zalmen op den avond doen', 'theme', 76),
    eyck('van-eyck-wat-zalmen', [49], 'What shall we do this evening', 'Wat zalmen op den avond doen', 'full', 72),
    eyck('van-eyck-bethlehem-tune', [55], 'O holy, blessed Bethlehem', 'O heyligh zaligh Bethlehem', 'theme', 72, 'bethlehem'),
    eyck('van-eyck-bethlehem', [55], 'O holy, blessed Bethlehem', 'O heyligh zaligh Bethlehem', 'full', 66, 'bethlehem'),
    eyck('van-eyck-doen-daphne-tune', [2], 'When Daphne, the fairest maiden', "Doen Daphne d'over schoone maeght", 'theme', 96),
    eyck('van-eyck-lachrymae-tune', [7], 'Lachrimae Pavan', 'Pavaen Lachrymae', 'theme', 66, 'lachrymae'),
    eyck('van-eyck-comagain-tune', [31], 'Come again', 'Comagain', 'theme', 72, 'comagain'),
    eyck('van-eyck-onan-tune', [16], 'Onan, or Tanneken', 'Onan of Tanneken', 'theme', 72),
    eyck('van-eyck-engels-lied', [36], 'English song', 'Engels lied', 'full', 72),
    eyck('van-eyck-malle-symen', [4], 'Silly Simon', 'Malle Symen', 'full', 72),
    eyck('van-eyck-rosemont', [9], 'Rosemont', 'Rosemont', 'full', 72),
    eyck('van-eyck-stil-stil', [13], 'Hush, hush a while', 'Stil, stil een reys', 'full', 72),
    eyck('van-eyck-meysje', [43], 'Meysje wilje by', 'Meysje wilje by', 'full', 72),
    eyck('van-eyck-courante-mars', [44], 'Courante Mars', 'Courante Mars', 'full', 80),
    eyck('van-eyck-janneman', [54], 'Janneman and Alemoer', 'Janneman en Alemoer', 'full', 72),
    eyck('van-eyck-prins-robberts-masco', [76], "Prince Rupert's Masque", 'Prins Robberts masco', 'full', 72),
    eyck('van-eyck-nachtegaeltje-tune', [26], 'The English Nightingale', 'Engels Nachtegaeltje', 'theme', 72, 'nachtegaeltje'),
    eyck('van-eyck-nachtegaeltje', [26], 'The English Nightingale', 'Engels Nachtegaeltje', 'full', 60, 'nachtegaeltje'),
    eyck('van-eyck-fantasia-echo', [14], 'Fantasia and Echo', 'Fantasia & Echo', 'full', 66, 'echo'),
    eyck('van-eyck-frans-ballet', [12], 'French Ballet', 'Frans Ballet', 'full', 72),
    eyck('van-eyck-van-goosen-tune', [21], 'About Goosen', 'Van Goosen', 'theme', 76),
    eyck('van-eyck-lus-de-mi-alma', [35], 'Light of my soul', 'Lus de mi alma', 'full', 66),
    eyck('van-eyck-tweede-daphne', [33], 'Second Daphne', 'Tweede Daphne', 'full', 88),
    eyck('van-eyck-courant', [32], 'Courante', 'Courant', 'full', 72),
    eyck('van-eyck-engels-liedt-duet', [80, 81], 'English song, for two', 'Engels liedt, à 2', 'duet', 72, 'engels-duet'),
    eyck('van-eyck-amarilli-duet', [84, 85], 'Amarilli mia bella, for two', 'Amarilli mia bella, à 2', 'duet', 66, 'amarilli'),
    dict(id='dowland-flow-my-tears', dir='personal', xml=f'{MS}/dowland-flow-my-tears-lachrimae-pavan-satb.mxl', pieces=[0], tempo=60, key='Am',
         staves=[(1, -12, 'Cantus', 'soprano')], band=[(2, 0, 'Altus', 'recorder'), (3, 0, 'Tenor', 'recorder'), (4, 0, 'Bassus', 'bassoon')],
         # The scan has one rest too many in bar 20 of the top voice.
         drop_last_rest=[(1, 19)],
         title={'en': 'Flow, my tears (Lachrimae)', 'nl': 'Flow, my tears (Lachrimae)'}, composer='John Dowland',
         source="MusicXML from an optical reading (Audiveris) of an SATB setting, from the user's own files; PERSONAL USE ONLY (no licence stated)",
         about={'en': "John Dowland (1563-1626), lute player at the courts of England and Denmark, published this song in 1600. Its falling first notes, like tears, made it the best-known tune in Europe: composers everywhere, Jacob van Eyck among them, wrote their own versions of \"Lachrimae\".",
                'nl': "John Dowland (1563-1626), luitspeler aan de hoven van Engeland en Denemarken, gaf dit lied uit in 1600. De dalende eerste noten, als tranen, maakten het de bekendste melodie van Europa: overal schreven componisten er hun eigen versie van \"Lachrimae\" van, ook Jacob van Eyck."},
         tip={'en': "Slow and sad: let each note of the falling La, Sol, Fa, Mi sing. With the band on, the other three voices of the song play along.",
              'nl': "Traag en droevig: laat elke noot van het dalende La, Sol, Fa, Mi zingen. Met de band aan spelen de drie andere stemmen van het lied mee."}),
]

for song in XML_SONGS:
    pieces_ = mx.load_mxl(song['xml'])
    def bars_of(index, staff=None, transpose=0):
        piece = pieces_[index]
        if song.get('theme'):
            piece = mx.theme(piece)
        for st, mi in song.get('drop_last_rest', []):
            if staff == st:
                m = piece['measures'][mi][0]
                rest = [n for n in m.findall('note') if n.findtext('staff') == str(st) and n.find('rest') is not None][-1]
                m.remove(rest)
        return mx.rebar(piece, staff, transpose)
    if 'staves' in song:
        playable = [(bars_of(song['pieces'][0], st, tr), name, instr) for st, tr, name, instr in song['staves']]
        band = [(bars_of(song['pieces'][0], st, tr), name, sound) for st, tr, name, sound in song['band']]
    else:
        names = ['Recorder 1', 'Recorder 2'] if len(song['pieces']) > 1 else ['Recorder']
        playable = [(bars_of(i), names[k], 'soprano') for k, i in enumerate(song['pieces'])]
        band = []
    time, pickup, _ = playable[0][0]
    music = [b for (_, _, b), _, _ in playable]
    length = max(len(b) for b in music)
    full = Fraction(int(time.split('/')[0]) * 4, int(time.split('/')[1]))
    def pad(bars):
        # A shorter part waits in whole-bar rests (the parts of a duet end together).
        return bars + [' '.join('R/' + mx.name_of(x) for x in mx.split(full))] * (length - len(bars))
    # The edition's own key signature (major name; the score only needs the signature).
    key = song['key'] or MAJOR[pieces_[song['pieces'][0]]['measures'][0][3]]
    rel = f"{song['dir']}/{song['id']}.json"
    path = os.path.join(ROOT, 'data', 'songs', rel)
    old = json.load(open(path, encoding='utf-8')) if os.path.exists(path) else {}
    out = {
        'id': song['id'], 'title': song['title'], 'composer': song['composer'], 'chapter': old.get('chapter', 0), 'tempo': song['tempo'],
        'time': time, 'key': key, 'source': song['source'],
        'about': old.get('about', song['about']), 'tip': old.get('tip', song['tip']),
        **({'pickup': mx.name_of(pickup)} if pickup else {}),
        'parts': [{'id': f'part{k + 1}', 'name': name, 'instrument': instr, 'music': ' | '.join(pad(b))} for k, ((_, _, b), name, instr) in enumerate(playable)],
    }
    if band:
        out['accompaniment'] = [{'id': f'voice{k + 2}', 'name': name, 'sound': sound, 'music': ' | '.join(pad(b))} for k, ((_, _, b), name, sound) in enumerate(band)]
    json.dump(out, open(path, 'w', encoding='utf-8'), ensure_ascii=False, indent=2)
    open(path, 'a').write('\n')
    if rel not in index:
        index.append(rel)
    print(f"{song['id']:34} {time} key {key:3} pickup {str(pickup):4} bars {length:4} parts {len(playable)} band {len(band)}")
json.dump(index, open(idx_path, 'w'), indent=2)
