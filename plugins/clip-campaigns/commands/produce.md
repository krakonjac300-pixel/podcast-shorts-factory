---
description: Run the Podcast Shorts Factory on a campaign's source content using its brief, then check the renders against the rules
argument-hint: "<campaign slug> [source url]"
---

Load the `clip-campaigns` skill.

Arguments: `$ARGUMENTS` — a slug under `campaigns/` and optionally one source URL.

1. Read `campaigns/<slug>/brief.md` and `overlay.yaml`. If they don't exist, tell the
   user to run `/clip-campaigns:brief` first.
2. Pick the source: the URL given, else the first source link in the brief that
   `yt-dlp` can download. If sources are in Drive/Dropbox folders, ask the user to
   download the file into `workdir/` and use the local path.
3. From the project root, with `PSF_CONFIG_OVERLAY` set to the overlay path, run
   `python run.py find "<source>"`, then show the candidates and let the user approve
   them (`python run.py review` is interactive; alternatively summarise candidates
   and approve the ones they pick). Then run `python run.py edit` and
   `python run.py finish` with the same env var. These can take a long time; run them
   in the background and report progress.
4. Hand the rendered files to the `campaign-compliance` agent together with the brief.
   Fix what the factory can fix (re-run with an adjusted overlay) and list what the
   user must fix by hand.
5. Do **not** upload unless the user asks. If they do, run
   `python run.py upload` with the overlay set, and remind them that campaign posts
   usually must be public and follow the brief's **Manual checklist**.
6. Record rendered clips (path, clip id, compliance result) in
   `campaigns/<slug>/clips.json`.
