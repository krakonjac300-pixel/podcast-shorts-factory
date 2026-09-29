using SecondCursor.Core.Content;
using UnityEngine;

namespace SecondCursor.CameraFeed
{
    /// <summary>
    /// Night 3's extra CCTV sets (expansion spec 11.2): CAM 04 in Sublevel C, a long aisle of storage shelves
    /// that pans slowly while its caption reads the shelf labels, and CAM 00 in Admin 1, a dark copy of the
    /// operator's office where the Custodian sits at the CRT.
    /// </summary>
    public sealed partial class SecurityCameraRig
    {
        /// <summary>Seconds for one full pan of CAM 04 (and one pass through its shelf labels).</summary>
        public const float ShelfCycleSeconds = 28f;
        const float ShelfPanDegrees = 8f;
        const int ShelfUnitsPerSide = 10;

        Area _sublevel, _admin;
        Transform _adminPoint;
        Material _cageLampMat, _adminScreenMat;

        /// <summary>CAM 04 has a picture (Night 3: "restored for this purpose"); otherwise it shows NO SIGNAL.</summary>
        public bool Cam04Online { get; set; }

        /// <summary>0 = night, 1 = full morning (the lobby's glass doors brighten; above 0.5 its lamp is off).</summary>
        public float DawnLevel { get; set; }

        /// <summary>CAM 04's shelf labels, shown one after another in step with the pan (null = no caption).</summary>
        public string[] ShelfLabels { get; set; }

        /// <summary>The bright point crossing the Admin 1 CRT: 0..1 along the screen, below 0 hidden.</summary>
        public float AdminPointer { get; set; } = -1f;

        /// <summary>When the feed first switched to CAM 04 (the caption loop starts there, M10); below 0 = not yet.</summary>
        float _cam04Since = -1f;

        /// <summary>
        /// The bottom-left caption for a camera (CAM 04's current shelf label), or "". M10: the loop starts at shelf 17
        /// the first time CAM 04 comes up and holds the player's own shelf 18 for 5 s (<see cref="Core.Story.ShelfCaptions"/>).
        /// </summary>
        public string CaptionFor(string camId) => ShelfCaption(camId, 0);

        /// <summary>
        /// Phase H: the shelf label that comes after the current one on CAM 04 ("SHELF 13"), or "": the viewer shows it as
        /// NEXT so a player waiting for one shelf knows how long.
        /// </summary>
        public string NextShelfFor(string camId)
        {
            string next = ShelfCaption(camId, 1);
            int colon = next.IndexOf(':');
            return colon > 0 ? next.Substring(0, colon) : next;
        }

        string ShelfCaption(string camId, int ahead)
        {
            if (camId != ContentIds.Cam04 || !Cam04Online || SignalLost || ShelfLabels == null || ShelfLabels.Length == 0) return "";
            int start = Core.Story.ShelfCaptions.Find(ShelfLabels, Core.Story.ShelfCaptions.StartShelf);
            int hold = Core.Story.ShelfCaptions.Find(ShelfLabels, Core.Story.ShelfCaptions.HoldShelf);
            float since = _cam04Since < 0f ? Time.time : _cam04Since;
            int i = Core.Story.ShelfCaptions.Index(Time.time - since, ShelfLabels.Length, start, hold);
            return ShelfLabels[(i + ahead) % ShelfLabels.Length];
        }

        /// <summary>Which of <paramref name="count"/> labels shows at time <paramref name="t"/> (one full cycle per pan).</summary>
        public static int ShelfIndex(float t, int count)
        {
            if (count <= 0) return 0;
            float phase = Mathf.Repeat(t, ShelfCycleSeconds) / ShelfCycleSeconds;
            return Mathf.Clamp(Mathf.FloorToInt(phase * count), 0, count - 1);
        }

        void BuildExtraAreas()
        {
#if !SC_DEMO
            // Night 3 only: the demo build leaves both sets (and their camera names) out.
            _sublevel = BuildSublevel();
            _admin = BuildAdmin();
#endif
        }

