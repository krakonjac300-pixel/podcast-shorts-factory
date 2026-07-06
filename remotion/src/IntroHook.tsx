import React from "react";
import {
  AbsoluteFill,
  useCurrentFrame,
  useVideoConfig,
  spring,
  interpolate,
} from "remotion";

export type IntroHookProps = {
  text: string;
  fontSize: number;
  accentWords: string[];
};

const norm = (s: string) => s.replace(/[^\w]/g, "").toLowerCase();

// A 2.5s kinetic-typography hook card: each word springs up + scales in,
// staggered, then the whole card eases out so the clip's normal captions
// take over. Transparent background — overlaid on the opening of the clip.
export const IntroHook: React.FC<IntroHookProps> = ({
  text,
  fontSize,
  accentWords,
}) => {
  const frame = useCurrentFrame();
  const { fps, durationInFrames, width } = useVideoConfig();
  const words = text.trim().split(/\s+/).filter(Boolean);
  const accent = new Set(accentWords.map(norm));

  // whole-card fade-out over the last 8 frames
  const out = interpolate(
    frame,
    [durationInFrames - 8, durationInFrames],
    [1, 0],
    { extrapolateLeft: "clamp", extrapolateRight: "clamp" },
  );

  return (
    <AbsoluteFill
      style={{ justifyContent: "flex-start", alignItems: "center", paddingTop: "18%" }}
    >
      <div
        style={{
          display: "flex",
          flexWrap: "wrap",
          justifyContent: "center",
          gap: "8px 22px",
          maxWidth: width * 0.86,
          padding: "0 50px",
          opacity: out,
        }}
      >
        {words.map((w, i) => {
          const delay = i * 3.5; // stagger the word entrances
          const enter = spring({
            frame: frame - delay,
            fps,
            config: { damping: 14, stiffness: 180, mass: 0.6 },
            durationInFrames: 14,
          });
          const y = interpolate(enter, [0, 1], [55, 0]);
          const scale = interpolate(enter, [0, 1], [0.55, 1]);
          const isAccent = accent.has(norm(w));
          return (
            <span
              key={i}
              style={{
                fontFamily: "'Arial Black', Arial, sans-serif",
                fontWeight: 900,
                fontSize,
                color: isAccent ? "#FFE100" : "#FFFFFF",
                translate: `0px ${y}px`,
                scale: String(scale),
                opacity: enter,
                textTransform: "uppercase",
                WebkitTextStroke: "10px black",
                paintOrder: "stroke fill",
                lineHeight: 1.05,
                letterSpacing: "-1px",
              }}
            >
              {w}
            </span>
          );
        })}
      </div>
    </AbsoluteFill>
  );
};
