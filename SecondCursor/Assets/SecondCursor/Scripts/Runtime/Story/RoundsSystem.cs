using System;
using System.Collections;
using SecondCursor.Apps;
using SecondCursor.CameraFeed;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.Story;
using SecondCursor.Game;
using SecondCursor.Input;
using SecondCursor.OS;
using UnityEngine;

namespace SecondCursor.Story
{
    /// <summary>
    /// Custodial rounds on the Camera Viewer (expansion spec 7.4). Feeds the engine-free
    /// <see cref="CustodialRounds"/> watch meter from what the viewer shows, cuts the figure to its next stage
    /// under a burst of static, forces the viewer open on the figure's camera on schedule (Security), and
    /// counts the player's own reopens. The night's director decides what an ending round means.
    /// </summary>
    public sealed class RoundsSystem : MonoBehaviour
    {
        GameServices _g;
        float _nextRepeat;
        int _nextForced;
        Action<string, CursorAgent> _onLaunched;
        Action<OSWindow, CursorAgent> _onRestored;

        public CustodialRounds Model { get; private set; }
        public bool Running { get; private set; }
        public float Elapsed { get; private set; }
        public int ForcedOpens { get; private set; }
        public int PlayerReopens { get; private set; }
        /// <summary>Who performs forced opens (null = Security, no cursor).</summary>
        [NonSerialized] public CursorAgent ForcedBy;

        /// <summary>Security opened the viewer on the figure: the index of this forced open (0 = the first).</summary>
        public event Action<int> ForcedOpen;
        /// <summary>The player opened or restored the viewer themselves.</summary>
        public event Action PlayerReopened;
        public event Action<int> StageAdvanced;
        public event Action ReachedFinal;
        public event Action SeatCleared;
        /// <summary>The round's time ran out (not raised by <see cref="Stop"/>).</summary>
        public event Action TimeUp;

        public static RoundsSystem Create(GameServices g, Transform parent)
        {
            var go = new GameObject("Custodial Rounds");
            go.transform.SetParent(parent, false);
            var r = go.AddComponent<RoundsSystem>();
            r._g = g;
            return r;
        }

        /// <summary>The viewer is open, not minimized, and shows the figure's current camera.</summary>
        public bool IsFigureOnShownCamera => Running && Model != null && !Model.Finished && ViewedCamera() == Model.FigureCamera;

        /// <summary>How long the second cursor waits before closing a viewer that shows the figure.</summary>
        public float CloseReaction() => Model != null ? Model.Config.CloseReaction(UnityEngine.Random.value) : 1.5f;

        public void Begin(RoundsConfig config)
        {
            Stop();
            Model = new CustodialRounds(config);
            Model.StageAdvanced += OnStageAdvanced;
            Model.ReachedFinal += () => ReachedFinal?.Invoke();
            Model.SeatCleared += () => SeatCleared?.Invoke();
            Running = true;
            Elapsed = 0f;
            ForcedOpens = 0;
            PlayerReopens = 0;
            _nextForced = 0;
            _nextRepeat = -1f;
            _onLaunched = (appId, a) => { if (appId == AppIds.Camera && a != null && a.IsPlayer) OnPlayerReopen(); };
            _onRestored = (w, a) => { if (w.AppId == AppIds.Camera && a != null && a.IsPlayer) OnPlayerReopen(); };
            _g.Apps.Launched += _onLaunched;
            _g.Windows.Restored += _onRestored;
            PlaceFigure(false);
            GameLog.Info(LogChannel.Story, "Rounds: begin " + config.Id + " at stage " + Model.Stage + " (" + Model.FigureStage + ")");
        }

        public void Stop()
        {
            if (_onLaunched != null) _g.Apps.Launched -= _onLaunched;
            if (_onRestored != null) _g.Windows.Restored -= _onRestored;
            _onLaunched = null;
            _onRestored = null;
            if (Running) GameLog.Info(LogChannel.Story, "Rounds: stopped after " + Elapsed.ToString("0") + "s at stage " + Model.Stage);
            Running = false;
        }

        /// <summary>Debug: put the figure on a stage now.</summary>
        public void ForceStage(int stage)
        {
            if (Model == null) return;
            Model.ForceStage(stage);
        }

