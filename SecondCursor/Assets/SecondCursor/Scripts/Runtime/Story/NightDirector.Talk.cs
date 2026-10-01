using System;
using System.Collections;
using System.Collections.Generic;
using SecondCursor.Apps;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
using SecondCursor.Core.Story;
using SecondCursor.Entity;
using SecondCursor.Input;
using SecondCursor.OS;
using SecondCursor.Rendering;
using UnityEngine;

namespace SecondCursor.Story
{
    /// <summary>
    /// Talking through Jotter: a cursor that speaks (<see cref="Speaker"/>), the lines it types, and the exchange chain that
    /// reads the player's replies (Enter sends), answers by keyword, and, for a listening chain, keeps reading until told otherwise.
    /// </summary>
    public abstract partial class NightDirector
    {
        // ------------------------------------------------------------------ talking through Notepad

        /// <summary>A cursor that talks, and the Notepad it talks in (reopened if the player closes it).</summary>
        protected sealed class Speaker
        {
            public readonly EntityController Cursor;
            public NotepadApp Pad;
            /// <summary>How the cursor moves when it goes to open its Notepad (Gary: Tired).</summary>
            public string MoveProfile = MovementProfiles.HumanLikeName;
            /// <summary>True while lines are being typed (so two routines never type into the same pad at once).</summary>
            public bool Typing;
            /// <summary>
            /// Types without moving the cursor (its Notepad is opened by its session, not by double-clicking the
            /// icon): used while the cursor's hands are busy elsewhere, e.g. Ellen talking while her brain fights.
            /// </summary>
            public bool Direct;

            public Speaker(EntityController cursor)
            {
                Cursor = cursor;
            }
        }

        /// <summary>Phase M (the scare gate): a speaker is typing a line right now.</summary>
        public bool AnySpeakerTyping => _speakers.Exists(s => s.Typing);

        /// <summary>Phase M (the scare gate): a conversation Jotter is waiting for the player's reply.</summary>
        public bool AwaitingReply => _speakers.Exists(s => s.Pad != null && s.Pad.IsOpen && s.Pad.ConversationMode && s.Pad.PlayerCanType);

        protected Speaker AddSpeaker(EntityController cursor)
        {
            var s = new Speaker(cursor);
            _speakers.Add(s);
            return s;
        }

        /// <summary>The speaker's Notepad if it is still open, else null (and forgotten).</summary>
        protected NotepadApp EnsurePad(Speaker s)
        {
            if (s.Pad != null && s.Pad.IsOpen)
            {
                ApplyIdentity(s.Pad, s.Cursor);
                return s.Pad;
            }
            s.Pad = null;
            return null;
        }

        /// <summary>
        /// Phase K (suggestion 3): a conversation Jotter says whose it is: "Session 017" (and her name once the player has seen it:
        /// Night 1's reveal, or any later night), "Session 209: Gary Pruitt", in that cursor's colours.
        /// </summary>
        protected void ApplyIdentity(NotepadApp pad, EntityController cursor)
        {
            if (pad == null || !pad.IsOpen) return;
            var c = _g.Content;
            bool gary = cursor == _g.Gary;
            string number = gary ? ContentIds.Employee209 : ContentIds.Employee017;
            bool known = gary || _g.Night > 1 || _g.Flags.Has(Flags.Staff017Revealed);
            string title = known ? c.Format("notepad.title.person", number, PersonName(number)) : c.Format("notepad.title.session", number);
            if (pad.Window.Title.StartsWith(title, StringComparison.Ordinal)) return;
            if (gary) pad.SetConversation(SystemNotices.SessionOf(_g, cursor.Agent), title, Palette.GaryFill, GaryCaptionB, Palette.GaryOutline);
            else pad.SetConversation(SystemNotices.SessionOf(_g, cursor.Agent), title, Palette.EntityFill, EllenCaptionB, Palette.EntityOutline);
        }

        static readonly Color32 GaryCaptionB = new Color32(0x7A, 0x5A, 0x1E, 0xFF), EllenCaptionB = new Color32(0x3A, 0x44, 0x42, 0xFF);

        /// <summary>"Marsh, Ellen R." in Personnel reads "Ellen Marsh" on a title.</summary>
        string PersonName(string number)
        {
            string name = _g.Content.Employee(number)?.name ?? number;
            int comma = name.IndexOf(',');
            if (comma < 0) return name;
            string first = name.Substring(comma + 1).Trim().Split(' ')[0];
            return first + " " + name.Substring(0, comma).Trim();
        }

