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
        RawImage _noise;
        Texture2D _noiseTex;
        PixelText _label;
        PixelText _time;
        Image _rec;
        PixelText _noSignal;
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

            var side = UIBuilder.Rect("Cameras", client).At(2, 2, 110, FeedH + 26);
            int y = 0;
            foreach (var cam in G.Content.Story.cameras)
            {
                string id = cam.id;
                var b = UiButton.Create(side, cam.label.Length > 16 ? cam.label.Substring(0, 16) : cam.label, a => Select(id, a), "camera:" + id);
                b.Label.Align = TextAlign.Left;
                b.Label.rectTransform.Stretch(4, 0, 2, 0);
                ((RectTransform)b.transform).At(0, y, 110, 22);
                _buttons[id] = b;
                y += 25;
            }

            var screen = UIBuilder.Bevel(client, BevelStyle.Sunken, "Monitor");
            screen.rectTransform.At(116, 2, FeedW + 4, FeedH + 4);
            var black = UIBuilder.Solid(screen.rectTransform, Color.black, "Black");
            black.rectTransform.Stretch(2, 2, 2, 2);
            _feed = UIBuilder.Raw(screen.rectTransform, G.CameraRig != null ? G.CameraRig.Feed : null, "Feed");
            _feed.rectTransform.Stretch(2, 2, 2, 2);
            _feed.color = new Color(0.86f, 0.93f, 0.88f, 1f);

            _noiseTex = new Texture2D(80, 60, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
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
            var recHolder = UIBuilder.Rect("REC", screen.rectTransform).TopRight(10, 8, 40, 12);
            _rec = UIBuilder.Icon(recHolder, "rec_dot", 1);
            _rec.rectTransform.anchoredPosition = new Vector2(0f, -3f);
            var recText = UIBuilder.Text(recHolder, "REC", Palette.BiosBright, true);
            recText.Shadow = true;
            recText.rectTransform.Stretch(9, 0, 0, 0);

            Select(G.CameraRig != null ? (G.CameraRig.ActiveCamera ?? ContentIds.Cam01) : ContentIds.Cam01, by);
            if (G.CameraRig != null) G.CameraRig.SetViewing(true);
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
            CameraSelected?.Invoke(camId, by);
        }

        public override void Tick(float dt)
        {
            _t += dt;
            _switchNoise = Mathf.Max(0f, _switchNoise - dt);
            bool signal = G.CameraRig != null && G.CameraRig.HasSignal(_current);
            _feed.enabled = signal;
            _noSignal.enabled = !signal;
            _time.text = G.Clock.FormatCamera();
            _rec.enabled = signal && (_t % 1.2f) < 0.7f;

            // Animated grain: stronger on dead channels and right after switching.
            float amount = !signal ? 0.9f : Mathf.Max(0.12f, _switchNoise * 3f) + (G.CameraRig != null ? G.CameraRig.ExtraNoise : 0f);
            if (G.CameraRig != null && _feed.texture != G.CameraRig.Feed) _feed.texture = G.CameraRig.Feed;
            if (_noisePixels == null) _noisePixels = new Color32[_noiseTex.width * _noiseTex.height];
            var px = _noisePixels;
            for (int i = 0; i < px.Length; i++)
            {
                byte v = (byte)UnityEngine.Random.Range(0, 256);
                px[i] = new Color32(v, v, v, (byte)(UnityEngine.Random.value < amount ? 70 + v / 3 : 0));
            }
            _noiseTex.SetPixels32(px);
            _noiseTex.Apply(false, false);
        }

        protected override void OnClosed(CursorAgent by)
        {
            if (G.CameraRig != null) G.CameraRig.SetViewing(false);
            if (_noiseTex != null) UnityEngine.Object.Destroy(_noiseTex);
        }
    }
}
