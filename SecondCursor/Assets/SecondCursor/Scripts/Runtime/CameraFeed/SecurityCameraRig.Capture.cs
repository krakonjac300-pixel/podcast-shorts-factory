using UnityEngine;

namespace SecondCursor.CameraFeed
{
    public sealed partial class SecurityCameraRig
    {
        Vector3 _rushStart, _rushEnd;
        Quaternion _rushFacing;
        Transform _rushLegL, _rushLegR, _rushArmL, _rushArmR;
        public float CaptureRushProgress { get; private set; }
        public bool CaptureRushActive { get; private set; }

        /// <summary>The final attack travels through the room instead of cutting to a close-up.</summary>
        public void BeginCaptureRush()
        {
            CaptureRushActive = true;
            FreezeFeed = false;
            Quiet = true;
            LightFlicker = 0f;
            ExtraNoise = 0f;
            _rushStart = _figure.position;
            var lens = _office.Cam.transform;
            _rushEnd = lens.position + lens.forward * 0.4f - Vector3.up * 1.95f;
            var facing = -lens.forward;
            facing.y = 0f;
            _rushFacing = Quaternion.LookRotation(facing.normalized);
            _rushLegL = _figure.Find("Leg L");
            _rushLegR = _figure.Find("Leg R");
            _rushArmL = _figure.Find("Arm L");
            _rushArmR = _figure.Find("Arm R");
            SetCaptureRush(0f);
        }

        public void SetCaptureRush(float progress)
        {
            if (_figure == null) return;
            float t = Mathf.Clamp01(progress);
            CaptureRushProgress = t;
            float travel = t * (0.35f + 0.65f * t);
            float stride = Mathf.Sin(t * Mathf.PI * 6f) * (1f - t);
            _figure.SetPositionAndRotation(Vector3.Lerp(_rushStart, _rushEnd, travel)
                + Vector3.up * (Mathf.Abs(stride) * 0.07f), _rushFacing);
            Pose(_rushLegL, stride * 38f, 0f);
            Pose(_rushLegR, -stride * 38f, 0f);
            Pose(_rushArmL, -stride * 48f - t * 55f, -3f);
            Pose(_rushArmR, stride * 48f - t * 55f, 3f);
        }

        void ResetCapturePose()
        {
            CaptureRushActive = false;
            CaptureRushProgress = 0f;
            Pose(_rushLegL, 0f, 0f);
            Pose(_rushLegR, 0f, 0f);
            Pose(_rushArmL, 0f, -3f);
            Pose(_rushArmR, 0f, 3f);
        }

        static void Pose(Transform limb, float pitch, float roll)
        {
            if (limb != null) limb.localRotation = Quaternion.Euler(pitch, 0f, roll);
        }
    }
}
