using System;
using System.Collections.Generic;
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
        RectTransform _side;
        bool _hiddenShown;
        readonly Dictionary<string, UiButton> _buttons = new Dictionary<string, UiButton>();
        string _current;
        float _t;
        float _switchNoise;
        Color32[] _noisePixels;

        public override string AppId => AppIds.Camera;
        public string CurrentCamera => _current;
        public event Action<string, CursorAgent> CameraSelected;

        public override void Open(Rect? zoomFrom, CursorAgent by)
        {
            CreateWindow(G.Content.Text("app.camera"), "icon_camera", 90, 40, FeedW + 128, FeedH + 60, WindowFlags.CanClose | WindowFlags.CanMinimize, zoomFrom);
            var client = Window.Client;

            _side = UIBuilder.Rect("Cameras", client).At(2, 2, 110, FeedH + 26);
            BuildButtons();

            var screen = UIBuilder.Bevel(client, BevelStyle.Sunken, "Monitor");
            screen.rectTransform.At(116, 2, FeedW + 4, FeedH + 4);
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
            var recHolder = UIBuilder.Rect("REC", screen.rectTransform).TopRight(10, 8, 40, 12);
            _rec = UIBuilder.Icon(recHolder, "rec_dot", 1);
            _rec.rectTransform.anchoredPosition = new Vector2(0f, -3f);
            var recText = UIBuilder.Text(recHolder, "REC", Palette.BiosBright, true);
            recText.Shadow = true;
            recText.rectTransform.Stretch(9, 0, 0, 0);

            Select(G.CameraRig != null ? (G.CameraRig.ActiveCamera ?? ContentIds.Cam01) : ContentIds.Cam01, by);
            if (G.CameraRig != null) G.CameraRig.SetViewing(true);
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

        public void Select(string camId, CursorAgent by)
        {
            _current = camId;
            foreach (var kv in _buttons) kv.Value.Toggled = kv.Key == camId;
            var cam = G.Content.Camera(camId);
            _label.text = cam != null ? cam.label : camId;
            _switchNoise = 0.25f;
            Sfx.Play("camera_switch", by);
            if (G.CameraRig != null) G.CameraRig.SetCamera(camId);
            if (camId == ContentIds.Cam00 && by != null && by.IsPlayer && !G.Flags.Has(Core.Story.Flags.N3Cam00Viewed))
            {
                // Watch the Watchers: the player found CAM 00 (AchievementWatcher listens for this flag).
                G.Flags.Set(Core.Story.Flags.N3Cam00Viewed);
            }
            CameraSelected?.Invoke(camId, by);
        }

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
                // M10: a shelf label swap is a one-frame static flicker.
                if (caption.Length > 0 && _caption.text.Length > 0) _captionFlickerFrame = Time.frameCount;
                _caption.text = caption;
                string next = caption.Length > 0 && G.CameraRig != null ? G.CameraRig.NextShelfFor(_current) : "";
                _next.text = next.Length > 0 ? string.Format(G.Content.Text("camera.next", "NEXT: {0}"), next) : "";
            }
            _switchNoise = Mathf.Max(0f, _switchNoise - dt);
            bool signal = G.CameraRig != null && G.CameraRig.HasSignal(_current);
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
            if (Time.frameCount == _captionFlickerFrame) amount = Mathf.Max(amount, 0.6f);
            if (G.CameraRig != null && _feed.texture != G.CameraRig.Feed) _feed.texture = G.CameraRig.Feed;
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
        int _captionFlickerFrame = -1;
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
