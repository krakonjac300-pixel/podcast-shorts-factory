---
description: Turn one campaign's rules into a brief and a factory config overlay
argument-hint: "<campaign id from index.json, or campaign URL>"
---

Load the `clip-campaigns` skill.

Campaign: `$ARGUMENTS`. Resolve it from `campaigns/index.json` (by id or name) or
treat it as a URL. If it is ambiguous, list the matches and ask.

1. Open the campaign detail page in `clip-browser` and read it fully (expand "read
   more"/accordions; open linked rule docs if they're on the same site).
2. Create `campaigns/<slug>/` where `<slug>` is the id with `:` replaced by `-`.
3. Write `campaigns/<slug>/brief.md` with sections: **Summary** (brand, rate, caps,
   budget left, deadline, allowed platforms), **Source content** (links), **Rules
   (verbatim)**, **What wins** (your read of what clips they want), **Factory
   settings** (what you put in the overlay and why), **Manual checklist** (rules the
   factory can't enforce, to do at posting time).
4. Write `campaigns/<slug>/overlay.yaml` following the skill's mapping table. Start
   `finder.selection_brief` from the user's current value in `config.yaml` and add the
   campaign's wants and don'ts on top. Only include keys you are changing.
5. Validate: `python -c "import yaml,sys; yaml.safe_load(open(sys.argv[1]))" campaigns/<slug>/overlay.yaml`.
6. Update the campaign's record in `index.json` (`source_links`, `requirements`).
7. Report the brief summary, anything unclear that the user should decide, and
   whether the campaign must be joined first (do **not** join it yourself; if the
   user asks you to, show what you'll click and wait for their OK). Next step:
   `/clip-campaigns:produce <slug>`.
