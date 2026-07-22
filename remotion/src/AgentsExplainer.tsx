import React from "react";
import {
  AbsoluteFill,
  Sequence,
  useCurrentFrame,
  useVideoConfig,
  spring,
  interpolate,
} from "remotion";

// 20s vertical explainer: how the 8 agents turn a podcast into a short.
// Marketing asset — rendered to D:\Downloads\Podaci, not part of the product.

const YELLOW = "#FFD154";
const RED = "#FF4757";
const BLUE = "#5E72EB";
const WHITE = "#F5F6FA";
const DIM = "#969BAF";
const CARD = "rgba(30,33,60,0.92)";
const FONT = "'Segoe UI', Arial, sans-serif";

const Bg: React.FC = () => (
  <AbsoluteFill
    style={{ background: "linear-gradient(180deg,#0d0f1e 0%,#181230 100%)" }}
  />
);

// spring-in helper: returns {opacity, transform} for a staggered entrance
const useEnter = (delay: number, from = 40) => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const s = spring({
    frame: frame - delay,
    fps,
    config: { damping: 16, stiffness: 160, mass: 0.7 },
    durationInFrames: 18,
  });
  return {
    opacity: s,
    transform: `translateY(${(1 - s) * from}px) scale(${0.92 + s * 0.08})`,
  };
};

// whole-scene fade out over its final 8 frames
const useSceneOut = (sceneDur: number) => {
  const frame = useCurrentFrame();
  return interpolate(frame, [sceneDur - 8, sceneDur], [1, 0], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
  });
};

const StepBadge: React.FC<{ n: string; name: string; color?: string }> = ({
  n,
  name,
  color = YELLOW,
}) => {
  const st = useEnter(0, 24);
  return (
    <div
      style={{
        ...st,
        display: "flex",
        alignItems: "center",
        gap: 22,
        marginBottom: 46,
      }}
    >
      <div
        style={{
          width: 86,
          height: 86,
          borderRadius: 43,
          background: color,
          color: "#14162b",
          fontFamily: FONT,
          fontWeight: 900,
          fontSize: 46,
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
        }}
      >
        {n}
      </div>
      <div
        style={{
          fontFamily: FONT,
          fontWeight: 900,
          fontSize: 72,
          color: WHITE,
          letterSpacing: 2,
        }}
      >
        {name}
      </div>
    </div>
  );
};

const Sub: React.FC<{ children: React.ReactNode; delay?: number }> = ({
  children,
  delay = 6,
}) => {
  const st = useEnter(delay, 24);
  return (
    <div
      style={{
        ...st,
        fontFamily: FONT,
        fontSize: 40,
        color: DIM,
        marginBottom: 40,
        maxWidth: 880,
      }}
    >
      {children}
    </div>
  );
};

// ── Scene 1: the promise ─────────────────────────────────────────────────
const S1: React.FC<{ dur: number }> = ({ dur }) => {
  const out = useSceneOut(dur);
  const kick = useEnter(0);
  const big1 = useEnter(6);
  const big2 = useEnter(12);
  const pill = useEnter(26, 60);
  return (
    <AbsoluteFill
      style={{
        opacity: out,
        alignItems: "center",
        justifyContent: "center",
        padding: 80,
      }}
    >
      <div
        style={{
          ...kick,
          fontFamily: FONT,
          fontWeight: 900,
          fontSize: 52,
          color: YELLOW,
          letterSpacing: 6,
          marginBottom: 26,
        }}
      >
        10 AI AGENTS
      </div>
      <div
        style={{
          ...big1,
          fontFamily: FONT,
          fontWeight: 900,
          fontSize: 104,
          color: WHITE,
          textAlign: "center",
          lineHeight: 1.05,
        }}
      >
        turn a 2-hour podcast
      </div>
      <div
        style={{
          ...big2,
          fontFamily: FONT,
          fontWeight: 900,
          fontSize: 104,
          color: RED,
          textAlign: "center",
          lineHeight: 1.05,
          marginBottom: 60,
        }}
      >
        into viral shorts
      </div>
      <div
        style={{
          ...pill,
          background: CARD,
          border: `3px solid ${BLUE}`,
          borderRadius: 60,
          padding: "26px 54px",
          fontFamily: FONT,
          fontSize: 42,
          color: WHITE,
        }}
      >
        🎙️ youtube.com/watch?v=…{" "}
        <span style={{ color: DIM }}>(2:14:37)</span>
      </div>
    </AbsoluteFill>
  );
};

// ── Scene 2: FINDER ──────────────────────────────────────────────────────
const CANDS = [
  { hook: "“The $10M mistake founders make”", score: 92 },
  { hook: "“Nobody talks about this…”", score: 87 },
  { hook: "“I was wrong about money”", score: 81 },
];