        /// <summary>The speaker double-clicks Notepad itself (or brings its open one back) and takes the keyboard.</summary>
        protected IEnumerator OpenNotepadAs(Speaker s)
        {
            var c = s.Cursor;
            if (EnsurePad(s) != null) { s.Pad.Window.Restore(c.Agent); yield break; }
            if (s.Direct)
            {
                s.Pad = (NotepadApp)_g.Apps.Launch(AppIds.Notepad, c.Agent);
                s.Pad.ConversationMode = true;
                s.Pad.PlayerCanType = false;
                ApplyIdentity(s.Pad, c);
                yield break;
            }
            var before = new HashSet<App>(_g.Apps.OpenApps);
            yield return c.OpenApp(AppIds.Notepad, MovementProfiles.Get(s.MoveProfile));
            foreach (var app in _g.Apps.OpenApps)
                if (app is NotepadApp n && !before.Contains(app) && n.FileId == null) s.Pad = n;
            if (s.Pad == null)
                s.Pad = (NotepadApp)_g.Apps.Launch(AppIds.Notepad, c.Agent);
            s.Pad.ConversationMode = true;
            s.Pad.PlayerCanType = false;
            ApplyIdentity(s.Pad, c);
            s.Pad.Window.Focus(c.Agent);
        }

        protected IEnumerator TypeLines(Speaker s, IEnumerable<string> lines, float cps = 4.5f)
        {
            if (lines == null) yield break;
            // Another routine is typing into this pad: wait for it (never interleave two lines).
            float wait = Time.time + 20f;
            while (s.Typing && Time.time < wait) yield return null;
            // (A stopped routine never reaches the end: jumps reset the flag, and the wait above times out.)
            s.Typing = true;
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line)) continue;
                if (EnsurePad(s) == null) yield return OpenNotepadAs(s);
                if (s.Pad.Text.Length > 0 && !s.Pad.Text.EndsWith("\n")) s.Pad.Append("\n");
                if (s.Direct) yield return s.Pad.TypeAsEntity(line, cps, s.Cursor.Agent, s.Cursor.TypoRate);
                else yield return s.Cursor.Type(s.Pad, line, cps);
                yield return Wait(0.5f);
            }
            if (s.Pad != null && s.Pad.IsOpen && !s.Pad.Text.EndsWith("\n")) s.Pad.Append("\n");
            s.Typing = false;
        }

        /// <summary>M2: a keyword hit waits this long (caret blinking) before its reply; a fallback or silence much less.</summary>
        const float ThinkPauseHit = 2.0f, ThinkPauseFallback = 0.4f;
        /// <summary>How long a listening chain waits for the next line once its silence lines are typed.</summary>
        const float QuietListenSeconds = 600f;

        /// <summary>
        /// A chain of dialogue exchanges starting at <paramref name="firstExchangeId"/>: the speaker types its
        /// lines, the player may answer (Enter sends), silence counts after <paramref name="silenceSeconds"/>,
        /// and it answers by keyword. Closing the Notepad reopens it (twice, then it counts as silence).
        /// <paramref name="onReply"/> sees each reply to something the player said (flags, memory); the last
        /// reply (with its Tag) is written to <paramref name="last"/>[0]. <paramref name="turnHintKey"/>: a NEXUS
        /// notice at the first turn saying how to answer. <paramref name="fallbackRetries"/>: typed replies that match
        /// nothing get that many more turns; the miss after them is answered with <paramref name="lastFallbackSet"/>.
        /// <paramref name="keepListening"/> (Phase I): when it returns true for a reply, the speaker keeps reading and takes
        /// another line from the player (up to <paramref name="maxExtraTurns"/>) instead of going deaf: the Night 3 finale
        /// waits for "stay" however many other things are typed first, and the silence lines are typed once.
        /// </summary>
        protected IEnumerator RunExchangeChain(Speaker s, string firstExchangeId, Action<DialogueReply, string> onReply = null,
            DialogueReply[] last = null, float firstCps = 2.2f, float cps = 4f, float silenceSeconds = 25f, string reopenLine = "DONT",
            Func<ExchangeData, IEnumerable<string>> extraLines = null, string turnHintKey = null, int fallbackRetries = 0,
            string lastFallbackSet = null, Func<DialogueReply, bool> keepListening = null, int maxExtraTurns = 40)
        {
            var exchange = _g.Dialogue.Get(firstExchangeId);
            bool first = true;
            int guard = 0;
            while (exchange != null && guard++ < 6)
            {
                yield return TypeLines(s, exchange.entityLines, first ? firstCps : cps);
                // Lines some exchanges add before the player's turn (a memory of an earlier night).
                var extra = extraLines?.Invoke(exchange);
                if (extra != null) yield return TypeLines(s, extra, cps);
                // Typing back is the game's hook: say so in the OS's own voice, for the first talk of every remote session
                // (Phase I: a second session that waits for a reply used to give no prompt at all).
                string hintKey = !string.IsNullOrEmpty(turnHintKey) ? turnHintKey : "notify.jotter.reply";
                if (first && (_hintedSpeakers.Add(s) || !string.IsNullOrEmpty(turnHintKey)))
                {
                    var pad = s;
                    // Phase L: the first remote session on this save gets a tip beside its Jotter (the notice says it instead when the tip
                    // has not shown in 8 s: another tip in the way, no room beside the window); later sessions and runs that show no tips
                    // get the notice, as before.
                    bool tipped = _g.Tips.Offer("reply", () => pad.Pad != null && pad.Pad.IsOpen && !pad.Pad.Window.IsMinimized ? pad.Pad.Window.WorldRect : (Rect?)null,
                        () => pad.Pad != null && pad.Pad.IsOpen && pad.Pad.LastPlayerKeyTime < 0f, 45f);
                    if (!tipped) ShowReplyNotice(pad, hintKey);
                    else RunSide(ReplyNoticeIfNoTip(pad, hintKey), "reply-notice");
                    GameLog.Info(LogChannel.Story, "Jotter reply hint shown");
                }
                first = false;
                int misses = 0, turns = 0, silences = 0;
                DialogueReply reply;
                while (true)
                {
                    string said = null;
                    // A listening chain waits quietly after its first silence line (nothing is typed again and again).
                    yield return PlayerTurn(s, silences > 0 && keepListening != null ? QuietListenSeconds : silenceSeconds, reopenLine, line => said = line);
                    if (said == null)
                    {
                        silences++;
                        if (silences > 1 && keepListening != null)
                        {
                            if (++turns > maxExtraTurns) break;
                            continue;
                        }
                        reply = new DialogueReply { Lines = exchange.silence, Category = "silence", IsFallback = true };
                    }
                    else
                    {
                        reply = _g.Dialogue.Respond(exchange, said);
                        _g.AchievementWatch?.OnReply(exchange.voice, reply.Tag);
                        _g.Memory.Record(MemoryKind.TypedMessage, reply.Category, Time.time);
                        PlayerLines.Add(said);
                        PlayerLineMinutes.Add(_g.Clock.TotalMinutes);
                        onReply?.Invoke(reply, said);
                        // What the player typed is theirs: the log (Player.log) only gets how much, and how the exchange took it.
                        GameLog.Info(LogChannel.Player, "Typed a reply of " + said.Length + " characters (" + reply.Category + ")");
                    }
                    bool missed = said != null && reply.IsFallback;
                    if (missed) misses++;
                    // M8: after the allowed misses a chat that keeps typing junk is steered to the two words that work,
                    // and gets one more turn to type them (Phase J: a listening chain steers every later miss too).
                    bool steer = missed && fallbackRetries > 0 && !string.IsNullOrEmpty(lastFallbackSet)
                                 && (misses == fallbackRetries + 1 || (keepListening != null && misses > fallbackRetries));
                    bool again = missed && (misses <= fallbackRetries || steer);
                    if (steer)
                    {
                        var lines = _g.Content.Lines(lastFallbackSet);
                        if (lines != null && lines.Length > 0) reply.Lines = lines;
                    }
                    if (last != null && last.Length > 0) last[0] = reply;
                    yield return ThinkThenType(s, reply, cps);
                    if (again) continue;
                    if (keepListening != null && turns++ < maxExtraTurns && keepListening(reply)) continue;
                    break;
                }
                exchange = string.IsNullOrEmpty(exchange.next) ? null : _g.Dialogue.Get(exchange.next);
            }
        }

        void ShowReplyNotice(Speaker pad, string hintKey) =>
            _g.Notifications.Show(_g.Content.Text("os.name"), _g.Content.Text(hintKey), "icon_notepad",
                a => { if (pad.Pad != null && pad.Pad.IsOpen) pad.Pad.Window.Focus(a); }, "ui_select");

        IEnumerator ReplyNoticeIfNoTip(Speaker pad, string hintKey)
        {
            yield return WaitUntil(() => _g.Tips.Seen("reply"), 8f);
            // Phase L review: not for a Jotter that is gone or already answered, nor while the tip still waits its turn.
            if (pad.Pad == null || !pad.Pad.IsOpen || pad.Pad.LastPlayerKeyTime >= 0f || _g.Tips.Seen("reply") || _g.Tips.IsQueued("reply")) yield break;
            ShowReplyNotice(pad, hintKey);
        }

        /// <summary>The player's turn: waits for a sent line (passed to <paramref name="onSaid"/>) or for silence.</summary>
        IEnumerator PlayerTurn(Speaker s, float silenceSeconds, string reopenLine, Action<string> onSaid)
        {
            if (EnsurePad(s) == null) yield return OpenNotepadAs(s);
            s.Pad.PlayerCanType = true;
            s.Pad.Window.Focus(null);
            string said = null;
            Action<string, CursorAgent> handler = (line, a) => said = line;
            s.Pad.LineSubmitted += handler;
            float waitStart = Time.time;
            int reopened = 0;
            while (said == null)
            {
                // Silence: counted from the last keystroke (or since it finished typing).
                float lastActivity = Mathf.Max(waitStart, s.Pad != null ? s.Pad.LastPlayerKeyTime : 0f);
                if (Time.time - lastActivity > silenceSeconds) break;
                NudgeIfUnseen(s, lastActivity);
                if (EnsurePad(s) == null && reopened >= 2) break; // keeps closing it: treat as silence
                if (EnsurePad(s) == null)
                {
                    reopened++;
                    // Closing it doesn't make it go away.
                    yield return OpenNotepadAs(s);
                    yield return TypeLines(s, new[] { reopenLine }, 3f);
                    s.Pad.PlayerCanType = true;
                    s.Pad.LineSubmitted += handler;
                    waitStart = Time.time;
                }
                yield return null;
            }
            if (s.Pad != null) s.Pad.LineSubmitted -= handler;
            if (s.Pad != null) s.Pad.PlayerCanType = false;
            if (said != null) onSaid(said);
        }

        /// <summary>Phase N: seconds a turn waits unseen before a notice says so, and the least time between two for one speaker.</summary>
        const float NudgeAfter = 12f, NudgeEvery = 60f;
        readonly Dictionary<Speaker, float> _nudgedAt = new Dictionary<Speaker, float>();

        /// <summary>
        /// Phase N (fifth blind playtest, finding 12): a Jotter waiting for the player's reply behind other windows (or minimized) says so
        /// in a notice once it has waited a while; a click brings it forward. The tester's chat sat unseen with "Your turn" for minutes.
        /// </summary>
        void NudgeIfUnseen(Speaker s, float since)
        {
            var pad = s.Pad;
            if (pad == null || !pad.IsOpen || (pad.Window.IsActive && !pad.Window.IsMinimized) || Time.time - since < NudgeAfter) return;
            if (_nudgedAt.TryGetValue(s, out float at) && Time.time - at < NudgeEvery) return;
            _nudgedAt[s] = Time.time;
            string who = SystemNotices.SessionOf(_g, s.Cursor.Agent);
            _g.Notifications.Show(_g.Content.Text("os.name"), _g.Content.Format("notify.jotter.waiting", char.ToUpperInvariant(who[0]) + who.Substring(1)), "icon_notepad",
                a => { if (pad.IsOpen) pad.Window.Focus(a); }, "ui_select");
            GameLog.Info(LogChannel.Story, "Jotter waiting notice: " + who);
        }

        /// <summary>M2: a keyword hit gets a visible think (the caret blinks, no key taps); a miss answers quickly.</summary>
        IEnumerator ThinkThenType(Speaker s, DialogueReply reply, float cps)
        {
            bool hit = !reply.IsFallback;
            if (s.Pad != null) s.Pad.ThinkingCaret = hit;
            yield return Wait(hit ? ThinkPauseHit : ThinkPauseFallback);
            if (s.Pad != null) s.Pad.ThinkingCaret = false;
            yield return TypeLines(s, reply.Lines, cps);
        }
    }
}
