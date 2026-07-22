"""Generate a royalty-free sound-effects pack with ffmpeg (synthesized = no license).

Creates the standard short-form SFX cues the editor's planner references:
whoosh, riser, impact, ding, pop, swoosh. Re-run anytime: python tools/generate_sfx.py
"""
from __future__ import annotations

import subprocess
from pathlib import Path

OUT = Path(__file__).resolve().parent.parent / "assets" / "sfx"
OUT.mkdir(parents=True, exist_ok=True)

SR = 44100

# name -> (lavfi input, optional -af filter chain)
SFX = {
    # punchy noise sweep for hard cuts / scene changes
    "whoosh": (f"anoisesrc=d=0.6:c=pink:a=0.6:r={SR}",
               "highpass=f=400,lowpass=f=5000,afade=t=in:d=0.3,"
               "afade=t=out:st=0.3:d=0.3,volume=2.5"),
    # rising tone building into the payoff line
    "riser": (f"aevalsrc='0.3*sin(2*PI*(150+700*t)*t)':d=1.2:s={SR}",
              "afade=t=out:st=1.0:d=0.2"),
    # low boom for the mic-drop / big reveal
    "impact": (f"aevalsrc='0.9*sin(2*PI*70*t)*exp(-4*t)':d=0.8:s={SR}",
               "volume=1.5"),
    # bright ding when a key word/number appears on screen
    "ding": (f"aevalsrc='0.5*sin(2*PI*1320*t)*exp(-5*t)+"
             f"0.2*sin(2*PI*2640*t)*exp(-7*t)':d=0.7:s={SR}", None),
    # short pop for emphasis
    "pop": (f"aevalsrc='0.7*sin(2*PI*600*t)*exp(-35*t)':d=0.15:s={SR}", None),
    # quick transition swoosh
    "swoosh": (f"anoisesrc=d=0.4:c=white:a=0.5:r={SR}",
               "bandpass=f=2000:width_type=h:w=1500,afade=t=in:d=0.2,"
               "afade=t=out:st=0.2:d=0.2,volume=3"),
}


def main():
    for name, (src, af) in SFX.items():
        out = OUT / f"{name}.wav"
        cmd = ["ffmpeg", "-y", "-f", "lavfi", "-i", src]
        if af:
            cmd += ["-af", af]
        cmd += [str(out)]
        subprocess.run(cmd, check=True, capture_output=True)
        print(f"  + {out.name}")
    print(f"\n{len(SFX)} sound effects written to {OUT}")


if __name__ == "__main__":
    main()
