using UnityEngine;

namespace SecondCursor
{
    /// <summary>
    /// "Find every loaded object of type T" across Unity versions: Unity 6.6 deprecates the
    /// FindObjectsSortMode overload that 2022.3 and early Unity 6 still need.
    /// </summary>
    public static class SceneObjects
    {
        public static T[] All<T>() where T : Object
        {
#if UNITY_6000_6_OR_NEWER
            return Object.FindObjectsByType<T>();
#else
            return Object.FindObjectsByType<T>(FindObjectsSortMode.None);
#endif
        }
    }
}