        void Update()
        {
            if (!Running || Model == null) return;
            float dt = Time.deltaTime;
            Elapsed += dt;
            var c = Model.Config;
            if (_nextForced < c.ForcedOpenTimes.Length && Elapsed >= c.ForcedOpenTimes[_nextForced])
            {
                _nextForced++;
                OpenViewer();
                if (_nextForced >= c.ForcedOpenTimes.Length && c.ForcedOpenRepeatMax > 0f)
                    _nextRepeat = Elapsed + UnityEngine.Random.Range(c.ForcedOpenRepeatMin, c.ForcedOpenRepeatMax);
            }
            else if (_nextRepeat > 0f && Elapsed >= _nextRepeat)
            {
                OpenViewer();
                _nextRepeat = Elapsed + UnityEngine.Random.Range(c.ForcedOpenRepeatMin, c.ForcedOpenRepeatMax);
            }
            Model.Tick(dt, ViewedCamera());
            if (Running && c.Duration > 0f && Elapsed >= c.Duration && !Model.Finished)
            {
                Stop();
                TimeUp?.Invoke();
            }
        }

        /// <summary>The camera the viewer shows right now, or null when it is closed or minimized.</summary>
        public string ViewedCamera()
        {
            var cam = _g.Apps.Find<CameraApp>();
            return cam != null && cam.IsOpen && !cam.Window.IsMinimized ? cam.CurrentCamera : null;
        }

        /// <summary>Security (or <see cref="ForcedBy"/>) opens or restores the viewer on the figure's camera.</summary>
        public void OpenViewer()
        {
            if (Model == null || Model.Finished) return;
            var cam = _g.Apps.Find<CameraApp>();
            if (cam == null) cam = _g.Apps.Launch(AppIds.Camera, ForcedBy) as CameraApp;
            else cam.Window.Restore(ForcedBy);
            if (cam == null) return;
            cam.Select(Model.FigureCamera, ForcedBy);
            int index = ForcedOpens++;
            var text = _g.Content;
            _g.Notifications.Show(text.Text("app.camera"), text.Text(index == 0 ? "rounds.begin" : "rounds.reopen"), "icon_camera", null, "sys_warning");
            GameLog.Info(LogChannel.Story, "Rounds: viewer forced open (" + (index + 1) + ") on " + Model.FigureCamera);
            ForcedOpen?.Invoke(index);
        }

        void OnPlayerReopen()
        {
            if (!Running || Model == null) return;
            PlayerReopens++;
            // Looking again brings it closer: the penalty lands on whatever camera the viewer came back on.
            Model.NotifyReopen(ViewedCamera());
            PlayerReopened?.Invoke();
        }

        void OnStageAdvanced(int stage)
        {
            GameLog.Info(LogChannel.Story, "Rounds: stage " + stage + " (" + Model.FigureStage + ")");
            _g.Audio?.Play("footstep_distant", 0.45f, 0.9f, 0.2f);
            StageAdvanced?.Invoke(stage);
            PlaceFigure(true);
        }

        /// <summary>Put the figure where the model says; on screen it moves under a burst of static.</summary>
        void PlaceFigure(bool cut)
        {
            var rig = _g.CameraRig;
            if (rig == null || Model == null) return;
            var stage = StageFor(Model.FigureStage);
            if (!cut || ViewedCamera() == null) { rig.Figure = stage; return; }
            StartCoroutine(StaticCut(() => rig.Figure = stage));
        }

        public static FigureStage StageFor(string name) =>
            Enum.TryParse(name, out FigureStage s) ? s : FigureStage.None;

        IEnumerator StaticCut(Action change)
        {
            var rig = _g.CameraRig;
            _g.Audio?.Play("camera_static", 0.6f);
            float t = 0f;
            bool changed = false;
            while (t < 0.45f)
            {
                t += Time.deltaTime;
                rig.ExtraNoise = 0.9f;
                if (!changed && t > 0.15f) { change(); changed = true; }
                yield return null;
            }
            if (!changed) change();
            rig.ExtraNoise = 0f;
        }

        void OnDestroy()
        {
            if (_g != null && _g.Apps != null) Stop();
        }
    }
}
