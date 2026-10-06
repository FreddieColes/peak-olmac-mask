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
    [BepInPlugin(Guid, "Ol' Mac Mask", "0.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.freddiecoles.olmacmask";
        public const string MaskName = "OlMacMask";

        internal static Plugin I;
        internal static ManualLogSource Log;

        internal static Texture2D Face;
        internal static Mesh MaskMesh, StrapMesh;
        internal static Material MaskMat, StrapMat;
        internal static float Aspect = 0.75f;
        internal static int HatIndex = -1;

        internal static ConfigEntry<float> MaskWidth, Forward, Up, Right, RotX, RotY, RotZ;
        internal static ConfigEntry<float> StrapRadiusX, StrapRadiusZ, StrapHeight, StrapUp;
        internal static ConfigEntry<string> StrapColour, ShaderName;
        internal static ConfigEntry<bool> HideForSelf;
        internal static ConfigEntry<Key> ReloadKey;

        Customization _lastCustomization;
        float _nextCheck;
        static bool _loggedSetup;

        void Awake()
        {
            I = this;
            Log = Logger;

            const string P = "1. Placement (metres)";
            MaskWidth = Config.Bind(P, "MaskWidth", 0.5f, "Width of the mask");
            Forward = Config.Bind(P, "Forward", 0.18f, "How far in front of the hat anchor the mask sits");
            Up = Config.Bind(P, "Up", -0.12f, "Up/down from the hat anchor (negative moves it down onto the face)");
            Right = Config.Bind(P, "Right", 0f, "Left/right nudge");
            RotX = Config.Bind(P, "RotX", 0f, "Extra tilt (degrees)");
            RotY = Config.Bind(P, "RotY", 0f, "Extra turn (degrees)");
            RotZ = Config.Bind(P, "RotZ", 0f, "Extra roll (degrees)");

            const string S = "2. Strap (metres)";
            StrapRadiusX = Config.Bind(S, "RadiusSideToSide", 0.17f, "Half the head width");
            StrapRadiusZ = Config.Bind(S, "RadiusFrontToBack", 0.17f, "Half the head depth");
            StrapHeight = Config.Bind(S, "Thickness", 0.035f, "How tall the band is");
            StrapUp = Config.Bind(S, "Up", 0.02f, "Band height relative to the middle of the mask");
            StrapColour = Config.Bind(S, "Colour", "5A3A1E", "Hex colour of the band");

            const string M = "3. Misc";
            HideForSelf = Config.Bind(M, "HideForSelf", true, "Hide your own mask from your camera (others still see it)");
            ShaderName = Config.Bind(M, "Shader", "", "Leave blank to pick automatically");
            ReloadKey = Config.Bind(M, "ReloadKey", Key.F9, "Press in game to reload this config and re-place the mask");

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
        }

        void ReloadPlacement()
        {
            Config.Reload();
            BuildStrapMesh();
            StrapMat.color = ParseColour(StrapColour.Value);
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
            BuildStrapMesh();
            MaskMat = MakeMaterial(Face, Color.white, true);
            StrapMat = MakeMaterial(Texture2D.whiteTexture, ParseColour(StrapColour.Value), false);
            Log.LogInfo($"Face {Face.width}x{Face.height}, shader {MaskMat.shader.name}");
        }

        // Mesh shaped like the face: a grid where only cells covered by the drawing get triangles,
        // so the outline works even if the shader can't do transparency. Front faces +Z, plus a back side.
        static void BuildMaskMesh()
        {
            int cols = 128, rows = Mathf.Max(1, Mathf.RoundToInt(cols * Aspect));
            var px = Face.GetPixels32();
            int w = Face.width, h = Face.height;
            bool Solid(float u, float v)
            {
                int x = Mathf.Clamp((int)(u * w), 0, w - 1), y = Mathf.Clamp((int)(v * h), 0, h - 1);
                return px[y * w + x].a > 100;
            }

            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var norms = new List<Vector3>();
            var tris = new List<int>();
            int stride = cols + 1, count = stride * (rows + 1);
            for (int side = 0; side < 2; side++)
                for (int j = 0; j <= rows; j++)
                    for (int i = 0; i <= cols; i++)
                    {
                        float u = (float)i / cols, v = (float)j / rows;
                        verts.Add(new Vector3(0.5f - u, (v - 0.5f) * Aspect, 0f));
                        uvs.Add(new Vector2(u, v));
                        norms.Add(side == 0 ? Vector3.forward : Vector3.back);
                    }
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < cols; i++)
                {
                    float cu = (i + 0.5f) / cols, cv = (j + 0.5f) / rows, du = 0.5f / cols, dv = 0.5f / rows;
                    if (!(Solid(cu, cv) || Solid(cu - du, cv - dv) || Solid(cu + du, cv - dv) || Solid(cu - du, cv + dv) || Solid(cu + du, cv + dv))) continue;
                    int a = j * stride + i, b = a + stride, c = a + 1, d = b + 1;
                    tris.AddRange(new[] { a, b, c, c, b, d });
                    int o = count;
                    tris.AddRange(new[] { o + a, o + c, o + b, o + c, o + d, o + b });
                }
            MaskMesh = new Mesh { name = MaskName };
            if (verts.Count > 65000) MaskMesh.indexFormat = IndexFormat.UInt32;
            MaskMesh.SetVertices(verts); MaskMesh.SetUVs(0, uvs); MaskMesh.SetNormals(norms); MaskMesh.SetTriangles(tris, 0);
            MaskMesh.RecalculateBounds();
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

        static Material MakeMaterial(Texture tex, Color col, bool clip)
        {
            Shader sh = null;
            if (!string.IsNullOrEmpty(ShaderName.Value)) sh = Shader.Find(ShaderName.Value);
            foreach (var n in new[] { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit", "Universal Render Pipeline/Unlit", "Sprites/Default" })
                if (sh == null) sh = Shader.Find(n);
            var m = new Material(sh) { name = MaskName + (clip ? "Mat" : "StrapMat") };
            m.mainTexture = tex;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
            m.color = col;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
            if (clip)
            {
                if (m.HasProperty("_AlphaClip")) m.SetFloat("_AlphaClip", 1f);
                if (m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", 0.5f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.renderQueue = (int)RenderQueue.AlphaTest;
            }
            return m;
        }

        static Color ParseColour(string hex)
        {
            return ColorUtility.TryParseHtmlString("#" + hex.TrimStart('#'), out var c) ? c : new Color(0.35f, 0.23f, 0.12f);
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
                Log.LogInfo($"Added Ol' Mac to the passport as hat #{HatIndex} (template {tpl.name})");
            }
            catch (System.Exception e) { Log.LogError("EnsureOption failed: " + e); }
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

                var root = new GameObject(MaskName);
                root.layer = template != null ? template.gameObject.layer : refs.hatTransform.gameObject.layer;
                root.transform.SetParent(refs.hatTransform, false);
                root.AddComponent<MeshFilter>().sharedMesh = MaskMesh;
                var mr = root.AddComponent<MeshRenderer>();
                mr.sharedMaterial = MaskMat;

                var strapGo = new GameObject(MaskName + "Strap") { layer = root.layer };
                strapGo.transform.SetParent(root.transform, false);
                var smf = strapGo.AddComponent<MeshFilter>();
                smf.sharedMesh = StrapMesh;
                var sr = strapGo.AddComponent<MeshRenderer>();
                sr.sharedMaterial = StrapMat;

                var ctl = root.AddComponent<MaskController>();
                ctl.Init(refs, owner, mr, sr, isDummy);

                if (usesSetActive) { root.SetActive(false); }
                else { mr.enabled = false; sr.enabled = false; }

                int myIndex = refs.playerHats.Length;
                refs.playerHats = refs.playerHats.Append(mr).ToArray();
                if (refs.AllRenderers != null) refs.AllRenderers = refs.AllRenderers.Concat(new Renderer[] { mr, sr }).ToArray();

                if (!_loggedSetup || myIndex != HatIndex)
                {
                    _loggedSetup = true;
                    var t = refs.hatTransform;
                    Log.LogInfo($"Mask added to {(isDummy ? "passport dummy" : "character")}: hat object #{myIndex}, passport hat #{HatIndex}, " +
                                $"toggle mode {(usesSetActive ? "SetActive" : "enabled")}, anchor '{t.name}' scale {t.lossyScale}, layer {root.layer}, template '{template?.name}'");
                    if (HatIndex >= 0 && myIndex != HatIndex) Log.LogWarning("Hat object number doesn't match passport number, tell Claude");
                }
            }
            catch (System.Exception e) { Log.LogError("InjectRefs failed: " + e); }
        }
    }

    // Places the mask relative to the head, keeps the strap in step with the mask, hides it from your own camera.
    public class MaskController : MonoBehaviour
    {
        internal static readonly List<MaskController> All = new List<MaskController>();

        CustomizationRefs _refs;
        Renderer _mask, _strap;
        Transform _strapT;
        bool _dummy;
        Character _character;
        Component _owner;
        float _nextFind;
        Vector3 _fwdL = Vector3.forward, _upL = Vector3.up;

        public void Init(CustomizationRefs refs, Component owner, Renderer mask, Renderer strap, bool dummy)
        {
            _refs = refs; _owner = owner; _mask = mask; _strap = strap; _strapT = strap.transform; _dummy = dummy;
            // Work out which way the face points, in the head's own axes, from the body's facing at spawn.
            var anchor = refs.hatTransform;
            _character = owner != null ? owner.GetComponentInParent<Character>() : null;
            Transform body = _character != null ? _character.transform : owner != null ? owner.transform : refs.transform;
            _fwdL = anchor.InverseTransformDirection(body.forward).normalized;
            _upL = anchor.InverseTransformDirection(body.up).normalized;
            All.Add(this);
            Apply();
        }

        void OnDestroy() { All.Remove(this); }

        public void Apply()
        {
            if (_refs == null || _refs.hatTransform == null) return;
            float s = Mathf.Max(0.0001f, _refs.hatTransform.lossyScale.x);
            float w = Mathf.Max(0.01f, Plugin.MaskWidth.Value);
            var rightL = Vector3.Cross(_upL, _fwdL).normalized;
            transform.localRotation = Quaternion.LookRotation(_fwdL, _upL) * Quaternion.Euler(Plugin.RotX.Value, Plugin.RotY.Value, Plugin.RotZ.Value);
            transform.localPosition = (_fwdL * Plugin.Forward.Value + _upL * Plugin.Up.Value + rightL * Plugin.Right.Value) / s;
            transform.localScale = Vector3.one * (w / s);
            _strapT.localScale = Vector3.one / w;
            _strapT.GetComponent<MeshFilter>().sharedMesh = Plugin.StrapMesh;
        }

        void LateUpdate()
        {
            if (_strap.enabled != _mask.enabled) _strap.enabled = _mask.enabled;
            if (_dummy) return;
            if (_character == null && Time.time > _nextFind)
            {
                _nextFind = Time.time + 1f;
                _character = GetComponentInParent<Character>();
            }
            bool hide = Plugin.HideForSelf.Value && _character != null && _character.IsLocal;
            var mode = hide ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            if (_mask.shadowCastingMode != mode) { _mask.shadowCastingMode = mode; _strap.shadowCastingMode = mode; }
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
