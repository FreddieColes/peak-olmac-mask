// Ol' Mac Mask for PEAK
// Adds a flat cardboard Ol' Mac face mask with a strap round the head as a new hat.
// Everything (mesh, material, passport icon) is built from olmac-face.png at load time, so no Unity or AssetBundles.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace OlMacMask
{
    [BepInPlugin(Guid, "Ol' Mac Mask", "0.6.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.freddiecoles.olmacmask";
        public const string MaskName = "OlMacMask";

        internal static Plugin I;
        internal static ManualLogSource Log;

        internal static Texture2D Face;
        internal static Mesh MaskMesh, BoardMesh, StrapMesh;
        internal static Material MaskMat, BoardMat, StrapMat;
        internal static float Aspect = 0.75f;
        internal static int HatIndex = -1;
        static readonly HashSet<CustomizationOption> ShiftedFits = new HashSet<CustomizationOption>();

        internal static ConfigEntry<float> MaskWidth, Forward, Up, Right, RotX, RotY, RotZ;
        internal static ConfigEntry<float> StrapRadiusX, StrapRadiusZ, StrapHeight, StrapUp;
        internal static ConfigEntry<string> StrapColour, BoardColour, ShaderName;
        internal static ConfigEntry<float> BoardBorder;
        internal static ConfigEntry<bool> HideForSelf, Flip, AutoFlip;
        internal static ConfigEntry<Key> ReloadKey, DebugKey, AdjustKey;

        Customization _lastCustomization;
        float _nextCheck;
        static bool _loggedSetup;

        void Awake()
        {
            I = this;
            Log = Logger;

            const string P = "1. Placement (metres)";
            MaskWidth = Config.Bind(P, "MaskWidth", 0.5f, "Width of the mask");
            Forward = Config.Bind(P, "Forward", 0.22f, "How far in front of the head bone the mask sits");
            Up = Config.Bind(P, "Up", 0.28f, "Height above the head bone (which sits low, near the neck)");
            Right = Config.Bind(P, "Right", 0f, "Left/right nudge");
            RotX = Config.Bind(P, "RotX", 0f, "Extra tilt (degrees)");
            RotY = Config.Bind(P, "RotY", 0f, "Extra turn (degrees)");
            RotZ = Config.Bind(P, "RotZ", 0f, "Extra roll (degrees)");
            Flip = Config.Bind(P, "Flip", false, "Turn the mask round to the other side of the head");
            AutoFlip = Config.Bind(P, "AutoFlip", true, "On first sight, flip the mask if it's on the back of your head");

            const string S = "2. Strap (metres)";
            StrapRadiusX = Config.Bind(S, "RadiusSideToSide", 0.17f, "Half the head width");
            StrapRadiusZ = Config.Bind(S, "RadiusFrontToBack", 0.17f, "Half the head depth");
            StrapHeight = Config.Bind(S, "Thickness", 0.02f, "How tall the band is");
            StrapUp = Config.Bind(S, "Up", 0.02f, "Band height relative to the middle of the mask");
            StrapColour = Config.Bind(S, "Colour", "F28AB2", "Hex colour of the rubber band");

            const string B = "3. Cardboard";
            BoardColour = Config.Bind(B, "Colour", "B8915F", "Hex colour of the cardboard");
            BoardBorder = Config.Bind(B, "Border", 2f, "How far the cardboard sticks out round the drawing (in grid cells, roughly 1% of the width each)");

            const string M = "4. Misc";
            HideForSelf = Config.Bind(M, "HideForSelf", true, "Hide your own mask from your first-person camera only (mirror, passport and other players still see it)");
            ShaderName = Config.Bind(M, "Shader", "", "Leave blank to copy the game's own hat material (recommended)");
            ReloadKey = Config.Bind(M, "ReloadKey", Key.F9, "Press in game to reload this config and re-place the mask");
            DebugKey = Config.Bind(M, "DebugKey", Key.F10, "Press in game to write where every mask is into the log");
            AdjustKey = Config.Bind(M, "AdjustKey", Key.F7, "Hold and use arrows (up/down = height, left/right = back/forward), Page Up/Down = size, F6 = flip");

            LoadFace();
            new Harmony(Guid).PatchAll();
            Log.LogInfo("Ol' Mac Mask loaded");
        }

        void Update()
        {
            if (Time.unscaledTime > _nextCheck)
            {
                _nextCheck = Time.unscaledTime + 1f;
                var c = Object.FindFirstObjectByType<Customization>(FindObjectsInactive.Include);
                if (c != null && c != _lastCustomization) { EnsureOption(c); _lastCustomization = c; }
            }
            var kb = Keyboard.current;
            if (kb != null && kb[ReloadKey.Value].wasPressedThisFrame) ReloadPlacement();
            if (kb != null && kb[DebugKey.Value].wasPressedThisFrame) MaskController.DumpAll();
            if (kb != null && kb[AdjustKey.Value].isPressed) Adjust(kb);
        }

        // Live nudging while holding the adjust key. Values save to the config straight away.
        void Adjust(Keyboard kb)
        {
            bool changed = false;
            void Nudge(ConfigEntry<float> e, float d) { e.Value = Mathf.Round((e.Value + d) * 1000f) / 1000f; changed = true; }
            if (kb.upArrowKey.wasPressedThisFrame) Nudge(Up, 0.01f);
            if (kb.downArrowKey.wasPressedThisFrame) Nudge(Up, -0.01f);
            if (kb.rightArrowKey.wasPressedThisFrame) Nudge(Forward, 0.01f);
            if (kb.leftArrowKey.wasPressedThisFrame) Nudge(Forward, -0.01f);
            if (kb.pageUpKey.wasPressedThisFrame) Nudge(MaskWidth, 0.02f);
            if (kb.pageDownKey.wasPressedThisFrame) Nudge(MaskWidth, -0.02f);
            if (kb.f6Key.wasPressedThisFrame) { Flip.Value = !Flip.Value; changed = true; }
            if (!changed) return;
            foreach (var m in MaskController.All.ToArray()) if (m != null) m.Apply();
            Log.LogInfo($"Adjusted: width {MaskWidth.Value}, forward {Forward.Value}, up {Up.Value}, flip {Flip.Value}");
        }

        void ReloadPlacement()
        {
            Config.Reload();
            BuildStrapMesh();
            BuildBoardMesh();
            if (StrapMat != null) SetTexture(StrapMat, SolidTexture(ParseColour(StrapColour.Value)));
            if (BoardMat != null) SetTexture(BoardMat, SolidTexture(ParseColour(BoardColour.Value)));
            foreach (var m in MaskController.All.ToArray()) if (m != null) m.Apply();
            Log.LogInfo($"Reloaded placement: width {MaskWidth.Value}, fwd {Forward.Value}, up {Up.Value}, right {Right.Value}, rot ({RotX.Value},{RotY.Value},{RotZ.Value})");
        }

        // ---------- assets ----------

        void LoadFace()
        {
            var path = Path.Combine(Path.GetDirectoryName(Info.Location), "olmac-face.png");
            Face = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = MaskName };
            if (!File.Exists(path) || !ImageConversion.LoadImage(Face, File.ReadAllBytes(path), false))
            {
                Log.LogError("Couldn't load " + path);
                return;
            }
            Face.wrapMode = TextureWrapMode.Clamp;
            Face.filterMode = FilterMode.Bilinear;
            Aspect = (float)Face.height / Face.width;
            BuildMaskMesh();
            BuildBoardMesh();
            BuildStrapMesh();
            Log.LogInfo($"Face {Face.width}x{Face.height}");
        }

        const int Cols = 128;
        static int Rows => Mathf.Max(1, Mathf.RoundToInt(Cols * Aspect));
        static bool[,] _cells;

        // Which grid cells the drawing covers, so the mesh itself is the outline (works even if the shader can't do transparency).
        static bool[,] Cells()
        {
            if (_cells != null) return _cells;
            int rows = Rows, w = Face.width, h = Face.height;
            var px = Face.GetPixels32();
            bool Solid(float u, float v)
            {
                int x = Mathf.Clamp((int)(u * w), 0, w - 1), y = Mathf.Clamp((int)(v * h), 0, h - 1);
                return px[y * w + x].a > 100;
            }
            _cells = new bool[Cols, rows];
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < Cols; i++)
                {
                    float cu = (i + 0.5f) / Cols, cv = (j + 0.5f) / rows, du = 0.5f / Cols, dv = 0.5f / rows;
                    _cells[i, j] = Solid(cu, cv) || Solid(cu - du, cv - dv) || Solid(cu + du, cv - dv) || Solid(cu - du, cv + dv) || Solid(cu + du, cv + dv);
                }
            return _cells;
        }

        // Grid mesh over the picture. Front faces +Z (towards whoever is looking at you), optional back side.
        static Mesh GridMesh(string name, bool[,] cells, float z, bool front, bool back)
        {
            int rows = cells.GetLength(1), stride = Cols + 1, count = stride * (rows + 1);
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var norms = new List<Vector3>(); var tris = new List<int>();
            for (int side = 0; side < 2; side++)
                for (int j = 0; j <= rows; j++)
                    for (int i = 0; i <= Cols; i++)
                    {
                        float u = (float)i / Cols, v = (float)j / rows;
                        verts.Add(new Vector3(0.5f - u, (v - 0.5f) * Aspect, z));
                        uvs.Add(new Vector2(u, v));
                        norms.Add(side == 0 ? Vector3.forward : Vector3.back);
                    }
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < Cols; i++)
                {
                    if (!cells[i, j]) continue;
                    int a = j * stride + i, b = a + stride, c = a + 1, d = b + 1;
                    if (front) tris.AddRange(new[] { a, b, c, c, b, d });
                    int o = count;
                    if (back) tris.AddRange(new[] { o + a, o + c, o + b, o + c, o + d, o + b });
                }
            var m = new Mesh { name = name };
            if (verts.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetNormals(norms); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        static void BuildMaskMesh()
        {
            MaskMesh = GridMesh(MaskName, Cells(), 0f, true, false);
        }

        // Cardboard: the drawing's outline grown by a border, sitting just behind the drawing, visible from both sides.
        internal static void BuildBoardMesh()
        {
            var src = Cells();
            int rows = src.GetLength(1), r = Mathf.Max(0, Mathf.RoundToInt(BoardBorder.Value));
            var cells = new bool[Cols, rows];
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < Cols; i++)
                {
                    bool hit = false;
                    for (int dj = -r; dj <= r && !hit; dj++)
                        for (int di = -r; di <= r && !hit; di++)
                        {
                            if (di * di + dj * dj > r * r) continue;
                            int x = i + di, y = j + dj;
                            hit = x >= 0 && y >= 0 && x < Cols && y < rows && src[x, y];
                        }
                    cells[i, j] = hit;
                }
            BoardMesh = GridMesh(MaskName + "Board", cells, -0.006f, true, true);
        }

        // Elliptical band round the head, in metres, centred behind the mask. Outer and inner faces.
        internal static void BuildStrapMesh()
        {
            int seg = 48;
            float rx = StrapRadiusX.Value, rz = StrapRadiusZ.Value, hh = StrapHeight.Value * 0.5f, y = StrapUp.Value;
            float cz = -(rz + 0.01f);
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            for (int side = 0; side < 2; side++)
                for (int s = 0; s <= seg; s++)
                {
                    float t = s * Mathf.PI * 2f / seg;
                    var p = new Vector3(rx * Mathf.Sin(t), 0, cz + rz * Mathf.Cos(t));
                    var n = new Vector3(Mathf.Sin(t) / rx, 0, Mathf.Cos(t) / rz).normalized * (side == 0 ? 1 : -1);
                    verts.Add(p + Vector3.up * (y - hh)); verts.Add(p + Vector3.up * (y + hh));
                    norms.Add(n); norms.Add(n);
                    uvs.Add(new Vector2((float)s / seg, 0)); uvs.Add(new Vector2((float)s / seg, 1));
                }
            int half = (seg + 1) * 2;
            for (int s = 0; s < seg; s++)
            {
                int a = s * 2, b = a + 1, c = a + 2, d = a + 3;
                tris.AddRange(new[] { a, c, b, b, c, d });
                tris.AddRange(new[] { half + a, half + b, half + c, half + b, half + d, half + c });
            }
            if (StrapMesh == null) StrapMesh = new Mesh { name = MaskName + "Strap" };
            StrapMesh.Clear();
            StrapMesh.SetVertices(verts); StrapMesh.SetNormals(norms); StrapMesh.SetUVs(0, uvs); StrapMesh.SetTriangles(tris, 0);
            StrapMesh.RecalculateBounds();
        }

        // Materials copy the game's own hat material (Unity's stock shaders are stripped from PEAK's build and draw nothing).
        // Colours are done with tiny solid textures so they work whatever the game shader's colour property is called.
        internal static void EnsureMaterials(Renderer template)
        {
            if (MaskMat != null) return;
            Material baseMat = template != null ? template.sharedMaterial : null;
            if (!string.IsNullOrEmpty(ShaderName.Value))
            {
                var sh = Shader.Find(ShaderName.Value);
                if (sh != null) baseMat = new Material(sh);
                else Log.LogWarning("Shader '" + ShaderName.Value + "' not found, using the game's hat material");
            }
            if (baseMat == null) { Log.LogError("No hat material to copy"); return; }
            MaskMat = Make(baseMat, Face, "Mat");
            BoardMat = Make(baseMat, SolidTexture(ParseColour(BoardColour.Value)), "BoardMat");
            StrapMat = Make(baseMat, SolidTexture(ParseColour(StrapColour.Value)), "StrapMat");
            var props = string.Join(", ", Enumerable.Range(0, baseMat.shader.GetPropertyCount())
                .Where(i => baseMat.shader.GetPropertyType(i) == ShaderPropertyType.Texture)
                .Select(i => baseMat.shader.GetPropertyName(i)));
            Log.LogInfo($"Materials copied from '{baseMat.name}', shader '{baseMat.shader.name}', texture slots: {props}");
        }

        static Material Make(Material baseMat, Texture tex, string suffix)
        {
            var m = new Material(baseMat) { name = MaskName + suffix };
            SetTexture(m, tex);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
            return m;
        }

        static void SetTexture(Material m, Texture tex)
        {
            m.mainTexture = tex;
            foreach (var n in new[] { "_MainTex", "_BaseMap", "_Texture", "_Albedo", "_MainTexture" })
                if (m.HasProperty(n)) m.SetTexture(n, tex);
        }

        static Texture2D SolidTexture(Color c)
        {
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = MaskName + "Solid" };
            var px = new Color[16];
            for (int i = 0; i < 16; i++) px[i] = c;
            t.SetPixels(px); t.Apply();
            return t;
        }

        static Color ParseColour(string hex)
        {
            return ColorUtility.TryParseHtmlString("#" + hex.TrimStart('#'), out var c) ? c : Color.magenta;
        }

        // ---------- injection ----------

        // Adds Ol' Mac to the passport's hat list.
        internal static void EnsureOption(Customization c)
        {
            try
            {
                if (c == null || c.hats == null || Face == null) return;
                int existing = System.Array.FindIndex(c.hats, o => o != null && o.name == MaskName);
                if (existing >= 0) { HatIndex = existing; return; }
                var tpl = c.hats.FirstOrDefault(o => o != null && !o.isBlank && !o.IsLocked) ?? c.hats.First(o => o != null);
                var opt = Object.Instantiate(tpl);
                opt.name = MaskName;
                opt.texture = Face;
                opt.isBlank = false;
                opt.testLocked = false;
                c.hats = c.hats.Append(opt).ToArray();
                HatIndex = c.hats.Length - 1;
                // Outfits that come with their own hat point at hat objects after the passport hats.
                // We insert the mask at HatIndex on every head, so those numbers move up by one.
                int shifted = 0;
                if (c.fits != null)
                    foreach (var f in c.fits)
                        if (f != null && f.overrideHat && f.overrideHatIndex >= HatIndex && ShiftedFits.Add(f)) { f.overrideHatIndex++; shifted++; }
                Log.LogInfo($"Added Ol' Mac to the passport as hat #{HatIndex} (template {tpl.name}), moved {shifted} outfit hats up one");
            }
            catch (System.Exception e) { Log.LogError("EnsureOption failed: " + e); }
        }

        // Hats are skinned to the head bone, the "Hat" object they live under doesn't move with the head.
        // So find the real head bone from the body's (or a hat's) skinned mesh.
        static bool _loggedBones;
        internal static Transform FindHeadBone(CustomizationRefs refs)
        {
            var smrs = new List<SkinnedMeshRenderer>();
            if (refs.mainRenderer != null) smrs.Add(refs.mainRenderer);
            smrs.AddRange(refs.playerHats.OfType<SkinnedMeshRenderer>());
            Transform best = null; int bestScore = 0;
            var seen = new HashSet<string>();
            foreach (var smr in smrs)
                foreach (var b in smr.bones)
                {
                    if (b == null) continue;
                    var n = b.name.ToLowerInvariant();
                    if (n.Contains("head")) seen.Add(b.name);
                    int score = n == "head" ? 3 : n.EndsWith("head") || n.EndsWith("_head") ? 2 : n.Contains("head") && !n.Contains("end") && !n.Contains("top") ? 1 : 0;
                    if (score > bestScore) { best = b; bestScore = score; }
                }
            if (!_loggedBones)
            {
                _loggedBones = true;
                Log.LogInfo($"Head-ish bones: {string.Join(", ", seen)}; using '{(best != null ? best.name : "none, falling back to Hat")}'");
            }
            return best != null ? best : refs.hatTransform;
        }

        // Adds the mask object to a character's (or the passport dummy's) list of hats.
        internal static void InjectRefs(CustomizationRefs refs, Component owner, bool isDummy)
        {
            try
            {
                if (refs == null || refs.hatTransform == null || refs.playerHats == null || MaskMesh == null) return;
                if (refs.playerHats.Any(r => r != null && r.name == MaskName)) return;

                var c = Object.FindFirstObjectByType<Customization>(FindObjectsInactive.Include);
                if (c != null) EnsureOption(c);

                var template = refs.playerHats.FirstOrDefault(r => r != null);
                bool usesSetActive = refs.playerHats.Any(r => r != null && !r.gameObject.activeSelf);
                EnsureMaterials(template);
                if (MaskMat == null) return;

                var root = new GameObject(MaskName);
                root.layer = template != null ? template.gameObject.layer : refs.hatTransform.gameObject.layer;
                var anchor = FindHeadBone(refs);
                root.transform.SetParent(anchor, false);
                root.AddComponent<MeshFilter>().sharedMesh = MaskMesh;
                var mr = root.AddComponent<MeshRenderer>();
                mr.sharedMaterial = MaskMat;

                var boardGo = new GameObject(MaskName + "Board") { layer = root.layer };
                boardGo.transform.SetParent(root.transform, false);
                boardGo.AddComponent<MeshFilter>().sharedMesh = BoardMesh;
                var br = boardGo.AddComponent<MeshRenderer>();
                br.sharedMaterial = BoardMat;

                var strapGo = new GameObject(MaskName + "Strap") { layer = root.layer };
                strapGo.transform.SetParent(root.transform, false);
                var smf = strapGo.AddComponent<MeshFilter>();
                smf.sharedMesh = StrapMesh;
                var sr = strapGo.AddComponent<MeshRenderer>();
                sr.sharedMaterial = StrapMat;

                var ctl = root.AddComponent<MaskController>();
                ctl.Init(refs, anchor, owner, mr, br, sr, isDummy);

                if (usesSetActive) { root.SetActive(false); }
                else { mr.enabled = false; br.enabled = false; sr.enabled = false; }

                var list = refs.playerHats.ToList();
                int before = list.Count;
                int myIndex = HatIndex >= 0 && HatIndex <= list.Count ? HatIndex : list.Count;
                list.Insert(myIndex, mr);
                refs.playerHats = list.ToArray();
                if (refs.AllRenderers != null) refs.AllRenderers = refs.AllRenderers.Concat(new Renderer[] { mr, br, sr }).ToArray();

                if (!_loggedSetup || myIndex != HatIndex)
                {
                    _loggedSetup = true;
                    var t = anchor;
                    Log.LogInfo($"Mask added to {(isDummy ? "passport dummy" : "character")}: hat object #{myIndex} of {before + 1}, passport hat #{HatIndex}, " +
                                $"toggle mode {(usesSetActive ? "SetActive" : "enabled")}, anchor '{t.name}' scale {t.lossyScale}, layer {root.layer}, template '{template?.name}'");
                    if (HatIndex >= 0 && myIndex != HatIndex) Log.LogWarning("Hat object number doesn't match passport number, tell Claude");
                }
            }
            catch (System.Exception e) { Log.LogError("InjectRefs failed: " + e); }
        }
    }

    // Places the mask relative to the head, keeps the cardboard and band in step with the face,
    // and hides your own mask from your first-person camera only.
    public class MaskController : MonoBehaviour
    {
        internal static readonly List<MaskController> All = new List<MaskController>();
        static bool _hooked;
        static Camera _firstPerson;

        CustomizationRefs _refs;
        Transform _anchor;
        Renderer _mask, _board, _strap;
        Transform _strapT;
        bool _dummy;
        Character _character;
        Component _owner;
        float _nextFind;
        bool _loggedVisible;
        static bool _autoChecked;
        Vector3 _fwdL = Vector3.forward, _upL = Vector3.up;

        public void Init(CustomizationRefs refs, Transform anchor, Component owner, Renderer mask, Renderer board, Renderer strap, bool dummy)
        {
            _refs = refs; _anchor = anchor; _owner = owner; _mask = mask; _board = board; _strap = strap; _strapT = strap.transform; _dummy = dummy;
            // Work out which way the face points, in the head's own axes, from the body's facing at spawn.
            _character = owner != null ? owner.GetComponentInParent<Character>() : null;
            Transform body = _character != null ? _character.transform : owner != null ? owner.transform : refs.transform;
            _fwdL = anchor.InverseTransformDirection(body.forward).normalized;
            _upL = anchor.InverseTransformDirection(body.up).normalized;
            All.Add(this);
            if (!_hooked) { _hooked = true; RenderPipelineManager.beginCameraRendering += OnBeginCamera; }
            Apply();
        }

        void OnDestroy() { All.Remove(this); }

        public void Apply()
        {
            if (_anchor == null) return;
            float s = Mathf.Max(0.0001f, _anchor.lossyScale.x);
            float w = Mathf.Max(0.01f, Plugin.MaskWidth.Value);
            var fwdL = Plugin.Flip.Value ? -_fwdL : _fwdL;
            var rightL = Vector3.Cross(_upL, fwdL).normalized;
            transform.localRotation = Quaternion.LookRotation(fwdL, _upL) * Quaternion.Euler(Plugin.RotX.Value, Plugin.RotY.Value, Plugin.RotZ.Value);
            transform.localPosition = (fwdL * Plugin.Forward.Value + _upL * Plugin.Up.Value + rightL * Plugin.Right.Value) / s;
            transform.localScale = Vector3.one * (w / s);
            _strapT.localScale = Vector3.one / w;
            _strapT.GetComponent<MeshFilter>().sharedMesh = Plugin.StrapMesh;
            _board.GetComponent<MeshFilter>().sharedMesh = Plugin.BoardMesh;
        }

        bool IsLocal => !_dummy && _character != null && _character.IsLocal;

        void LateUpdate()
        {
            if (_board.enabled != _mask.enabled) _board.enabled = _mask.enabled;
            if (_strap.enabled != _mask.enabled) _strap.enabled = _mask.enabled;
            if (!_dummy && _character == null && Time.time > _nextFind)
            {
                _nextFind = Time.time + 1f;
                _character = GetComponentInParent<Character>();
            }
            if (!_autoChecked && IsLocal && _mask.enabled && gameObject.activeInHierarchy && MainCamera.instance != null)
            {
                _autoChecked = true;
                var worldFwd = _anchor.TransformDirection(Plugin.Flip.Value ? -_fwdL : _fwdL);
                var toCam = MainCamera.instance.transform.position - _anchor.position;
                toCam -= Vector3.Project(toCam, _anchor.TransformDirection(_upL));
                if (Plugin.AutoFlip.Value && Vector3.Dot(toCam, worldFwd) < 0f)
                {
                    Plugin.Flip.Value = !Plugin.Flip.Value;
                    foreach (var m in All.ToArray()) if (m != null) m.Apply();
                    Plugin.Log.LogInfo("Mask was on the back of your head, flipped it round");
                }
                Plugin.AutoFlip.Value = false;
            }
            if (!_loggedVisible && _mask.enabled && gameObject.activeInHierarchy)
            {
                _loggedVisible = true;
                Plugin.Log.LogInfo("Mask now showing: " + Describe());
            }
        }

        // Before each camera draws: hide your own mask only from the camera inside your head.
        static void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (_firstPerson == null && MainCamera.instance != null) _firstPerson = MainCamera.instance.GetComponent<Camera>();
            bool firstPerson = cam == _firstPerson;
            foreach (var m in All)
            {
                if (m == null) continue;
                bool hide = firstPerson && Plugin.HideForSelf.Value && m.IsLocal;
                var mode = hide ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
                if (m._mask.shadowCastingMode != mode)
                {
                    m._mask.shadowCastingMode = mode; m._board.shadowCastingMode = mode; m._strap.shadowCastingMode = mode;
                }
            }
        }

        string Describe()
        {
            var head = _anchor;
            return $"{(_dummy ? "passport dummy" : IsLocal ? "you" : "other player")}, flip {Plugin.Flip.Value}, active {gameObject.activeInHierarchy}, enabled {_mask.enabled}, " +
                   $"pos {transform.position}, head '{head.name}' {head.position}, size {_mask.bounds.size}, anchor scale {head.lossyScale}, " +
                   $"layer {gameObject.layer}, shader {_mask.sharedMaterial.shader.name}";
        }

        internal static void DumpAll()
        {
            var cam = MainCamera.instance != null ? MainCamera.instance.GetComponent<Camera>() : null;
            Plugin.Log.LogInfo($"--- {All.Count} masks, hat #{Plugin.HatIndex}, camera {(cam ? cam.transform.position.ToString() : "none")} culling mask {(cam ? cam.cullingMask : 0)} ---");
            foreach (var m in All) if (m != null) Plugin.Log.LogInfo(m.Describe());
        }
    }

    [HarmonyPatch(typeof(CharacterCustomization), "Awake")]
    static class Patch_CharacterAwake
    {
        static void Postfix(CharacterCustomization __instance) => Plugin.InjectRefs(__instance.refs, __instance, false);
    }

    [HarmonyPatch(typeof(PlayerCustomizationDummy), "OnEnable")]
    static class Patch_DummyEnable
    {
        static void Prefix(PlayerCustomizationDummy __instance) => Plugin.InjectRefs(__instance.refs, __instance, true);
    }

    [HarmonyPatch(typeof(PassportManager), "Awake")]
    static class Patch_PassportAwake
    {
        static void Prefix()
        {
            var c = Object.FindFirstObjectByType<Customization>(FindObjectsInactive.Include);
            if (c != null) Plugin.EnsureOption(c);
        }
    }
}
