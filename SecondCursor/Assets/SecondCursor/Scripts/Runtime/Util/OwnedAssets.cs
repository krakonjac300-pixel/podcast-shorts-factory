using System.Collections.Generic;
using UnityEngine;

namespace SecondCursor
{
    /// <summary>
    /// Destroys runtime-created assets (textures, materials...) together with the GameObject that shows them,
    /// however that GameObject goes away: a closed window, a restarted shift, or leaving Play mode.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OwnedAssets : MonoBehaviour
    {
        readonly List<Object> _assets = new List<Object>();

        /// <summary>Ties <paramref name="asset"/>'s lifetime to <paramref name="owner"/> and returns it.</summary>
        public static T Own<T>(GameObject owner, T asset) where T : Object
        {
            if (owner == null || asset == null) return asset;
            var holder = owner.GetComponent<OwnedAssets>();
            if (holder == null) holder = owner.AddComponent<OwnedAssets>();
            holder._assets.Add(asset);
            return asset;
        }

        void OnDestroy()
        {
            foreach (var a in _assets) if (a != null) Destroy(a);
            _assets.Clear();
        }
    }
}
