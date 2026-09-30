using SecondCursor.Core;
using UnityEngine;

namespace SecondCursor
{
    /// <summary>Conversions between the engine-free Core types and Unity types, and rectangle helpers.</summary>
    public static class VecExt
    {
        public static Vector2 ToUnity(this Vec2 v) => new Vector2(v.x, v.y);
        public static Vec2 ToCore(this Vector2 v) => new Vec2(v.x, v.y);
        public static Vec2 ToCore(this Vector3 v) => new Vec2(v.x, v.y);

        public static Vector2 Round(this Vector2 v) => new Vector2(Mathf.Round(v.x), Mathf.Round(v.y));

        /// <summary>World-space rect of a RectTransform. The OS canvas maps 1 world unit to 1 virtual pixel.</summary>
        public static Rect WorldRect(this RectTransform rt)
        {
            var c = RectCorners;
            rt.GetWorldCorners(c);
            float xMin = Mathf.Min(c[0].x, c[2].x), xMax = Mathf.Max(c[0].x, c[2].x);
            float yMin = Mathf.Min(c[0].y, c[2].y), yMax = Mathf.Max(c[0].y, c[2].y);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        public static Vector2 WorldCenter(this RectTransform rt) => rt.WorldRect().center;

        /// <summary>The area where two rectangles overlap (0 when they do not).</summary>
        public static float OverlapArea(this Rect a, Rect b)
        {
            float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
            float h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            return w > 0f && h > 0f ? w * h : 0f;
        }

        static readonly Vector3[] RectCorners = new Vector3[4];
    }
}
