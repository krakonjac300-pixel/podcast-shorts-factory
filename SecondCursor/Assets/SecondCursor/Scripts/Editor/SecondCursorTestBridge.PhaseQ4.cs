using System.Collections;
using System.Globalization;
using System.Linq;
using SecondCursor.Core.Game;
using SecondCursor.Game;
using UnityEngine;

namespace SecondCursor.EditorTools
{
    /// <summary>Phase Q4 bridge commands: test notices of every kind, a sound by id (for captions), and the access options as the running game sees them.</summary>
    public static partial class SecondCursorTestBridge
    {
        const string PhaseQ4Help =
            "Phase Q4: notice KIND TEXT (plain|entity|gary|deadline|sticky: a test notice; _ is a space) | sound ID [VOLUME] [PAN] (play a sound now: captions) | access (the options as the game sees them)\n" +
            "          noticelog (the Recent notices list) | settingsset noticeTime|relaxed|captions|sudden|shake|mono|bigCursor|clickSpeed VALUE\n";

        static IEnumerator TryGameQ4Command(GameServices g, string cmd, string[] a, string rest)
        {
            switch (cmd)
            {
                case "notice":
                {
                    string kindName = a.Length > 1 ? a[1].ToLowerInvariant() : "plain";
                    string text = a.Length > 2 ? string.Join(" ", a.Skip(2)).Replace('_', ' ') : "A test notice that is long enough to wrap onto a second line.";
                    var kind = kindName == "entity" ? NoticeKind.Entity : kindName == "gary" ? NoticeKind.Gary : kindName == "deadline" ? NoticeKind.Deadline : NoticeKind.Plain;
                    bool sticky = kindName == "sticky";
                    g.Notifications.Show("NEXUS OS", text, "icon_info", null, "ui_select", sticky, null, kind);
                    Say("notice " + kind + ": " + text);
                    return Done();
                }
                case "sound":
                    g.Audio.Play(a.Length > 1 ? a[1] : "knock_door", F(a, 2, 1f), 1f, F(a, 3, 0f));
                    Say("played " + (a.Length > 1 ? a[1] : "knock_door"));
                    return Done();
                case "access":
                    Say("access noticeTime=" + AccessSettings.NoticeTime + " relaxed=" + AccessSettings.RelaxedTimingOn + " timeScale=" + g.TimeScale.ToString("0.0", CultureInfo.InvariantCulture)
                        + " captions=" + AccessSettings.Captions + " softSounds=" + AccessSettings.SoftSounds(g.Fx.ReduceFlashing) + " shake=" + AccessSettings.Shake(g.Fx.ReduceFlashing)
                        + " mono=" + AccessSettings.MonoAudio + " largeCursor=" + AccessSettings.LargeCursor + " clickSpeed=" + AccessSettings.ClickSpeed
                        + " doubleClick=" + g.Router.DoubleClickTime.ToString("0.00", CultureInfo.InvariantCulture) + "s/" + g.Router.DoubleClickDistance.ToString("0", CultureInfo.InvariantCulture) + "px");
                    return Done();
                case "noticelog":
                    foreach (var e in g.Notifications.History.Entries) Say("  " + e.Stamp + " [" + e.Kind + "] " + e.Title + ": " + e.Body.Replace("\n", " / "));
                    Say("recent notices: " + g.Notifications.History.Count);
                    return Done();
                default: return null;
            }
        }
    }
}
