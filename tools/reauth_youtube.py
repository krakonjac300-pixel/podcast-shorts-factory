"""One-time re-consent to add the yt-analytics scope (retention data for the
Manager). Prints the URL to open; writes youtube_token.json only on success,
so the existing token keeps working if this is never completed.

Run:  .venv\\Scripts\\python.exe -u tools\\reauth_youtube.py
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from google_auth_oauthlib.flow import InstalledAppFlow  # noqa: E402

from factory.agents.uploader import YOUTUBE_SCOPES  # noqa: E402
from factory.config import ROOT  # noqa: E402

flow = InstalledAppFlow.from_client_secrets_file(
    str(ROOT / "client_secret.json"), YOUTUBE_SCOPES)
creds = flow.run_local_server(
    port=0, open_browser=False,
    authorization_prompt_message="AUTH_URL: {url}\n")
(ROOT / "youtube_token.json").write_text(creds.to_json())
print("SUCCESS - token updated with analytics scope")
