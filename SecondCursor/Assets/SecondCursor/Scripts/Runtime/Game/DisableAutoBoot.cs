using UnityEngine;

namespace SecondCursor.Game
{
    /// <summary>
    /// Put this on any GameObject in a scene where SECOND CURSOR should NOT start automatically
    /// (Add Component > Disable Auto Boot). It lives in its own file so the Editor can attach it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DisableAutoBoot : MonoBehaviour { }
}