        Area ExtraAreaFor(string camId)
        {
            if (camId == ContentIds.Cam04) return Cam04Online ? _sublevel : null;
            if (camId == ContentIds.Cam00) return _admin;
            return null;
        }

        /// <summary>Where the figure stands for the Night 3 stages (area-local metres).</summary>
        bool ExtraStage(FigureStage stage, out Area area, out Vector3 pos, out float yaw, out float pitch)
        {
            area = _office;
            pos = Vector3.zero;
            yaw = 0f;
            pitch = 0f;
            switch (stage)
            {
                case FigureStage.SublevelC: area = _sublevel; pos = new Vector3(0f, 0f, 7.5f); yaw = 180f; return true;   // mid-aisle, facing CAM 04
                case FigureStage.Lobby: area = _lobby; pos = new Vector3(-1.45f, 0f, 5.8f); yaw = 137f; return true;      // by the glass doors, facing CAM 01
                case FigureStage.Seated00: area = _admin; pos = new Vector3(SeatX, -0.5f, DeskZ); yaw = -90f; pitch = 8f; return true;   // at the Admin 1 CRT, head bowed
                default: return false;
            }
        }

        /// <summary>CAM 04 pans slowly along the aisle (the other cameras are fixed).</summary>
        float PanFor(Area a, float t) => a == _sublevel ? ShelfPanDegrees * Mathf.Sin(t * 2f * Mathf.PI / ShelfCycleSeconds) : 0f;

