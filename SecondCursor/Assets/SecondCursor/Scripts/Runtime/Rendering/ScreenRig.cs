using UnityEngine;
using UnityEngine.UI;

namespace SecondCursor.Rendering
{
    /// <summary>
    /// The fake monitor. The whole OS is a world-space canvas (1 world unit = 1 virtual pixel, origin at the
    /// bottom-left) rendered by an orthographic camera into a fixed 960x540 RenderTexture, which is then
    /// shown letterboxed on the real screen through an overlay canvas where the CRT effects live.
    /// A fixed virtual resolution keeps pixel art crisp, layout deterministic, and makes all gameplay
    /// coordinates (both cursors, hit-testing, entity targets) resolution-independent.
    /// At non-integer display scales the OS is rendered at the next whole multiple (e.g. 1920x1080 for a
    /// 1.24x window) and scaled down smoothly ("sharp bilinear"), so pixel text stays crisp at any size.
    /// </summary>
    public sealed class ScreenRig : MonoBehaviour
    {
        public const int Width = 960;
        public const int Height = 540;
        public const int UiLayer = 5;      // Unity's built-in "UI" layer
        public const int SceneLayer = 8;   // 3D security-camera set (unnamed user layer; works without a name)

        public RenderTexture ScreenTexture { get; private set; }
        /// <summary>The screen texture was replaced (supersampling factor changed): re-bind any copies.</summary>
        public event System.Action ScreenTextureChanged;
        /// <summary>Screen texture pixels per virtual pixel (1 at integer display scales).</summary>
        public int TextureScale { get; private set; } = 1;
        public Camera UiCamera { get; private set; }
        public Canvas OsCanvas { get; private set; }
        public RectTransform OsRoot { get; private set; }
        public Canvas DisplayCanvas { get; private set; }
        public RectTransform DisplayRoot { get; private set; }
        public RawImage DisplayImage { get; private set; }
        public RectTransform DisplayRect { get; private set; }

        /// <summary>Where the virtual screen sits on the real screen, in real pixels.</summary>
        public Rect DisplayPixelRect { get; private set; }
        public float Scale { get; private set; } = 1f;

        int _lastW = -1, _lastH = -1;

        public void Build()
        {
            ScreenTexture = MakeScreenTexture(1);

            // Backbuffer clear camera: renders nothing, keeps the real screen black behind the display.
            var clearGo = new GameObject("Backbuffer Camera");
            clearGo.transform.SetParent(transform, false);
            var clear = clearGo.AddComponent<Camera>();
            clear.clearFlags = CameraClearFlags.SolidColor;
            clear.backgroundColor = Color.black;
            clear.cullingMask = 0;
            clear.depth = -100;
            clear.orthographic = true;
            clear.useOcclusionCulling = false;
            clear.allowHDR = false;
            clear.allowMSAA = false;

            // Camera that renders the OS canvas into the screen texture.
            var camGo = new GameObject("OS Camera");
            camGo.transform.SetParent(transform, false);
            camGo.transform.position = new Vector3(Width * 0.5f, Height * 0.5f, -100f);
            UiCamera = camGo.AddComponent<Camera>();
            UiCamera.orthographic = true;
            UiCamera.orthographicSize = Height * 0.5f;
            UiCamera.nearClipPlane = 1f;
            UiCamera.farClipPlane = 200f;
            UiCamera.cullingMask = 1 << UiLayer;
            UiCamera.clearFlags = CameraClearFlags.SolidColor;
            UiCamera.backgroundColor = Color.black;
            UiCamera.targetTexture = ScreenTexture;
            UiCamera.depth = -50;
            UiCamera.useOcclusionCulling = false;
            UiCamera.allowHDR = false;
            UiCamera.allowMSAA = false;

            // World-space OS canvas.
            var osGo = new GameObject("NEXUS OS", typeof(RectTransform));
            osGo.layer = UiLayer;
            osGo.transform.SetParent(transform, false);
            OsCanvas = osGo.AddComponent<Canvas>();
            OsCanvas.renderMode = RenderMode.WorldSpace;
            OsCanvas.worldCamera = UiCamera;
            OsRoot = (RectTransform)osGo.transform;
            OsRoot.pivot = Vector2.zero;
            OsRoot.anchorMin = OsRoot.anchorMax = Vector2.zero;
            OsRoot.sizeDelta = new Vector2(Width, Height);
            OsRoot.position = Vector3.zero;
            OsRoot.localScale = Vector3.one;

            // Overlay canvas presenting the screen texture on the real display.
            var dispGo = new GameObject("Display", typeof(RectTransform));
            dispGo.transform.SetParent(transform, false);
            DisplayCanvas = dispGo.AddComponent<Canvas>();
            DisplayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            DisplayCanvas.sortingOrder = 1000;
            DisplayCanvas.pixelPerfect = false;
            DisplayRoot = (RectTransform)dispGo.transform;

            var bg = new GameObject("Letterbox", typeof(RectTransform));
            bg.transform.SetParent(DisplayRoot, false);
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = Color.black;
            bgImg.raycastTarget = false;
            var bgRt = (RectTransform)bg.transform;
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;

            var imgGo = new GameObject("Screen Image", typeof(RectTransform));
            imgGo.transform.SetParent(DisplayRoot, false);
            DisplayImage = imgGo.AddComponent<RawImage>();
            DisplayImage.texture = ScreenTexture;
            DisplayImage.raycastTarget = false;
            DisplayRect = (RectTransform)imgGo.transform;
            DisplayRect.anchorMin = DisplayRect.anchorMax = Vector2.zero;
            DisplayRect.pivot = Vector2.zero;

            UpdateLetterbox(true);
        }