const S2: React.FC<{ dur: number }> = ({ dur }) => {
  const out = useSceneOut(dur);
  return (
    <AbsoluteFill style={{ opacity: out, padding: "150px 90px" }}>
      <StepBadge n="1" name="FINDER" />
      <Sub>transcribes the episode + scores the most clip-worthy moments</Sub>
      {CANDS.map((c, i) => {
        const st = useEnter(16 + i * 10, 60);
        return (
          <div
            key={c.hook}
            style={{
              ...st,
              background: CARD,
              borderRadius: 28,
              padding: "34px 40px",
              marginBottom: 30,
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              border: i === 0 ? `3px solid ${YELLOW}` : "3px solid transparent",
            }}
          >
            <div
              style={{
                fontFamily: FONT,
                fontSize: 44,
                color: WHITE,
                fontWeight: 600,
                maxWidth: 640,
              }}
            >
              {c.hook}
            </div>
            <div
              style={{
                fontFamily: FONT,
                fontWeight: 900,
                fontSize: 46,
                color: i === 0 ? "#14162b" : WHITE,
                background: i === 0 ? YELLOW : "rgba(94,114,235,0.35)",
                borderRadius: 20,
                padding: "10px 26px",
              }}
            >
              {c.score}
            </div>
          </div>
        );
      })}
    </AbsoluteFill>
  );
};

// ── Scene 3: EDITOR ──────────────────────────────────────────────────────
const CAPTION_WORDS = ["THIS", "CHANGES", "EVERYTHING"];

const S3: React.FC<{ dur: number }> = ({ dur }) => {
  const frame = useCurrentFrame();
  const out = useSceneOut(dur);
  const phone = useEnter(10, 80);
  // caption words pop one per 12 frames starting at frame 28
  const active = Math.min(
    CAPTION_WORDS.length - 1,
    Math.max(0, Math.floor((frame - 28) / 12)),
  );
  const barW = interpolate(frame, [20, dur], [0, 76], {
    extrapolateRight: "clamp",
    extrapolateLeft: "clamp",
  });
  return (
    <AbsoluteFill style={{ opacity: out, padding: "150px 90px" }}>
      <StepBadge n="2" name="EDITOR" color={RED} />
      <Sub>cuts to 9:16 · burns captions · music · zooms · b-roll</Sub>
      <div style={{ display: "flex", justifyContent: "center" }}>
        <div
          style={{
            ...phone,
            width: 460,
            height: 820,
            borderRadius: 46,
            border: `6px solid ${RED}`,
            background: "#101226",
            position: "relative",
            overflow: "hidden",
          }}
        >
          {/* fake video area */}
          <div
            style={{
              position: "absolute",
              inset: 0,
              background:
                "radial-gradient(circle at 50% 34%, #2a2f55 0%, #14162b 70%)",
            }}
          />
          {/* captions */}
          <div
            style={{
              position: "absolute",
              top: "44%",
              width: "100%",
              display: "flex",
              justifyContent: "center",
              gap: 14,
              flexWrap: "wrap",
              padding: "0 20px",
            }}
          >
            {CAPTION_WORDS.map((w, i) => (
              <span
                key={w}
                style={{
                  fontFamily: FONT,
                  fontWeight: 900,
                  fontSize: 58,
                  color: i === active ? YELLOW : WHITE,
                  transform: i === active ? "scale(1.14)" : "scale(1)",
                  textShadow: "0 4px 18px rgba(0,0,0,0.8)",
                }}
              >
                {w}
              </span>
            ))}
          </div>
          {/* progress bar */}
          <div
            style={{
              position: "absolute",
              bottom: 0,
              left: 0,
              height: 12,
              width: `${barW}%`,
              background: RED,
            }}
          />
        </div>
      </div>
    </AbsoluteFill>
  );
};

// ── Scene 4: UPLOADER ────────────────────────────────────────────────────
const S4: React.FC<{ dur: number }> = ({ dur }) => {
  const out = useSceneOut(dur);
  const card = useEnter(12, 60);
  const times = ["09:00", "14:00", "19:00"];
  return (
    <AbsoluteFill style={{ opacity: out, padding: "150px 90px" }}>
      <StepBadge n="3" name="UPLOADER" color={BLUE} />
      <Sub>writes titles + hashtags, schedules the whole day on YouTube</Sub>
      <div
        style={{
          ...card,
          background: CARD,
          borderRadius: 32,
          padding: "44px 48px",
        }}
      >
        <div
          style={{
            fontFamily: FONT,
            fontWeight: 900,
            fontSize: 52,
            color: WHITE,
            marginBottom: 18,
          }}
        >
          The $10M Mistake Founders Make
        </div>
        <div
          style={{
            fontFamily: FONT,
            fontSize: 38,
            color: BLUE,
            marginBottom: 40,
          }}
        >
          #shorts #startup #business
        </div>
        <div style={{ display: "flex", gap: 26 }}>
          {times.map((t, i) => {
            const st = useEnter(30 + i * 8, 30);
            return (
              <div
                key={t}
                style={{
                  ...st,
                  fontFamily: FONT,
                  fontWeight: 700,
                  fontSize: 40,
                  color: "#14162b",
                  background: YELLOW,
                  borderRadius: 18,
                  padding: "14px 30px",
                }}
              >
                ✓ {t}
              </div>
            );
          })}
        </div>
      </div>
    </AbsoluteFill>
  );
};

