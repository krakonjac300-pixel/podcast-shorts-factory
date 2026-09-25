---
description: Open Vyro and Clipping.net in the plugin's browser so you can log in once (session is remembered)
argument-hint: "[vyro|clipping|all]"
---

Load the `clip-campaigns` skill first.

Target: `$ARGUMENTS` (empty means `all`).

1. With the `clip-browser` tools, open https://app.vyro.com/campaigns (for `vyro`/`all`)
   and https://clipping.net/dashboard/campaigns (for `clipping`/`all`), each in its own tab.
2. Snapshot each tab. If it shows a login, signup, captcha or 2FA screen, tell the user
   to finish logging in **in that browser window** (you must not type credentials), and
   wait for them to say they are done.
3. Re-snapshot and confirm you can see the campaign list on each site. Report which
   sites are logged in. The browser profile is persistent, so this is normally a
   one-time step.
