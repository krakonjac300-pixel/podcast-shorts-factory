using System.Collections.Generic;
using SecondCursor.Core.Content;
using SecondCursor.Game;
using SecondCursor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace SecondCursor.CameraFeed
{
    public enum FigureStage { None, Corridor, Doorway, Middle, BehindChair, HallFar, SublevelC, Lobby, Seated00, AtLens }

    /// <summary>
    /// SecureView's CCTV: a tiny 3D building (lobby, corridor, the operator's own office) built from primitives at
    /// runtime far from the origin on <see cref="ScreenRig.SceneLayer"/>, rendered by one camera at a time into a
    /// 160x120 point-filtered texture while the app is open. Shading uses the "SecondCursor/CCTV" shader, which
    /// lights everything itself from a few globals, so the footage looks the same in the Built-in pipeline and URP.
    ///
    /// CAM 03 looks down from the high corner behind the operator's right shoulder: the operator sits at a desk
    /// against the west wall facing a glowing CRT, their mouse arm follows the player's real mouse, and the door in
    /// the far (south) wall is behind their back. The figure never moves while watched; it only cuts between stages.
    /// </summary>
    public sealed partial class SecurityCameraRig : MonoBehaviour
    {
        const int Layer = ScreenRig.SceneLayer;
        const int FeedWidth = 160, FeedHeight = 120;
        static readonly Vector3 SetOrigin = new Vector3(10000f, 0f, 0f);   // far away from anything in the scene

        // Office (CAM 03), metres, office-local. X = east, Z = north.
        const float RoomHalfW = 1.5f, RoomHalfD = 1.9f, RoomH = 2.6f;
        const float DoorX = 0.4f, DoorW = 0.9f, DoorH = 2.1f, DoorMaxAngle = 70f;
        const float DoorwayMinOpen = 0.9f;                          // the figure only stands in a wide-open doorway
        const float DeskZ = 0.35f;
        const float SeatX = -RoomHalfW + 0.96f;
        const float MonitorX = -RoomHalfW + 0.22f + 0.213f;          // CRT front face (screen quad)
        const float MonitorGlow = 2.0f;
        const float UpperArm = 0.30f, Forearm = 0.30f;
        static readonly Vector3 ShoulderLocal = new Vector3(0.19f, 0.58f, 0.02f);   // right shoulder, torso space
        static readonly Vector3 NeckLocal = new Vector3(0f, 0.64f, 0.03f);
        static readonly Vector3 ElbowPole = new Vector3(0.3f, -1f, 1f);             // elbow points out, down, back

        // Corridor (CAM 02): runs north (+Z) from the camera to the office door in its end wall.
        const float CorridorDoorX = -0.2f, CorridorDoorZ = 10.97f;

        // CreatePrimitive adds these components; referencing them stops engine code stripping from removing them.
        internal static readonly System.Type[] PrimitiveComponents =
            { typeof(MeshFilter), typeof(MeshRenderer), typeof(BoxCollider), typeof(SphereCollider), typeof(CapsuleCollider), typeof(MeshCollider) };

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int EmissionId = Shader.PropertyToID("_Emission");
        static readonly int LightAId = Shader.PropertyToID("_SC_LightA");
        static readonly int LightBId = Shader.PropertyToID("_SC_LightB");
        static readonly int LightIntensityId = Shader.PropertyToID("_SC_LightIntensity");
        static readonly int LightRangeId = Shader.PropertyToID("_SC_LightRange");
        static readonly int MonitorPosId = Shader.PropertyToID("_SC_MonitorPos");
        static readonly int MonitorDirId = Shader.PropertyToID("_SC_MonitorDir");
        static readonly int MonitorIntensityId = Shader.PropertyToID("_SC_MonitorIntensity");
        static readonly int AmbientId = Shader.PropertyToID("_SC_Ambient");
        static readonly int FogColorId = Shader.PropertyToID("_SC_FogColor");
        static readonly int FogDensityId = Shader.PropertyToID("_SC_FogDensity");
        static readonly int CameraPosId = Shader.PropertyToID("_SC_CameraPos");

        /// <summary>One room with its own camera and lighting (globals are swapped in when its camera renders).</summary>
        sealed class Area
        {
            public Transform Root;
            public Camera Cam;
            public Quaternion CamRotation;
            public Vector3 LightA, LightB;          // area-local tube segment
            public float LightIntensity, LightRange, Ambient, FogDensity;
            public Light Fallback;                  // only when the CCTV shader is unavailable
        }

        public RenderTexture Feed { get; private set; }
        public string ActiveCamera { get; private set; } = ContentIds.Cam01;
        /// <summary>Phase N: the camera the player picked last (null = none yet); a reopened viewer comes back on it.</summary>
        public string PlayerCamera { get; set; }
        public float ExtraNoise { get; set; }
        public float DoorOpen { get => _doorTarget; set => _doorTarget = Mathf.Clamp01(value); }
        public FigureStage Figure { get => _figureStage; set => SetFigure(value); }
        /// <summary>CCTV time in minutes since midnight: it only advances while a feed is watched (-1 until first viewed).</summary>
        public double FeedMinutes { get; set; } = -1.0;
        public bool SeatedMimicsPlayer { get; set; } = true;
        public Vector2 PlayerHand { get; set; }
        public float SeatedHeadTurn { get; set; }
        public float LightFlicker { get; set; }
        public bool LightsOn { get; set; } = true;
        public bool SignalLost { get; set; }
        /// <summary>Phase M: the feed makes no sound at all (no step when the figure moves, no feed hum): SHRED's silent head turn, a hit's silence.</summary>
        public bool Quiet { get; set; }
        /// <summary>Phase M: the picture holds perfectly still (nothing moves, no grain, the timestamp stops): the frame before a hit.</summary>
        public bool FreezeFeed
        {
            get => _frozen;
            set
            {
                if (value && !_frozen && _active != null)
                {
                    // The still frame is lit steadily: a flicker's dark instant must not be the frame that holds.
                    LightFlicker = 0f;
                    _dipTime = 0f;
                    Tick(0f, false);
                }
                _frozen = value;
            }
        }

        bool _frozen;

        GameServices _g;
        Shader _shader;
        bool _fallback;
        Material _fallbackTemplate;
        readonly Dictionary<int, Material> _sharedMaterials = new Dictionary<int, Material>();
        readonly List<Material> _materials = new List<Material>();

        Transform _setRoot;
        Area _lobby, _corridor, _office, _active;
        bool _viewing;

        // Animated parts
        Transform _doorHinge, _corridorDoor, _torso, _head, _upperArm, _forearm, _hand, _figure, _clockHand, _lap;
        Material _tubeMat, _screenMat, _corridorLampMat, _corridorGapMat, _lobbyLampMat, _lobbyGlassMat;

        // Story state as displayed
        float _doorTarget, _door, _doorVelocity, _headTurn, _monitorPulse = 1f;
        Vector2 _handPos;
        float _officeSince = -100f;
        /// <summary>M1: seconds the arm ignores the mouse after the office feed opens, its lag, and how long the lag lasts.</summary>
        const float ArmDeadSeconds = 1.2f, ArmLagSeconds = 0.3f, ArmLagWindow = 2f;
        FigureStage _figureStage;
        float _lightLevel = 1f, _dipTime, _dipDepth;

        public static SecurityCameraRig Create(Transform parent, GameServices g)
        {
            var go = new GameObject("Security Camera Rig");
            go.transform.SetParent(parent, false);
            var rig = go.AddComponent<SecurityCameraRig>();
            rig._g = g;
            rig.Build();
            return rig;
        }

        public void SetCamera(string camId)
        {
            ActiveCamera = camId;
            RefreshCameras();
        }

        /// <summary>
        /// M1: where the seated operator's name tag goes on the office feed (viewport 0..1, y up), just above the head.
        /// False when the office feed is not the one rendering or the head is behind the camera.
        /// </summary>
        public bool OperatorTagViewport(out Vector2 viewport)
        {
            viewport = default;
            // An empty chair (Phase Q3, D1) keeps the tag where the head would be.
            if (_active == null || _active != _office || _head == null || (SeatedVisible && !_head.gameObject.activeInHierarchy)) return false;
            var p = _office.Cam.WorldToViewportPoint(_head.position + Vector3.up * 0.32f);
            if (p.z <= 0f) return false;
            viewport = new Vector2(p.x, p.y);
            return viewport.x > 0.02f && viewport.x < 0.98f && viewport.y > 0.02f && viewport.y < 0.98f;
        }

        public bool HasSignal(string camId) => !SignalLost && AreaFor(camId) != null;   // cam04 only while it is online (Night 3)

        public void SetViewing(bool viewing)
        {
            _viewing = viewing;
            RefreshCameras();
        }

        // ------------------------------------------------------------------------------------------ construction

        void Build()
        {
            Feed = new RenderTexture(FeedWidth, FeedHeight, 24, RenderTextureFormat.ARGB32)
            {
                name = "CCTV Feed",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                antiAliasing = 1,
            };
            Feed.Create();

            _shader = Shader.Find("SecondCursor/CCTV");
            _fallback = _shader == null || !_shader.isSupported;
            if (_fallback)
            {
                Debug.LogWarning("SecurityCameraRig: shader 'SecondCursor/CCTV' is missing or unsupported; " +
                                 "falling back to the default material with Unity lights.");
                var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                probe.SetActive(false);
                _fallbackTemplate = probe.GetComponent<Renderer>().sharedMaterial;
                Destroy(probe);
            }

            // Lights that already exist in the scene must never touch the set.
            foreach (var l in SceneObjects.All<Light>()) l.cullingMask &= ~(1 << Layer);

            _setRoot = new GameObject("CCTV Set").transform;
            _setRoot.SetParent(transform, false);
            _setRoot.SetPositionAndRotation(SetOrigin, Quaternion.identity);

            _lobby = BuildLobby();
            _corridor = BuildCorridor();
            _office = BuildOffice();
            BuildExtraAreas();
            _figure = BuildFigure();
            SetFigure(_figureStage);
        }

        Area NewArea(string name, Vector3 offset)
        {
            var go = new GameObject(name) { layer = Layer };
            go.transform.SetParent(_setRoot, false);
            go.transform.localPosition = offset;
            return new Area { Root = go.transform };
        }

        void AddCamera(Area a, string name, Vector3 pos, Vector3 target, float fov, float far)
        {
            var go = new GameObject(name) { layer = Layer };
            var t = go.transform;
            t.SetParent(a.Root, false);
            t.localPosition = pos;
            a.CamRotation = Quaternion.LookRotation(target - pos);
            t.localRotation = a.CamRotation;
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.02f, 1f);
            cam.cullingMask = 1 << Layer;
            cam.targetTexture = Feed;
            cam.orthographic = false;
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = far;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.useOcclusionCulling = false;
            cam.depth = -60f;   // before the OS camera (-50) samples the feed
            a.Cam = cam;
        }

        void SetLighting(Area a, Vector3 lightA, Vector3 lightB, float intensity, float range, float ambient, float fogDensity)
        {
            a.LightA = lightA;
            a.LightB = lightB;
            a.LightIntensity = intensity;
            a.LightRange = range;
            a.Ambient = ambient;
            a.FogDensity = fogDensity;
            if (!_fallback) return;
            // Fallback only: one real light per area (range/intensity chosen to read in both pipelines).
            var go = new GameObject("Fallback Light") { layer = Layer };
            go.transform.SetParent(a.Root, false);
            go.transform.localPosition = (lightA + lightB) * 0.5f + Vector3.down * 0.2f;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 10f;
            light.intensity = 2f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.cullingMask = 1 << Layer;
            light.enabled = false;
            a.Fallback = light;
        }

        /// <summary>Grey material: albedo 0..1 plus emission added after lighting. Static ones are shared.</summary>
        Material Mat(float albedo, float emission = 0f, bool shared = true)
        {
            int key = Mathf.RoundToInt(albedo * 1000f) * 4096 + Mathf.RoundToInt(emission * 1000f);
            if (shared && _sharedMaterials.TryGetValue(key, out var cached)) return cached;
            var m = _fallback ? new Material(_fallbackTemplate) : new Material(_shader);
            m.name = "CCTV Grey " + albedo.ToString("0.00");
            SetGrey(m, albedo, emission);
            _materials.Add(m);
            if (shared) _sharedMaterials[key] = m;
            return m;
        }

        void SetGrey(Material m, float albedo, float emission)
        {
            if (_fallback)
            {
                // No custom lighting: fold the glow into the base colour so screens and tubes still read.
                float v = Mathf.Clamp01(albedo + emission);
                m.color = new Color(v, v, v, 1f);
                return;
            }
            m.SetColor(ColorId, new Color(albedo, albedo, albedo, 1f));
            m.SetFloat(EmissionId, emission);
        }

        Transform Prim(PrimitiveType type, Transform parent, string name, Vector3 pos, Vector3 euler, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.layer = Layer;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            t.localRotation = Quaternion.Euler(euler);
            t.localScale = scale;
            return t;
        }

        Transform Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat) =>
            Prim(PrimitiveType.Cube, parent, name, pos, Vector3.zero, size, mat);

        /// <summary>Single-sided quad; the euler angles pick the facing (identity faces -Z).</summary>
        Transform Panel(Transform parent, string name, Vector3 pos, Vector3 euler, float width, float height, Material mat) =>
            Prim(PrimitiveType.Quad, parent, name, pos, euler, new Vector3(width, height, 1f), mat);

        static Transform Pivot(Transform parent, string name, Vector3 pos, Vector3 euler)
        {
            var t = new GameObject(name) { layer = Layer }.transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            t.localRotation = Quaternion.Euler(euler);
            return t;
        }

        // CAM 01: an 8 x 7 m lobby seen from the corner above the reception counter, looking at the entrance.
        Area BuildLobby()
        {
            var a = NewArea("Lobby", new Vector3(0f, 0f, 120f));
            var r = a.Root;
            var wall = Mat(0.48f);
            Panel(r, "Floor", new Vector3(0f, 0f, 3f), new Vector3(90f, 0f, 0f), 8f, 8f, Mat(0.42f));   // runs on under the camera
            Panel(r, "Ceiling", new Vector3(0f, 3.2f, 3.5f), new Vector3(-90f, 0f, 0f), 8f, 7f, Mat(0.55f));
            Panel(r, "Entrance Wall", new Vector3(0f, 1.6f, 7f), Vector3.zero, 8f, 3.2f, Mat(0.52f));
            Panel(r, "West Wall", new Vector3(-4f, 1.6f, 3f), new Vector3(0f, -90f, 0f), 8f, 3.2f, wall);
            Panel(r, "East Wall", new Vector3(4f, 1.6f, 3.5f), new Vector3(0f, 90f, 0f), 7f, 3.2f, wall);
            Box(r, "Reception Counter", new Vector3(2.1f, 0.55f, 2.6f), new Vector3(2.6f, 1.1f, 0.7f), Mat(0.30f));
            Box(r, "Counter Top", new Vector3(2.1f, 1.125f, 2.6f), new Vector3(2.7f, 0.05f, 0.85f), Mat(0.66f));
            _lobbyGlassMat = Mat(0.05f, 0.2f, false);   // street light through the glass doors (brighter at dawn)
            Panel(r, "Glass Door L", new Vector3(-1.95f, 1.2f, 6.994f), Vector3.zero, 0.95f, 2.4f, _lobbyGlassMat);
            Panel(r, "Glass Door R", new Vector3(-0.95f, 1.2f, 6.994f), Vector3.zero, 0.95f, 2.4f, _lobbyGlassMat);
            Box(r, "Exit Sign", new Vector3(-1.45f, 2.66f, 6.96f), new Vector3(0.4f, 0.15f, 0.05f), Mat(0.2f, 0.85f));
            var clock = new Vector3(1f, 2.35f, 6.985f);
            Prim(PrimitiveType.Cylinder, r, "Clock", clock, new Vector3(90f, 0f, 0f), new Vector3(0.5f, 0.012f, 0.5f), Mat(0.85f));
            _clockHand = Pivot(r, "Clock Hand Pivot", clock, Vector3.zero);
            Box(_clockHand, "Minute Hand", new Vector3(0f, 0.1f, -0.02f), new Vector3(0.025f, 0.2f, 0.01f), Mat(0.05f));
            var bench = Mat(0.34f);
            Box(r, "Bench A", new Vector3(-3.6f, 0.23f, 2f), new Vector3(0.5f, 0.46f, 1.9f), bench);
            Box(r, "Bench B", new Vector3(-3.6f, 0.23f, 4.6f), new Vector3(0.5f, 0.46f, 1.9f), bench);
            _lobbyLampMat = Mat(0.5f, 0.9f, false);
            Box(r, "Ceiling Lamp", new Vector3(-0.2f, 3.16f, 3.4f), new Vector3(0.3f, 0.06f, 3f), _lobbyLampMat);
            SetLighting(a, new Vector3(-0.2f, 3.1f, 2.1f), new Vector3(-0.2f, 3.1f, 4.7f), 1.35f, 3.2f, 0.09f, 0.04f);
            AddCamera(a, "CAM 01", new Vector3(3.7f, 2.95f, 0.3f), new Vector3(-1.3f, 0.6f, 5.2f), 72f, 25f);
            return a;
        }

        // CAM 02: an 11 m hallway; the office door is in the end wall (the figure's Corridor stage stands next to it).
        Area BuildCorridor()
        {
            var a = NewArea("Corridor", new Vector3(0f, 0f, 60f));
            var r = a.Root;
            var wall = Mat(0.52f);
            var wood = Mat(0.26f);
            Panel(r, "Floor", new Vector3(0f, 0f, 5.5f), new Vector3(90f, 0f, 0f), 2f, 11f, Mat(0.30f));
            Panel(r, "Ceiling", new Vector3(0f, 2.6f, 5.5f), new Vector3(-90f, 0f, 0f), 2f, 11f, Mat(0.55f));
            Panel(r, "West Wall", new Vector3(-1f, 1.3f, 5.5f), new Vector3(0f, -90f, 0f), 11f, 2.6f, wall);
            Panel(r, "East Wall", new Vector3(1f, 1.3f, 5.5f), new Vector3(0f, 90f, 0f), 11f, 2.6f, wall);
            Panel(r, "End Wall", new Vector3(0f, 1.3f, 11f), Vector3.zero, 2f, 2.6f, Mat(0.56f));
            // Office door seen from outside: a flush slab narrowed by cos(angle) fakes it swinging away from us,
            // revealing the office light behind it.
            _corridorGapMat = Mat(0f, 0.32f, false);
            Panel(r, "Office Doorway", new Vector3(CorridorDoorX, 1.05f, 10.994f), Vector3.zero, DoorW, DoorH, _corridorGapMat);
            _corridorDoor = Box(r, "Office Door", new Vector3(CorridorDoorX, 1.06f, CorridorDoorZ), new Vector3(0.92f, 2.12f, 0.045f), wood);
            Box(r, "Door 2", new Vector3(-0.985f, 1.05f, 3.4f), new Vector3(0.04f, 2.1f, 0.9f), wood);
            Box(r, "Door 3", new Vector3(0.985f, 1.05f, 6.2f), new Vector3(0.04f, 2.1f, 0.9f), wood);
            _corridorLampMat = Mat(0.5f, 0.9f, false);
            Box(r, "Lamp A", new Vector3(0f, 2.575f, 3f), new Vector3(0.22f, 0.05f, 1.1f), _corridorLampMat);
            Box(r, "Lamp B", new Vector3(0f, 2.575f, 7.6f), new Vector3(0.22f, 0.05f, 1.1f), _corridorLampMat);
            SetLighting(a, new Vector3(0f, 2.5f, 2.4f), new Vector3(0f, 2.5f, 9.6f), 1f, 3.8f, 0.06f, 0.06f);
            AddCamera(a, "CAM 02", new Vector3(0.52f, 2.32f, 0.2f), new Vector3(-0.12f, 0.85f, 11f), 70f, 25f);
            return a;
        }

        // CAM 03: the operator's office, 3 x 3.8 m. The north wall is behind the camera and never seen.
        Area BuildOffice()
        {
            var a = NewArea("Office", Vector3.zero);
            var r = a.Root;
            var wall = Mat(0.52f);
            var wood = Mat(0.26f);
            var skin = Mat(0.64f);
            var shirt = Mat(0.56f);
            var fabric = Mat(0.16f);
            // The floor runs on under the camera so the steep bottom corner of the frame never sees past it.
            Panel(r, "Floor", new Vector3(0f, 0f, 0.35f), new Vector3(90f, 0f, 0f), 2f * RoomHalfW, 2f * RoomHalfD + 0.7f, Mat(0.30f));
            Panel(r, "Ceiling", new Vector3(0f, RoomH, 0f), new Vector3(-90f, 0f, 0f), 2f * RoomHalfW, 2f * RoomHalfD, Mat(0.55f));
            Panel(r, "West Wall", new Vector3(-RoomHalfW, RoomH / 2f, 0.25f), new Vector3(0f, -90f, 0f), 2f * RoomHalfD + 0.5f, RoomH, wall);
            Panel(r, "East Wall", new Vector3(RoomHalfW, RoomH / 2f, 0f), new Vector3(0f, 90f, 0f), 2f * RoomHalfD, RoomH, wall);
            Panel(r, "South Wall", new Vector3(0f, RoomH / 2f, -RoomHalfD), new Vector3(0f, 180f, 0f), 2f * RoomHalfW, RoomH, wall);

            // The door: a pale slab swinging into the room on its west jamb, in front of a dark doorway void (dim
            // corridor light, bright enough that the black figure still reads against it). Pale door + dark gap
            // makes "slightly ajar" noticeable at 160x120.
            Panel(r, "Doorway", new Vector3(DoorX, DoorH / 2f, -RoomHalfD + 0.006f), new Vector3(0f, 180f, 0f), DoorW, DoorH, Mat(0f, 0.2f));
            _doorHinge = Pivot(r, "Door Hinge", new Vector3(DoorX - DoorW / 2f, 0f, -RoomHalfD + 0.0405f), Vector3.zero);
            Box(_doorHinge, "Door", new Vector3(0.46f, 1.06f, 0f), new Vector3(0.92f, 2.12f, 0.045f), Mat(0.62f));

            // Desk against the west wall with a CRT (emissive screen facing east), keyboard, chair.
            float deskX = -RoomHalfW + 0.375f;
            Box(r, "Desk", new Vector3(deskX, 0.74f, DeskZ), new Vector3(0.75f, 0.04f, 1.5f), Mat(0.30f));
            Box(r, "Desk Drawers", new Vector3(deskX, 0.36f, DeskZ + 0.51f), new Vector3(0.68f, 0.72f, 0.42f), wood);
            Box(r, "Desk Side", new Vector3(deskX, 0.36f, DeskZ - 0.72f), new Vector3(0.68f, 0.72f, 0.04f), wood);
            Box(r, "CRT", new Vector3(-RoomHalfW + 0.22f, 0.955f, DeskZ), new Vector3(0.42f, 0.39f, 0.42f), Mat(0.62f));
            _screenMat = Mat(0f, 0.85f, false);
            Panel(r, "Screen", new Vector3(MonitorX, 0.965f, DeskZ), new Vector3(0f, -90f, 0f), 0.32f, 0.25f, _screenMat);
            Box(r, "Keyboard", new Vector3(-RoomHalfW + 0.58f, 0.772f, DeskZ - 0.02f), new Vector3(0.16f, 0.024f, 0.46f), Mat(0.55f));
            Box(r, "Chair Seat", new Vector3(SeatX, 0.47f, DeskZ), new Vector3(0.46f, 0.07f, 0.48f), fabric);
            Prim(PrimitiveType.Cube, r, "Chair Back", new Vector3(SeatX + 0.25f, 0.8f, DeskZ), new Vector3(0f, 0f, -8f), new Vector3(0.06f, 0.42f, 0.46f), fabric);
            Prim(PrimitiveType.Cylinder, r, "Chair Post", new Vector3(SeatX, 0.22f, DeskZ), Vector3.zero, new Vector3(0.06f, 0.22f, 0.06f), Mat(0.1f));

            // The seated operator (the player), facing the monitor (-X). Torso and head pivots are unscaled.
            _torso = Pivot(r, "Operator", new Vector3(SeatX, 0.52f, DeskZ), new Vector3(6f, -90f, 0f));
            Prim(PrimitiveType.Capsule, _torso, "Torso", new Vector3(0f, 0.33f, 0f), Vector3.zero, new Vector3(0.4f, 0.33f, 0.24f), shirt);
            _lap = Box(r, "Lap", new Vector3(SeatX - 0.2f, 0.53f, DeskZ), new Vector3(0.42f, 0.14f, 0.36f), Mat(0.2f));
            _head = Pivot(_torso, "Head Pivot", NeckLocal, Vector3.zero);
            Prim(PrimitiveType.Sphere, _head, "Face", new Vector3(0f, 0.11f, 0.015f), Vector3.zero, new Vector3(0.19f, 0.23f, 0.21f), skin);
            // Dark hair covers the back of the head, so only a turned head shows the pale face.
            Prim(PrimitiveType.Sphere, _head, "Hair", new Vector3(0f, 0.125f, -0.02f), Vector3.zero, new Vector3(0.2f, 0.22f, 0.2f), Mat(0.09f));
            _upperArm = Prim(PrimitiveType.Cylinder, r, "Upper Arm", Vector3.zero, Vector3.zero, new Vector3(0.1f, 0.15f, 0.1f), shirt);
            _forearm = Prim(PrimitiveType.Cylinder, r, "Forearm", Vector3.zero, Vector3.zero, new Vector3(0.085f, 0.15f, 0.085f), skin);
            _hand = Prim(PrimitiveType.Sphere, r, "Hand", Vector3.zero, Vector3.zero, new Vector3(0.085f, 0.05f, 0.11f), skin);
            BuildKeyboardArm(r, shirt, skin);

            // Detail: filing cabinet by the door, fluorescent tube, wall clock, a notice on the wall.
            Box(r, "Filing Cabinet", new Vector3(-RoomHalfW + 0.3f, 0.66f, -RoomHalfD + 0.3f), new Vector3(0.6f, 1.32f, 0.5f), Mat(0.44f));
            _tubeMat = Mat(0.5f, 0.95f, false);
            Box(r, "Fluorescent Tube", new Vector3(-0.1f, RoomH - 0.05f, -0.05f), new Vector3(0.24f, 0.07f, 1.25f), _tubeMat);
            Prim(PrimitiveType.Cylinder, r, "Wall Clock", new Vector3(-0.45f, 2.05f, -RoomHalfD + 0.012f), new Vector3(90f, 0f, 0f), new Vector3(0.3f, 0.015f, 0.3f), Mat(0.8f));
            Panel(r, "Notice", new Vector3(-RoomHalfW + 0.006f, 1.6f, -0.75f), new Vector3(0f, -90f, 0f), 0.7f, 0.5f, Mat(0.74f));

            SetLighting(a, new Vector3(-0.1f, 2.51f, 0.55f), new Vector3(-0.1f, 2.51f, -0.65f), 1.5f, 2.4f, 0.1f, 0.06f);
            AddCamera(a, "CAM 03", new Vector3(RoomHalfW - 0.15f, 2.34f, RoomHalfD - 0.15f), new Vector3(-0.5f, 0.92f, -0.45f), 70f, 20f);
            return a;
        }

        // The figure: ~2.1 m, too thin, arms hanging to the knees, no face, near-black. Six primitives.
        Transform BuildFigure()
        {
            var root = Pivot(_setRoot, "Figure", Vector3.zero, Vector3.zero);
            var black = Mat(0.012f);
            Prim(PrimitiveType.Cylinder, root, "Leg L", new Vector3(-0.085f, 0.49f, 0f), Vector3.zero, new Vector3(0.11f, 0.49f, 0.11f), black);
            Prim(PrimitiveType.Cylinder, root, "Leg R", new Vector3(0.085f, 0.49f, 0f), Vector3.zero, new Vector3(0.11f, 0.49f, 0.11f), black);
            Prim(PrimitiveType.Capsule, root, "Body", new Vector3(0f, 1.36f, 0f), Vector3.zero, new Vector3(0.3f, 0.45f, 0.17f), black);
            Prim(PrimitiveType.Sphere, root, "Head", new Vector3(0f, 1.95f, 0.01f), Vector3.zero, new Vector3(0.17f, 0.26f, 0.19f), black);
            Prim(PrimitiveType.Cylinder, root, "Arm L", new Vector3(-0.205f, 1.16f, 0f), new Vector3(0f, 0f, -3f), new Vector3(0.065f, 0.6f, 0.065f), black);
            Prim(PrimitiveType.Cylinder, root, "Arm R", new Vector3(0.205f, 1.16f, 0f), new Vector3(0f, 0f, 3f), new Vector3(0.065f, 0.6f, 0.065f), black);
            return root;
        }

        // ------------------------------------------------------------------------------------------ runtime

        Area AreaFor(string camId)
        {
            if (camId == ContentIds.Cam01) return _lobby;
            if (camId == ContentIds.Cam02) return _corridor;
            if (camId == ContentIds.Cam03) return _office;
            return ExtraAreaFor(camId);
        }

        /// <summary>Cuts to a stalking position. The committed capture attack has a separate visible rush.</summary>
        void SetFigure(FigureStage stage)
        {
            ResetCapturePose();
            // The frame it moves on lands with a sound (it is never seen moving). Phase M: in the office it is a step on the carpet,
            // louder every time (M6: closer every time you look); anywhere else the building's thump.
            if (stage != _figureStage && stage != FigureStage.None && stage != FigureStage.AtLens && !Quiet)
            {
                bool inRoom = stage >= FigureStage.Doorway && stage <= FigureStage.BehindChair;
                _g?.Audio?.Play(inRoom ? "step_near" : "low_thump", StepVolume(stage), inRoom ? 1f : 0.95f);
            }
            _figureStage = stage;
            // It only ever stands in a wide-open doorway, and the door opened between frames like everything it does.
            if (stage == FigureStage.Doorway && _doorTarget < DoorwayMinOpen)
            {
                _doorTarget = _door = DoorwayMinOpen;
                _doorVelocity = 0f;
                ApplyDoor();
            }
            if (_figure == null) return;
            Area area = _office;
            Vector3 pos;
            float yaw, pitch = 0f;
            switch (stage)
            {
                case FigureStage.HallFar: area = _corridor; pos = new Vector3(0f, 0f, 3.0f); yaw = 0f; break;   // near end of the hall, walking away
                case FigureStage.Corridor: area = _corridor; pos = new Vector3(0.42f, 0f, 10.3f); yaw = 180f; break;   // beside the office door, facing CAM 02
                case FigureStage.Doorway: pos = new Vector3(DoorX, 0f, -RoomHalfD + 0.13f); yaw = 0f; break;
                case FigureStage.Middle: pos = new Vector3(0.22f, 0f, -0.62f); yaw = 300f; break;
                case FigureStage.BehindChair: pos = new Vector3(SeatX + 0.48f, 0f, DeskZ - 0.33f); yaw = 280f; break;   // behind, a little to the left
                case FigureStage.AtLens:
                {
                    // Phase M: the KEEP hit's frame: its head right in front of CAM 03's lens, facing it.
                    var lens = _office.Cam.transform;
                    Vector3 toLens = -lens.forward;
                    toLens.y = 0f;
                    _figure.SetPositionAndRotation(lens.position + lens.forward * 0.4f - Vector3.up * 1.95f, Quaternion.LookRotation(toLens.normalized));
                    _figure.gameObject.SetActive(true);
                    return;
                }
                default:
                    if (!ExtraStage(stage, out area, out pos, out yaw, out pitch))
                    {
                        _figure.gameObject.SetActive(false);
                        return;
                    }
                    break;
            }
            _figure.SetPositionAndRotation(area.Root.TransformPoint(pos), area.Root.rotation * Quaternion.Euler(pitch, yaw, 0f));
            _figure.gameObject.SetActive(true);
        }

        static float StepVolume(FigureStage stage) => stage == FigureStage.Doorway ? 0.6f : stage == FigureStage.Middle ? 0.8f : stage == FigureStage.BehindChair ? 1f : 0.55f;

        /// <summary>Only the watched camera renders; switching (or opening the app) is a cut.</summary>
        void RefreshCameras()
        {
            var want = _viewing && HasSignal(ActiveCamera) ? AreaFor(ActiveCamera) : null;
            if (want == _active) return;
            if (_active != null) EnableArea(_active, false);
            // M1: the office feed just came up: the seated arm plays dead for a moment before it answers the mouse.
            if (want == _office && _office != null) _officeSince = Time.time;
            // M10: the loop starts at shelf 17 the first time CAM 04 comes up. Phase H: after that it keeps running while
            // you look elsewhere, so a viewer that keeps being closed or switched still reaches every shelf in one cycle.
            if (want != null && want == _sublevel && _cam04Since < 0f) _cam04Since = Time.time;
            _active = want;
            if (_active == null) return;
            EnableArea(_active, true);
            Tick(0f, true);   // everything jumps to its current state before the first frame renders
        }

        static void EnableArea(Area a, bool on)
        {
            a.Cam.enabled = on;
            if (a.Fallback != null) a.Fallback.enabled = on;
        }

        void LateUpdate()
        {
            RefreshCameras();   // picks up SignalLost
            if (_active != null && !FreezeFeed) Tick(Time.deltaTime, false);
        }

        void Tick(float dt, bool cut)
        {
            float t = Time.time;
            _lightLevel = Flicker(dt);
            UpdateDoor(dt, cut);
            if (_active == _office) UpdateOperator(dt, t, cut);

            // Faint sway of the camera housing: the picture is never perfectly still (CAM 04 also pans the aisle).
            float pitch = (Mathf.PerlinNoise(t * 0.21f, 4.2f) - 0.5f) * 0.6f;
            float yaw = (Mathf.PerlinNoise(9.7f, t * 0.19f) - 0.5f) * 0.6f + PanFor(_active, t);
            _active.Cam.transform.localRotation = _active.CamRotation * Quaternion.Euler(pitch, yaw, 0f);

            ApplyLighting(_active, t);
        }

        void UpdateDoor(float dt, bool cut)
        {
            float target = _figureStage == FigureStage.Doorway ? Mathf.Max(_doorTarget, DoorwayMinOpen) : _doorTarget;
            if (cut)
            {
                _door = target;
                _doorVelocity = 0f;
            }
            else if (dt > 0f)
            {
                _door = Mathf.SmoothDamp(_door, target, ref _doorVelocity, 0.8f, 1.5f, dt);   // a slow creak
            }
            ApplyDoor();
        }

        void ApplyDoor()
        {
            if (_doorHinge == null) return;
            float angle = DoorMaxAngle * _door;
            _doorHinge.localRotation = Quaternion.Euler(0f, -angle, 0f);
            // From the corridor the door swings away from the camera: its visible width shrinks by cos(angle).
            float c = Mathf.Cos(angle * Mathf.Deg2Rad);
            _corridorDoor.localScale = new Vector3(0.92f * c, 2.12f, 0.045f);
            _corridorDoor.localPosition = new Vector3(CorridorDoorX - 0.46f + 0.46f * c, 1.06f, CorridorDoorZ);
        }

        void UpdateOperator(float dt, float t, bool cut)
        {
            // The head comes round slowly; idle life dies away while it stares up at the camera.
            float turnTarget = Mathf.Clamp01(SeatedHeadTurn);
            _headTurn = cut ? turnTarget : Mathf.MoveTowards(_headTurn, turnTarget, dt * 0.6f);
            float turn = Mathf.SmoothStep(0f, 1f, _headTurn);
            float idle = 1f - turn;

            float breath = Mathf.Sin(t * 1.45f) * 0.8f * idle;
            _torso.localRotation = Quaternion.Euler(6f + breath, -90f + 18f * turn, 0f);
            float glanceYaw = (Mathf.PerlinNoise(t * 0.17f, 1.3f) - 0.5f) * 10f * idle;
            float glancePitch = (Mathf.PerlinNoise(t * 0.23f, 7.1f) - 0.5f) * 5f * idle;
            var atMonitor = _torso.rotation * Quaternion.Euler(12f + glancePitch, glanceYaw, 0f);
            var atCamera = Quaternion.LookRotation(_office.Cam.transform.position - _head.position);
            _head.rotation = Quaternion.Slerp(atMonitor, atCamera, turn);

            // The mouse hand follows the player's real mouse (how they recognise themselves), else idles on the mouse.
            // M1: for the first 1.2 s after the feed opens it ignores the mouse (the streamer wiggles, nothing), then it
            // answers with a 0.3 s lag for a while before it tracks normally.
            float watched = Time.time - _officeSince;
            bool mimic = SeatedMimicsPlayer && watched >= ArmDeadSeconds;
            var target = mimic
                ? new Vector2(Mathf.Clamp(PlayerHand.x, -1f, 1f), Mathf.Clamp(PlayerHand.y, -1f, 1f))
                : new Vector2((Mathf.PerlinNoise(t * 0.35f, 3.3f) - 0.5f) * 0.6f, (Mathf.PerlinNoise(t * 0.29f, 8.8f) - 0.5f) * 0.5f);
            float follow = mimic && watched < ArmDeadSeconds + ArmLagWindow ? 1f / ArmLagSeconds : 14f;
            _handPos = cut ? target : Vector2.Lerp(_handPos, target, 1f - Mathf.Exp(-dt * follow));
            UpdateTyping(dt);
            UpdateArm();
            UpdateKeyboardArm();

            _monitorPulse = 1f + 0.04f * Mathf.Sin(t * 1.7f) + 0.05f * (Mathf.PerlinNoise(t * 2.3f, 0.5f) - 0.5f);
        }

        void UpdateArm()
        {
            var room = _office.Root;
            Vector3 shoulder = _torso.TransformPoint(ShoulderLocal);
            // Mouse area right of the keyboard: player x -> the operator's right (+Z), player y -> forward (-X).
            Vector3 wrist = room.TransformPoint(new Vector3(-RoomHalfW + 0.62f - _handPos.y * 0.12f, 0.8f, DeskZ + 0.37f + _handPos.x * 0.17f));
            // Phase Q3 (V10): while the player types, the mouse hand comes back to the keyboard and dips with the keys.
            if (_typingBlend > 0f) wrist = Vector3.Lerp(wrist, room.TransformPoint(KeyboardWrist(1f)), _typingBlend);
            Vector3 elbow = SolveElbow(shoulder, ref wrist, room.TransformDirection(ElbowPole));
            SetLimb(_upperArm, shoulder, elbow);
            SetLimb(_forearm, elbow, wrist);
            Vector3 dir = (wrist - elbow).normalized;
            _hand.SetPositionAndRotation(wrist + dir * 0.05f, Quaternion.LookRotation(dir));
        }

        /// <summary>Two-bone IK: the elbow for shoulder -> wrist, bending toward the pole. Clamps the wrist to reach.</summary>
        static Vector3 SolveElbow(Vector3 shoulder, ref Vector3 wrist, Vector3 pole)
        {
            Vector3 d = wrist - shoulder;
            Vector3 dir = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.down;
            float dist = Mathf.Clamp(d.magnitude, Mathf.Abs(UpperArm - Forearm) + 0.01f, UpperArm + Forearm - 0.001f);
            wrist = shoulder + dir * dist;
            float along = (UpperArm * UpperArm - Forearm * Forearm + dist * dist) / (2f * dist);
            float bend = Mathf.Sqrt(Mathf.Max(0f, UpperArm * UpperArm - along * along));
            return shoulder + dir * along + Vector3.ProjectOnPlane(pole, dir).normalized * bend;
        }

        /// <summary>Stretches a unit cylinder (height 2) between two world points.</summary>
        static void SetLimb(Transform limb, Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float length = Mathf.Max(d.magnitude, 0.001f);
            limb.SetPositionAndRotation((from + to) * 0.5f, Quaternion.FromToRotation(Vector3.up, d / length));
            var s = limb.localScale;
            limb.localScale = new Vector3(s.x, length * 0.5f, s.z);
        }

        const float ReducedFlicker = 0.1f;

        /// <summary>Fluorescent flicker: random brown-outs while LightFlicker > 0, plus a faint constant shimmer.</summary>
        float Flicker(float dt)
        {
            float f = Mathf.Clamp01(LightFlicker);
            // Review M2: Reduce flashing keeps the lamp to a rare, shallow dip (the finale's and the reveal's full flicker dipped 2 to 3 times a second).
            if (_g != null && _g.Fx != null && _g.Fx.ReduceFlashing) f = Mathf.Min(f, ReducedFlicker);
            if (_dipTime > 0f)
            {
                _dipTime -= dt;
            }
            else if (f > 0f && Random.value < f * dt * 2.5f)
            {
                _dipTime = Random.Range(0.05f, 0.35f) * (0.5f + f);
                _dipDepth = Random.Range(0.4f, 1f) * Mathf.Lerp(0.6f, 1f, f);
            }
            float level = _dipTime > 0f ? 1f - _dipDepth * Random.Range(0.75f, 1f) : 1f;
            return level * (1f - (0.012f + 0.06f * f) * Random.value);
        }

        /// <summary>Per-area lamp/screen glow and the shader globals for the camera about to render.</summary>
        void ApplyLighting(Area a, float t)
        {
            float level = _lightLevel;                    // the whole building shares one flickering supply
            float officeLevel = LightsOn ? level : 0f;    // LightsOn only concerns the office
            float intensity = a.LightIntensity * level;
            float ambient = a.Ambient;
            float monitor = 0f;
            Transform monitorRoot = _office.Root;
            if (a == _office)
            {
                intensity = a.LightIntensity * officeLevel;
                if (!LightsOn) ambient = 0.06f;
                monitor = MonitorGlow * _monitorPulse;
                SetGrey(_tubeMat, 0.5f, LightsOn ? 0.95f * level : 0.02f);
                SetGrey(_screenMat, 0f, 0.85f * _monitorPulse);
            }
            else if (a == _corridor)
            {
                SetGrey(_corridorLampMat, 0.5f, 0.9f * level);
                SetGrey(_corridorGapMat, 0f, 0.05f + 0.3f * officeLevel);   // office light spilling past the door
            }
            else if (a == _lobby)
            {
                // Dawn (the LOG OFF ending): brighter glass doors, and the lamp is off once it is light out.
                float dawn = Mathf.Clamp01(DawnLevel);
                bool lampOff = dawn > 0.5f;
                SetGrey(_lobbyLampMat, 0.5f, lampOff ? 0.02f : 0.9f * level);
                SetGrey(_lobbyGlassMat, 0.05f, 0.2f + 0.5f * dawn);
                // Grey morning light through the doors instead of the lamp.
                if (lampOff) intensity = a.LightIntensity * 0.3f;
                ambient = a.Ambient + 0.3f * dawn;
                int minutes = _g != null && _g.Clock != null ? _g.Clock.TotalMinutes : 167 + (int)(t / 60f);
                _clockHand.localRotation = Quaternion.Euler(0f, 0f, -(minutes % 60) * 6f);
            }
            else
            {
                ApplyExtraLighting(a, t, level, ref intensity, ref ambient, ref monitor, ref monitorRoot);
            }

            if (a.Fallback != null)
            {
                a.Fallback.intensity = 2f * (a == _office ? Mathf.Max(officeLevel, 0.15f) : level);
                return;
            }
            var r = a.Root;
            Shader.SetGlobalVector(LightAId, r.TransformPoint(a.LightA));
            Shader.SetGlobalVector(LightBId, r.TransformPoint(a.LightB));
            Shader.SetGlobalFloat(LightIntensityId, intensity);
            Shader.SetGlobalFloat(LightRangeId, a.LightRange);
            Shader.SetGlobalFloat(AmbientId, ambient);
            Shader.SetGlobalVector(MonitorPosId, monitorRoot.TransformPoint(new Vector3(MonitorX, 0.965f, DeskZ)));
            Shader.SetGlobalVector(MonitorDirId, monitorRoot.TransformDirection(Vector3.right));
            Shader.SetGlobalFloat(MonitorIntensityId, monitor);
            Shader.SetGlobalVector(FogColorId, new Vector4(0.03f, 0.03f, 0.03f, 1f));
            Shader.SetGlobalFloat(FogDensityId, a.FogDensity);
            Shader.SetGlobalVector(CameraPosId, a.Cam.transform.position);
        }

        void OnDestroy()
        {
            foreach (var a in new[] { _lobby, _corridor, _office, _sublevel, _admin })
                if (a != null && a.Cam != null) a.Cam.targetTexture = null;
            if (Feed != null)
            {
                Feed.Release();
                Destroy(Feed);
            }
            foreach (var m in _materials) if (m != null) Destroy(m);
        }
    }
}
