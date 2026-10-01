using UnityEngine;

namespace SecondCursor.CameraFeed
{
    /// <summary>
    /// The seated operator on CAM 03 (split from SecurityCameraRig.cs): the free hand on the keyboard that dips when the player types (Phase Q3,
    /// board C V10, "the figure types when you type") and the empty chair (D1, Night 2's coda: the feed agrees with the door log).
    /// </summary>
    public sealed partial class SecurityCameraRig
    {
        /// <summary>Seconds a keystroke keeps both hands on the keyboard, how fast they come and go, how deep a key dips (metres) and how fast it recovers.</summary>
        const float TypingHold = 1.2f, TypingBlendRate = 4f, KeyDip = 0.022f, KeyDipRecover = 9f;

        Transform _kbUpper, _kbFore, _kbHand;
        float _lastKeyTime = -100f, _keyDip, _typingBlend;
        int _keyCount;
        bool _seatedVisible = true;

        /// <summary>
        /// False: nobody sits in the chair (the torso, head, lap and both arms are hidden); the workstation tag still floats where the head would be.
        /// Night 2's coda shows it once the session is suspended. Every night starts with the operator seated (the rig is built per night).
        /// </summary>
        public bool SeatedVisible
        {
            get => _seatedVisible;
            set
            {
                if (_seatedVisible == value) return;
                _seatedVisible = value;
                foreach (var t in new[] { _torso, _lap, _upperArm, _forearm, _hand, _kbUpper, _kbFore, _kbHand })
                    if (t != null) t.gameObject.SetActive(value);
            }
        }

        /// <summary>The player pressed a key this frame: the keyboard hand dips (and the mouse hand comes back to the keyboard for a moment).</summary>
        public void NoteKey()
        {
            _lastKeyTime = Time.time;
            _keyDip = 1f;
            _keyCount++;
        }

        /// <summary>The left hand of the operator, which rests on the keyboard (a free hand: the right one follows the mouse).</summary>
        void BuildKeyboardArm(Transform r, Material shirt, Material skin)
        {
            _kbUpper = Prim(PrimitiveType.Cylinder, r, "Left Upper Arm", Vector3.zero, Vector3.zero, new Vector3(0.1f, 0.15f, 0.1f), shirt);
            _kbFore = Prim(PrimitiveType.Cylinder, r, "Left Forearm", Vector3.zero, Vector3.zero, new Vector3(0.085f, 0.15f, 0.085f), skin);
            _kbHand = Prim(PrimitiveType.Sphere, r, "Left Hand", Vector3.zero, Vector3.zero, new Vector3(0.085f, 0.05f, 0.11f), skin);
        }

        /// <summary>Both hands go to the keyboard while keys are pressed, except while the head turns, the picture is held still or the mouse arm is not the player's.</summary>
        void UpdateTyping(float dt)
        {
            bool shown = SeatedVisible && SeatedMimicsPlayer && SeatedHeadTurn < 0.01f && !_frozen && !Quiet;
            bool typing = shown && Time.time - _lastKeyTime < TypingHold;
            _typingBlend = Mathf.MoveTowards(_typingBlend, typing ? 1f : 0f, dt * TypingBlendRate);
            _keyDip = Mathf.MoveTowards(_keyDip, 0f, dt * KeyDipRecover);
        }

        /// <summary>The wrist over the keyboard (room-local): the left half for the free hand (side -1), the right half for the mouse hand (side 1), dipped by the last key.</summary>
        Vector3 KeyboardWrist(float side)
        {
            // Alternate hands: even key counts dip the left hand, odd ones the right.
            bool dipsNow = ((_keyCount & 1) == 0) == (side < 0f);
            float dip = _keyDip * KeyDip * (dipsNow ? 1f : 0.4f);
            return new Vector3(-RoomHalfW + 0.6f, 0.806f - dip, DeskZ - 0.02f + side * 0.13f);
        }

        void UpdateKeyboardArm()
        {
            if (_kbUpper == null || !SeatedVisible) return;
            var room = _office.Root;
            Vector3 shoulder = _torso.TransformPoint(new Vector3(-ShoulderLocal.x, ShoulderLocal.y, ShoulderLocal.z));
            Vector3 wrist = room.TransformPoint(KeyboardWrist(-1f));
            Vector3 elbow = SolveElbow(shoulder, ref wrist, room.TransformDirection(new Vector3(ElbowPole.x, ElbowPole.y, -ElbowPole.z)));
            SetLimb(_kbUpper, shoulder, elbow);
            SetLimb(_kbFore, elbow, wrist);
            Vector3 dir = (wrist - elbow).normalized;
            _kbHand.SetPositionAndRotation(wrist + dir * 0.05f, Quaternion.LookRotation(dir));
        }
    }
}
