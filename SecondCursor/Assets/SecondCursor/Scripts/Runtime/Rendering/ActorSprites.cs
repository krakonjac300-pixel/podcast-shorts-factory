using SecondCursor.Core.Game;
using UnityEngine;
using UnityEngine.UI;

namespace SecondCursor.Rendering
{
    /// <summary>
    /// Phase Q4 (R2): a cursor sprite in a session's own colours (session 017 black with a pale rim, session 209 amber on brown), for the
    /// places that say who did something: a notice's icon, a taskbar button. The same recoloured sprites the pointers themselves use.
    /// </summary>
    public static class ActorSprites
    {
        public const string EntityKey = "entity", GaryKey = "gary";

        /// <summary>The recoloured sprite, or the plain one for a notice that belongs to nobody.</summary>
        public static Sprite For(string spriteName, NoticeKind kind)
        {
            switch (kind)
            {
                case NoticeKind.Entity:
                    return SpriteLibrary.GetVariant(spriteName, EntityKey, c => c == 'K' ? Palette.EntityOutline : c == 'W' ? (Color32?)Palette.EntityFill : null);
                case NoticeKind.Gary:
                    return SpriteLibrary.GetVariant(spriteName, GaryKey, c => c == 'K' ? Palette.GaryOutline : c == 'W' ? (Color32?)Palette.GaryFill : null);
                default:
                    return SpriteLibrary.Get(spriteName);
            }
        }

        /// <summary>A small pointer (6x8) for lines of text and taskbar buttons, where the full cursor would be taller than the line.</summary>
        public const string Tiny = "cursor_tiny";

        static bool _defined;

        /// <summary>Registers the small sprites (once).</summary>
        public static void Define()
        {
            if (_defined && SpriteLibrary.Has(Tiny)) return;
            _defined = true;
            SpriteLibrary.Define(Tiny, 0, 0,
                "K.....",
                "KK....",
                "KWK...",
                "KWWK..",
                "KWWWK.",
                "KWWWWK",
                "KWKK..",
                "KK....");
        }

        /// <summary>The small pointer in a session's colours (a notice kind of Plain gives the player's white arrow).</summary>
        public static Image TinyIcon(Transform parent, NoticeKind kind)
        {
            Define();
            var img = UI.UIBuilder.Icon(parent, Tiny, 1, "Actor " + kind);
            img.sprite = For(Tiny, kind);
            return img;
        }

        /// <summary>The cursor sprite that stands for a session: Gary's hand, 017's arrow.</summary>
        public static string CursorOf(NoticeKind kind) => kind == NoticeKind.Gary ? "cursor_hand" : "cursor_arrow";

        /// <summary>An icon image showing the session's pointer (top-left anchored, like <see cref="UI.UIBuilder.Icon"/>).</summary>
        public static Image Icon(Transform parent, NoticeKind kind, int scale = 1)
        {
            string name = CursorOf(kind);
            var img = UI.UIBuilder.Icon(parent, name, scale, "Actor " + kind);
            img.sprite = For(name, kind);
            return img;
        }
    }
}
