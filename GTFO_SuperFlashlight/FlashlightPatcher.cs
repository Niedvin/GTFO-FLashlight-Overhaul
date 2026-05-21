using GameData;
using Gear;
using HarmonyLib;
using Il2CppInterop.Runtime;
using ItemSetup;
using UnityEngine;

namespace GTFO_SuperFlashlight
{
    // Five named presets covering vanilla GTFO's FlashlightSettingsDataBlock
    // archetypes. Patch_GearManager classifies each block by name and writes
    // the matched preset's Core values into it; ApplySandwich + EnsureRangedSettings
    // then drive the RF layers directly from the preset (bypassing the global
    // Step/I/R multipliers for Ranged).
    //
    // PID 1 (HelmetLight) and PID 4 (melee torch) are zeroed elsewhere — both
    // are replaced by RF_HelmetSynth.
    internal static class RangedPresets
    {
        internal struct Preset
        {
            public string Name;
            public Color  Tint;
            public bool   HasMid;
            public float CoreAngle;     public float CoreIntensity;     public float CoreRange;
            public float MidAngle;      public float MidIntensity;      public float MidRange;
            public float FallAngle;     public float FallIntensity;     public float FallRange;
        }

        // Tints. Cool LED for whites, warm tungsten for the yellow short-range.
        internal static readonly Color CoolWhite  = new Color(0.85f, 0.93f, 1.00f);
        internal static readonly Color WarmYellow = new Color(1.00f, 0.93f, 0.75f);

        // Mutable — populated by BuildFromConfig() from Plugin.ApplyPreset().
        // Don't write to these from anywhere else.
        internal static Preset ShortRange;
        internal static Preset MediumRange1;
        internal static Preset MediumRange2;
        internal static Preset WideRange;
        internal static Preset ExtendedRange;

        // Fallback when a ranged Light can't be resolved; mirrors ExtendedRange.
        internal static Preset Default;

        // noMid = true → MINIMAL mode kills the L1 (Mid) layer on every preset.
        internal static void BuildFromConfig(PresetOffset offset, bool noMid = false)
        {
            bool hasMid = !noMid;
            ShortRange = new Preset
            {
                Name   = "Short range (Yellow, Wide)",
                Tint   = WarmYellow,
                HasMid = hasMid,
                CoreAngle     = Plugin.P_Short_CoreAngle.Value     + offset.CoreAngle,
                CoreIntensity = Plugin.P_Short_CoreIntensity.Value + offset.CoreIntensity,
                CoreRange     = Plugin.P_Short_CoreRange.Value     + offset.CoreRange,
                MidAngle      = Plugin.P_Short_MidAngle.Value      + offset.MidAngle,
                MidIntensity  = Plugin.P_Short_MidIntensity.Value  + offset.MidIntensity,
                MidRange      = Plugin.P_Short_MidRange.Value      + offset.MidRange,
                FallAngle     = Plugin.P_Short_FallAngle.Value     + offset.FallAngle,
                FallIntensity = Plugin.P_Short_FallIntensity.Value + offset.FallIntensity,
                FallRange     = Plugin.P_Short_FallRange.Value     + offset.FallRange,
            };

            MediumRange1 = new Preset
            {
                Name   = "Medium range #1 (White, Medium)",
                Tint   = CoolWhite,
                HasMid = hasMid,
                CoreAngle     = Plugin.P_Med1_CoreAngle.Value     + offset.CoreAngle,
                CoreIntensity = Plugin.P_Med1_CoreIntensity.Value + offset.CoreIntensity,
                CoreRange     = Plugin.P_Med1_CoreRange.Value     + offset.CoreRange,
                MidAngle      = Plugin.P_Med1_MidAngle.Value      + offset.MidAngle,
                MidIntensity  = Plugin.P_Med1_MidIntensity.Value  + offset.MidIntensity,
                MidRange      = Plugin.P_Med1_MidRange.Value      + offset.MidRange,
                FallAngle     = Plugin.P_Med1_FallAngle.Value     + offset.FallAngle,
                FallIntensity = Plugin.P_Med1_FallIntensity.Value + offset.FallIntensity,
                FallRange     = Plugin.P_Med1_FallRange.Value     + offset.FallRange,
            };

            MediumRange2 = new Preset
            {
                Name   = "Medium range #2 (White, Tight)",
                Tint   = CoolWhite,
                HasMid = hasMid,
                CoreAngle     = Plugin.P_Med2_CoreAngle.Value     + offset.CoreAngle,
                CoreIntensity = Plugin.P_Med2_CoreIntensity.Value + offset.CoreIntensity,
                CoreRange     = Plugin.P_Med2_CoreRange.Value     + offset.CoreRange,
                MidAngle      = Plugin.P_Med2_MidAngle.Value      + offset.MidAngle,
                MidIntensity  = Plugin.P_Med2_MidIntensity.Value  + offset.MidIntensity,
                MidRange      = Plugin.P_Med2_MidRange.Value      + offset.MidRange,
                FallAngle     = Plugin.P_Med2_FallAngle.Value     + offset.FallAngle,
                FallIntensity = Plugin.P_Med2_FallIntensity.Value + offset.FallIntensity,
                FallRange     = Plugin.P_Med2_FallRange.Value     + offset.FallRange,
            };

            WideRange = new Preset
            {
                Name   = "Wide range (White, Wide)",
                Tint   = CoolWhite,
                HasMid = hasMid,
                CoreAngle     = Plugin.P_Wide_CoreAngle.Value     + offset.CoreAngle,
                CoreIntensity = Plugin.P_Wide_CoreIntensity.Value + offset.CoreIntensity,
                CoreRange     = Plugin.P_Wide_CoreRange.Value     + offset.CoreRange,
                MidAngle      = Plugin.P_Wide_MidAngle.Value      + offset.MidAngle,
                MidIntensity  = Plugin.P_Wide_MidIntensity.Value  + offset.MidIntensity,
                MidRange      = Plugin.P_Wide_MidRange.Value      + offset.MidRange,
                FallAngle     = Plugin.P_Wide_FallAngle.Value     + offset.FallAngle,
                FallIntensity = Plugin.P_Wide_FallIntensity.Value + offset.FallIntensity,
                FallRange     = Plugin.P_Wide_FallRange.Value     + offset.FallRange,
            };

            ExtendedRange = new Preset
            {
                Name   = "Extended range (White, Tight) — base",
                Tint   = CoolWhite,
                HasMid = hasMid,
                CoreAngle     = Plugin.P_Ext_CoreAngle.Value     + offset.CoreAngle,
                CoreIntensity = Plugin.P_Ext_CoreIntensity.Value + offset.CoreIntensity,
                CoreRange     = Plugin.P_Ext_CoreRange.Value     + offset.CoreRange,
                MidAngle      = Plugin.P_Ext_MidAngle.Value      + offset.MidAngle,
                MidIntensity  = Plugin.P_Ext_MidIntensity.Value  + offset.MidIntensity,
                MidRange      = Plugin.P_Ext_MidRange.Value      + offset.MidRange,
                FallAngle     = Plugin.P_Ext_FallAngle.Value     + offset.FallAngle,
                FallIntensity = Plugin.P_Ext_FallIntensity.Value + offset.FallIntensity,
                FallRange     = Plugin.P_Ext_FallRange.Value     + offset.FallRange,
            };

            Default = ExtendedRange;
        }

        // Populated by Classify() during Patch_GearManager.
        internal static readonly System.Collections.Generic.Dictionary<uint, Preset> ByPID
            = new System.Collections.Generic.Dictionary<uint, Preset>();

        // Map a FlashlightSettingsDataBlock to a preset. Tries explicit name
        // patterns first; if nothing matches, falls back to (angle × range)
        // bucketing.
        internal static Preset Classify(string blockName, float origAngle, float origRange)
        {
            string n = blockName ?? "";

            bool isWide     = n.IndexOf("wide",   System.StringComparison.OrdinalIgnoreCase) >= 0;
            bool isTight    = n.IndexOf("tight",  System.StringComparison.OrdinalIgnoreCase) >= 0;
            bool isMedium   = n.IndexOf("medium", System.StringComparison.OrdinalIgnoreCase) >= 0;
            bool isShort    = n.IndexOf("short",  System.StringComparison.OrdinalIgnoreCase) >= 0;
            bool isExtended = n.IndexOf("extend", System.StringComparison.OrdinalIgnoreCase) >= 0;

            // 1) Explicit name-pattern (most specific first).
            if (isShort)              return ShortRange;
            if (isMedium && isTight)  return MediumRange2;
            if (isMedium)             return MediumRange1;
            if (isWide && isExtended) return WideRange;
            if (isWide)               return WideRange;
            if (isExtended)           return ExtendedRange;
            if (isTight)              return ExtendedRange;

            // 2) Fallback: classify by (angle, range) tuple.
            //    Axis 1 (angle):  ≤40° = tight, 40-55° = medium, ≥55° = wide
            //    Axis 2 (range):  ≤12m = short, 12-17m = medium, ≥17m = long
            bool tightAngle = origAngle <= 40f;
            bool wideAngle  = origAngle >= 55f;
            bool shortRng   = origRange <= 12f;
            bool longRng    = origRange >= 17f;

            if (wideAngle && shortRng)  return ShortRange;
            if (tightAngle && longRng)  return ExtendedRange;
            if (wideAngle && longRng)   return WideRange;
            if (wideAngle)              return WideRange;
            if (tightAngle)             return MediumRange2;
            return MediumRange1;
        }

        // Reflection on Il2Cpp wrappers can't see underlying game-data fields,
        // so we identify weapons by transform-tree names and keyword-match
        // against the vanilla weapon→preset table.
        private static readonly System.Collections.Generic.Dictionary<int, Preset?> _ownerPresetCache
            = new System.Collections.Generic.Dictionary<int, Preset?>();

        internal static void ClearOwnerCaches() => _ownerPresetCache.Clear();

        // Drop a single owner's cached resolve. Called on wield so the
        // re-resolve runs against the now-fully-instantiated transform tree
        // (the lazy build at sandwich time may have run before
        // "Flashlight_X(Clone)" appeared in the descendants, producing a
        // null/Default match that then stuck via the cache).
        internal static void ClearOwnerCacheEntry(ItemEquippable? owner)
        {
            if (owner == null) return;
            int oid;
            try { oid = owner.GetInstanceID(); }
            catch { return; }
            _ownerPresetCache.Remove(oid);
        }

