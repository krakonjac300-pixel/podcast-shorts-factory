# Connecting YouTube (one-time, ~5 minutes)

The Uploader posts Shorts via the official **YouTube Data API v3**. You create a free OAuth
client in Google Cloud, download the secrets file, and run one command. I can't do this part for
you — it requires signing into *your* Google account — but here's the exact path.

## 1. Create a Google Cloud project
1. Go to https://console.cloud.google.com/ and sign in with the Google account that owns (or
   manages) the YouTube channel.
2. Top bar → project dropdown → **New Project** → name it e.g. `shorts-factory` → Create.

## 2. Enable the YouTube Data API
1. Menu → **APIs & Services → Library**.
2. Search **YouTube Data API v3** → **Enable**.

## 3. Configure the OAuth consent screen
1. **APIs & Services → OAuth consent screen**.
2. User type: **External** → Create.
3. Fill App name + your email (support + developer). Save and continue.
4. **Scopes:** you can skip adding scopes here (the app requests them at runtime). Continue.
5. **Test users:** click **Add Users** and add your own Google email. Save.
6. **Publishing status → Publish app → "In production".** IMPORTANT for the daily
   automation: while the app is in "Testing", Google expires the refresh token every
   7 days and unattended uploads break. In production the token persists (you'll click
   past a one-time "unverified app" notice). No Google verification is needed for your
   own channel.

## 4. Create the OAuth client
1. **APIs & Services → Credentials → Create Credentials → OAuth client ID**.
2. Application type: **Desktop app** → name it → Create.
3. Click **Download JSON**. Save it into the project folder as `client_secret.json`
   (or anywhere, and point `.env` at it).

## 5. Tell the app where it is
In `.env`:
```
YOUTUBE_CLIENT_SECRETS=client_secret.json
```

## 6. Authorize
```powershell
.\.venv\Scripts\Activate.ps1
python run.py auth-youtube
```
A browser opens → pick your account → "Google hasn't verified this app" → **Continue** (it's
your own app) → allow. The token is cached to `youtube_token.json` and you won't need to repeat
this. The command prints your channel name on success.

## Notes
- **Quota:** the API gives ~10,000 units/day; each upload costs ~1,600 units → ~6 uploads/day.
  Plenty for clips; request more in the console if you scale up.
- **Privacy:** uploads default to `private` (see `config.yaml` → `uploader.privacy`). Switch to
  `public` once you trust the output.
- **Token scope:** the cached token covers upload **and** read access, so the Manager can pull
  view/like stats with the same login.
