import React from "react";
import {
  AbsoluteFill,
  useCurrentFrame,
  useVideoConfig,
  spring,
} from "remotion";

export type Word = { word: string; start: number; end: number };
export type CaptionsProps = {
  words: Word[];
  emphasis: string[];
  wordsPerPage: number;
  fontSize: number;
};

const norm = (s: string) => s.replace(/[^\w]/g, "").toLowerCase();
const clean = (s: string) => s.replace(/[.,!?;:]+$/g, "");

// Group words into pages: up to `perPage`, but break early at sentence-ending
// punctuation so a page never straddles two sentences (matches the ASS logic).
function buildPages(words: Word[], perPage: number): Word[][] {
  const pages: Word[][] = [];
  let cur: Word[] = [];
  for (const w of words) {
    cur.push(w);
    const endsSentence = /[.?!]$/.test(w.word.trim());
    if (cur.length >= perPage || endsSentence) {
      pages.push(cur);
      cur = [];
    }
  }
  if (cur.length) pages.push(cur);
  return pages;
}

export const Captions: React.FC<CaptionsProps> = ({
  words,
  emphasis,
  wordsPerPage,
  fontSize,
}) => {
  const frame = useCurrentFrame();
  const { fps, width } = useVideoConfig();
  const t = frame / fps;
  const emphSet = new Set(emphasis.map(norm));
  const pages = buildPages(words, wordsPerPage);

  // Only the page whose time window contains `t` is on screen.
  const page = pages.find(
    (p) => t >= p[0].start - 0.04 && t <= p[p.length - 1].end + 0.12,
  );
  if (!page) return null;

  return (
    <AbsoluteFill style={{ justifyContent: "center", alignItems: "center" }}>
      <div
        style={{
          display: "flex",
          flexWrap: "wrap",
          justifyContent: "center",
          alignItems: "center",
          gap: "6px 20px",
          maxWidth: width * 0.84,
          padding: "0 40px",
        }}
      >
        {page.map((w, i) => {
          const spoken = t >= w.start;
          const isActive = t >= w.start && t < w.end;
          const emph = emphSet.has(norm(w.word));

          // spring pop-in the instant the word is spoken (70% -> 100%)
          const pop = spring({
            frame: frame - w.start * fps,
            fps,
            config: { damping: 13, stiffness: 220, mass: 0.5 },
            durationInFrames: 9,
          });
          const scale = spoken ? 0.7 + 0.3 * pop : 0.82;

          const color = isActive
            ? emph
              ? "#FF2A2A" // active + key word -> RED
              : "#FFE100" // active -> YELLOW
            : emph
              ? "#FF2A2A" // key word (not active) -> RED
              : "#FFFFFF"; // default -> WHITE

          return (
            <span
              key={i}
              style={{
                fontFamily: "'Arial Black', Arial, sans-serif",
                fontWeight: 900,
                fontSize: emph ? fontSize * 1.1 : fontSize,
                color,
                scale: String(scale),
                opacity: spoken ? 1 : 0.45,
                textTransform: "uppercase",
                WebkitTextStroke: "9px black",
                paintOrder: "stroke fill",
                lineHeight: 1.08,
                letterSpacing: "-0.5px",
              }}
            >
              {clean(w.word)}
            </span>
          );
        })}
      </div>
    </AbsoluteFill>
  );
};