        void ApplyExtraLighting(Area a, float t, float level, ref float intensity, ref float ambient, ref float monitor, ref Transform monitorRoot)
        {
            if (a == _sublevel)
            {
                // A caged lamp at the far end; the LEDs on the drives are steady.
                SetGrey(_cageLampMat, 0.5f, 0.85f * level);
                return;
            }
            if (a == _admin)
            {
                // A dim ceiling tube and the CRT: the Custodian is a silhouette against the screen.
                intensity = a.LightIntensity * level;
                ambient = a.Ambient;
                monitor = MonitorGlow * 1.3f * _monitorPulse;
                monitorRoot = _admin.Root;
                SetGrey(_adminScreenMat, 0f, 0.95f * _monitorPulse);
                if (_adminPoint != null)
                {
                    bool on = AdminPointer >= 0f;
                    _adminPoint.gameObject.SetActive(on);
                    if (on) _adminPoint.localPosition = new Vector3(MonitorX + 0.004f, 0.965f + 0.03f, DeskZ + 0.13f - 0.26f * Mathf.Clamp01(AdminPointer));
                }
            }
        }

#if !SC_DEMO
        // CAM 04: Sublevel C, a 3 x 14 m aisle between storage shelves, seen from above the entrance.
        Area BuildSublevel()
        {
            var a = NewArea("Sublevel C", new Vector3(0f, 0f, 180f));
            var r = a.Root;
            const float halfW = 1.5f, length = 14f, height = 3.0f;
            Panel(r, "Floor", new Vector3(0f, 0f, length / 2f - 0.5f), new Vector3(90f, 0f, 0f), 2f * halfW, length + 1f, Mat(0.26f));
            Panel(r, "Ceiling", new Vector3(0f, height, length / 2f), new Vector3(-90f, 0f, 0f), 2f * halfW, length, Mat(0.22f));
            Panel(r, "West Wall", new Vector3(-halfW, height / 2f, length / 2f), new Vector3(0f, -90f, 0f), length, height, Mat(0.36f));
            Panel(r, "East Wall", new Vector3(halfW, height / 2f, length / 2f), new Vector3(0f, 90f, 0f), length, height, Mat(0.36f));
            Panel(r, "End Wall", new Vector3(0f, height / 2f, length), Vector3.zero, 2f * halfW, height, Mat(0.30f));

            var frame = Mat(0.18f);
            var board = Mat(0.46f);
            var drive = Mat(0.62f);
            var label = Mat(0.85f, 0.15f);
            var led = Mat(0.2f, 0.9f);
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * (halfW - 0.25f);
                for (int k = 0; k < ShelfUnitsPerSide; k++)
                {
                    float z = 1.0f + k * 1.3f;
                    var unit = new GameObject("Shelf " + (side < 0 ? "W" : "E") + k) { layer = Layer }.transform;
                    unit.SetParent(r, false);
                    unit.localPosition = new Vector3(x, 0f, z);
                    Box(unit, "Back", new Vector3(side * 0.22f, 1.0f, 0f), new Vector3(0.06f, 2.0f, 1.2f), frame);
                    Box(unit, "Post A", new Vector3(0f, 1.0f, -0.58f), new Vector3(0.5f, 2.0f, 0.04f), frame);
                    Box(unit, "Post B", new Vector3(0f, 1.0f, 0.58f), new Vector3(0.5f, 2.0f, 0.04f), frame);
                    for (int s = 0; s < 3; s++)
                    {
                        float y = 0.35f + s * 0.6f;
                        Box(unit, "Board " + s, new Vector3(0f, y, 0f), new Vector3(0.5f, 0.03f, 1.2f), board);
                        // Two drive boxes per board, never quite aligned.
                        Box(unit, "Drive " + s + "a", new Vector3(side * 0.04f, y + 0.12f, -0.28f + 0.05f * ((k + s) % 3)), new Vector3(0.3f, 0.21f, 0.34f), drive);
                        Box(unit, "Drive " + s + "b", new Vector3(side * 0.04f, y + 0.12f, 0.24f - 0.04f * ((k * 2 + s) % 3)), new Vector3(0.3f, 0.21f, 0.3f), drive);
                    }
                    Box(unit, "Label", new Vector3(-side * 0.255f, 1.78f, 0f), new Vector3(0.01f, 0.1f, 0.34f), label);
                    Box(unit, "LED", new Vector3(-side * 0.255f, 0.5f + 0.6f * (k % 3), -0.3f), new Vector3(0.012f, 0.03f, 0.03f), led);
                }
            }
            // The caged lamp at the far end.
            _cageLampMat = Mat(0.5f, 0.85f, false);
            Box(r, "Caged Lamp", new Vector3(0f, height - 0.18f, length - 0.35f), new Vector3(0.22f, 0.14f, 0.12f), _cageLampMat);
            var cage = Mat(0.1f);
            for (int i = -1; i <= 1; i++)
                Box(r, "Cage " + (i + 1), new Vector3(i * 0.09f, height - 0.18f, length - 0.28f), new Vector3(0.015f, 0.2f, 0.015f), cage);
            SetLighting(a, new Vector3(0f, 2.25f, 1.5f), new Vector3(0f, 2.25f, length - 0.4f), 0.95f, 3.2f, 0.06f, 0.08f);
            AddCamera(a, "CAM 04", new Vector3(0.25f, 2.3f, 0.05f), new Vector3(-0.1f, 0.1f, 9f), 60f, 25f);
            return a;
        }

