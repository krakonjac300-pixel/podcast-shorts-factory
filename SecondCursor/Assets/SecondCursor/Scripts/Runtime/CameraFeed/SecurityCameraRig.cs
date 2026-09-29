using System;
using SecondCursor.Core.Content;
using SecondCursor.Game;
using SecondCursor.Rendering;
using UnityEngine;

namespace SecondCursor.CameraFeed
{
    public enum FigureStage { None, Corridor, Doorway, Middle, BehindChair }

    /// <summary>
    /// PLACEHOLDER (being replaced by the full 3D office set). Owns the CCTV render texture and the story
    /// controls for Camera 03. Public API is final.
    /// </summary>
    public sealed class SecurityCameraRig : MonoBehaviour
    {
        public RenderTexture Feed { get; private set; }
        public string ActiveCamera { get; private set; } = ContentIds.Cam01;
        public float ExtraNoise { get; set; }
        public float DoorOpen { get; set; }
        public FigureStage Figure { get; set; }
        public bool SeatedMimicsPlayer { get; set; } = true;
        public Vector2 PlayerHand { get; set; }
        public float SeatedHeadTurn { get; set; }
        public float LightFlicker { get; set; }
        public bool LightsOn { get; set; } = true;
        public bool SignalLost { get; set; }

        public static SecurityCameraRig Create(Transform parent, GameServices g)
        {
            var go = new GameObject("Security Camera Rig");
            go.transform.SetParent(parent, false);
            var rig = go.AddComponent<SecurityCameraRig>();
            rig.Feed = new RenderTexture(160, 120, 16, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point, name = "CCTV Feed" };
            rig.Feed.Create();
            return rig;
        }

        public void SetCamera(string camId) => ActiveCamera = camId;
        public bool HasSignal(string camId) => !SignalLost && camId != ContentIds.Cam04;
        public void SetViewing(bool viewing) { }
    }
}