        internal static Preset? ResolveByOwner(ItemEquippable? owner)
        {
            if (owner == null) return null;
            int oid;
            try { oid = owner.GetInstanceID(); }
            catch { return null; }
            if (_ownerPresetCache.TryGetValue(oid, out var cached)) return cached;

            // Build a name blob from the transform tree so MatchKeywords can
            // identify the weapon. Wrappers don't expose game-data fields via
            // reflection, so transform names are the only reliable source.
            var sb = new System.Text.StringBuilder(256);

            try
            {
                var go = owner.gameObject;
                if (go != null) sb.Append(" goName=").Append(go.name);
            }
            catch { }

            try
            {
                Transform? t = owner.transform;
                int safety = 8;
                sb.Append(" path=");
                bool first = true;
                while (t != null && safety-- > 0)
                {
                    if (!first) sb.Append('/');
                    sb.Append(t.name ?? "?");
                    first = false;
                    t = t.parent;
                }
            }
            catch { }

            try
            {
                // Direct children — the flashlight Part GO ("WPN_PistolFlashLight"
                // etc.) is the keyword the matcher actually needs.
                var rootT = owner.transform;
                if (rootT != null)
                {
                    int n = rootT.childCount;
                    sb.Append(" children=[");
                    for (int i = 0; i < n && i < 12; i++)
                    {
                        var c = rootT.GetChild(i);
                        if (i > 0) sb.Append(',');
                        sb.Append(c.name ?? "?");
                    }
                    sb.Append(']');
                }
            }
            catch { }

            try
            {
                var rootT = owner.transform;
                if (rootT != null)
                {
                    var found = new System.Text.StringBuilder();
                    CollectDescendantNamesContaining(rootT, "Flashlight", found, maxDepth: 8, maxResults: 6);
                    if (found.Length > 0)
                        sb.Append(" flashlightGOs=[").Append(found).Append(']');
                    found.Clear();
                    CollectDescendantNamesContaining(rootT, "WPN_", found, maxDepth: 8, maxResults: 6);
                    if (found.Length > 0)
                        sb.Append(" wpnGOs=[").Append(found).Append(']');
                }
            }
            catch { }

            string blob = sb.ToString();
            Preset? p = MatchKeywords(blob);
            _ownerPresetCache[oid] = p;
            // Diagnostic: log the probe blob + matched preset name (or "null")
            // once per unique (blob, result) pair. Helps verify keyword matches
            // when the visible preset doesn't look right.
            try
            {
                string presetName = p.HasValue ? (p.Value.Name ?? "Unnamed") : "null";
                string key = blob + " => " + presetName;
                if (_loggedProbes.Add(key))
                {
                    Plugin.Logger.LogInfo("[RF] OwnerNameProbe " + key);
                }
            }
            catch { }
            return p;
        }

        private static readonly System.Collections.Generic.HashSet<string> _loggedProbes
            = new System.Collections.Generic.HashSet<string>();

        private static void CollectDescendantNamesContaining(Transform t, string substring, System.Text.StringBuilder sink, int maxDepth, int maxResults)
        {
            if (t == null || maxDepth < 0) return;
            try
            {
                int n = t.childCount;
                for (int i = 0; i < n; i++)
                {
                    var c = t.GetChild(i);
                    string nm = c.name ?? "";
                    if (nm.IndexOf(substring, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (sink.Length > 0) sink.Append(',');
                        sink.Append(nm);
                        if (sink.Length >= maxResults * 32) return;
                    }
                    if (maxDepth > 0) CollectDescendantNamesContaining(c, substring, sink, maxDepth - 1, maxResults);
                }
            }
            catch { }
        }

        // Keyword → preset map, derived from the vanilla GTFO weapon table:
        //   ShortRange    : SMG, Shotgun, Revolver, Precision Rifle, Combat Shotgun
        //   MediumRange #1: Bullpup, Heavy AR, Choke Mod Shotgun, HEL Shotgun, HEL Gun,
        //                   Scattergun, Rifle (generic), Double Tap Rifle, Slug Shotgun
        //   MediumRange #2: Assault Rifle, DMR, Pistol, Carbine, HEL Revolver,
        //                   High Caliber Pistol, Machine Pistol, HEL Autopistol,
        //                   Sawed-off Shotgun, Heavy SMG, Short Rifle, Burst Pistol
        //   WideRange     : Veruta XII, PDW, Arbalist V, Burst Cannon
        //   ExtendedRange : Sniper, Burst Rifle, HEL Rifle
        //
        // Order matters: more specific patterns first.
        private static Preset? MatchKeywords(string s)
        {
            bool C(string k) => s.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0;

            // GTFO instantiates each weapon's flashlight as "Flashlight_X(Clone)"
            // where X = A..E maps 1:1 to GunLight_A..E. Short-circuit on that
            // before any keyword fallback so a weapon name like "Pistol_A"
            // can't accidentally collide.
            if (C("Flashlight_A")) return ShortRange;
            if (C("Flashlight_B")) return MediumRange2;
            if (C("Flashlight_C")) return ExtendedRange;
            if (C("Flashlight_D")) return WideRange;
            if (C("Flashlight_E")) return MediumRange1;

            // Keyword fallback for any future GTFO update that changes the
            // Flashlight_X convention.

            // Extended range — explicit long-distance weapons
            if (C("Sniper"))                      return ExtendedRange;
            if (C("HELRifle") || (C("HEL") && C("Rifle") && !C("HELShotgun"))) return ExtendedRange;
            if (C("BurstRifle"))                  return ExtendedRange;

            // Wide range — wide-cone heavies
            if (C("Veruta"))                      return WideRange;
            if (C("Arbalist"))                    return WideRange;
            if (C("BurstCannon"))                 return WideRange;
            if (C("PDW"))                         return WideRange;

            // Medium #1 — generic rifles, heavier shotguns, "heavy AR" family
            if (C("Bullpup"))                     return MediumRange1;
            if (C("HELShotgun") || C("HELGun"))   return MediumRange1;
            if (C("HeavyAssault"))                return MediumRange1;
            if (C("Scattergun"))                  return MediumRange1;
            if (C("DoubleTap"))                   return MediumRange1;
            if (C("SlugShotgun"))                 return MediumRange1;
            if (C("ChokeMod"))                    return MediumRange1;

            // Medium #2 — most pistols, ARs, DMRs, carbines, sawed-offs
            if (C("DMR"))                         return MediumRange2;
            if (C("Carbine"))                     return MediumRange2;
            if (C("MachinePistol"))               return MediumRange2;
            if (C("Sawed"))                       return MediumRange2;
            if (C("HELRevolver"))                 return MediumRange2;
            if (C("HELAuto") && C("Pistol"))      return MediumRange2;
            if (C("HighCal") && C("Pistol"))      return MediumRange2;
            if (C("BurstPistol"))                 return MediumRange2;
            if (C("AssaultRifle"))                return MediumRange2;
            if (C("ShortRifle"))                  return MediumRange2;
            if (C("HeavySMG"))                    return MediumRange2;
            if (C("Pistol"))                      return MediumRange2; // generic pistol

            // Short range — revolvers, SMGs, generic shotguns, precision rifles
            if (C("Revolver"))                    return ShortRange;
            if (C("CombatShotgun"))               return ShortRange;
            if (C("PrecisionRifle"))              return ShortRange;
            if (C("SMG"))                         return ShortRange;
            if (C("Shotgun"))                     return ShortRange;

            // Final generic fallbacks — only when nothing more specific matched
            if (C("Rifle"))                       return MediumRange1;

            return null;
        }

        // Fingerprint-resolve via (parent.spotAngle, range, intensity). Wide
        // and Extended share angle+range, hence the +0.01 intensity bias on
        // Wide in Patch_GearManager.
        internal static Preset ResolveByLight(Light light)
        {
            if (light == null || ByPID.Count == 0) return Default;
            float la = light.spotAngle;
            float lr = light.range;
            float li = light.intensity;
            const float angleTol = 1.0f, rangeTol = 1.0f, intensityTol = 0.005f;
            foreach (var kv in ByPID)
            {
                var p = kv.Value;
                float parentAngleForPreset = p.CoreAngle - Plugin.Step1.Value;
                if (Mathf.Abs(la - parentAngleForPreset) <= angleTol &&
                    Mathf.Abs(lr - p.CoreRange) <= rangeTol &&
                    Mathf.Abs(li - p.CoreIntensity) <= intensityTol)
                {
                    return p;
                }
            }
            return Default;
        }
    }

    // Sandwich helpers — build / sync / suppress the RF layer tree.
    internal static class LightSandwich
    {
        // RF_Core replaces the vanilla Light (which has a hard-edge cookie).
        // The vanilla one is hidden via cullingMask=0.
        internal const string RFCORE = "RF_Core";
        internal const string L1 = "RF_L1";
        internal const string L2 = "RF_L2";
        // Point-light spillover: fills the hemisphere "behind" the 179°-clamped
        // falloff cone so the user doesn't see a hard dark border near walls
        // when moving sideways. Unity's spot type caps spotAngle at 179°.
        internal const string SPILL = "RF_Spill";
        // RF_Dust — name reserved for the per-light dust child. Currently
        // unused (DustEffect uses a single global pool instead), but kept in
        // LegacyLayers so RebuildAll purges leftover children from older DLLs.
        internal const string DUST = "RF_Dust";
        internal const string L3 = "RF_L3";
        internal const string L4 = "RF_L4";
        internal const string L5 = "RF_L5";

        // 3-layer beam: bright centre + soft halo + gentle outer glow.
        private static readonly string[] AllLayers     = { RFCORE, L1, L2 };

        // Per-layer overlap: extends each inner layer's spotAngle past its
        // nominal preset value so the cookie's soft tail bleeds into the
        // next outer layer. Higher → smoother seam, but too much washes out
        // each layer's distinct shape.
        private const float CoreLayerOverlap = 0.18f;
        private const float MidLayerOverlap  = 0.12f;

        // idx 0 = Core, 1 = Mid, 2 = Fall. Clamps to 179° so we never produce
        // a degenerate spotlight.
        private static float WithLayerOverlap(float nominalAngle, int idx)
        {
            float f = idx == 0 ? CoreLayerOverlap
                    : idx == 1 ? MidLayerOverlap
                    : 0f;
            return Mathf.Min(nominalAngle * (1f + f), 179f);
        }
        // Used by RebuildAll to also purge RF_L3..L5 left over from older DLL versions.
        private static readonly string[] LegacyLayers  = { RFCORE, L1, L2, SPILL, DUST, L3, L4, L5 };

        internal enum ManagedKind { None, Pickable, MeleeOrTool, Ranged }
        private static readonly System.Collections.Generic.Dictionary<int, ManagedKind> _managed
            = new System.Collections.Generic.Dictionary<int, ManagedKind>();
        internal static ManagedKind GetManagedKind(Light l) =>
            (l != null && _managed.TryGetValue(l.GetInstanceID(), out var k)) ? k : ManagedKind.None;

        // Per-Light preset cache. Captured at sandwich-build time before
        // Patch_LightAngle zeroes parent.spotAngle (which would break any later
        // fingerprint resolve). EnsureRangedSettings reads this instead of
        // re-resolving every tick.
        internal static readonly System.Collections.Generic.Dictionary<int, RangedPresets.Preset> _lightPreset
            = new System.Collections.Generic.Dictionary<int, RangedPresets.Preset>();