        Core.Game.ReadingSize _lastSize;

        void LateUpdate()
        {
            UpdateLetterbox(false);
        }

        void UpdateLetterbox(bool force)
        {
            int sw = Mathf.Max(1, Screen.width);
            int sh = Mathf.Max(1, Screen.height);
            var size = Game.DisplaySettings.Size;
            if (!force && sw == _lastW && sh == _lastH && size == _lastSize) return;
            _lastW = sw;
            _lastH = sh;
            _lastSize = size;

            float s = Mathf.Min(sw / (float)Width, sh / (float)Height);
            // Prefer an exact integer scale when it loses little area: perfectly square pixels.
            float si = Mathf.Floor(s);
            bool integer = si >= 1f && si >= s * 0.93f;
            if (integer) s = si;
            Scale = s;
            // Phase Q1: Medium reading text (1.5x) needs a 2x texture to keep square pixels.
            SetTextureScale(Core.Game.DisplayOptions.TextureScale(s, integer, size));
            float w = Width * s, h = Height * s;
            float x = Mathf.Floor((sw - w) * 0.5f), y = Mathf.Floor((sh - h) * 0.5f);
            DisplayPixelRect = new Rect(x, y, w, h);
            ScreenTexture.filterMode = integer ? FilterMode.Point : FilterMode.Bilinear;

            // The overlay canvas is in real pixels (scale factor 1).
            DisplayRect.anchoredPosition = new Vector2(x, y);
            DisplayRect.sizeDelta = new Vector2(w, h);
        }

        RenderTexture MakeScreenTexture(int scale)
        {
            var rt = new RenderTexture(Width * scale, Height * scale, 16, RenderTextureFormat.ARGB32)
            {
                name = "NEXUS Screen x" + scale,
                filterMode = FilterMode.Point,
                useMipMap = false,
                antiAliasing = 1,
                wrapMode = TextureWrapMode.Clamp,
            };
            rt.Create();
            return rt;
        }

        void SetTextureScale(int scale)
        {
            if (scale == TextureScale) return;
            var old = ScreenTexture;
            TextureScale = scale;
            ScreenTexture = MakeScreenTexture(scale);
            UiCamera.targetTexture = ScreenTexture;
            DisplayImage.texture = ScreenTexture;
            if (old != null)
            {
                old.Release();
                Destroy(old);
            }
            ScreenTextureChanged?.Invoke();
        }

        /// <summary>Real screen pixels (origin bottom-left, e.g. mouse position) to virtual OS pixels.</summary>
        public Vector2 ScreenToVirtual(Vector2 screenPixels)
        {
            var r = DisplayPixelRect;
            return new Vector2((screenPixels.x - r.x) / Scale, (screenPixels.y - r.y) / Scale);
        }

        public Vector2 VirtualToScreen(Vector2 virtualPixels)
        {
            var r = DisplayPixelRect;
            return new Vector2(r.x + virtualPixels.x * Scale, r.y + virtualPixels.y * Scale);
        }

        public static Vector2 ClampToScreen(Vector2 v) => new Vector2(Mathf.Clamp(v.x, 0f, Width - 1), Mathf.Clamp(v.y, 0f, Height - 1));

        void OnDestroy()
        {
            if (ScreenTexture != null)
            {
                ScreenTexture.Release();
                Destroy(ScreenTexture);
            }
        }
    }
}
