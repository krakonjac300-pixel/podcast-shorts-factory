using System.Collections.Generic;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
using SecondCursor.Core.Story;
using SecondCursor.Input;
using SecondCursor.OS;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;

namespace SecondCursor.Apps
{
    /// <summary>
    /// System Monitor (the brief's TaskManagerSimulator): running processes and their load. Once the second
    /// cursor has shown itself, a process owned by another user appears, and its CPU tracks what the
    /// entity is doing. It cannot be ended.
    /// </summary>
    public sealed class SystemMonitorApp : App
    {
        public const string Id = "sysmon";
        const string GhostProcess = "remote_session.exe";

        ListView _list;
        PixelText _status;
        UiButton _end;
        float _refresh;
        int _endAttempts;
        readonly Dictionary<string, float> _cpu = new Dictionary<string, float>();

        public override string AppId => Id;

        public override void Open(Rect? zoomFrom, CursorAgent by)
        {
            CreateWindow("System Monitor", "icon_system", 330, 90, 380, 280, WindowFlags.Standard, zoomFrom);
            _list = new ListView(Window.Client, "Processes", new[] { 150, 90, 50, 60 }, new[] { "Process", "User", "CPU", "Memory" }, false);
            _list.Root.Stretch(2, 2, 2, 50);
            _end = UiButton.Create(Window.Client, "End Process", a => EndProcess(a), "button:End Process");
            ((RectTransform)_end.transform).BottomRight(4, 24, 96, 22);
            var status = UIBuilder.Bevel(Window.Client, BevelStyle.StatusField, "Status");
            status.rectTransform.BottomStrip(0, 18, 2, 2);
            _status = UIBuilder.Text(status.rectTransform, "", Palette.Text);
            _status.rectTransform.Stretch(4, 0, 4, 0);
            _status.VAlign = TextVAlign.Middle;
            Refresh();
        }

        IEnumerable<(string name, string user, float baseCpu, string mem)> Processes()
        {
            string me = G.Content.Text("login.username");
            yield return ("nexus_kernel.sys", "SYSTEM", 2f, "412 K");
            yield return ("shell.exe", me, 1f, "1,204 K");
            yield return ("input.drv", "SYSTEM", 0.5f, "96 K");
            yield return ("reclaim_svc.exe", "SYSTEM", 6f, "3,880 K");
            foreach (var app in G.Apps.OpenApps)
                if (app.IsOpen) yield return (app.AppId + ".exe", me, 1.5f, "640 K");
            if (G.Flags.Has(Flags.EntitySeen)) yield return (GhostProcess, "017", 0f, "????");
        }

        readonly List<string> _shownNames = new List<string>();

        void Refresh()
        {
            var procs = new List<(string name, string user, float baseCpu, string mem)>(Processes());
            bool sameSet = procs.Count == _shownNames.Count;
            for (int i = 0; sameSet && i < procs.Count; i++) sameSet = procs[i].name == _shownNames[i];

            float total = 0f;
            var cpus = new float[procs.Count];
            for (int i = 0; i < procs.Count; i++)
            {
                var p = procs[i];
                float cpu;
                if (p.name == GhostProcess)
                {
                    // Tracks the entity: idle when it hides, spikes when it acts.
                    var e = G.Entity;
                    cpu = e.IsVisible ? Mathf.Clamp(8f + e.Agent.Velocity.magnitude / 40f + (G.Conflict.IsFighting ? 45f : 0f), 3f, 97f) : Random.Range(0f, 2f);
                }
                else
                {
                    _cpu.TryGetValue(p.name, out var prev);
                    cpu = Mathf.Lerp(prev, p.baseCpu + Random.Range(-0.8f, 1.2f), 0.5f);
                }
                cpus[i] = Mathf.Max(0f, cpu);
                _cpu[p.name] = cpus[i];
                total += cpus[i];
            }

            if (sameSet)
            {
                // Update the numbers in place so clicks and selection are never disturbed.
                for (int i = 0; i < procs.Count && i < _list.Rows.Count; i++)
                    if (_list.Rows[i].Columns.Count > 2) _list.Rows[i].Columns[2].text = Mathf.RoundToInt(cpus[i]) + "%";
            }
            else
            {
                string sel = _list.Selected?.Tag as string;
                _list.Clear();
                _shownNames.Clear();
                for (int i = 0; i < procs.Count; i++)
                {
                    var p = procs[i];
                    _list.AddRow(null, p.name, "process:" + p.name, p.name, p.user, Mathf.RoundToInt(cpus[i]) + "%", p.mem);
                    _shownNames.Add(p.name);
                }
                if (sel != null) _list.SelectWhere(r => (string)r.Tag == sel, null);
            }
            _status.text = procs.Count + " processes   CPU " + Mathf.Clamp(Mathf.RoundToInt(total), 0, 100) + "%";
        }

        void EndProcess(CursorAgent a)
        {
            string sel = _list.Selected?.Tag as string;
            if (sel == null) return;
            if (sel == GhostProcess)
            {
                _endAttempts++;
                G.Memory.Record(MemoryKind.ResistedEntity, "process", Time.time);
                G.Flags.Increment("tried_to_kill_process");
                string msg = _endAttempts < 3 ? "Unable to end " + GhostProcess + ".\nAccess is denied." : "The process is not running.";
                Dialogs.Message(G, "System Monitor", msg, "icon_error", new[] { "OK" }, null);
                if (_endAttempts >= 3) G.Fx.Glitch(0.2f, 0.8f);
                return;
            }
            Dialogs.Message(G, "System Monitor", "Ending system processes may make the workstation unstable.\nContact IT Services.", "icon_warning", new[] { "OK" }, null);
        }

        public override void Tick(float dt)
        {
            _refresh -= dt;
            if (_refresh > 0f) return;
            _refresh = 1f;
            Refresh();
        }
    }
}