        // Runtime-generated cookie textures. Unity's default spot falloff hits
        // zero hard at spotAngle, producing the visible cone-edge ring. Our
        // cookies use a smoother fade so all RF layers blend seamlessly.
        private static Texture2D? _smoothCookie;
        private static Texture2D? _coreCookie;
        private static Texture2D? _midCookie;
        private static Texture2D? _falloffCookie;
        // Quintic smootherstep: C2-continuous (zero 1st AND 2nd derivative at
        // endpoints). Used everywhere instead of Mathf.SmoothStep to keep the
        // brightness gradient kink-free.
        private static float Ss(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        internal static Texture2D GetSmoothCookie()
        {
            if (_smoothCookie != null) return _smoothCookie;
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode   = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.hideFlags  = HideFlags.HideAndDontSave;
            float center = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center) / center;
                    float dy = (y - center) / center;
                    float r  = Mathf.Sqrt(dx * dx + dy * dy);
                    float a;
                    if (r >= 1f) a = 0f;
                    else
                    {
                        // Smooth cosine falloff: 1 at centre, 0 at r=1, with
                        // gentle slope near the edge (no quadratic cliff).
                        a = (1f + Mathf.Cos(r * Mathf.PI)) * 0.5f;
                    }
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            _smoothCookie = tex;
            return tex;
        }

