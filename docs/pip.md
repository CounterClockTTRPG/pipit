# Pip, the guide

Pip is a meadow pipit, drawn in code (`src/App/Views/PipView.cs`) as a cut-out rig of vector
parts in a 200 x 200 design space. No image files: Pip stays sharp at any size and new moods
are a few numbers.

**Character.** A small, brown, streaky songbird of open fields, leaning forward, facing right
(towards the speech bubble). From the real bird: buff breast with dark streaks, pale eyebrow
(supercilium), slim pointed beak, pinkish legs with a long hind claw, a narrow tail with white
outer feathers, and the tail bob. The one invented touch: the breast streaks are small note
shapes. Everything else stays flat and plain: no gradients, glows or sparkles.

**Palette** (taken from the app so Pip belongs to it):

| Part | Colour |
|---|---|
| back, head, tail | olive-brown `#8a6a3f` |
| wing, feather edges | `#a8865a` |
| outlines, streaks, eye | app ink `#4a3423` |
| breast, throat | buff `#f3dcae` |
| eyebrow, wing bars, tail edge | paper `#fcf4e5` |
| beak | app accent `#d9822b` |
| legs | `#e4a07a`; cheek blush `#e8a27a` at 55 % |

**Moods.** The eyebrow does most of the acting.

| Mood | Pose | Used for |
|---|---|---|
| Idle | breathing, blinks, an occasional tail bob | resting state |
| Listening | head tilted up, brow raised and twitching, eye wide | waiting for the player |
| Singing | beak opening in rhythm, notes rising | greeting, after praise, right notes |
| Cheering | hops, wing up, happy squint, notes rising | stars, finished tasks, streaks |
| Thinking | head tilted down, eye up, brow knitted | a wrong note, a run without stars |
| Dozing | head lowered, eyes shut, slow breathing, z's | a minute without input on Home |

`SetMood(mood, seconds, after)` switches for a while and then returns. Motion only follows the
mood; nothing wobbles constantly.
