# Editor Craft — how professional human editors think

Distilled 2026-07-04 from three full editing courses: Marcus Jones' 4-hour YouTube
editing masterclass (DaVinci Resolve, incl. a real video edited live + pro editors'
"biggest small-creator mistakes"), Content Creators' CapCut pro tutorial, and Tausif
Khalid's Filmora 15 course. The tools differ; the METHODS below are tool-agnostic and
map directly onto our pipeline (trimmer, planner, punch zooms, flashes, b-roll, SFX,
music, captions).

## The three laws every pro repeated
1. **Editing is a supplement, not the star.** Over-editing is the #1 small-creator
   mistake — every effect must either entertain more or explain better. If it does
   neither, leave it out. One restrained, consistent style reads "professional";
   a different transition on every cut reads "child".
2. **Direct the viewer's eye.** Humans focus on ONE thing at a time. At any moment
   there must be exactly one focal point (face, caption, b-roll, graphic). Never two
   text elements competing; never b-roll over footage that is itself the story.
3. **Front-load the effort.** Most viewers are watching the first seconds; edit
   density should be highest at the start (hook, fastest pacing, best moment) and can
   relax after. But front-loading ≠ dumping effects — it means the best CONTENT first.

## Cuts & pacing (trimmer / camera cuts)
- Cut every silence, breath, false start, and forgotten-line pause. Keep a pause ONLY
  when it's deliberate dramatic effect before a punchline.
- Disguise jump cuts: alternate punch-in/punch-out between takes and keep the punch
  amount CONSISTENT; align the subject's eyes across the cut so the jump is invisible.
- Hide cuts under a visual change: start b-roll/overlay slightly BEFORE the cut, not
  exactly on it — mouth-closed→mid-sentence is obvious when the swap lands on the cut.
- J/L cuts: let audio lead or trail the picture across a cut; a short audio crossfade
  (10–500ms) turns a harsh audio joint seamless. Cut the AUDIO gap without cutting
  the video when the frame would visibly teleport.
- Cutting on the end of a word/sentence sounds natural; mid-word never does.
- Something should change on screen every 2–6s, but each on-screen element must live
  long enough to read — 2.5s of unreadable text is worse than no text.

## Sound design (sfx / music)
- Two kinds of SFX: (a) exaggerations/foley — whoosh for anything flying in/out,
  pop for graphics appearing, impact for hits, themed sounds for named things
  (cash register when money is mentioned); (b) ambience that sets the scene
  (crowd, clapping, room tone — real audience reactions make a talk feel live).
- **Never reuse the identical SFX file back-to-back** — in nature no two whooshes
  sound the same. Rotate 2–3 variants of each sound (our _resolve_sfx does this).
- SFX sit UNDER the voice: timed to the exact frame of the visual event, volume low.
- Music = the emotion you want the viewer to feel at that moment, exaggerated.
  Match music energy to voice energy: upbeat music under monotone talk (or slow music
  under an exciting moment) feels broken. NEVER use music to patch a slow section —
  if it drags, cut it; music only enhances pacing that's already good.
- Start music at the beat drop, duck ~-16dB under voice, and end it WITH the video.
- Keep voice volume consistent: ride the level up where the speaker trails off
  (keyframe the quiet words louder), normalize across clips, never clip into the red.

## Directing the eye (b-roll / graphics / captions)
- **Audio-visual sync rule:** show the thing at the exact word that names it
  ("Two Moose" appears when he SAYS "Two Moose"). Reveal parts of a graphic in sync
  with speech — show progression, don't swap static slides.
- B-roll exists to add information or motion during talking-head stretches. Reframe
  and color-match it; irrelevant b-roll is the #1 "what am I looking at" killer.
- Text must be readable in <1s: heavy stroke, drop shadow, or a background box —
  and high contrast with what's behind it. To force focus on text, darken (lower
  opacity toward black) or blur the footage behind it.
- Keep text/graphics out of platform UI zones; text behind the subject's head
  (cutout layering) looks premium but only when it stays legible.
- Small detail on screen? Zoom into it or point at it (arrow/circle/highlight) —
  if viewers can miss it, they will.

## Hooks & intros (planner / finder)
- **Don't spoil the payoff.** If the intro shows the end result, there is no reason
  to keep watching. Tease the premise; deliver the result late.
- The intro must match the packaging: deliver exactly what the title/hook promises,
  in the first seconds, or viewers feel baited and leave.
- Start ON the first spoken word — no inhale, no glance away, no dead frames. An
  unintentional breath or look-off in second one sets a low-energy tone.
- "So that" rule: state benefits, not features — "5 chords SO THAT you can play 80%
  of songs" beats "5 chords". Name the specific famous examples when possible.
- No unexplained jargon in the first line; a confused viewer is a gone viewer.
- Trim lead-in fluff from clipped moments ("that sounds good…", "well, you know")
  so the first caption word IS the message; end at a natural stopping sentence,
  never mid-waffle — extend or shorten to the nearest clean landing.
- A short on-screen context banner at clip start ("why you should never lie") rescues
  moments that need setup the audio doesn't give.

## Techniques worth knowing (visual toolbox)
- Keyframe animation is start-value → end-value; the machine interpolates. To hold a
  zoom you need a HOLD keyframe, or it drifts back — attack, hold, release.
- Anchor point controls where a scale/rotation grows from — reveal from a corner,
  not always the center.
- Blending modes: SCREEN removes black backgrounds of overlay effects (light leaks,
  film burn, particles); MULTIPLY removes white. Prefer black-background overlays
  over green-screen assets — cleaner edges, zero keying work.
- Speed ramps (slow→fast→slow) add drama to motion b-roll; keep voice pitch when
  retiming; frame-interpolate (optical flow) when slowing low-fps footage, but not
  on fine detail (water, sparks) where it smears.
- Shake effect on text/frame during shouting or impact = comedic exaggeration.
- Camera-shake, punch-zooms, and flash-cuts are strongest at TOPIC CHANGES and
  emphasis words — that's when a "new shot" resets attention.

## Final pass (manager / editor QA)
- Watch the whole edit back like a stranger: most fixes are found on rewatch, not
  while editing ("a lot of editing is realizing you're worse than you think").
- Checklist: no visible cut artifacts, no text spilling or unreadable, one focal
  point per moment, audio level consistent, SFX varied, music matches emotion,
  hook delivers the packaging's promise, payoff not spoiled early.
