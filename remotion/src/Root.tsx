import "./index.css";
import { Composition, CalculateMetadataFunction } from "remotion";
import { Captions, CaptionsProps } from "./Captions";
import { IntroHook } from "./IntroHook";

const FPS = 30;

// Duration comes from the last word's end; dimensions are our vertical short.
const calc: CalculateMetadataFunction<CaptionsProps> = ({ props }) => {
  const last = props.words.length
    ? props.words[props.words.length - 1].end
    : 1;
  return {
    durationInFrames: Math.max(1, Math.ceil((last + 0.3) * FPS)),
    fps: FPS,
    width: 1080,
    height: 1920,
  };
};

export const RemotionRoot: React.FC = () => {
  return (
    <>
      <Composition
        id="Captions"
        component={Captions}
        durationInFrames={60}
        fps={FPS}
        width={1080}
        height={1920}
        calculateMetadata={calc}
        defaultProps={{
          words: [
            { word: "creatine", start: 0.0, end: 0.5 },
            { word: "does", start: 0.5, end: 0.8 },
            { word: "NOT", start: 0.8, end: 1.2 },
            { word: "hurt", start: 1.2, end: 1.6 },
            { word: "your", start: 1.6, end: 1.9 },
            { word: "kidneys.", start: 1.9, end: 2.6 },
          ],
          emphasis: ["NOT", "kidneys"],
          wordsPerPage: 3,
          fontSize: 96,
        }}
      />
      <Composition
        id="IntroHook"
        component={IntroHook}
        durationInFrames={70}
        fps={FPS}
        width={1080}
        height={1920}
        defaultProps={{
          text: "Doctors Read This WRONG",
          fontSize: 118,
          accentWords: ["WRONG"],
        }}
      />
    </>
  );
};
