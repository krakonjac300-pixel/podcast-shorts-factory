using System;
using System.Collections.Generic;
using SecondCursor.Apps;
using SecondCursor.Core;
using SecondCursor.Core.Audio;
using SecondCursor.Core.Story;
using SecondCursor.Game;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SecondCursor.Story
{
    /// <summary>
    /// Phase M: plays every ambient scare, rarely. The directors name a moment (<see cref="Slot"/>); the pool fills long quiet
    /// stretches "from time to time". Both go through <see cref="ScareRules.Check"/> (the night's budget, a global cooldown,
    /// never over a tug, a dialog, typing, a reply being waited for, a file on the pointer, a tip or the tutorial). A slot that
    /// never finds the gate open inside its window is skipped and costs nothing. Each scare ducks the room 6 dB while it plays.
    /// </summary>
    public sealed class ScareScheduler : MonoBehaviour
    {
        sealed class Pending
        {
            public string Id;
            public float Volume, At, Until;
            public Func<float> Pan;
            public Action OnFire;
            public ScareGate Ignore, LastBlock;
        }

        /// <summary>Seconds a pool pick keeps waiting for the gate before it is rescheduled; seconds an armed click answer waits for a click.</summary>
        const float PoolWindow = 20f, ClickAnswerWindow = 20f;

        GameServices _g;
        ScareNight _night;
        readonly List<Pending> _slots = new List<Pending>();
        readonly List<string> _skips = new List<string>();
        float _lastScare = -1000f, _nextPool = -1f, _poolUntil = -1f, _duckUntil = -1f;
        int _played, _poolPlayed;
        string _lastPool;
        /// <summary>A click answer is armed (and from the pool): it counts when it plays, and is skipped if no click comes in time.</summary>
        bool _answerPending, _answerPool;
        ScareGate _poolBlock;

        /// <summary>Set by a climax while it runs (nothing ambient plays; the stinger's quiet time follows).</summary>
        public bool ClimaxRunning;
        /// <summary>Night 3's finale: its own, shorter cooldown.</summary>
        public bool Finale;

        public static ScareScheduler Create(GameServices g, Transform parent)
        {
            var go = new GameObject("Scares");
            go.transform.SetParent(parent, false);
            var s = go.AddComponent<ScareScheduler>();
            s._g = g;
            s._night = ScareRules.For(g.Night);
            // Review M7: the answer is checked like any scare when the click comes (the budget, typing, the ending too).
            g.Audio.ClickAnswerAllowed = () => !PauseMenu.IsPaused && !s.ClimaxRunning && !(g.Conflict != null && g.Conflict.IsFighting) && !s.DialogOpen()
                && s._played < s._night.Budget && !g.Flags.Has(Flags.Ending) && !(g.Director != null && g.Director.AnySpeakerTyping) && Time.time - g.Audio.LastKeyAt >= 1.5f;
            g.Audio.ClickAnswered += s.OnClickAnswered;
            return s;
        }

        /// <summary>
        /// Try <paramref name="id"/> <paramref name="delay"/> seconds from now, then every frame for up to <paramref name="window"/>
        /// seconds until the gate is open (<paramref name="ignore"/>: gates this moment does not care about; <paramref name="onFire"/>: what
        /// the story does with it when it plays).
        /// </summary>
        public void Slot(string id, float volume, Func<float> pan, float delay, float window, ScareGate ignore = ScareGate.None, Action onFire = null)
        {
            float now = Time.time;
            _slots.Add(new Pending { Id = id, Volume = volume, Pan = pan, At = now + delay, Until = now + delay + window, Ignore = ignore, OnFire = onFire });
        }

        public void Slot(string id, float volume, float pan, float delay, float window, ScareGate ignore = ScareGate.None) =>
            Slot(id, volume, () => pan, delay, window, ignore);

        /// <summary>Drops every waiting slot and the pool's next pick (a climax or an ending is coming), and gives the room back its level.</summary>
        public void CancelAll()
        {
            foreach (var s in _slots) GameLog.Info(LogChannel.Audio, "Scare cancelled: " + s.Id);
            _slots.Clear();
            _nextPool = -1f;
            _g.Audio.ArmClickAnswer(0f);
            _answerPending = false;
            if (_duckUntil > 0f) _g.Audio.SetAmbienceLevel(1f, 0.3f);
            _duckUntil = -1f;
        }

        void Update()
        {
            var d = _g.Director;
            if (d == null || string.IsNullOrEmpty(d.CurrentBeat)) return;
            float now = Time.time;
            if (_duckUntil > 0f && now >= _duckUntil)
            {
                _duckUntil = -1f;
                _g.Audio.SetAmbienceLevel(1f, 1.5f);
            }
            if (_answerPending && !_g.Audio.ClickAnswerArmed)
            {
                _answerPending = false;
                Skipped("click_wrong", ScareGate.None);
            }
            for (int i = _slots.Count - 1; i >= 0; i--)
            {
                var s = _slots[i];
                if (now < s.At) continue;
                if (now > s.Until)
                {
                    Skipped(s.Id, s.LastBlock);
                    _slots.RemoveAt(i);
                    continue;
                }
                var block = ScareRules.Check(Context(), _night, false, s.Ignore);
                if (block != ScareGate.None)
                {
                    s.LastBlock = block;
                    continue;
                }
                _slots.RemoveAt(i);
                Fire(s.Id, s.Volume, s.Pan(), false);
                s.OnFire?.Invoke();
            }
            TickPool(d.CurrentBeat, now);
        }

        void TickPool(string beat, float now)
        {
            if (_night.Pool.Length == 0 || !ScareRules.PoolBeat(_night, beat))
            {
                _nextPool = -1f;
                return;
            }
            if (_nextPool < 0f)
            {
                _nextPool = now + ScareRules.PoolDelay(_night, Random.value);
                _poolUntil = _nextPool + PoolWindow;
                return;
            }
            if (now < _nextPool) return;
            var block = ScareRules.Check(Context(), _night, true);
            if (block == ScareGate.None)
            {
                var p = ScareRules.PickPool(_night, Random.value, _lastPool);
                _lastPool = p.Id;
                Fire(p.Id, p.Volume, PanFor(p.Pan), true);
            }
            else if (now < _poolUntil && block != ScareGate.Budget)
            {
                _poolBlock = block;
                return;
            }
            else Skipped("pool", block == ScareGate.Budget ? block : _poolBlock);
            _nextPool = block == ScareGate.Budget ? float.MaxValue : now + ScareRules.PoolDelay(_night, Random.value);
            _poolUntil = _nextPool + PoolWindow;
        }

        float PanFor(ScarePan pan)
        {
            switch (pan)
            {
                case ScarePan.Cursor: return Audio.AudioManager.PanFor(_g.Player.Position.x);
                case ScarePan.OneEar: return Random.value < 0.5f ? -0.6f : 0.6f;
                default: return 0f;
            }
        }

        void Fire(string id, float volume, float pan, bool pool)
        {
            if (id == "click_wrong")
            {
                // It answers the player's next click instead of playing now (counted then, OnClickAnswered).
                _g.Audio.ArmClickAnswer(ClickAnswerWindow);
                _answerPending = true;
                _answerPool = pool;
                GameLog.Info(LogChannel.Audio, "Scare: click_wrong armed for the next click");
                return;
            }
            Count(pool);
            _g.Audio.Play(id, volume, 1f, pan);
            GameLog.Info(LogChannel.Audio, "Scare: " + id + " v=" + volume.ToString("0.00") + CountText(pool));
            // The room ducks 6 dB under it, so a quiet sound is heard without being loud.
            if (_g.Audio.IsLoopPlaying("amb_room"))
            {
                _g.Audio.SetAmbienceLevel(0.5f, 0.3f);
                _duckUntil = Time.time + _g.Audio.Length(id) + 0.5f;
            }
        }

        void Count(bool pool)
        {
            _played++;
            if (pool) _poolPlayed++;
            _lastScare = Time.time;
        }

        string CountText(bool pool) => " (" + _played + "/" + _night.Budget + (pool ? ", pool" : "") + ")";

        void OnClickAnswered()
        {
            _answerPending = false;
            Count(_answerPool);
            GameLog.Info(LogChannel.Audio, "Scare: click_wrong answered the player's click" + CountText(_answerPool));
        }

        void Skipped(string id, ScareGate why)
        {
            string line = id + " (" + (why == ScareGate.None ? "no click in time" : why.ToString().ToLowerInvariant()) + ")";
            GameLog.Info(LogChannel.Audio, "Scare skipped: " + line);
            _skips.Add(line);
            if (_skips.Count > 5) _skips.RemoveAt(0);
        }

        ScareContext Context()
        {
            var g = _g;
            var d = g.Director;
            float now = Time.time;
            return new ScareContext
            {
                Paused = PauseMenu.IsPaused,
                Climax = ClimaxRunning || g.Flags.Has(Flags.Ending),
                Tug = g.Conflict != null && g.Conflict.IsFighting,
                Dialog = DialogOpen(),
                Typing = d.AnySpeakerTyping || now - g.Audio.LastKeyAt < 1.5f,
                AwaitingReply = d.AwaitingReply,
                Dragging = g.Player.Payload != null,
                TipShowing = g.Tips != null && g.Tips.Busy,
                TutorialOpen = g.Night == 1 && !g.Flags.Has(Flags.TutorialDone),
                WatchingSelf = g.Night == 1 && WatchingSelf(),
                Finale = Finale,
                NightSeconds = d.NightElapsed,
                BeatSeconds = now - d.BeatStartedAt,
                SinceScare = now - _lastScare,
                SinceStinger = now - g.Audio.LastStingerAt,
                SinceEvent = now - g.Audio.LastEventAt,
                Played = _played,
                PoolPlayed = _poolPlayed,
            };
        }

        bool DialogOpen()
        {
            if (_g.Shred.Busy) return true;
            foreach (var w in _g.Windows.Windows)
                if (!w.IsClosed && !w.IsMinimized && (w.AppId == "dialog" || w.AppId == "progress")) return true;
            return false;
        }

        /// <summary>M1: the player is waving at themselves on CAM 03 and the door is still shut (a scare would kill the "no.").</summary>
        bool WatchingSelf()
        {
            var cam = _g.Apps.Find<CameraApp>();
            return cam != null && cam.IsShowing(Core.Content.ContentIds.Cam03) && _g.CameraRig != null && _g.CameraRig.DoorOpen < 0.05f;
        }

        /// <summary>The bridge's scares line: budget used and left, cooldown left, waiting slots, the last skips.</summary>
        internal string Describe()
        {
            float cooldown = (Finale ? _night.FinaleCooldown : _night.Cooldown) - (Time.time - _lastScare);
            return "night " + _night.Night + ": played " + _played + "/" + _night.Budget + " (pool " + _poolPlayed + "/" + _night.PoolLimit + "), cooldown left "
                + Mathf.Max(0f, cooldown).ToString("0") + " s, waiting " + _slots.Count + ", next pool "
                + (_nextPool < 0f ? "-" : _nextPool > 1e6f ? "never" : (_nextPool - Time.time).ToString("0") + " s")
                + (ClimaxRunning ? ", climax" : "") + ", last skips: " + (_skips.Count > 0 ? string.Join("; ", _skips) : "none");
        }

        /// <summary>Debug (bridge scareforce): plays a scare now with the gate bypassed.</summary>
        internal void Force(string id) => Fire(id, 0.6f, 0f, false);
    }
}