        // RF_Core cookie — small bright tip + very long gentle fade.
        // plateauR=0.10: the bright plateau covers only the central 10 % of the
        // cone. The C2-continuous smootherstep fade covers the remaining 90 %,
        // so Core's brightness drops very gradually all the way to the cone
        // edge with NO visible "ring" where Core finishes. Combined with Mid's
        // small plateau (also ~10-15 %) both layers contribute a continuously
        // changing brightness gradient across the visible cone — no
        // constant-brightness shoulder anywhere.
        internal static Texture2D GetCoreCookie()
        {
            if (_coreCookie != null) return _coreCookie;
            const int size = 128;
            var tex2 = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex2.wrapMode   = TextureWrapMode.Clamp;
            tex2.filterMode = FilterMode.Bilinear;
            tex2.hideFlags  = HideFlags.HideAndDontSave;
            const float plateauR = 0.10f;
            float center2 = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center2) / center2;
                    float dy = (y - center2) / center2;
                    float r2 = Mathf.Sqrt(dx * dx + dy * dy);
                    float a2;
                    if (r2 >= 1f)             a2 = 0f;
                    else if (r2 <= plateauR)  a2 = 1.0f;
                    else
                    {
                        // Quintic smootherstep from plateauR to 1.0 (C2-continuous).
                        float t = (r2 - plateauR) / (1f - plateauR);
                        a2 = 1f - Ss(t);
                    }
                    tex2.SetPixel(x, y, new Color(1f, 1f, 1f, a2));
                }
            }
            tex2.Apply();
            _coreCookie = tex2;
            return tex2;
        }

        // Fall cookie — three-zone profile:
        //   r 0.00–0.50: full-alpha plateau (~90° of cone)
        //   r 0.50–0.75: gentle quintic fade 1.0 → 0.45
        //   r 0.75–1.00: sharper quintic fade 0.45 → 0
        // The plateau lets Fall add ambient brightness across Core+Mid so the
        // beam centre reads as one bright cone; the outer phases give a defined
        // outer rim without leaking into unrelated areas.
        internal static Texture2D GetFalloffCookie()
        {
            if (_falloffCookie != null) return _falloffCookie;
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode   = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.hideFlags  = HideFlags.HideAndDontSave;
            // Profile tuned so the visible cone edge fades to ~0 well INSIDE the
            // 179° outer radius — eliminates the hard "edge line" the player saw
            // where L2 ended and RF_Spill began. Phase split:
            //   0.00–0.50 plateau (~90° core of the falloff cone, full alpha)
            //   0.50–0.68 first quintic fade  1.0 → midAlpha (still bright halo)
            //   0.68–1.00 LONG soft quintic tail midAlpha → 0 (effective edge ~r≈0.92)
            // The long outer tail means the visible cone shrinks slightly versus
            // the nominal 179° but the boundary is no longer a sharp line.
            const float plateauR   = 0.50f; // ~90° in a 179° cone
            const float smoothEndR = 0.68f; // ~122° in a 179° cone
            const float midAlpha   = 0.30f; // lower handoff alpha → softer outer tail
            float center = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center) / center;
                    float dy = (y - center) / center;
                    float r  = Mathf.Sqrt(dx * dx + dy * dy);
                    float a;
                    if (r >= 1f)
                        a = 0f;
                    else if (r <= plateauR)
                        a = 1.0f;
                    else if (r <= smoothEndR)
                    {
                        // Gentle quintic fade 1.0 → midAlpha across 90°–135°.
                        // C2-continuous: no visible curvature kink at the plateau
                        // boundary (r=0.50) or at the phase junction (r=0.75).
                        float t = (r - plateauR) / (smoothEndR - plateauR);
                        a = 1.0f - (1.0f - midAlpha) * Ss(t);
                    }
                    else
                    {
                        // Sharper quintic fade midAlpha → 0 across 135°–179°.
                        // Both phases are C2 and share zero-derivative endpoints,
                        // so the junction at r=0.75 is seamless.
                        float t = (r - smoothEndR) / (1f - smoothEndR);
                        a = midAlpha * (1f - Ss(t));
                    }
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            _falloffCookie = tex;
            return tex;
        }

        // Mid cookie — small plateau + long quintic fade. plateauR matches
        // Core's profile so neither layer has a constant-alpha shoulder past
        // the central highlight.
        internal static Texture2D GetMidCookie()
        {
            if (_midCookie != null) return _midCookie;
            const int size = 128;
            var tex3 = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex3.wrapMode   = TextureWrapMode.Clamp;
            tex3.filterMode = FilterMode.Bilinear;
            tex3.hideFlags  = HideFlags.HideAndDontSave;
            const float plateauR = 0.22f;
            float center3 = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center3) / center3;
                    float dy = (y - center3) / center3;
                    float r3 = Mathf.Sqrt(dx * dx + dy * dy);
                    float a3;
                    if (r3 >= 1f)             a3 = 0f;
                    else if (r3 <= plateauR)  a3 = 1.0f;
                    else
                    {
                        float t = (r3 - plateauR) / (1f - plateauR);
                        a3 = 1f - Ss(t);
                    }
                    tex3.SetPixel(x, y, new Color(1f, 1f, 1f, a3));
                }
            }
            tex3.Apply();
            _midCookie = tex3;
            return tex3;
        }

        // Delegates to DustEffect (manual lit cubes drifting in world space).
        // The name BuildDustPS is historical — kept so existing call sites
        // in EnsureRangedSettings / EnsurePickableSettings stay stable.
        private static void BuildDustPS(Transform rfCoreTransform, ItemEquippable owner)
        {
            if (rfCoreTransform == null || owner == null) return;
            DustEffect.EnsurePool(rfCoreTransform);
        }

        // Walk up to the nearest ItemEquippable and classify it. Returns
        // (false, false) for both "no owner" and "BulletWeapon" — use
        // IsOwnerWeapon to distinguish those.
        internal static (bool isPickable, bool isMeleeOrTool) ClassifyOwner(Transform t)
        {
            try
            {
                Transform? cur = t;
                ItemEquippable? owner = null;
                int safety = 32;
                while (cur != null && owner == null && safety-- > 0)
                {
                    var go = cur.gameObject;
                    if (go == null) break;
                    owner = go.GetComponent<ItemEquippable>();
                    cur = cur.parent;
                }
                if (owner == null) return (false, false);
                if (owner.TryCast<ConsumableFlashlight>() != null) return (true, false);
                if (owner.TryCast<global::Gear.BulletWeapon>() != null) return (false, false);
                return (false, true);
            }
            catch { return (false, false); }
        }

        // True only when the nearest ItemEquippable ancestor is a BulletWeapon.
        // Lets us tell env lights apart from real weapon lights — important so
        // env fixtures don't get the ExtendedRange preset stamped onto them.
        internal static bool IsOwnerWeapon(Transform t)
        {
            try
            {
                Transform? cur = t;
                int safety = 32;
                while (cur != null && safety-- > 0)
                {
                    var go = cur.gameObject;
                    if (go == null) break;
                    var owner = go.GetComponent<ItemEquippable>();
                    if (owner != null)
                    {
                        try { return owner.TryCast<global::Gear.BulletWeapon>() != null; }
                        catch { return false; }
                    }
                    cur = cur.parent;
                }
            }
            catch { }
            return false;
        }

        // GTFO's first-person camera is a custom FPSCamera class, not a real
        // UnityEngine.Camera, so we match by GO name too.
        internal static bool IsCameraAttached(Transform t)
        {
            try
            {
                Transform? cur = t;
                int safety = 32;
                while (cur != null && safety-- > 0)
                {
                    var go = cur.gameObject;
                    if (go == null) break;
                    if (go.GetComponent<Camera>() != null) return true;
                    var nm = go.name;
                    if (nm != null &&
                        (nm.IndexOf("FPSCamera", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                         nm.IndexOf("FPSLookCamera", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                         nm.IndexOf("FlashlightHolder", System.StringComparison.OrdinalIgnoreCase) >= 0))
                        return true;
                    cur = cur.parent;
                }
            }
            catch { }
            return false;
        }

        internal static bool IsRFChild(string name) => name.StartsWith("RF_");

        // HelmetLight Lights edited in-place (no RF children). Tracked so
        // sync helpers can still mirror values, and IsUnmanaged returns false.
        internal static readonly System.Collections.Generic.HashSet<int> HelmetEditedIDs
            = new System.Collections.Generic.HashSet<int>();

        internal static bool IsUnmanaged(Transform t, string name)
        {
            if (IsRFChild(name)) return true;
            if (t.Find(RFCORE) != null) return false;
            var pl = t.GetComponent<Light>();
            if (pl != null && HelmetEditedIDs.Contains(pl.GetInstanceID())) return false;
            return true;
        }

        // Walk UP to find the nearest ItemEquippable ancestor.
        // Returns null if none found (pure env light or HelmetLight).
        internal static ItemEquippable? FindItemEquippableOwner(Transform t)
        {
            try
            {
                Transform? cur = t;
                int safety = 32;
                while (cur != null && safety-- > 0)
                {
                    var ie = cur.gameObject.GetComponent<ItemEquippable>();
                    if (ie != null) return ie;
                    cur = cur.parent;
                }
            }
            catch { }
            return null;
        }

        // True for GTFO's HelmetLight (melee/tool head lamp). The "HelmetLight"
        // name substring is a unique-enough discriminator on its own.
        internal static bool IsHelmetLight(Transform t)
        {
            try
            {
                Transform? cur = t;
                int safety = 32;
                while (cur != null && safety-- > 0)
                {
                    var nm = cur.gameObject.name;
                    if (nm != null && nm.IndexOf("HelmetLight", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                    cur = cur.parent;
                }
            }
            catch { }
            return false;
        }

        // HelmetLight is toggled by GTFO via SetActive on an ancestor
        // (FlashlightHolder), not via Light.enabled. Wrapping it in an RF tree
        // desyncs on re-enable, so we edit it in place instead.
        internal static void ApplyHelmetInPlace(Light l)
        {
            if (l == null || l.type != LightType.Spot) return;
            int id = l.GetInstanceID();
            if (!HelmetEditedIDs.Add(id)) return;

            // Strip any RF sandwich Patch_LightType may have already built —
            // otherwise cullingMask=0 hides the real light and this method
            // does nothing visible.
            foreach (var n in new[] { RFCORE, L1, L2, L3, L4, L5 })
            {
                var child = l.transform.Find(n);
                if (child != null) Object.Destroy(child.gameObject);
            }
            l.cullingMask = -1;

            l.intensity      = Plugin.HelmetIntensity;
            l.range          = Plugin.HelmetRange;
            l.spotAngle      = Mathf.Min(Plugin.HelmetAngle, 179f);
            l.innerSpotAngle = 0f;
            l.shadows        = LightShadows.None;
            try { l.cookie = GetSmoothCookie(); } catch { }
            _managed[id] = ManagedKind.MeleeOrTool;

            // Sibling "Flashlight" children with offset localRotation produce
            // a rightward beam bias. Walk two levels up to catch both
            // HelmetLight_Prefab and FlashlightHolder.
            var node = l.transform.parent;
            for (int lvl = 0; lvl < 2 && node != null; lvl++, node = node.parent)
            {
                var sibs = node.GetComponentsInChildren<Light>(true);
                if (sibs == null) continue;
                foreach (var sl in sibs)
                {
                    if (sl == null || sl == l) continue;
                    if (IsRFChild(sl.gameObject.name)) continue;
                    if (sl.type == LightType.Spot) sl.cullingMask = 0;
                }
            }

            LightUpdater.RegisterFlickerBaseline(l);
            LightUpdater.RegisterFlickerRef(l);
        }

        // Owner-aware overload used by the scanner so we don't have to
        // transform-walk for classification.
        internal static void ApplySandwich(Light light, ItemEquippable owner)
        {
            bool isPickable    = owner != null && owner.TryCast<ConsumableFlashlight>() != null;
            bool isRanged      = owner != null && owner.TryCast<global::Gear.BulletWeapon>() != null;
            bool isMeleeOrTool = owner != null && !isPickable && !isRanged;
            ApplySandwichInternal(light, isPickable, isMeleeOrTool);
            // For ranged: kill any other non-RF light on the weapon GO so the
            // original hard-edge circle can't bleed through.
            if (isRanged && owner != null)
            {
                try
                {
                    var all = owner.gameObject.GetComponentsInChildren<Light>(true);
                    if (all != null)
                    {
                        foreach (var sl in all)
                        {
                            if (sl == null || sl == light) continue;
                            if (IsRFChild(sl.gameObject.name)) continue;
                            // Tear down any RF sandwich a sibling already grew —
                            // otherwise it renders its own beam alongside m_lt's.
                            foreach (var n in new[] { RFCORE, L1, L2, L3, L4, L5 })
                            {
                                var c = sl.transform.Find(n);
                                if (c != null) Object.Destroy(c.gameObject);
                            }
                            _managed.Remove(sl.GetInstanceID());
                            sl.intensity   = 0f;
                            sl.range       = 0f;
                            sl.spotAngle   = 0f;
                            sl.cullingMask = 0;
                        }
                    }
                }
                catch { }
            }
        }

        // Re-suppress the ranged weapon's vanilla beam every frame so GTFO can't
        // un-hide it on swap/equip.
        //
        // m_lt itself is left alone except for cullingMask=0. We MUST NOT zero
        // its intensity/range/spotAngle — GTFO reads those during the wield
        // handshake to decide whether the weapon has a flashlight at all; if
        // they're zero, GTFO enters a stuck "flashlight disabled" state for
        // that weapon for the rest of the session (visible bug: the first
        // ranged wielded after any swap stops emitting a beam entirely).
        //
        // The small "vanilla dot" the user saw on some weapons came from a
        // SEPARATE Light component sibling on the same weapon GO (not m_lt).
        // Those are fully zeroed below — GTFO doesn't read their values for
        // the flashlight state machine, only m_lt's.
        internal static void SuppressRangedSiblings(Light mLt, ItemEquippable owner)
        {
            if (mLt == null || owner == null) return;
            try
            {
                if (mLt.cullingMask    != 0)  mLt.cullingMask    = 0;
                if (mLt.spotAngle      != 0f) mLt.spotAngle      = 0f;
                if (mLt.innerSpotAngle != 0f) mLt.innerSpotAngle = 0f;
                if (mLt.intensity      != 0f) mLt.intensity      = 0f;
                if (mLt.range          != 0f) mLt.range          = 0f;

                var all = owner.gameObject.GetComponentsInChildren<Light>(true);
                if (all == null) return;
                foreach (var sl in all)
                {
                    if (sl == null || sl == mLt) continue;
                    if (IsRFChild(sl.gameObject.name)) continue;
                    if (sl.cullingMask != 0)  sl.cullingMask = 0;
                    if (sl.intensity   != 0f) sl.intensity   = 0f;
                    if (sl.range       != 0f) sl.range       = 0f;
                    if (sl.spotAngle   != 0f) sl.spotAngle   = 0f;
                }
            }
            catch { }
        }

        internal static void ApplySandwich(Light light)
        {
            var (isPickable, isMeleeOrTool) = ClassifyOwner(light.transform);
            ApplySandwichInternal(light, isPickable, isMeleeOrTool);
        }

        // Re-stamp pickable values on a Light whose sandwich was built by a
        // lazy patch (Patch_LightType / set_intensity / set_enabled) before
        // the Light was reparented under ConsumableFlashlight — at which point
        // the owner walk found nothing and classified it as Ranged. The owner
        // guard inside also prevents stamping pickable values on a ranged m_lt
        // that GTFO temporarily reparents (root cause of the "ranged inherits
        // pickable visuals after consume" bug).
        internal static void EnsurePickableSettings(Light light)
        {
            if (light == null) return;

            // Hierarchy verification — only act when the Light is genuinely
            // owned by a ConsumableFlashlight. If not, bail without touching.
            var owner = FindItemEquippableOwner(light.transform);
            if (owner == null) return;
            bool genuinelyPickable;
            try { genuinelyPickable = owner.TryCast<ConsumableFlashlight>() != null; }
            catch { return; }
            if (!genuinelyPickable) return;

            int id = light.GetInstanceID();
            if (_managed.TryGetValue(id, out var kind) && kind == ManagedKind.Pickable)
                return;

            var warm = Plugin.PickColor;

            float parentAngle = Mathf.Min(Plugin.PickAngle, 179f);
            float l1Outer   = Mathf.Min(parentAngle + Plugin.Step1.Value, 179f);
            float l2Outer   = Mathf.Min(l1Outer    + Plugin.Step2.Value, 179f);
            float midOuter  = Mathf.Min(l1Outer    + Plugin.Step2.Value * Plugin.MidStepFraction.Value, 179f);
            float coreI     = Plugin.PickIntensity;
            float coreR     = Plugin.PickRange;

            // Parent stays culled out, but we keep its bookkeeping values
            // consistent so patches reading parent.intensity see the right scale.
            light.intensity      = coreI;
            light.range          = coreR;
            light.spotAngle      = parentAngle;
            light.innerSpotAngle = 0f;
            light.color          = warm;
            _managed[id]         = ManagedKind.Pickable;

            void UpdateLayer(string layerName, float intensity, float range, float angle)
            {
                var child = light.transform.Find(layerName);
                if (child == null) return;
                var cl = child.GetComponent<Light>();
                if (cl == null) return;
                cl.intensity      = intensity;
                cl.range          = range;
                cl.spotAngle      = angle;
                cl.innerSpotAngle = InnerForLayer(layerName, angle);
                cl.color          = warm;
            }
            UpdateLayer(RFCORE, coreI,                       coreR,                       l1Outer);
            UpdateLayer(L1,     coreI * Plugin.I1.Value,     coreR * Plugin.R1.Value,     midOuter);
            UpdateLayer(L2,     coreI * Plugin.I2.Value,     coreR * Plugin.R2.Value,     l2Outer);

            // Sandwich may have been built with Ranged classification — re-anchor
            // the flicker baseline to the new pickable intensity so per-frame
            // modulation targets the correct value.
            var coreT = light.transform.Find(RFCORE);
            if (coreT != null)
            {
                var coreL = coreT.GetComponent<Light>();
                if (coreL != null) LightUpdater.RegisterFlickerBaseline(coreL);

                BuildDustPS(coreT, owner);
            }
        }

        // Re-stamp Ranged values on a sandwich that was built with the wrong
        // classification. DoScan calls this every tick as a recovery path.
        //
        // Idempotent: bails immediately when the Light is already managed as
        // Ranged. Without this guard the per-tick intensity rewrite would
        // overwrite any in-progress flicker on the wielded weapon — the
        // visible "constant flicker spike" bug.
        //
        // forceResolve=true is for the OnWield path: drop the per-light and
        // per-owner caches so the resolve runs against the now-complete
        // transform tree, fixing the case where lazy-build at sandwich time
        // matched against an incomplete tree and cached a wrong/null preset.
        internal static void EnsureRangedSettings(Light light, bool forceResolve = false)
        {
            if (light == null) return;
            EnsureRangedSettings(light, FindItemEquippableOwner(light.transform), forceResolve);
        }

        // Owner-explicit overload — preferred form. GTFO's m_lt is SHARED across
        // all weapons (one Light component repurposed on wield), so walking up
        // from light.transform can return a stale/wrong owner. Callers that
        // know the wielded weapon should pass it directly.
        //
        // Because m_lt is shared, _lightPreset[lt_id] effectively stores the
        // "currently applied" preset, not a stable per-Light identity. We MUST
        // re-stamp the RF children with the new owner's preset on every wield
        // — otherwise the first-wielded weapon's preset locks in for all
        // subsequent wields.
        internal static void EnsureRangedSettings(Light light, ItemEquippable? owner, bool forceResolve)
        {
            if (light == null || owner == null) return;
            bool genuinelyRanged;
            try { genuinelyRanged = owner.TryCast<global::Gear.BulletWeapon>() != null; }
            catch { return; }
            if (!genuinelyRanged) return;

            int id = light.GetInstanceID();
            if (forceResolve)
            {
                // Drop the owner-side cache so the next ResolveByOwner re-walks
                // the now-(possibly) more complete transform tree. We do NOT
                // gate on the _managed/_lightPreset cache here — the shared
                // Light's preset must be replaced every wield.
                RangedPresets.ClearOwnerCacheEntry(owner);
            }

            RangedPresets.Preset preset;
            var ownerPreset = RangedPresets.ResolveByOwner(owner);
            if (ownerPreset.HasValue)
            {
                preset = ownerPreset.Value;
            }
            else if (_lightPreset.TryGetValue(id, out var cached))
            {
                // No keyword match — fall back to whatever the build-time
                // fingerprint resolved (cached on first ApplySandwichInternal).
                preset = cached;
            }
            else
            {
                preset = RangedPresets.ResolveByLight(light);
            }
            _lightPreset[id] = preset;

            _managed[id] = ManagedKind.Ranged;

            void UpdateLayer(string layerName, float intensity, float range, float angle)
            {
                var child = light.transform.Find(layerName);
                if (child == null) return;
                var cl = child.GetComponent<Light>();
                if (cl == null) return;
                cl.intensity      = intensity;
                cl.range          = range;
                cl.spotAngle      = angle;
                cl.innerSpotAngle = InnerForLayer(layerName, angle);
                cl.color          = preset.Tint;
            }
            UpdateLayer(RFCORE, preset.CoreIntensity, preset.CoreRange, WithLayerOverlap(preset.CoreAngle, 0));
            UpdateLayer(L1,     preset.HasMid ? preset.MidIntensity : 0f,
                                preset.HasMid ? preset.MidRange     : 0f,
                                preset.HasMid ? WithLayerOverlap(preset.MidAngle, 1) : 0f);
            UpdateLayer(L2,     preset.FallIntensity, preset.FallRange, preset.FallAngle);

            var coreT = light.transform.Find(RFCORE);
            if (coreT != null)
            {
                var coreL = coreT.GetComponent<Light>();
                if (coreL != null) LightUpdater.RegisterFlickerBaseline(coreL);

                BuildDustPS(coreT, owner);
            }
        }

        private static void ApplySandwichInternal(Light light, bool isPickable, bool isMeleeOrTool)
        {
            Transform t = light.transform;
            if (t.Find(L1) != null) return;
            if (light.type == LightType.Spot)
            {
                if (isPickable)
                {
                    light.intensity      = Plugin.PickIntensity;
                    light.range          = Plugin.PickRange;
                    light.spotAngle      = Plugin.PickAngle;
                    light.innerSpotAngle = 0f;
                    // Tint resolved by Plugin.ApplyPreset: warm amber for
                    // DEFAULT/CASUAL, more neutral for ITS_2050_BRUH.
                    light.color = Plugin.PickColor;
                    _managed[light.GetInstanceID()] = ManagedKind.Pickable;
                }
                else if (isMeleeOrTool)
                {
                    light.spotAngle      = Mathf.Min(Plugin.HelmetAngle, 179f);
                    light.intensity      = Plugin.HelmetIntensity;
                    light.range          = Plugin.HelmetRange;
                    light.innerSpotAngle = 0f;
                    _managed[light.GetInstanceID()] = ManagedKind.MeleeOrTool;
                }
            }

            float i = light.intensity;
            float r = light.range;
            // Require a BulletWeapon owner so env lights (owner == null) aren't
            // misclassified as Ranged and stamped with ExtendedRange.
            bool isRanged = !isPickable && !isMeleeOrTool && IsOwnerWeapon(light.transform);

            // Resolve and cache the per-Light preset NOW, while the parent
            // (angle, range, intensity) triple still matches the data-block
            // values from Patch_GearManager. Patch_LightAngle zeroes the angle
            // after this method runs, so any later fingerprint-resolve would
            // miss and fall back to Default.
            RangedPresets.Preset rangedPreset = RangedPresets.Default;
            if (isRanged && light.type == LightType.Spot)
            {
                var owner = FindItemEquippableOwner(light.transform);
                var ownerP = RangedPresets.ResolveByOwner(owner);
                rangedPreset = ownerP ?? RangedPresets.ResolveByLight(light);
                int rid = light.GetInstanceID();
                _managed[rid] = ManagedKind.Ranged;
                _lightPreset[rid] = rangedPreset;
            }

            Color c = isRanged ? rangedPreset.Tint : light.color;
            bool  e = light.enabled;

            // Point lights (some env lights) — 3-layer halo.
            if (light.type == LightType.Point)
            {
                float[] pI = { 0.55f, 0.30f, 0.10f };
                float[] pR = { 1.30f, 1.80f, 2.40f };
                string[] pNames = { L1, L2, L3 };
                for (int idx = 0; idx < pNames.Length; idx++)
                {
                    var go = new GameObject(pNames[idx]);
                    go.transform.SetParent(t, false);
                    go.transform.localPosition = Vector3.zero;
                    go.layer = t.gameObject.layer;
                    var pl = go.AddComponent<Light>();
                    pl.type      = LightType.Point;
                    pl.intensity = i * pI[idx];
                    pl.range     = r * pR[idx];
                    pl.color     = c;
                    pl.enabled   = e;
                    pl.shadows   = LightShadows.None;
                    pl.cullingMask = -1;
                }
                return;
            }

            // Spot lights — replace the vanilla beam. Ranged uses preset values
            // directly (each preset's per-layer spec is exact). Pickable/Melee/
            // env use the global multiplier path so config still affects them.
            float a, parentI, parentR;
            if (isRanged && light.type == LightType.Spot)
            {
                a       = rangedPreset.CoreAngle - Plugin.Step1.Value;
                parentI = rangedPreset.CoreIntensity;
                parentR = rangedPreset.CoreRange;
            }
            else
            {
                a       = light.spotAngle;
                parentI = i;
                parentR = r;
            }

            // Per-layer outer angles + intensity + range.
            float[] outerAngles, intensities, ranges;
            if (isRanged && light.type == LightType.Spot)
            {
                outerAngles = new[] { rangedPreset.CoreAngle,
                                      rangedPreset.HasMid ? rangedPreset.MidAngle      : 0f,
                                      rangedPreset.FallAngle };
                intensities = new[] { rangedPreset.CoreIntensity,
                                      rangedPreset.HasMid ? rangedPreset.MidIntensity  : 0f,
                                      rangedPreset.FallIntensity };
                ranges      = new[] { rangedPreset.CoreRange,
                                      rangedPreset.HasMid ? rangedPreset.MidRange      : 0f,
                                      rangedPreset.FallRange };
            }
            else
            {
                // Multiplier path — Step/MidStepFraction angles + I/R fractions
                // from config drive Pickable, Melee, and env lights.
                float l1Outer  = Mathf.Min(a + Plugin.Step1.Value, 179f);
                float l2Outer  = Mathf.Min(l1Outer + Plugin.Step2.Value, 179f);
                float midOuter = Mathf.Min(l1Outer + Plugin.Step2.Value * Plugin.MidStepFraction.Value, 179f);
                outerAngles = new[] { l1Outer, midOuter, l2Outer };
                intensities = new[] { parentI, parentI * Plugin.I1.Value, parentI * Plugin.I2.Value };
                ranges      = new[] { parentR, parentR * Plugin.R1.Value, parentR * Plugin.R2.Value };

                // Pickable: wider + brighter outer halo (vintage maglite feel).
                if (isPickable)
                {
                    intensities[2] = parentI * Plugin.I2.Value * 2.5f;
                    outerAngles[2] = Mathf.Min(l2Outer + 50f, 179f);
                }
            }

            for (int idx = 0; idx < AllLayers.Length; idx++)
            {
                AddLayer(t, AllLayers[idx],
                    intensities[idx], ranges[idx],
                    0f, WithLayerOverlap(outerAngles[idx], idx), c, e, idx == 0);
            }

            // Spillover Point light — short-range, low-intensity ambient that
            // fills the hemisphere just past the 179°-clamped falloff cone. The
            // user sees a hard dark border when walking near walls because the
            // outermost Spot caps at 179° (Unity limit); a Point light has no
            // FOV so it gently lights the area "behind" the cone. Pickable
            // gets a stronger spill (wider beam → more boundary to fill);
            // ranged gets a modest one. Built only for Spot lights since
            // Point-typed sandwiches (env lights) don't have a directional cone.
            if (light.type == LightType.Spot)
            {
                var spillGO = new GameObject(SPILL);
                spillGO.transform.SetParent(t, false);
                spillGO.transform.localPosition = Vector3.zero;
                spillGO.layer = t.gameObject.layer;
                var spillL = spillGO.AddComponent<Light>();
                spillL.type = LightType.Point;
                float spillFactor = isPickable ? 0.20f : 0.18f;
                spillL.intensity   = intensities[2] * spillFactor;
                spillL.range       = isPickable ? 8f : 7f;
                spillL.color       = c;
                spillL.enabled     = e;
                spillL.shadows     = LightShadows.None;
                spillL.cullingMask = -1;
            }

            // Hide the GTFO original. cullingMask=0 alone is the primary mask;
            // for ranged we also zero spotAngle so even a re-enabled vanilla
            // beam emits nothing.
            if (isRanged && light.type == LightType.Spot)
                light.spotAngle = 0f;

            light.cullingMask = 0;

            // Suppress sibling Spot/Point children — ConsumableFlashlight ships a
            // "Flashlight" child with offset localRotation that produces the
            // visible sideways beam if left alone.
            var parentGO = light.gameObject;
            var siblingLights = parentGO.GetComponentsInChildren<Light>(true);
            if (siblingLights != null)
            {
                foreach (var sl in siblingLights)
                {
                    if (sl == null || sl == light) continue;
                    if (IsRFChild(sl.gameObject.name)) continue;
                    if (sl.type == LightType.Spot || sl.type == LightType.Point)
                        sl.cullingMask = 0;
                }
            }
        }

        // Per-layer innerSpotAngle policy.
        //
        // Unity applies its own angular falloff between innerSpotAngle and
        // spotAngle on TOP of the cookie. For Core and Mid we set inner ≈ outer
        // (hair-thin Unity band) so the cookie alone shapes the layer profile —
        // otherwise the two falloffs compound into a visible brightness step at
        // each layer boundary.
        //
        // For the OUTERMOST (L2 falloff) layer there is no further outer layer
        // to step into, so we deliberately widen the Unity band to ~9° so the
        // last 9 degrees of the cone fade through BOTH the cookie's quintic
        // tail AND Unity's linear angular blend. The user perceived a "hard
        // dark edge" near walls even though the cookie was mathematically C2
        // at r=1 — adding a Unity-side fade band on top makes the visible
        // boundary disappear into the surrounding darkness.
        internal static float InnerForLayer(string layerName, float outerAngle)
        {
            const float falloffBandDegrees = 3f;
            if (layerName == L2) return Mathf.Max(0f, outerAngle - falloffBandDegrees);
            return Mathf.Max(0f, outerAngle - 0.5f);
        }

        private static void AddLayer(Transform parent, string name,
            float intensity, float range,
            float innerAngle, float outerAngle,
            Color color, bool enabled, bool isCore = false)
        {
            var parentLight = parent.GetComponent<Light>();

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.layer = parent.gameObject.layer;

            var l = go.AddComponent<Light>();
            l.type           = LightType.Spot;
            l.intensity      = intensity;
            l.range          = range;
            l.spotAngle      = outerAngle;
            l.innerSpotAngle = InnerForLayer(name, outerAngle);
            l.color          = color;
            l.enabled        = enabled;
            l.shadows        = LightShadows.None;
            l.cullingMask    = -1;

            Texture2D cookieTex;
            if (isCore)           cookieTex = GetCoreCookie();
            else if (name == L1)  cookieTex = GetMidCookie();
            else                  cookieTex = GetFalloffCookie();
            try { l.cookie = cookieTex; } catch { }

            if (parentLight != null)
            {
                l.renderMode      = parentLight.renderMode;
                l.bounceIntensity = parentLight.bounceIntensity;
            }

            if (isCore)
            {
                LightUpdater.RegisterFlickerBaseline(l);
                LightUpdater.RegisterFlickerRef(l);
            }
        }

        // Rebuild all sandwich layers (config change / scene change). Destroys
        // legacy L1..L5 children left over from older mod versions so the next
        // ApplySandwich starts fresh.
        //
        // _managed and the owner caches are intentionally kept across scenes:
        // ItemEquippable instances persist (loadout isn't rebuilt), and clearing
        // them forces a re-probe before the new prefab tree is populated.
        internal static void RebuildAll()
        {
            HelmetEditedIDs.Clear();
            _lightPreset.Clear();
            DustEffect.DestroyPool();
            // Do NOT clear _knownItems / _managed / owner caches — ItemEquippable
            // instances persist across scenes (loadout isn't rebuilt every level),
            // and dropping them forces a re-probe before the new prefab tree is
            // fully populated.
            foreach (var l in Object.FindObjectsOfType<Light>())
            {
                if (l == null) continue;
                if (IsRFChild(l.gameObject.name)) continue;
                foreach (var n in LegacyLayers) DestroyChild(l.transform, n);
            }
        }

        private static void DestroyChild(Transform parent, string childName)
        {
            var c = parent.Find(childName);
            if (c != null) Object.Destroy(c.gameObject);
        }

        internal static void ProcessAllSpotLights()
        {
            if (!Plugin.Enabled.Value) return;
            foreach (var l in Object.FindObjectsOfType<Light>())
            {
                if (l == null) continue;
                if (l.type != LightType.Spot && l.type != LightType.Point) continue;
                if (IsRFChild(l.gameObject.name)) continue;
                if (IsHelmetLight(l.transform)) { ApplyHelmetInPlace(l); continue; }
                // Skip tool-owned lights to avoid turret/sentry targeting crash.
                var (_, isMt) = ClassifyOwner(l.transform);
                if (isMt) continue;
                ApplySandwich(l);
            }
        }

        // Toggle both gameObject.SetActive and Light.enabled. Setting only
        // Light.enabled doesn't survive a parent SetActive(false→true) cycle:
        // Unity snapshots and restores child component state on reactivation.
        //
        // Owner-vs-wielded gate: refuse to ENABLE RF children whose owner is
        // not the currently wielded item. Mid-swap, multiple call sites may
        // drive this helper with the OLD weapon's transform — without this
        // gate, the children get re-activated right before Unity deactivates
        // the parent, then snapshot back to "active" on the next equip and
        // produce the stuck-beam bug. Disabling is never gated.
        internal static void SyncEnabled(Transform t, bool v)
        {
            // GTFO's SetAttachedFlashlightEnabled resets cullingMask=-1 when
            // turning the flashlight on, briefly revealing the vanilla beam.
            // Re-zero it here in addition to Patch_LightCullingMask.
            var pl = t.GetComponent<Light>();
            if (pl != null && t.Find(L1) != null)
                pl.cullingMask = 0;

            if (v)
            {
                var owner = FindItemEquippableOwner(t);
                // Only suppress when we positively identify a non-wielded owner.
                // Null owner = env light / HelmetLight tree / unparented Light —
                // those have no wield concept, so trust the caller.
                if (owner != null
                    && Patch_ItemWield.CurrentWieldedItem != null
                    && owner != Patch_ItemWield.CurrentWieldedItem)
                {
                    v = false;
                }
            }

            foreach (var n in AllLayers)
            {
                var child = t.Find(n);
                if (child == null) continue;
                var go = child.gameObject;
                if (v)
                {
                    // Activate GO first so the Light component is addressable,
                    // then flip enabled = true.
                    if (!go.activeSelf) go.SetActive(true);
                    var l = child.GetComponent<Light>();
                    if (l != null) l.enabled = true;
                }
                else
                {
                    var l = child.GetComponent<Light>();
                    if (l != null) l.enabled = false;
                    if (go.activeSelf) go.SetActive(false);
                }
            }

            // Toggle the Point-light spillover alongside the main layers.
            var spillT = t.Find(SPILL);
            if (spillT != null)
            {
                var sgo = spillT.gameObject;
                if (v)
                {
                    if (!sgo.activeSelf) sgo.SetActive(true);
                    var sl = spillT.GetComponent<Light>();
                    if (sl != null) sl.enabled = true;
                }
                else
                {
                    var sl = spillT.GetComponent<Light>();
                    if (sl != null) sl.enabled = false;
                    if (sgo.activeSelf) sgo.SetActive(false);
                }
            }

        }

        internal static void SyncColor(Transform t, Color v)
        {
            // For ranged and pickable weapons we use a fixed tint set at
            // sandwich build time. Do not let GTFO overwrite it with the
            // datablock color.
            var pl = t.GetComponent<Light>();
            var kind = GetManagedKind(pl);
            if (kind == ManagedKind.None || kind == ManagedKind.Ranged || kind == ManagedKind.Pickable)
                return;
            foreach (var n in AllLayers) Apply(t.Find(n), x => x.color = v);
        }

        // For managed Lights, force our absolute values regardless of what
        // GTFO writes to the parent — otherwise GTFO's Update resets values
        // and halos collapse. Ranged children are owned by EnsureRangedSettings
        // (per-preset spec), so we skip them here entirely.
        internal static void SyncIntensity(Transform t, float parentIntensity)
        {
            var pl = t.GetComponent<Light>();
            var kind = GetManagedKind(pl);
            if (kind == ManagedKind.MeleeOrTool) parentIntensity = Plugin.HelmetIntensity;
            else if (kind == ManagedKind.Pickable) parentIntensity = Plugin.PickIntensity;
            else if (kind == ManagedKind.Ranged) return;
            else if (parentIntensity <= 0f) return;

            float[] mults = { 1.00f, Plugin.I1.Value, Plugin.I2.Value };
            for (int i = 0; i < AllLayers.Length; i++)
                Apply(t.Find(AllLayers[i]), x => x.intensity = parentIntensity * mults[i]);
        }

        internal static void SyncRange(Transform t, float parentRange)
        {
            var pl = t.GetComponent<Light>();
            var kind = GetManagedKind(pl);
            if (kind == ManagedKind.MeleeOrTool) parentRange = Plugin.HelmetRange;
            else if (kind == ManagedKind.Pickable) parentRange = Plugin.PickRange;
            else if (kind == ManagedKind.Ranged) return;
            else if (parentRange <= 0f) return;

            float[] mults = { 1.00f, Plugin.R1.Value, Plugin.R2.Value };
            for (int i = 0; i < AllLayers.Length; i++)
                Apply(t.Find(AllLayers[i]), x => x.range = parentRange * mults[i]);
        }

        internal static void SyncAngle(Transform t, float coreAngle)
        {
            var pl = t.GetComponent<Light>();
            var kind = GetManagedKind(pl);
            if (kind == ManagedKind.MeleeOrTool) coreAngle = Plugin.HelmetAngle;
            else if (kind == ManagedKind.Pickable) coreAngle = Plugin.PickAngle;
            // Ranged children are angle-locked by the preset; syncing the
            // parent's forced-0 spotAngle into them would collapse the halo.
            else if (kind == ManagedKind.Ranged) return;

            // Same angle scheme as ApplySandwichInternal so GTFO's per-frame
            // writes don't reshape the halo through a different formula.
            float l1Outer = Mathf.Min(coreAngle + Plugin.Step1.Value, 179f);
            float l2Outer = Mathf.Min(l1Outer + Plugin.Step2.Value, 179f);
            float midOuter = Mathf.Min(l1Outer + Plugin.Step2.Value * Plugin.MidStepFraction.Value, 179f);
            float[] outerAngles = {
                WithLayerOverlap(l1Outer,  0),
                WithLayerOverlap(midOuter, 1),
                l2Outer,
            };

            for (int i = 0; i < AllLayers.Length; i++)
            {
                float outer = outerAngles[i];
                string layerName = AllLayers[i];
                Apply(t.Find(layerName), x => { x.innerSpotAngle = InnerForLayer(layerName, outer); x.spotAngle = outer; });
            }
        }

        private static void Apply(Transform? child, System.Action<Light> action)
        {
            if (child == null) return;
            var l = child.GetComponent<Light>();
            if (l != null) action(l);
        }
    }

    // Volumetric dust simulated as a pool of tiny lit cubes drifting in
    // world space. Each mote stores its own velocity; LightUpdater.Update
    // calls Tick() once per frame to drift them and respawn ones that
    // wandered too far from the camera.
    //
    // Why not a real ParticleSystem: the IL2CPP-stripped Particle-
    // SystemModule.dll in this build doesn't expose the module struct
    // setters needed to configure one programmatically (ps.main / ps.shape
    // are read-only getters; MinMaxCurve(float,float) ctor missing;
    // ShapeModule fields stripped). And the scene contains 0 PS components
    // anywhere, so cloning a vanilla one isn't an option either.
    //
    // Cubes get their visible brightness from RF_Core's spotlight via
    // Unity's standard per-pixel lighting — when a mote sits inside the
    // active beam it glows; outside the cone it falls to ambient (near
    // black in deep-complex environments). One shared Standard material
    // across all motes keeps the GPU cost reasonable.
    internal static class DustEffect
    {
        private const int   Count        = 60;
        private const float MaxDistance  = 5f;    // respawn if farther than this from anchor
        private const float SpawnRadius  = 3.5f;  // respawn within this radius around anchor
        private const float Scale        = 0.008f; // ~8 mm — reads as a fine speck, not a cube
        private const float VelMin       = 1.0f;   // m/s — temporarily aggressive
        private const float VelMax       = 2.5f;   // m/s — to confirm visible motion
        // Resample velocity every ~3-7s so motes shift direction (air-current feel).
        private const float ResampleMin  = 3f;
        private const float ResampleMax  = 7f;

        // Diag: log a few Tick samples right after pool build so we can
        // confirm motion. Wiped on DestroyPool.
        private static int _tickDiagBudget = 0;
        private static float _nextTickDiagAt = 0f;
        // Throw budget — surfaced through the Update-side try/catch.
        internal static int _tickThrowLogBudget = 3;

        private static GameObject? _root;
        private static GameObject[]? _motes;
        private static Vector3[]?   _vels;
        private static float[]?     _nextResample;
        private static Material?    _sharedMat;
        private static Transform?   _anchor;

        // Hook from LightSandwich.Ensure*Settings — anchorHint is the
        // RF_Core transform (parented under m_lt → ancestor chain reaches
        // FPSCameraHolder, which is what we want as the camera anchor).
        internal static void EnsurePool(Transform anchorHint)
        {
            if (!Plugin.DustEnabled.Value) return;
            if (anchorHint == null) return;

            if (_anchor == null) _anchor = FindCameraAnchor(anchorHint);
            if (_anchor == null) return;

            if (_root != null && _motes != null && _vels != null) return;

            try
            {
                if (_sharedMat == null) _sharedMat = BuildSharedMaterial();
                if (_sharedMat == null) return;

                _root = new GameObject("RF_DustPool");
                // Survive scene transitions — without this Unity destroys the
                // root (and all cubes) on every level load, leaving stale
                // wrappers that evaluate as Unity-null. The diag log proved
                // this was exactly the failure mode: alive=0 every Tick.
                Object.DontDestroyOnLoad(_root);

                _motes = new GameObject[Count];
                _vels  = new Vector3[Count];
                _nextResample = new float[Count];

                Vector3 origin = _anchor.position;
                float now = Time.unscaledTime;
                for (int i = 0; i < Count; i++)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = "RF_DustMote_" + i;
                    go.transform.SetParent(_root.transform, false);
                    // Strip collider — we don't want physics interactions.
                    var col = go.GetComponent<Collider>();
                    if (col != null) Object.Destroy(col);
                    var rend = go.GetComponent<MeshRenderer>();
                    if (rend != null)
                    {
                        rend.sharedMaterial    = _sharedMat;
                        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        rend.receiveShadows    = false;
                    }
                    // Variable scale per mote — most tiny specks, occasional
                    // bigger flecks add visual variety (user request).
                    float s = Scale * Random.Range(0.5f, 2.0f);
                    go.transform.localScale = new Vector3(s, s, s);
                    go.transform.position   = origin + Random.insideUnitSphere * SpawnRadius;
                    _vels[i]         = Random.onUnitSphere * Random.Range(VelMin, VelMax);
                    _nextResample[i] = now + Random.Range(ResampleMin, ResampleMax);
                }

                _tickDiagBudget = 6;
                _nextTickDiagAt = Time.unscaledTime + 1f;
                Plugin.Logger.LogWarning("[RF] DustEffect: pool built, count=" + Count
                    + " anchor=" + _anchor.gameObject.name
                    + " anchorPos=(" + _anchor.position.x.ToString("F2")
                    + "," + _anchor.position.y.ToString("F2")
                    + "," + _anchor.position.z.ToString("F2") + ")");
            }
            catch (System.Exception ex)
            {
                Plugin.Logger.LogWarning("[RF] DustEffect.EnsurePool failed: "
                    + ex.GetType().Name + " " + ex.Message);
                DestroyPool();
            }
        }

        internal static void Tick(float dt)
        {
            // Entry diag — fires BEFORE null guards so we can tell "Tick not
            // called at all" apart from "called but bailed on null guard".
            float now0 = Time.unscaledTime;
            bool diagThisFrame = _tickDiagBudget > 0 && now0 >= _nextTickDiagAt;
            if (diagThisFrame)
            {
                _tickDiagBudget--;
                _nextTickDiagAt = now0 + 2f;
                Plugin.Logger.LogWarning("[RF] DustEffect Tick entry: dt=" + dt.ToString("F3")
                    + " motes=" + (_motes != null)
                    + " vels="  + (_vels  != null)
                    + " anchor=" + (_anchor != null));
            }

            if (_motes == null || _vels == null || _anchor == null) return;
            Vector3 origin = _anchor.position;
            float now = now0;
            float r2 = MaxDistance * MaxDistance;

            int processed = 0, alive = 0, respawned = 0;
            Vector3 dbgPre = Vector3.zero, dbgPost = Vector3.zero, dbgV = Vector3.zero;
            bool dbgCaptured = false;

            for (int i = 0; i < _motes.Length; i++)
            {
                var go = _motes[i];
                if (go == null) continue;
                alive++;
                Vector3 prevPos = go.transform.position;
                Vector3 pos = prevPos + _vels[i] * dt;
                if ((pos - origin).sqrMagnitude > r2)
                {
                    pos = origin + Random.insideUnitSphere * SpawnRadius;
                    _vels[i] = Random.onUnitSphere * Random.Range(VelMin, VelMax);
                    if (_nextResample != null)
                        _nextResample[i] = now + Random.Range(ResampleMin, ResampleMax);
                    respawned++;
                }
                else if (_nextResample != null && now >= _nextResample[i])
                {
                    _vels[i] = Random.onUnitSphere * Random.Range(VelMin, VelMax);
                    _nextResample[i] = now + Random.Range(ResampleMin, ResampleMax);
                }
                go.transform.position = pos;
                processed++;
                if (!dbgCaptured) { dbgPre = prevPos; dbgPost = pos; dbgV = _vels[i]; dbgCaptured = true; }
            }

            if (diagThisFrame)
            {
                Plugin.Logger.LogWarning("[RF] DustEffect Tick body:"
                    + " arrLen=" + _motes.Length
                    + " alive=" + alive
                    + " processed=" + processed
                    + " respawned=" + respawned
                    + " anchor=(" + origin.x.ToString("F2") + "," + origin.y.ToString("F2") + "," + origin.z.ToString("F2") + ")"
                    + " pre=(" + dbgPre.x.ToString("F2") + "," + dbgPre.y.ToString("F2") + "," + dbgPre.z.ToString("F2") + ")"
                    + " post=(" + dbgPost.x.ToString("F2") + "," + dbgPost.y.ToString("F2") + "," + dbgPost.z.ToString("F2") + ")"
                    + " v=(" + dbgV.x.ToString("F2") + "," + dbgV.y.ToString("F2") + "," + dbgV.z.ToString("F2") + ")"
                    + " dt=" + dt.ToString("F3"));
            }
        }

        internal static void DestroyPool()
        {
            try { if (_root != null) Object.Destroy(_root); } catch { }
            _root = null;
            _motes = null;
            _vels = null;
            _nextResample = null;
            _tickDiagBudget = 0;
            // Keep _anchor / _sharedMat — they survive scene reloads cheaply.
        }

        // Walks UP from the RF_Core transform looking for the camera-rooted
        // ancestor. Mirrors LightSandwich.IsCameraAttached's name-matching
        // since GTFO's FPSCamera isn't a real UnityEngine.Camera.
        private static Transform? FindCameraAnchor(Transform t)
        {
            try
            {
                Transform? cur = t;
                int safety = 32;
                while (cur != null && safety-- > 0)
                {
                    var nm = cur.gameObject.name;
                    if (nm != null &&
                        (nm.IndexOf("FPSCameraHolder", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                         nm.IndexOf("FPSCamera",       System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                         nm.IndexOf("FPSLookCamera",   System.StringComparison.OrdinalIgnoreCase) >= 0))
                        return cur;
                    cur = cur.parent;
                }
            }
            catch { }
            return null;
        }

        private static Material? BuildSharedMaterial()
        {
            // Standard shader → motes participate in real-time lighting from
            // RF_Core. Fall back to a couple of other lit shaders if the
            // primary one isn't available in this Unity build.
            var sh = Shader.Find("Standard");
            if (sh == null) sh = Shader.Find("Mobile/Diffuse");
            if (sh == null) sh = Shader.Find("Diffuse");
            if (sh == null) return null;

            var mat = new Material(sh);
            mat.hideFlags = HideFlags.HideAndDontSave;
            // Dark grey albedo: ambient * 0.10 ≈ near-invisible outside the
            // beam; direct light (RF_Core) * 0.10 still shows clearly because
            // RF_Core intensity is ~0.80. Tuning knob if needed: 0.05 darker,
            // 0.20 brighter / more ambient bleed.
            try { mat.SetColor("_Color", new Color(0.10f, 0.10f, 0.10f, 1f)); } catch { }
            // Disable specular highlights / smoothness for a flat dust look.
            try { mat.SetFloat("_Glossiness", 0f); } catch { }
            try { mat.SetFloat("_Metallic",   0f); } catch { }
            return mat;
        }
    }

    // Classifies each FlashlightSettingsDataBlock and rewrites its Core values
    // before gear is loaded. PID 1 (HelmetLight) and PID 4 (melee torch) are
    // zeroed since both are replaced by RF_HelmetSynth.
    [HarmonyPatch(typeof(GearManager), nameof(GearManager.LoadOfflineGearDatas))]
    internal static class Patch_GearManager
    {
        [HarmonyPrefix]
        static void Prefix()
        {
            if (!Plugin.Enabled.Value) return;
            if (Plugin.Mode.Value == FlashLightMode.VANILLA) return;

            FlashlightSettingsDataBlock[] blocks =
                GameDataBlockBase<FlashlightSettingsDataBlock>.Wrapper.Blocks.ToArray();

            RangedPresets.ByPID.Clear();

            foreach (var b in blocks)
            {
                if (b == null) continue;
                uint pid = ((GameDataBlockBase<FlashlightSettingsDataBlock>)(object)b).persistentID;

                // PID 1 = HelmetLight, PID 4 = melee torch — replaced by
                // RF_HelmetSynth, so their originals must emit nothing.
                if (pid == 1u || pid == 4u)
                {
                    b.angle     = 0f;
                    b.intensity = 0f;
                    b.range     = 0f;
                    continue;
                }

                string blockName = b.name ?? $"PID_{pid}";
                var preset = RangedPresets.Classify(blockName, b.angle, b.range);
                RangedPresets.ByPID[pid] = preset;

                // Wide and Extended share (50°, 26m); bias Wide's intensity by
                // +0.01 so ResolveByLight can tell them apart by the triple.
                float storedAngle     = Mathf.Max(0f, preset.CoreAngle - Plugin.Step1.Value);
                float storedIntensity = preset.CoreIntensity;
                if (preset.Name == RangedPresets.WideRange.Name) storedIntensity += 0.01f;

                b.angle     = storedAngle;
                b.intensity = storedIntensity;
                b.range     = preset.CoreRange;
            }
        }
    }

    // Lazy-build sandwich when a Spot light is first set up. HelmetLight goes
    // through ApplyHelmetInPlace instead so it stays inside GTFO's own
    // SetActive lifecycle (an RF tree desyncs on toggles).
    [HarmonyPatch(typeof(Light), "set_type")]
    internal static class Patch_LightType
    {
        [HarmonyPostfix]
        static void Postfix(Light __instance, LightType value)
        {
            if (!Plugin.Enabled.Value || value != LightType.Spot) return;
            if (LightSandwich.IsRFChild(__instance.gameObject.name)) return;
            if (LightSandwich.IsHelmetLight(__instance.transform))
            {
                LightSandwich.ApplyHelmetInPlace(__instance);
                return;
            }
            // Never sandwich tools/turrets/mines/sentries — forcing cullingMask=0
            // breaks the sentry targeting cone (it reads layer masks from its
            // own light). Their visual flashlight is the helmet synth anyway.
            var (isPick, isMt) = LightSandwich.ClassifyOwner(__instance.transform);
            if (isMt) return;
            LightSandwich.ApplySandwich(__instance);
        }
    }

    // Primary hook for the weapon flashlight. State application is owned by
    // EnforceRFState; this patch only does the immediate side effects (lazy
    // build, sibling suppression, penumbra reset).
    [HarmonyPatch(typeof(ItemEquippable), nameof(ItemEquippable.SetAttachedFlashlightEnabled))]
    internal static class Patch_EquippableSetFlashlight
    {
        [HarmonyPostfix]
        static void Postfix(ItemEquippable __instance, bool mode)
        {
            if (!Plugin.Enabled.Value) return;
            if (__instance == null) return;

            Light light = __instance.m_lt;
            if (light == null) return;

            // Pass __instance as owner so classification is reliable.
            if (light.transform.Find(LightSandwich.L1) == null)
                LightSandwich.ApplySandwich(light, __instance);

            if (__instance.TryCast<global::Gear.BulletWeapon>() != null)
            {
                LightSandwich.SuppressRangedSiblings(light, __instance);
                // The sandwich may have been built earlier (set_type / set_enabled /
                // set_intensity) before the Light was reparented under the weapon,
                // so classification missed and the preset fell back to Default.
                // Force a re-resolve now that the real owner is known.
                LightSandwich.EnsureRangedSettings(light, __instance, forceResolve: false);
            }
            else if (__instance.TryCast<ConsumableFlashlight>() != null)
            {
                // Same logic for the consumable flashlight: GTFO's m_lt is
                // shared, and a previous ranged wield may have left ranged
                // values on the RF children. Re-stamp pickable settings so the
                // very first frame after wield shows the correct beam shape.
                LightSandwich.EnsurePickableSettings(light);
            }

            // GTFO sometimes sets innerSpotAngle != 0, producing a hard ring.
            light.innerSpotAngle = 0f;

            // GTFO can also write cullingMask AFTER our Postfix via a queued
            // call, so re-zero here too.
            if (light.transform.Find(LightSandwich.L1) != null)
                light.cullingMask = 0;

            // Don't enable RF here — the dark-gap gate inside EnforceRFState
            // owns enable timing.
        }
    }

    // Syncs enabled state + lazily builds the sandwich for any spot/point
    // light first being enabled (covers environment lights and enemies).
    [HarmonyPatch(typeof(Behaviour), "set_enabled")]
    internal static class Patch_BehaviourEnabled
    {
        [HarmonyPostfix]
        static void Postfix(Behaviour __instance, bool value)
        {
            if (!Plugin.Enabled.Value) return;

            var t = __instance.transform;
            var name = __instance.gameObject.name;
            if (LightSandwich.IsRFChild(name)) return;

            var light = __instance.TryCast<Light>();
            if (light == null) return;
            if (light.type != LightType.Spot && light.type != LightType.Point) return;

            if (value && t.Find(LightSandwich.L1) == null)
            {
                if (LightSandwich.IsHelmetLight(t))
                {
                    LightSandwich.ApplyHelmetInPlace(light);
                    return;
                }
                var (isPick2, isMt2) = LightSandwich.ClassifyOwner(t);
                if (isMt2) return;
                LightSandwich.ApplySandwich(light);
                return;
            }

            // RF enable/disable is driven entirely by EnforceRFState (LateUpdate
            // + OnPreCull). Mirroring light.enabled here would race against
            // GTFO clearing the wielded weapon's parent.m_lt.enabled every frame.
        }
    }

    // Mirror GTFO's color writes onto RF children — except for Ranged/Pickable
    // which use a fixed tint set at sandwich build time.
    [HarmonyPatch(typeof(Light), "set_color")]
    internal static class Patch_LightColor
    {
        [HarmonyPostfix]
        static void Postfix(Light __instance, Color value)
        {
            var t = __instance.transform;
            if (LightSandwich.IsUnmanaged(t, __instance.gameObject.name)) return;
            LightSandwich.SyncColor(t, value);
        }
    }

    // Keeps layers proportional to per-weapon intensity.
    // Also lazy-builds sandwich when a Spot light has its intensity set for
    // the first time — catches vanilla melee Lights activated via SetActive()
    // which Patch_BehaviourEnabled cannot intercept.
    [HarmonyPatch(typeof(Light), "set_intensity")]
    internal static class Patch_LightIntensity
    {
        [HarmonyPostfix]
        static void Postfix(Light __instance, float value)
        {
            if (!Plugin.Enabled.Value) return;
            var t    = __instance.transform;
            var name = __instance.gameObject.name;
            if (LightSandwich.IsRFChild(name)) return;

            if (t.Find(LightSandwich.L1) == null)
            {
                if (__instance.type == LightType.Spot || __instance.type == LightType.Point)
                {
                    if (LightSandwich.IsHelmetLight(__instance.transform))
                    {
                        LightSandwich.ApplyHelmetInPlace(__instance);
                        return;
                    }
                    // Skip tool-owned lights (turret/sentry crash, see Patch_LightType).
                    var (isPick3, isMt3) = LightSandwich.ClassifyOwner(__instance.transform);
                    if (isMt3) return;
                    LightSandwich.ApplySandwich(__instance);
                }
                return;
            }
            LightSandwich.SyncIntensity(t, value);
        }
    }

    // Keeps layers proportional to per-weapon range
    [HarmonyPatch(typeof(Light), "set_range")]
    internal static class Patch_LightRange
    {
        [HarmonyPostfix]
        static void Postfix(Light __instance, float value)
        {
            var t = __instance.transform;
            if (LightSandwich.IsUnmanaged(t, __instance.gameObject.name)) return;
            LightSandwich.SyncRange(t, value);
        }
    }

    // Keeps RF layers in sync with parent.spotAngle and forces ranged parents
    // to spotAngle=0 so the vanilla beam can't emit anything.
    [HarmonyPatch(typeof(Light), "set_spotAngle")]
    internal static class Patch_LightAngle
    {
        [HarmonyPostfix]
        static void Postfix(Light __instance, float value)
        {
            var t = __instance.transform;
            var name = __instance.gameObject.name;
            if (LightSandwich.IsRFChild(name)) return;
            if (t.Find(LightSandwich.L1) != null)
            {
                if (__instance.innerSpotAngle != 0f) __instance.innerSpotAngle = 0f;
                if (LightSandwich.GetManagedKind(__instance) == LightSandwich.ManagedKind.Ranged)
                {
                    // The value != 0 guard is critical: writing spotAngle=0
                    // re-fires this Postfix; without it we recurse forever
                    // and stack-overflow at lobby load.
                    if (value != 0f) __instance.spotAngle = 0f;
                    return;
                }
            }
            if (LightSandwich.IsUnmanaged(t, name)) return;
            LightSandwich.SyncAngle(t, value);
        }
    }

    // Prevent GTFO from restoring a hard edge via innerSpotAngle.
    [HarmonyPatch(typeof(Light), "set_innerSpotAngle")]
    internal static class Patch_LightInnerAngle
    {
        [HarmonyPrefix]
        static bool Prefix(Light __instance, ref float value)
        {
            if (LightSandwich.IsRFChild(__instance.gameObject.name)) return true;
            if (__instance.transform.Find(LightSandwich.L1) == null) return true;
            value = 0f;
            return true;
        }
    }

    // Force cullingMask=0 on any sandwiched parent Light. GTFO's
    // SetAttachedFlashlightEnabled writes cullingMask=-1 to "show" the
    // flashlight, which would reveal the vanilla hard-edge beam under our
    // sandwich. Also hides sibling Spot lights on the same GO (e.g.
    // ConsumableFlashlight's offset-rotated "Flashlight" child).
    [HarmonyPatch(typeof(Light), "set_cullingMask")]
    internal static class Patch_LightCullingMask
    {
        // Re-entrance guard: writing sl.cullingMask=0 below re-fires this patch
        // on each sibling, which can cascade and overflow on turret/sentry GOs.
        [System.ThreadStatic] private static bool _inPatch;

        [HarmonyPrefix]
        static bool Prefix(Light __instance, ref int value)
        {
            if (LightSandwich.IsRFChild(__instance.gameObject.name)) return true;
            if (__instance.transform.Find(LightSandwich.L1) != null)
            {
                value = 0;
                if (!_inPatch)
                {
                    _inPatch = true;
                    try
                    {
                        var siblings = __instance.gameObject.GetComponentsInChildren<Light>(true);
                        if (siblings != null)
                        {
                            foreach (var sl in siblings)
                            {
                                if (sl == null || sl == __instance) continue;
                                if (LightSandwich.IsRFChild(sl.gameObject.name)) continue;
                                if ((sl.type == LightType.Spot || sl.type == LightType.Point) && sl.cullingMask != 0)
                                    sl.cullingMask = 0;
                            }
                        }
                    }
                    catch { }
                    finally { _inPatch = false; }
                }
            }
            return true;
        }
    }

    // Authoritative wielded-item tracking.
    internal static class Patch_ItemWield
    {
        internal static ItemEquippable? CurrentWieldedItem = null;
    }

    // Prefix (not Postfix): GTFO's OnWield body calls SetAttachedFlashlightEnabled
    // internally. Setting CurrentWieldedItem before the body lets the SetFL
    // postfix see the correct wielded item immediately.
    [HarmonyPatch(typeof(ItemEquippable), "OnWield")]
    internal static class Patch_OnWield
    {
        [HarmonyPrefix]
        static void Prefix(ItemEquippable __instance)
        {
            if (__instance == null) return;
            Patch_ItemWield.CurrentWieldedItem = __instance;
            // _pendingWieldItem is a deterministic fallback for the mid-swap
            // window where m_isWielded hasn't transitioned yet. EnforceRFState
            // clears it once the poll catches up.
            LightUpdater._pendingWieldItem = __instance;
            LightUpdater.OnWieldedChanged(__instance);

            try
            {
                bool isGunOrPick = __instance.TryCast<global::Gear.BulletWeapon>() != null
                                || __instance.TryCast<ConsumableFlashlight>() != null;
                if (isGunOrPick)
                    LightUpdater._lastObservedWasGun = true;
                LightUpdater.EndSwap();
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(ItemEquippable), "OnUnWield")]
    internal static class Patch_OnUnWield
    {
        [HarmonyPostfix]
        static void Postfix(ItemEquippable __instance)
        {
            if (__instance == null) return;
            // Register here too so the patch-tracked item list picks up items
            // we observed unwield-first (auto-stow before our first OnWield).
            LightUpdater.RegisterItem(__instance);
            // Disable the outgoing m_lt RF children before the swap completes
            // so they can't persist as stuck on the new wielded slot. Melee/tool
            // unwield is excluded: those don't own an RF sandwich, and we'd
            // wrongly disable the gun's layers on turret-placement unwield.
            try
            {
                bool isGunOrPick = __instance.TryCast<global::Gear.BulletWeapon>() != null
                                || __instance.TryCast<ConsumableFlashlight>() != null;
                if (isGunOrPick)
                {
                    var lt = __instance.m_lt;
                    if (lt != null && lt.transform.Find(LightSandwich.L1) != null)
                        LightSandwich.SyncEnabled(lt.transform, false);
                }
                // Arm swap-in-progress so Enforce force-disables every source
                // until the matching OnWield clears it. Covers swap paths
                // whose input we don't observe (auto-stow, network sync, etc.).
                LightUpdater.BeginSwap();
            }
            catch { }
            if (Patch_ItemWield.CurrentWieldedItem == __instance)
                Patch_ItemWield.CurrentWieldedItem = null;
            // Clear pending if this was a stale write from an aborted swap.
            if (LightUpdater._pendingWieldItem == __instance)
                LightUpdater._pendingWieldItem = null;
        }
    }
}