// ── Scene 5: MANAGER learning loop ───────────────────────────────────────
const S5: React.FC<{ dur: number }> = ({ dur }) => {
  const frame = useCurrentFrame();
  const out = useSceneOut(dur);
  const heights = [0.35, 0.5, 0.42, 0.68, 0.9];
  return (
    <AbsoluteFill style={{ opacity: out, padding: "150px 90px" }}>
      <StepBadge n="4" name="MANAGER" />
      <Sub>tracks real views + retention — the team learns what works</Sub>
      <div
        style={{
          display: "flex",
          alignItems: "flex-end",
          gap: 34,
          height: 460,
          marginTop: 30,
          marginBottom: 50,
        }}
      >
        {heights.map((h, i) => {
          const grow = interpolate(frame, [14 + i * 7, 40 + i * 7], [0, h], {
            extrapolateLeft: "clamp",
            extrapolateRight: "clamp",
          });
          return (
            <div
              key={i}
              style={{
                width: 130,
                height: `${grow * 100}%`,
                borderRadius: "16px 16px 0 0",
                background: i === heights.length - 1 ? YELLOW : BLUE,
              }}
            />
          );
        })}
      </div>
      <Sub delay={44}>
        + TREND SCOUT · PLANNER · COMMUNITY · TRAINER keep it improving
      </Sub>
    </AbsoluteFill>
  );
};

// ── Scene 6: CTA with the 8-agent ring ───────────────────────────────────
const S6: React.FC<{ dur: number }> = ({ dur }) => {
  const frame = useCurrentFrame();
  const title = useEnter(8);
  const cta = useEnter(20);
  const N = 10;
  const R = 260;
  const pulse = 1 + 0.04 * Math.sin(frame / 7);
  return (
    <AbsoluteFill
      style={{ alignItems: "center", justifyContent: "center", padding: 80 }}
    >
      <div
        style={{
          position: "relative",
          width: 640,
          height: 640,
          marginBottom: 40,
          transform: `scale(${pulse})`,
        }}
      >
        {Array.from({ length: N }).map((_, i) => {
          const a = (Math.PI * 2 * i) / N - Math.PI / 2 + frame / 220;
          const x = 320 + R * Math.cos(a);
          const y = 320 + R * Math.sin(a);
          return (
            <React.Fragment key={i}>
              <div
                style={{
                  position: "absolute",
                  left: 320,
                  top: 320,
                  width: R,
                  height: 3,
                  background: "rgba(94,114,235,0.4)",
                  transformOrigin: "0 50%",
                  transform: `rotate(${(a * 180) / Math.PI}deg)`,
                }}
              />
              <div
                style={{
                  position: "absolute",
                  left: x - 26,
                  top: y - 26,
                  width: 52,
                  height: 52,
                  borderRadius: 26,
                  background: BLUE,
                  border: "3px solid #8c9bff",
                }}
              />
            </React.Fragment>
          );
        })}
        <div
          style={{
            position: "absolute",
            left: 320 - 42,
            top: 320 - 42,
            width: 84,
            height: 84,
            borderRadius: 42,
            background: YELLOW,
            border: "4px solid #ffeba0",
          }}
        />
      </div>
      <div
        style={{
          ...title,
          fontFamily: FONT,
          fontWeight: 900,
          fontSize: 82,
          color: WHITE,
          textAlign: "center",
          marginBottom: 24,
        }}
      >
        PODCAST SHORTS FACTORY
      </div>
      <div
        style={{
          ...cta,
          fontFamily: FONT,
          fontWeight: 700,
          fontSize: 46,
          color: YELLOW,
          textAlign: "center",
        }}
      >
        Full source code · runs on free AI · link below
      </div>
    </AbsoluteFill>
  );
};

// ── timeline: 600 frames = 20s @ 30fps ───────────────────────────────────
const SCENES: [React.FC<{ dur: number }>, number][] = [
  [S1, 96], // 0.0–3.2s  the promise
  [S2, 114], // 3.2–7.0s  finder
  [S3, 114], // 7.0–10.8s editor
  [S4, 96], // 10.8–14.0s uploader
  [S5, 96], // 14.0–17.2s manager loop
  [S6, 84], // 17.2–20.0s cta
];

export const AgentsExplainer: React.FC = () => {
  let at = 0;
  return (
    <AbsoluteFill>
      <Bg />
      {SCENES.map(([C, dur], i) => {
        const from = at;
        at += dur;
        return (
          <Sequence key={i} from={from} durationInFrames={dur}>
            <C dur={dur} />
          </Sequence>
        );
      })}
    </AbsoluteFill>
  );
};
