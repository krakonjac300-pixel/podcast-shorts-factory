using System;
using System.Collections;
using System.Collections.Generic;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SecondCursor.FX
{
    /// <summary>
    /// CRT presentation and screen-level horror effects, all shader-free so they work in any render
    /// pipeline: scanlines, vignette with rounded tube corners, grain, mains flicker, horizontal tearing
    /// glitches with colour fringes, screen shake, flashes and the CRT power-off collapse.
    /// </summary>
    public sealed class VisualFx : MonoBehaviour
    {
        const int StripCount = 10;

        ScreenRig _rig;
        RawImage _scanlines;
        RawImage _vignette;
        RawImage _grain;
        Image _flicker;
        Image _flash;
        Image _black;
        Texture2D _scanTex;
        readonly List<Texture2D> _ownedTextures = new List<Texture2D>();
        readonly List<RawImage> _strips = new List<RawImage>();
        readonly List<RawImage> _fringes = new List<RawImage>();
        float _glitchTime;
        float _glitchIntensity;
        float _shakeTime;
        float _shakeAmp;
        float _flashAlpha;
        bool _crt = true;

        /// <summary>Extra grain (0..1) layered on top of the base amount (tension moments).</summary>
        public float ExtraGrain;
        /// <summary>0..1 strength of the random brightness flicker.</summary>
        public float FlickerAmount = 0.35f;
        /// <summary>Photosensitivity option: glitches, flashes, shakes and flicker spikes are toned right down.</summary>
        public bool ReduceFlashing;
        public bool IsPoweredOff { get; private set; }

        public static VisualFx Create(ScreenRig rig)
        {
            var fx = rig.gameObject.AddComponent<VisualFx>();
            fx._rig = rig;
            fx.Build();
            rig.ScreenTextureChanged += fx.RebindScreen;
            return fx;
        }

        void Build()
        {
            var parent = _rig.DisplayRect;

            for (int i = 0; i < StripCount; i++)
            {
                var strip = UIBuilder.Raw(parent, _rig.ScreenTexture, "Glitch Strip " + i);
                strip.enabled = false;
                var rt = strip.rectTransform;
                rt.anchorMin = new Vector2(0f, i / (float)StripCount);
                rt.anchorMax = new Vector2(1f, (i + 1) / (float)StripCount);
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                strip.uvRect = new Rect(0f, i / (float)StripCount, 1f, 1f / StripCount);
                _strips.Add(strip);

                var fringe = UIBuilder.Raw(parent, _rig.ScreenTexture, "Fringe " + i);
                fringe.enabled = false;
                fringe.rectTransform.anchorMin = rt.anchorMin;
                fringe.rectTransform.anchorMax = rt.anchorMax;
                fringe.rectTransform.offsetMin = fringe.rectTransform.offsetMax = Vector2.zero;
                fringe.uvRect = strip.uvRect;
                fringe.color = new Color(1f, 0.2f, 0.25f, 0.35f);
                _fringes.Add(fringe);
            }

            _scanTex = SpriteLibrary.MakeTexture(1, 2, (x, y) => y == 0 ? new Color32(0, 0, 0, 58) : new Color32(0, 0, 0, 0), FilterMode.Point, TextureWrapMode.Repeat);
            _ownedTextures.Add(_scanTex);
            _scanlines = UIBuilder.Raw(parent, _scanTex, "Scanlines");
            _scanlines.rectTransform.Stretch();
            _scanlines.uvRect = new Rect(0f, 0f, 1f, ScreenRig.Height);

            var grain = SpriteLibrary.MakeTexture(128, 128, (x, y) =>
            {
                byte v = (byte)UnityEngine.Random.Range(0, 256);
                return new Color32(v, v, v, (byte)UnityEngine.Random.Range(0, 22));
            }, FilterMode.Point, TextureWrapMode.Repeat);
            _ownedTextures.Add(grain);
            _grain = UIBuilder.Raw(parent, grain, "Grain");
            _grain.rectTransform.Stretch();

            _flicker = UIBuilder.Solid(parent, new Color(0f, 0f, 0f, 0f), "Flicker");
            _flicker.rectTransform.Stretch();

            var vig = SpriteLibrary.MakeTexture(128, 72, (x, y) =>
            {
                float u = (x + 0.5f) / 128f * 2f - 1f;
                float v = (y + 0.5f) / 72f * 2f - 1f;
                // Superellipse: flat centre, gently darker toward the edges, black only in the rounded
                // tube corners. Kept light so the taskbar and window edges stay readable.
                float d = Mathf.Pow(Mathf.Pow(Mathf.Abs(u), 6f) + Mathf.Pow(Mathf.Abs(v), 6f), 1f / 6f);
                float a = Edge(0.78f, 1.12f, d) * 0.42f + Edge(0.4f, 1.6f, u * u + v * v) * 0.08f;
                if (d > 1.105f) a = 1f;
                return new Color32(0, 0, 0, (byte)(Mathf.Clamp01(a) * 255f));
            }, FilterMode.Bilinear, TextureWrapMode.Clamp);
            _ownedTextures.Add(vig);
            _vignette = UIBuilder.Raw(parent, vig, "Vignette");
            _vignette.rectTransform.Stretch();

            _flash = UIBuilder.Solid(parent, new Color(1f, 1f, 1f, 0f), "Flash");
            _flash.rectTransform.Stretch();
            _black = UIBuilder.Solid(parent, Color.black, "Black");
            _black.rectTransform.Stretch();
            _black.enabled = false;
        }

        /// <summary>GLSL-style smoothstep: 0 below e0, 1 above e1 (Mathf.SmoothStep interpolates instead).</summary>
        static float Edge(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        void RebindScreen()
        {
            foreach (var s in _strips) s.texture = _rig.ScreenTexture;
            foreach (var f in _fringes) f.texture = _rig.ScreenTexture;
        }

        public bool CrtEnabled
        {
            get => _crt;
            set
            {
                _crt = value;
                _scanlines.enabled = value;
                _vignette.enabled = value;
                _grain.enabled = value;
                _flicker.enabled = value;
            }
        }

        public void Glitch(float duration, float intensity = 1f)
        {
            if (ReduceFlashing)
            {
                duration *= 0.5f;
                intensity *= 0.3f;
            }
            _glitchTime = Mathf.Max(_glitchTime, duration);
            _glitchIntensity = Mathf.Max(_glitchIntensity, intensity);
        }

        public void Shake(float duration, float amplitudePx)
        {
            if (ReduceFlashing) amplitudePx *= 0.3f;
            _shakeTime = Mathf.Max(_shakeTime, duration);
            _shakeAmp = Mathf.Max(_shakeAmp, amplitudePx);
        }

        public void Flash(float alpha = 0.6f) => _flashAlpha = Mathf.Max(_flashAlpha, ReduceFlashing ? alpha * 0.2f : alpha);

        public void SetBlack(bool black)
        {
            _black.enabled = black;
            _black.color = Color.black;
        }

        /// <summary>
        /// Classic tube switch-off: the picture collapses to a line, then to a dot, then dark. <paramref name="flash"/> false: no white flash of
        /// its own (Review M1: after a climax's hit it was a second full-screen flash 0.2 s after the first).
        /// </summary>
        public IEnumerator PowerOff(float duration = 0.9f, bool flash = true)
        {
            IsPoweredOff = true;
            var rt = _rig.DisplayRect;
            Vector2 pos = rt.anchoredPosition;
            Vector2 size = rt.sizeDelta;
            if (flash) Flash(0.8f);
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                float h = k < 0.55f ? Mathf.Lerp(size.y, 3f, Mathf.SmoothStep(0f, 1f, k / 0.55f)) : 3f;
                float w = k < 0.55f ? size.x : Mathf.Lerp(size.x, 3f, Mathf.SmoothStep(0f, 1f, (k - 0.55f) / 0.45f));
                rt.sizeDelta = new Vector2(w, h);
                rt.anchoredPosition = pos + (size - new Vector2(w, h)) * 0.5f;
                _rig.DisplayImage.color = Color.Lerp(Color.white, new Color(2f, 2f, 2f, 1f), k);
                yield return null;
            }
            _rig.DisplayImage.enabled = false;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            _rig.DisplayImage.color = Color.white;
        }

        public void PowerOn()
        {
            IsPoweredOff = false;
            _rig.DisplayImage.enabled = true;
            _rig.DisplayImage.color = Color.white;
            Flash(0.35f);
        }

        void OnDestroy()
        {
            foreach (var t in _ownedTextures) if (t != null) Destroy(t);
            _ownedTextures.Clear();
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;

            if (_crt)
            {
                _grain.uvRect = new Rect(UnityEngine.Random.value, UnityEngine.Random.value, _rig.DisplayPixelRect.width / 256f, _rig.DisplayPixelRect.height / 256f);
                var gc = _grain.color;
                gc.a = Mathf.Clamp01(0.55f + ExtraGrain * 2f);
                _grain.color = gc;
                float n = Mathf.PerlinNoise(Time.unscaledTime * 7f, 0.3f);
                float spike = !ReduceFlashing && UnityEngine.Random.value < 0.004f * FlickerAmount ? 0.08f : 0f;
                _flicker.color = new Color(0f, 0f, 0f, FlickerAmount * 0.035f * n + spike);
                _scanTex.filterMode = Mathf.Approximately(_rig.Scale, Mathf.Round(_rig.Scale)) ? FilterMode.Point : FilterMode.Bilinear;
            }

            _flashAlpha = Mathf.MoveTowards(_flashAlpha, 0f, dt * 2.5f);
            _flash.color = new Color(1f, 1f, 1f, _flashAlpha);

            // Screen shake moves the whole picture on the real display.
            if (_shakeTime > 0f)
            {
                _shakeTime -= dt;
                Vector2 o = UnityEngine.Random.insideUnitCircle * _shakeAmp * _rig.Scale;
                _rig.DisplayImage.rectTransform.anchoredPosition = new Vector2(_rig.DisplayPixelRect.x + Mathf.Round(o.x), _rig.DisplayPixelRect.y + Mathf.Round(o.y));
                if (_shakeTime <= 0f)
                {
                    _shakeAmp = 0f;
                    _rig.DisplayImage.rectTransform.anchoredPosition = _rig.DisplayPixelRect.position;
                }
            }

            if (_glitchTime > 0f)
            {
                _glitchTime -= dt;
                bool on = _glitchTime > 0f;
                for (int i = 0; i < _strips.Count; i++)
                {
                    bool active = on && UnityEngine.Random.value < 0.35f * _glitchIntensity;
                    var s = _strips[i];
                    var f = _fringes[i];
                    s.enabled = active;
                    f.enabled = active && UnityEngine.Random.value < 0.6f;
                    if (!active) continue;
                    float shift = UnityEngine.Random.Range(-0.04f, 0.04f) * _glitchIntensity;
                    s.uvRect = new Rect(shift, i / (float)StripCount, 1f, 1f / StripCount);
                    f.uvRect = new Rect(shift + UnityEngine.Random.Range(-0.01f, 0.01f), i / (float)StripCount, 1f, 1f / StripCount);
                    f.color = UnityEngine.Random.value < 0.5f ? new Color(1f, 0.15f, 0.2f, 0.3f) : new Color(0.1f, 0.9f, 1f, 0.3f);
                }
                if (!on) _glitchIntensity = 0f;
            }
        }
    }
}
