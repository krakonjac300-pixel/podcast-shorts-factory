#!/bin/bash
# Wait for the in-flight produce to finish, restore the normal posting slots,
# then produce a SECOND batch from a DIFFERENT episode for tomorrow.
# One episode per day: six clips from one audit would all be the same person's
# story, and clips 4-6 are the weakest moments of it.
cd "D:/Downloads/Podaci/podcast-shorts-factory"

echo "[chain] waiting for the current produce to release its lock..."
for i in $(seq 1 120); do            # up to 2h
  [ -f workdir/.produce.lock ] || break
  sleep 60
done

if [ -f workdir/.produce.lock ]; then
  echo "[chain] lock still held after 2h — NOT starting a second run"
  exit 1
fi

echo "[chain] today's run finished. Restoring normal slots for tomorrow."
python -c "import json,pathlib; pathlib.Path('post_times.json').write_text(json.dumps(['09:00','14:00','21:30']))"
cat post_times.json

echo "[chain] producing tomorrow's batch from the next fresh episode..."
.venv/Scripts/python.exe run.py produce --force
echo "[chain] done"
