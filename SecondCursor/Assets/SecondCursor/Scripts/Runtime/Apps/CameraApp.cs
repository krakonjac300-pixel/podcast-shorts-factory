using System;
using System.Collections.Generic;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Input;
using SecondCursor.OS;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SecondCursor.Apps
{
    /// <summary>
    /// SecureView: the building's CCTV. Low-resolution grey feeds from a small 3D set, a camera list,
    /// timestamp and blinking REC. CAM 03 looks at the operator's own office.
    /// </summary>
    public sealed class CameraApp : App
    {
        const int FeedW = 320;
        const int FeedH = 240;

        RawImage _feed;
        PixelText _nameTag;
        RawImage _noise;
        Texture2D _noiseTex;
        PixelText _label;
        PixelText _time;
        Image _rec;
        PixelText _noSignal;
        PixelText _caption;
        PixelText _next;
        RectTransform _side, _screen;
        PixelText _big;
        Image _fullBack;
        string _bigCaption = "";
        float _bigShown;
        bool _scripted;
        bool _hiddenShown;
        readonly Dictionary<string, UiButton> _buttons = new Dictionary<string, UiButton>();
        string _current;
        float _t;
        float _switchNoise;
        Color32[] _noisePixels;
        /// <summary>The grain is regenerated every 1/60 s (as it was every frame at 60 Hz), not at the display's rate.</summary>
        StepTimer _noiseTimer;

        public override string AppId => AppIds.Camera;
        public string CurrentCamera => _current;
        public event Action<string, CursorAgent> CameraSelected;

        public override void Open(Rect? zoomFrom, CursorAgent by)
        {
            CreateWindow(G.Content.Text("app.camera"), "icon_camera", 90, 40, FeedW + 128, FeedH + 60, WindowFlags.CanClose | WindowFlags.CanMinimize | WindowFlags.CanMaximize, zoomFrom);
            var client = Window.Client;

            // Phase Q3 (V4): a scripted full view sits on black (the CCTV monitor), not on the window's grey.
            _fullBack = UIBuilder.Solid(client, new Color32(0x0A, 0x0C, 0x0B, 0xFF), "Full Back");
            _fullBack.rectTransform.Stretch();
            _fullBack.raycastTarget = false;
            _fullBack.enabled = false;
            _side = UIBuilder.Rect("Cameras", client).At(2, 2, 110, FeedH + 26);
            BuildButtons();

            var screen = UIBuilder.Bevel(client, BevelStyle.Sunken, "Monitor");
            _screen = screen.rectTransform;
            _screen.At(116, 2, FeedW + 4, FeedH + 4);
            var black = UIBuilder.Solid(screen.rectTransform, Color.black, "Black");
            black.rectTransform.Stretch(2, 2, 2, 2);
            _feed = UIBuilder.Raw(screen.rectTransform, G.CameraRig != null ? G.CameraRig.Feed : null, "Feed");
            _feed.rectTransform.Stretch(2, 2, 2, 2);
            _feed.color = new Color(0.86f, 0.93f, 0.88f, 1f);

            _noiseTex = OwnedAssets.Own(Window.gameObject,
                new Texture2D(160, 120, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat });
            _noise = UIBuilder.Raw(screen.rectTransform, _noiseTex, "Noise");
            _noise.rectTransform.Stretch(2, 2, 2, 2);

            _noSignal = UIBuilder.Text(screen.rectTransform, G.Content.Text("camera.nosignal"), Palette.BiosBright, true);
            _noSignal.Scale = 2;
            _noSignal.rectTransform.Stretch(2, 2, 2, 2);
            _noSignal.Align = TextAlign.Center;
            _noSignal.VAlign = TextVAlign.Middle;

            _label = UIBuilder.Text(screen.rectTransform, "", Palette.BiosBright, true);
            _label.Shadow = true;
            _label.rectTransform.TopStrip(8, 12, 10, 10);
            _time = UIBuilder.Text(screen.rectTransform, "", Palette.BiosBright);
            _time.Shadow = true;
            _time.rectTransform.BottomStrip(8, 12, 10, 10);
            _time.Align = TextAlign.Right;
            // CAM 04's shelf label under the camera name (Phase H: at the top, where a window over the viewer's lower half
            // cannot hide it), and the label that comes next, so waiting for one shelf has a visible end.
            _caption = UIBuilder.Text(screen.rectTransform, "", Palette.BiosBright, true);
            _caption.Shadow = true;
            _caption.rectTransform.TopStrip(24, 12, 10, 10);
            _next = UIBuilder.Text(screen.rectTransform, "", Palette.BiosText);
            _next.Shadow = true;
            _next.rectTransform.TopStrip(38, 12, 10, 10);
            // M1: the workstation's own tag over the seated operator on CAM 03 (it is you).
            _nameTag = UIBuilder.Text(_feed.rectTransform, G.Content.Text("camera.operatortag", "CROURKE / WS-04"), new Color32(0xD8, 0xDC, 0xD2, 0xFF));
            _nameTag.Shadow = true;
            _nameTag.Align = TextAlign.Center;
            _nameTag.enabled = false;
            // Phase Q3 (V4): her line as a big CCTV caption in the lower third of the feed, over the centre column (it survives a 9:16 crop).
            _big = UIBuilder.Text(_feed.rectTransform, "", new Color32(0xE4, 0xEA, 0xDE, 0xFF), true, "Big Caption");
            _big.Shadow = true;
            _big.Scale = 2;
            _big.Wrap = true;
            _big.Align = TextAlign.Center;
            _big.VAlign = TextVAlign.Bottom;
            _big.raycastTarget = false;
            _big.enabled = false;
            Window.Resized += _ =>
            {
                // Restored (by the player, or by the story): the scripted view is over.
                if (!Window.IsMaximized) _scripted = false;
                Layout();
            };
            var recHolder = UIBuilder.Rect("REC", screen.rectTransform).TopRight(10, 8, 40, 12);
            _rec = UIBuilder.Icon(recHolder, "rec_dot", 1);
            _rec.rectTransform.anchoredPosition = new Vector2(0f, -3f);
            var recText = UIBuilder.Text(recHolder, "REC", Palette.BiosBright, true);
            recText.Shadow = true;
            recText.rectTransform.Stretch(9, 0, 0, 0);

            Select(CameraOnOpen(G), by);
            if (G.CameraRig != null) G.CameraRig.SetViewing(true);
        }

        /// <summary>
        /// Phase N (fifth blind playtest, finding 4): the viewer opens on the camera the player picked last, never on Custodial's during a
        /// round (it used to reopen on Security's camera, which session 017 closed again a second later). Security's opens pick their own.
        /// </summary>
        public static string CameraOnOpen(Game.GameServices g)
        {
            var rig = g.CameraRig;
            string pick = rig != null ? rig.PlayerCamera ?? rig.ActiveCamera ?? ContentIds.Cam01 : ContentIds.Cam01;
            var rounds = g.Rounds;
            string figure = rounds != null && rounds.Running && rounds.Model != null && !rounds.Model.Finished ? rounds.Model.FigureCamera : null;
            if (pick != figure) return pick;
            foreach (var cam in g.Content.Story.cameras)
                if (cam != null && !cam.hidden && cam.id != figure) return cam.id;
            return pick;
        }

        /// <summary>
        /// One button per camera. A hidden camera (CAM 00) is listed only once camview.cfg allows it; the list is
        /// rebuilt when that changes.
        /// </summary>
        void BuildButtons()
        {
            for (int i = _side.childCount - 1; i >= 0; i--)
            {
                var old = _side.GetChild(i).gameObject;
                old.SetActive(false);
                UnityEngine.Object.Destroy(old);
            }
            _buttons.Clear();
            _hiddenShown = G.Flags.Has(Core.Story.MemoryFlags.N3Cam00);
            int y = 0;
            foreach (var cam in G.Content.Story.cameras)
            {
                if (cam == null || (cam.hidden && !_hiddenShown)) continue;
                string id = cam.id;
                var b = UiButton.Create(_side, ButtonLabel(cam.label, 96), a => Select(id, a), "camera:" + id);
                b.Label.Align = TextAlign.Left;
                b.Label.rectTransform.Stretch(4, 0, 2, 0);
                ((RectTransform)b.transform).At(0, y, 110, 22);
                b.Toggled = id == _current;
                _buttons[id] = b;
                y += 25;
            }
        }

        /// <summary>The viewer is open, not minimized, and shows this camera.</summary>
        public bool IsShowing(string camId) => IsOpen && !Window.IsMinimized && _current == camId;

        /// <summary>"CAM 04 - SUBLEVEL C" -> "CAM 04 SUBLEVEL C", dropping trailing words until it fits.</summary>
        static string ButtonLabel(string label, int maxWidth)
        {
            string s = label.Replace(" - ", " ");
            while (PixelFont.Measure(s, 0, false, 1).x > maxWidth)
            {
                int cut = s.LastIndexOf(' ');
                if (cut <= 0) break;
                s = s.Substring(0, cut);
            }
            return s;
        }

        /// <summary>Phase Q1: while set, only the player can switch the viewer away from this camera (the first CAM 03 view).</summary>
        [System.NonSerialized] public string HeldOn;

        public void Select(string camId, CursorAgent by)
        {
            if (HeldOn != null && camId != HeldOn && (by == null || !by.IsPlayer)) return;
            _current = camId;
            foreach (var kv in _buttons) kv.Value.Toggled = kv.Key == camId;
            var cam = G.Content.Camera(camId);
            _label.text = cam != null ? cam.label : camId;
            _switchNoise = 0.25f;
            Sfx.Play("camera_switch", by);
            if (G.CameraRig != null)
            {
                G.CameraRig.SetCamera(camId);
                if (by != null && by.IsPlayer) G.CameraRig.PlayerCamera = camId;
            }
            if (camId == ContentIds.Cam00 && by != null && by.IsPlayer && !G.Flags.Has(Core.Story.Flags.N3Cam00Viewed))
            {
                // Watch the Watchers: the player found CAM 00 (AchievementWatcher listens for this flag).
                G.Flags.Set(Core.Story.Flags.N3Cam00Viewed);
            }
            CameraSelected?.Invoke(camId, by);
        }

        /// <summary>
        /// Phase Q3 (V4): a scripted full view. With the window maximized the feed fills the client at its largest whole step and the camera
        /// buttons hide; <paramref name="caption"/> (her line, 18 characters a line at most) types in over the lower third. The view ends
        /// when the window is restored or <see cref="EndFullView"/> is called.
        /// </summary>
        public void BeginFullView(string caption)
        {
            _scripted = true;
            _bigCaption = caption ?? "";
            _bigShown = 0f;
            Layout();
        }

        /// <summary>The scripted view is over: the buttons and the caption come back (the window itself is restored by whoever maximized it).</summary>
        public void EndFullView()
        {
            _scripted = false;
            _bigCaption = "";
            Layout();
        }

        /// <summary>The viewer fills the desktop for a scripted full view.</summary>
        public bool IsFullView => _scripted && Window != null && !Window.IsClosed && Window.IsMaximized;

        /// <summary>Whole-step size of the feed in a maximized window: 320x240 grows to 640x480 (a whole 4x of the 160x120 picture).</summary>
        void Layout()
        {
            if (Window == null || Window.IsClosed || _screen == null) return;
            bool max = Window.IsMaximized;
            bool full = max && _scripted;
            _side.gameObject.SetActive(!full);
            _fullBack.enabled = full;
            var size = Window.Size;
            float cw = size.x - 8f, ch = size.y - 8f - OSWindow.CaptionHeight - 1f;
            float left = full ? 0f : 116f;
            int step = max ? Mathf.Max(1, Mathf.FloorToInt(Mathf.Min((cw - left - 4f) / FeedW, (ch - 4f) / FeedH))) : 1;
            float w = FeedW * step + 4f, h = FeedH * step + 4f;
            if (!max) _screen.At(116f, 2f, FeedW + 4f, FeedH + 4f);
            else _screen.At(Mathf.Floor(left + (cw - left - w) * 0.5f), Mathf.Floor((ch - h) * 0.5f), w, h);
            // The caption sits in the middle of the feed's lower third, inside the column a 9:16 crop of the screen keeps (about 300 px).
            float margin = Mathf.Max(0f, Mathf.Floor((FeedW * step - CaptionColumn) * 0.5f));
            _big.rectTransform.BottomStrip(Mathf.Floor(FeedH * step * 0.12f), CaptionHeightPx, margin, margin);
            _big.enabled = full && _bigCaption.Length > 0;
            if (!full) _big.text = "";
        }

        /// <summary>The caption types in at a CCTV pace (about 10 characters a second) once the feed is big.</summary>
        void TickBigCaption(float dt, bool signal)
        {
            if (!IsFullView) return;
            // NO SIGNAL (the door's climax) takes the picture and the caption with it.
            _big.enabled = signal && _bigCaption.Length > 0;
            _bigShown = Mathf.Min(_bigCaption.Length, _bigShown + dt * BigCaptionCps);
            string shown = _bigCaption.Substring(0, Mathf.FloorToInt(_bigShown));
            if (_big.text != shown) _big.text = shown;
        }

        const float BigCaptionCps = 10f, CaptionColumn = 300f, CaptionHeightPx = 56f;

        void UpdateNameTag(bool signal)
        {
            var rig = G.CameraRig;
            Vector2 vp = default;
            bool show = signal && rig != null && _switchNoise <= 0f && rig.OperatorTagViewport(out vp);
            if (show)
            {
                var r = _feed.rectTransform.rect;
                const int w = 120;
                _nameTag.rectTransform.At(Mathf.Round(vp.x * r.width - w * 0.5f), Mathf.Round((1f - vp.y) * r.height - 14f), w, 12);
            }
            if (_nameTag.enabled != show) _nameTag.enabled = show;
        }

        public override void Tick(float dt)
        {
            _t += dt;
            if (_hiddenShown != G.Flags.Has(Core.Story.MemoryFlags.N3Cam00)) BuildButtons();
            string caption = G.CameraRig != null ? G.CameraRig.CaptionFor(_current) : "";
            if (_caption.text != caption)
            {
                // M10: a shelf label swap is a one-step (1/60 s) static flicker.
                if (caption.Length > 0 && _caption.text.Length > 0) _captionFlicker = true;
                _caption.text = caption;
                string next = caption.Length > 0 && G.CameraRig != null ? G.CameraRig.NextShelfFor(_current) : "";
                _next.text = next.Length > 0 ? string.Format(G.Content.Text("camera.next", "NEXT: {0}"), next) : "";
            }
            _switchNoise = Mathf.Max(0f, _switchNoise - dt);
            bool signal = G.CameraRig != null && G.CameraRig.HasSignal(_current);
            TickBigCaption(dt, signal);
            _feed.enabled = signal;
            _noSignal.enabled = !signal;
            // CCTV time only runs while someone watches: close the feed and the clock waits for you (and it stops on a frozen frame).
            var rig = G.CameraRig;
            UpdateHum(signal);
            if (rig != null && !Window.IsMinimized && !rig.FreezeFeed)
            {
                if (rig.FeedMinutes < 0) rig.FeedMinutes = G.Clock.ExactMinutes;
                else rig.FeedMinutes += dt * G.Clock.Rate;
                _time.text = Core.Story.GameClock.FormatCamera(rig.FeedMinutes);
            }
            else if (rig == null) _time.text = G.Clock.FormatCamera();
            EchoClicks();
            _rec.enabled = signal && (_t % 1.2f) < 0.7f;

            // A minimized viewer neither renders the 3D set nor animates its grain.
            bool visible = !Window.IsMinimized;
            if (visible != _visible)
            {
                _visible = visible;
                if (G.CameraRig != null) G.CameraRig.SetViewing(visible);
            }
            if (!visible) return;
            UpdateNameTag(signal);

            // Animated grain: stronger on dead channels and right after switching.
            // Fine 2x2 speckle: a light constant hiss that never hides the picture, heavier on static cuts.
            float amount = !signal ? 0.9f : Mathf.Max(0.05f, _switchNoise * 3f) + (G.CameraRig != null ? G.CameraRig.ExtraNoise : 0f);
            if (rig != null && rig.FreezeFeed && signal) amount = 0f;
            if (_captionFlicker) amount = Mathf.Max(amount, 0.6f);
            if (G.CameraRig != null && _feed.texture != G.CameraRig.Feed) _feed.texture = G.CameraRig.Feed;
            bool regenerate = _captionFlicker || _noiseTimer.Tick(Time.unscaledDeltaTime);
            if (_captionFlicker) _noiseTimer = default;   // the flicker holds for a whole step
            _captionFlicker = false;
            if (!regenerate) return;
            if (_noisePixels == null) _noisePixels = new Color32[_noiseTex.width * _noiseTex.height];
            var px = _noisePixels;
            uint threshold = (uint)(Mathf.Clamp01(amount) * 65535f);
            for (int i = 0; i < px.Length; i++)
            {
                // One cheap xorshift per pixel: low byte is the grey, the next 16 bits decide coverage.
                _noiseState ^= _noiseState << 13;
                _noiseState ^= _noiseState >> 17;
                _noiseState ^= _noiseState << 5;
                byte v = (byte)_noiseState;
                px[i] = new Color32(v, v, v, (byte)(((_noiseState >> 8) & 0xFFFF) < threshold ? 40 + v / 4 : 0));
            }
            _noiseTex.SetPixels32(px);
            _noiseTex.Apply(false, false);
        }

        /// <summary>Phase M: the viewer's own sound, a faint CCTV hiss while a feed shows (louder on a dead channel); none on a quiet or frozen feed.</summary>
        void UpdateHum(bool signal)
        {
            var rig = G.CameraRig;
            bool silent = rig != null && (rig.Quiet || rig.FreezeFeed);
            float want = Window.IsMinimized || silent ? 0f : signal ? FeedHum : DeadChannelHum;
            if (want == _hum) return;
            _hum = want;
            if (want > 0f) G.Audio?.PlayLoop("camera_static", want, 0.4f);
            else G.Audio?.StopLoop("camera_static", silent ? 0.03f : 0.3f);
        }

        const float FeedHum = 0.12f, DeadChannelHum = 0.4f;
        float _hum;
        bool _visible = true;
        bool _captionFlicker;
        float _echoAt = -1f;

        /// <summary>On CAM 03 your clicks come back a moment later, quiet and dull, as if the room heard them.</summary>
        void EchoClicks()
        {
            bool watchingYourself = !Window.IsMinimized && _current == ContentIds.Cam03 && G.CameraRig != null && G.CameraRig.HasSignal(_current);
            if (watchingYourself && G.Input.LeftDown) _echoAt = Time.time + UnityEngine.Random.Range(0.18f, 0.26f);
            if (_echoAt > 0f && Time.time >= _echoAt)
            {
                _echoAt = -1f;
                if (watchingYourself) G.Audio?.Play("mouse_click", 0.28f, 0.78f, UnityEngine.Random.Range(-0.15f, 0.15f));
            }
        }
        uint _noiseState = 0x9E3779B9u;

        protected override void OnClosed(CursorAgent by)
        {
            if (G.CameraRig != null) G.CameraRig.SetViewing(false);
            G.Audio?.StopLoop("camera_static", 0.2f);
        }
    }
}