        // CAM 00: Admin 1, a dark copy of the operator's office seen from the same high corner.
        Area BuildAdmin()
        {
            var a = NewArea("Admin 1", new Vector3(0f, 0f, 240f));
            var r = a.Root;
            var wall = Mat(0.42f);
            Panel(r, "Floor", new Vector3(0f, 0f, 0.35f), new Vector3(90f, 0f, 0f), 2f * RoomHalfW, 2f * RoomHalfD + 0.7f, Mat(0.26f));
            Panel(r, "Ceiling", new Vector3(0f, RoomH, 0f), new Vector3(-90f, 0f, 0f), 2f * RoomHalfW, 2f * RoomHalfD, Mat(0.3f));
            Panel(r, "West Wall", new Vector3(-RoomHalfW, RoomH / 2f, 0.25f), new Vector3(0f, -90f, 0f), 2f * RoomHalfD + 0.5f, RoomH, wall);
            Panel(r, "East Wall", new Vector3(RoomHalfW, RoomH / 2f, 0f), new Vector3(0f, 90f, 0f), 2f * RoomHalfD, RoomH, wall);
            Panel(r, "South Wall", new Vector3(0f, RoomH / 2f, -RoomHalfD), new Vector3(0f, 180f, 0f), 2f * RoomHalfW, RoomH, wall);
            float deskX = -RoomHalfW + 0.375f;
            var wood = Mat(0.18f);
            Box(r, "Desk", new Vector3(deskX, 0.74f, DeskZ), new Vector3(0.75f, 0.04f, 1.5f), Mat(0.22f));
            Box(r, "Desk Drawers", new Vector3(deskX, 0.36f, DeskZ + 0.51f), new Vector3(0.68f, 0.72f, 0.42f), wood);
            Box(r, "Desk Side", new Vector3(deskX, 0.36f, DeskZ - 0.72f), new Vector3(0.68f, 0.72f, 0.04f), wood);
            Box(r, "CRT", new Vector3(-RoomHalfW + 0.22f, 0.955f, DeskZ), new Vector3(0.42f, 0.39f, 0.42f), Mat(0.45f));
            _adminScreenMat = Mat(0f, 0.75f, false);
            Panel(r, "Screen", new Vector3(MonitorX, 0.965f, DeskZ), new Vector3(0f, -90f, 0f), 0.32f, 0.25f, _adminScreenMat);
            _adminPoint = Panel(r, "Pointer", new Vector3(MonitorX + 0.004f, 0.995f, DeskZ), new Vector3(0f, -90f, 0f), 0.025f, 0.025f, Mat(1f, 1.4f));
            _adminPoint.gameObject.SetActive(false);
            Box(r, "Keyboard", new Vector3(-RoomHalfW + 0.58f, 0.772f, DeskZ - 0.02f), new Vector3(0.16f, 0.024f, 0.46f), Mat(0.4f));
            var fabric = Mat(0.1f);
            Box(r, "Chair Seat", new Vector3(SeatX, 0.47f, DeskZ), new Vector3(0.46f, 0.07f, 0.48f), fabric);
            Prim(PrimitiveType.Cube, r, "Chair Back", new Vector3(SeatX + 0.25f, 0.8f, DeskZ), new Vector3(0f, 0f, -8f), new Vector3(0.06f, 0.42f, 0.46f), fabric);
            Box(r, "Filing Cabinet", new Vector3(-RoomHalfW + 0.3f, 0.66f, -RoomHalfD + 0.3f), new Vector3(0.6f, 1.32f, 0.5f), Mat(0.3f));
            Panel(r, "Door", new Vector3(DoorX, DoorH / 2f, -RoomHalfD + 0.006f), new Vector3(0f, 180f, 0f), DoorW, DoorH, Mat(0.35f));
            SetLighting(a, new Vector3(-0.1f, 2.51f, 0.55f), new Vector3(-0.1f, 2.51f, -0.65f), 1.0f, 2.6f, 0.09f, 0.05f);
            // The other Custodian: always here, at the CRT, head bowed (000 is on rounds; 001 logs on at the same minute).
            var seated = BuildFigure();
            seated.name = "Seated Custodian";
            seated.SetParent(r, false);
            seated.localPosition = new Vector3(SeatX, -0.5f, DeskZ);
            seated.localRotation = Quaternion.Euler(8f, -90f, 0f);
            AddCamera(a, "CAM 00", new Vector3(RoomHalfW - 0.15f, 2.34f, RoomHalfD - 0.15f), new Vector3(-0.5f, 0.92f, -0.45f), 70f, 20f);
            return a;
        }
#endif
    }
}
