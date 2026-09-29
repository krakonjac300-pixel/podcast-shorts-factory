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
        /// <summary>Night 3: Personnel follows the figure (spec 5.6).</summary>
        [NonSerialized] public bool PatchPersonnel;
        /// <summary>
        /// Called instead of opening the viewer itself when set (finished Gary opens it by hand); the round passes
        /// the camera to show. The handler must end up calling <see cref="ShowOnViewer"/>.
        /// </summary>
        [NonSerialized] public Action<string> ForcedOpenHandler;

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
            if (PatchPersonnel) PatchPersonnelFor(Model.FigureStage);
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
        public void OpenViewer() => OpenViewer(null);

        /// <summary>A forced open on a given camera (null = the figure's camera).</summary>
        public void OpenViewer(string camera)
        {
            if (Model == null || Model.Finished) return;
            string cam = string.IsNullOrEmpty(camera) ? Model.FigureCamera : camera;
            if (ForcedOpenHandler != null)
            {
                ForcedOpenHandler(cam);
                return;
            }
            ShowOnViewer(cam, ForcedBy);
        }

        /// <summary>The viewer comes up (launched or restored) on <paramref name="camera"/>: counted and announced as a forced open.</summary>
        public void ShowOnViewer(string camera, CursorAgent by)
        {
            if (Model == null || Model.Finished) return;
            var cam = _g.Apps.Find<CameraApp>();
            if (cam == null) cam = _g.Apps.Launch(AppIds.Camera, by) as CameraApp;
            else cam.Window.Restore(by);
            if (cam == null) return;
            if (cam.CurrentCamera != camera) cam.Select(string.IsNullOrEmpty(camera) ? Model.FigureCamera : camera, by);
            int index = ForcedOpens++;
            var text = _g.Content;
            _g.Notifications.Show(text.Text("app.camera"), text.Text(index == 0 ? "rounds.begin" : "rounds.reopen"), "icon_camera", null, "sys_warning");
            GameLog.Info(LogChannel.Story, "Rounds: viewer forced open (" + (index + 1) + ") on " + cam.CurrentCamera);
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
            if (PatchPersonnel) PatchPersonnelFor(Model.FigureStage);
        }

        /// <summary>
        /// Night 3's live Personnel (spec 5.6): Custodial's office follows the figure, 001's last login copies
        /// 000's, and Ruth goes on leave when it reaches the B-Level hall. The records are patched in memory
        /// (the content database is rebuilt every shift) and an open Personnel window shows them at once.
        /// </summary>
        public void PatchPersonnelFor(string stage)
        {
            var c = _g.Content;
            var custodial = c.Employee(ContentIds.Employee000);
            if (custodial == null) return;
            string office = OfficeFor(stage);
            if (office == null) return;
            custodial.office = office;
            custodial.lastLogin = "11/20/98 3:00 AM (on rounds)";
            var twin = c.Employee(ContentIds.Employee001);
            if (twin != null) twin.lastLogin = custodial.lastLogin;
            if (stage == "HallFar")
            {
                var ruth = c.Employee(ContentIds.Employee118);
                if (ruth != null && ruth.status != "ON LEAVE")
                {
                    ruth.status = "ON LEAVE";
                    ruth.notes = "Extended leave from 11/20/98. Do not forward calls. Personal effects held by Custodial.";
                    GameLog.Info(LogChannel.Story, "Personnel: 118 on leave");
                }
            }
            _g.Apps.Find<StaffApp>()?.Refresh();
        }

        /// <summary>Custodial's office as Personnel lists it at each stage.</summary>
        public static string OfficeFor(string stage)
        {
            switch (stage)
            {
                case "SublevelC": return "Sublevel C";
                case "Lobby": return "Lobby";
                case "HallFar": return "B-Level hall";
                case "Corridor": return "B-Level hall (B-7)";
                case "Doorway":
                case "Middle": return "B-7";
                case "BehindChair": return "B-7 (WS-04)";
                default: return null;
            }
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
