using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GTFO_SuperFlashlight
{
    // DEFAULT is the source of truth — every config value is a DEFAULT value.
    // CASUAL and ITS_2050_BRUH apply hardcoded offsets on top, so a tweak to
    // any DEFAULT config value shifts the other modes by the same amount.
    // MINIMAL forces all ranged weapons to the two-layer (Core + Falloff) preset.
    public enum FlashLightMode
    {
        MINIMAL,         // All ranged weapons: Core + Falloff only, no Mid layer.
        VANILLA,         // No data-block changes at all.
        DEFAULT,         // Balanced baseline.
        CASUAL,          // DEFAULT + small range/FOV offsets.
        ITS_2050_BRUH    // DEFAULT + large range/FOV offsets.
    }

    // Edit these to retune CASUAL / ITS_2050_BRUH without touching DEFAULT.
    // CS0649: some offset slots aren't used by the current CASUAL / ITS_2050
    // tables but are kept available for future tuning without bumping the
    // config schema. Suppress the "never assigned" warning for the whole
    // struct family.
#pragma warning disable CS0649
    internal struct PresetOffset
    {
        public float CoreAngle, CoreIntensity, CoreRange;
        public float MidAngle,  MidIntensity,  MidRange;
        public float FallAngle, FallIntensity, FallRange;
    }

    internal struct ModeAdjustments
    {
        // Applied to ALL 5 ranged presets (Short, Med1, Med2, Wide, Ext).
        public PresetOffset Ranged;

        // Applied to helmet synth (`RF_HelmetSynth` for melee / tools).
        public float HelmetAngleOffset;
        public float HelmetIntensityOffset;
        public float HelmetRangeOffset;
        // The helmet falloff cone's range = MAIN range + this. When >0, the
        // falloff cone extends past the main cone, giving a soft fade past
        // the main range instead of a hard "wall" at the end.
        public float HelmetFallRangeExtra;

        // Applied to the pickable (consumable long range) flashlight.
        public float PickRangeOffset;
        // null = keep the default warm amber tint.
        public Color? PickColorOverride;
    }
#pragma warning restore CS0649

    internal static class ModePresets
    {
        // MINIMAL: same DEFAULT values but -2m Core range, no Mid layer.
        // Fall range stays at DEFAULT. Mid layer is killed by noMid=true
        // passed into BuildFromConfig from ApplyPreset.
        private static readonly ModeAdjustments _minimal = new ModeAdjustments
        {
            Ranged = new PresetOffset
            {
                CoreRange = -2f,
            },
        };

        // CASUAL is small generous offsets on top of DEFAULT.
        private static readonly ModeAdjustments _casual = new ModeAdjustments
        {
            Ranged = new PresetOffset
            {
                CoreRange   = +2f,
                MidAngle    = +10f,
                MidRange    = +2f,
                FallAngle   = +3f,     // slightly smoother (wider) falloff cone
                FallRange   = +1f,
            },
            HelmetRangeOffset    = +1f,
            HelmetFallRangeExtra = +3f, // soft fade ~3m past main range
        };

        // ITS_2050_BRUH is much wider, longer, and bumps pickable + helmet too.
        private static readonly ModeAdjustments _its2050 = new ModeAdjustments
        {
            Ranged = new PresetOffset
            {
                CoreRange   = +4f,
                MidAngle    = +20f,
                MidRange    = +4f,
                FallAngle   = +6f,     // a bit smoother falloff
                FallRange   = +3f,
            },
            HelmetRangeOffset     = +4f,
            HelmetIntensityOffset = +0.3f,
            HelmetFallRangeExtra  = +5f,
            PickRangeOffset       = +2f,
            // Slightly more neutral than the default warm-amber (1.0, 0.84, 0.60).
            PickColorOverride     = new Color(1.0f, 0.92f, 0.82f),
        };

        // DEFAULT / VANILLA → no offsets (zeroed struct).
        // MINIMAL has -2m range offsets; the "no Mid layer" part is handled
        // separately via the noMid flag passed to BuildFromConfig.
        internal static ModeAdjustments For(FlashLightMode m)
        {
            return m switch
            {
                FlashLightMode.MINIMAL       => _minimal,
                FlashLightMode.CASUAL        => _casual,
                FlashLightMode.ITS_2050_BRUH => _its2050,
                _                            => default,
            };
        }
    }

    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BasePlugin
    {
        internal static ManualLogSource Logger = null!;

        // User-editable config. All values here are the DEFAULT-mode baseline;
        // other modes derive from these via ModePresets.For().

        // 01 - General
        public static ConfigEntry<bool>           Enabled                  = null!;
        public static ConfigEntry<FlashLightMode> Mode                     = null!;
        public static ConfigEntry<bool>           AttachLightToWeaponBone  = null!;

        // 02 - Core layer outer-angle step (global; same for every preset).
        public static ConfigEntry<float> Step1 = null!;

        // 03 - Middle layer (used ONLY by Pickable + env-light multiplier path;
        //                    ranged presets define their own Mid values).
        public static ConfigEntry<float> I1              = null!;
        public static ConfigEntry<float> R1              = null!;
        public static ConfigEntry<float> MidStepFraction = null!;

        // 04 - Falloff layer (same — multiplier-path only).
        public static ConfigEntry<float> I2    = null!;
        public static ConfigEntry<float> R2    = null!;
        public static ConfigEntry<float> Step2 = null!;

        // 05 - Helmet synth (DEFAULT mode values; CASUAL / ITS_2050 add offsets).
        public static ConfigEntry<float> MeleeAngle     = null!;
        public static ConfigEntry<float> MeleeIntensity = null!;
        public static ConfigEntry<float> MeleeRange     = null!;

        // 06 - Perlin Flicker
        public static ConfigEntry<bool>  FlickerEnabled   = null!;
        public static ConfigEntry<float> FlickerSpeed     = null!;
        public static ConfigEntry<float> FlickerAmplitude = null!;

        // 07 - Rare Flicker
        public static ConfigEntry<bool>  RareFlickerEnabled     = null!;
        public static ConfigEntry<float> RareFlickerIntervalMin = null!;
        public static ConfigEntry<float> RareFlickerIntervalMax = null!;

        // 08 - Horror Flicker
        public static ConfigEntry<bool>  HorrorFlickerEnabled     = null!;
        public static ConfigEntry<float> HorrorFlickerIntervalMin = null!;
        public static ConfigEntry<float> HorrorFlickerIntervalMax = null!;

        // 08a - Brownout (slow dim-hold-recover)
        public static ConfigEntry<bool>  BrownoutEnabled     = null!;
        public static ConfigEntry<float> BrownoutIntervalMin = null!;
        public static ConfigEntry<float> BrownoutIntervalMax = null!;

        // 08b - Stutter (continuous high-frequency flutter)
        public static ConfigEntry<bool>  StutterEnabled     = null!;
        public static ConfigEntry<float> StutterIntervalMin = null!;
        public static ConfigEntry<float> StutterIntervalMax = null!;

        // 08c - Restrike (LED bulb restart sequence)
        public static ConfigEntry<bool>  RestrikeEnabled     = null!;
        public static ConfigEntry<float> RestrikeIntervalMin = null!;
        public static ConfigEntry<float> RestrikeIntervalMax = null!;

        // 09 - Preset Short Range (pistols, revolvers) — DEFAULT values
        public static ConfigEntry<float> P_Short_CoreAngle = null!, P_Short_CoreIntensity = null!, P_Short_CoreRange = null!;
        public static ConfigEntry<float> P_Short_MidAngle  = null!, P_Short_MidIntensity  = null!, P_Short_MidRange  = null!;
        public static ConfigEntry<float> P_Short_FallAngle = null!, P_Short_FallIntensity = null!, P_Short_FallRange = null!;

        // 10 - Preset Medium Range 1 (SMG / AR) — DEFAULT values
        public static ConfigEntry<float> P_Med1_CoreAngle = null!, P_Med1_CoreIntensity = null!, P_Med1_CoreRange = null!;
        public static ConfigEntry<float> P_Med1_MidAngle  = null!, P_Med1_MidIntensity  = null!, P_Med1_MidRange  = null!;
        public static ConfigEntry<float> P_Med1_FallAngle = null!, P_Med1_FallIntensity = null!, P_Med1_FallRange = null!;

        // 11 - Preset Medium Range 2 (DMR / tight) — DEFAULT values
        public static ConfigEntry<float> P_Med2_CoreAngle = null!, P_Med2_CoreIntensity = null!, P_Med2_CoreRange = null!;
        public static ConfigEntry<float> P_Med2_MidAngle  = null!, P_Med2_MidIntensity  = null!, P_Med2_MidRange  = null!;
        public static ConfigEntry<float> P_Med2_FallAngle = null!, P_Med2_FallIntensity = null!, P_Med2_FallRange = null!;

        // 12 - Preset Wide Range (shotguns) — DEFAULT values
        public static ConfigEntry<float> P_Wide_CoreAngle = null!, P_Wide_CoreIntensity = null!, P_Wide_CoreRange = null!;
        public static ConfigEntry<float> P_Wide_MidAngle  = null!, P_Wide_MidIntensity  = null!, P_Wide_MidRange  = null!;
        public static ConfigEntry<float> P_Wide_FallAngle = null!, P_Wide_FallIntensity = null!, P_Wide_FallRange = null!;

        // 13 - Preset Extended Range (snipers / fallback) — DEFAULT values
        public static ConfigEntry<float> P_Ext_CoreAngle = null!, P_Ext_CoreIntensity = null!, P_Ext_CoreRange = null!;
        public static ConfigEntry<float> P_Ext_MidAngle  = null!, P_Ext_MidIntensity  = null!, P_Ext_MidRange  = null!;
        public static ConfigEntry<float> P_Ext_FallAngle = null!, P_Ext_FallIntensity = null!, P_Ext_FallRange = null!;

        // 14 - Pickable (consumable long range flashlight) — DEFAULT mode values
        // 16 - Dust effect (volumetric motes in the Core cone).
        public static ConfigEntry<bool>  DustEnabled    = null!;

        public static ConfigEntry<float> Pick_Angle     = null!;
        public static ConfigEntry<float> Pick_Intensity = null!;
        public static ConfigEntry<float> Pick_Range     = null!;
        public static ConfigEntry<float> Pick_RareWeight     = null!;
        public static ConfigEntry<int>   Pick_MaxPerLevel    = null!;
        public static ConfigEntry<int>   Pick_GuaranteeFloor = null!;

        // 99 - Legacy unused entries (dummies so old configs don't surface
        //      orphan-key warnings).
        public static ConfigEntry<float> I3 = null!, I4 = null!, I5 = null!;
        public static ConfigEntry<float> R3 = null!, R4 = null!, R5 = null!;
        public static ConfigEntry<float> Step3 = null!, Step4 = null!, Step5 = null!;

        // Resolved runtime values (config + mode offset). Read by patches;
        // updated on each ApplyPreset() call.

        // Helmet — config + mode offset.
        internal static float HelmetAngle;
        internal static float HelmetIntensity;
        internal static float HelmetRange;
        internal static float HelmetFallRange; // falloff cone range (may exceed main range)

        // Pickable — config + mode offset.
        internal static float PickAngle;
        internal static float PickIntensity;
        internal static float PickRange;
        internal static Color PickColor = new Color(1.0f, 0.84f, 0.60f); // warm amber default

        public override void Load()
        {
            Logger = Log;
            BindConfig();
            ApplyPreset(Mode.Value);

            SceneManager.sceneLoaded += new System.Action<Scene, LoadSceneMode>(
                (_, __) =>
                {
                    LightSandwich.RebuildAll();
                    LightSandwich.ProcessAllSpotLights();
                    PickableFlashlightTracker.Reset();
                });

            try
            {
                new Harmony(PluginInfo.GUID).PatchAll();
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[FO] Harmony PatchAll threw: {ex}");
            }

            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<LightUpdater>();
                ClassInjector.RegisterTypeInIl2Cpp<PreCullEnforcer>();
                var holder = new GameObject(nameof(LightUpdater));
                Object.DontDestroyOnLoad(holder);
                holder.hideFlags = HideFlags.HideAndDontSave;
                holder.AddComponent<LightUpdater>();
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[FO] ClassInjector / AddComponent threw: {ex}");
            }

            Logger.LogInfo($"{PluginInfo.Name} v{PluginInfo.Version} loaded (Mode={Mode.Value}).");
        }

        // Sections numbered 01..99 so the .cfg file reads top-to-bottom:
        // general → layers → helmet → flicker → presets.
        private void BindConfig()
        {
            // 01 - General
            Enabled = Config.Bind(
                "01 - General",
                "Enabled", true,
                "EN: Master switch for the entire mod. False = fully disabled.\n" +
                "UA: Головний вимикач мода. False = повністю вимкнено.");

            Mode = Config.Bind(
                "01 - General",
                "Mode", FlashLightMode.DEFAULT,
                "EN: Pick the flashlight style you want.\n" +
                "    MINIMAL       — For near-vanilla gameplay. Just a clean\n" +
                "                    main beam and a soft outer glow.\n" +
                "    DEFAULT       — I think it's balanced.\n" +
                "    CASUAL        — A bit wider and a bit further than DEFAULT.\n" +
                "    ITS_2050_BRUH — Sleepers will hear you, but they probably\n" +
                "                    won't see you. Shine!\n" +
                "    VANILLA       — Fully disables all mod changes.\n" +
                "UA: Обери стиль ліхтариків.\n" +
                "    MINIMAL       — Для около-ванільного геймплею. Просто чистий\n" +
                "                    основний промінь і м'яке зовнішнє світло.\n" +
                "    DEFAULT       — По-моєму, збалансовано.\n" +
                "    CASUAL        — Трохи ширше і трохи далі за DEFAULT.\n" +
                "    ITS_2050_BRUH — Сплячі тебе почують, але побачити вже\n" +
                "                    навряд. Сяй!\n" +
                "    VANILLA       — Повністю вимикає всі зміни мода.");

            AttachLightToWeaponBone = Config.Bind(
                "01 - General",
                "AttachLightToWeaponBone", true,
                "EN: True — the flashlight rides along with the gun model, so the\n" +
                "    beam moves during reload and weapon-swap animations instead of\n" +
                "    staring straight ahead. False — beam locked rigidly to the\n" +
                "    camera.\n" +
                "UA: True — ліхтарик їздить разом з моделлю зброї, тож промінь\n" +
                "    рухається під час перезарядки та зміни зброї, а не дивиться\n" +
                "    рівно вперед. False — промінь жорстко прив'язаний до камери.");

            // 02 - Core Layer (only AngleStep here; per-preset Core values in 09–13).
            Step1 = Config.Bind(
                "02 - Core Layer (RF_Core)",
                "AngleStep", 30f,
                "EN: Extra degrees added to the inner bright beam (used for the\n" +
                "    pickable flashlight and map lights). Weapon presets ignore\n" +
                "    this and set their beam width directly.\n" +
                "UA: Додаткові градуси до ширини яскравого центру (для\n" +
                "    підбираного ліхтарика і світла на рівні). Зброя задає\n" +
                "    свою ширину сама.");

            // 03 - Middle Layer (multiplier path: pickable + env)
            I1 = Config.Bind(
                "03 - Middle Layer (RF_L1) — multiplier path",
                "IntensityFraction", 0.625f,
                "EN: How bright the middle glow ring is, as a fraction of the\n" +
                "    inner beam. Only affects the pickable flashlight and map\n" +
                "    lights; weapons set their own value.\n" +
                "UA: Яскравість середнього кільця світла відносно центру.\n" +
                "    Стосується лише підбираного ліхтарика і світла на рівні;\n" +
                "    зброя має свої значення.");

            R1 = Config.Bind(
                "03 - Middle Layer (RF_L1) — multiplier path",
                "RangeFraction", 0.62f,
                "EN: How far the middle glow ring reaches, as a fraction of the\n" +
                "    inner beam's reach.\n" +
                "UA: Як далеко сягає середнє кільце світла, відносно дальності\n" +
                "    яскравого центру.");

            MidStepFraction = Config.Bind(
                "03 - Middle Layer (RF_L1) — multiplier path",
                "AngleStepFraction", 0.252f,
                "EN: How much wider the middle glow ring is than the inner beam,\n" +
                "    relative to the outer glow's width step.\n" +
                "UA: Наскільки середнє кільце ширше за яскравий центр, відносно\n" +
                "    зовнішнього кроку розширення.");

            // 04 - Falloff Layer (multiplier path)
            I2 = Config.Bind(
                "04 - Falloff Layer (RF_L2) — multiplier path",
                "IntensityFraction", 0.3125f,
                "EN: How bright the soft outer glow is, as a fraction of the\n" +
                "    inner beam (pickable / map lights only).\n" +
                "UA: Яскравість м'якого зовнішнього світла, відносно центру\n" +
                "    (лише для підбираного ліхтарика і світла на рівні).");

            R2 = Config.Bind(
                "04 - Falloff Layer (RF_L2) — multiplier path",
                "RangeFraction", 0.38f,
                "EN: How far the soft outer glow reaches, as a fraction of the\n" +
                "    inner beam's reach (pickable / map lights only).\n" +
                "UA: Як далеко сягає м'яке зовнішнє світло, відносно дальності\n" +
                "    центру (лише для підбираного ліхтарика і світла на рівні).");

            Step2 = Config.Bind(
                "04 - Falloff Layer (RF_L2) — multiplier path",
                "AngleStep", 119f,
                "EN: How much wider the soft outer glow is than the inner beam,\n" +
                "    in degrees.\n" +
                "UA: Наскільки м'яке зовнішнє світло ширше за центр, у градусах.");

            // 05 - Helmet Synth (melee/tools) — DEFAULT values.
            //      CASUAL: +1m range + soft fade. ITS_2050: +4m, +0.3 intensity,
            //      larger fade.
            MeleeAngle = Config.Bind(
                "05 - Helmet Synth (melee/tools) — DEFAULT",
                "Angle", 85f,
                "EN: How wide the helmet light is, in degrees.\n" +
                "UA: Наскільки широке світло шолома, у градусах.");

            MeleeIntensity = Config.Bind(
                "05 - Helmet Synth (melee/tools) — DEFAULT",
                "Intensity", 1.75f,
                "EN: How bright the helmet light is.\n" +
                "UA: Наскільки яскраве світло шолома.");

            MeleeRange = Config.Bind(
                "05 - Helmet Synth (melee/tools) — DEFAULT",
                "Range", 12f,
                "EN: How far the helmet light reaches, in metres.\n" +
                "UA: Як далеко світить шолом, у метрах.");

            // 06 - Perlin Flicker (continuous tremor)
            FlickerEnabled = Config.Bind(
                "06 - Perlin Flicker (continuous tremor)",
                "Enabled", true,
                "EN: Subtle, always-on light tremor — like a real flashlight.\n" +
                "UA: Тонкий постійний тремор світла — як у справжнього ліхтарика.");

            FlickerSpeed = Config.Bind(
                "06 - Perlin Flicker (continuous tremor)",
                "Speed", 2.5f,
                "EN: How fast the tremor wobbles. Higher = faster.\n" +
                "UA: Швидкість тремору. Більше = швидше.");

            FlickerAmplitude = Config.Bind(
                "06 - Perlin Flicker (continuous tremor)",
                "Amplitude", 0.04f,
                "EN: How strong the tremor is (0.04 = ±4% brightness).\n" +
                "UA: Сила тремору (0.04 = ±4% яскравості).");

            // 07 - Rare Flicker (loose-battery bursts)
            RareFlickerEnabled = Config.Bind(
                "07 - Rare Flicker (bad-contact bursts)",
                "Enabled", true,
                "EN: Occasional bursts of 3-7 quick blinks, like a loose battery.\n" +
                "UA: Випадкові серії з 3-7 швидких миготінь — наче поганий\n" +
                "    контакт батареї.");

            RareFlickerIntervalMin = Config.Bind(
                "07 - Rare Flicker (bad-contact bursts)",
                "IntervalMinSeconds", 180f,
                "EN: Minimum seconds between blink bursts.\n" +
                "UA: Мінімум секунд між серіями миготінь.");

            RareFlickerIntervalMax = Config.Bind(
                "07 - Rare Flicker (bad-contact bursts)",
                "IntervalMaxSeconds", 420f,
                "EN: Maximum seconds between blink bursts.\n" +
                "UA: Максимум секунд між серіями миготінь.");

            // 08 - Horror Flicker (long dark pause)
            HorrorFlickerEnabled = Config.Bind(
                "08 - Horror Flicker (long dark pause)",
                "Enabled", true,
                "EN: Horror-movie blinks: 6-14 flickers with one long dark pause.\n" +
                "    Pretty creepy.\n" +
                "UA: Хоррор-миготіння: 6-14 спалахів з однією довгою темною\n" +
                "    паузою. Доволі моторошно.");

            HorrorFlickerIntervalMin = Config.Bind(
                "08 - Horror Flicker (long dark pause)",
                "IntervalMinSeconds", 240f,
                "EN: Minimum seconds between horror events.\n" +
                "UA: Мінімум секунд між хоррор-подіями.");

            HorrorFlickerIntervalMax = Config.Bind(
                "08 - Horror Flicker (long dark pause)",
                "IntervalMaxSeconds", 600f,
                "EN: Maximum seconds between horror events.\n" +
                "UA: Максимум секунд між хоррор-подіями.");

            // 08a - Brownout: 4–10s smooth dim → hold → recover. No blinks.
            BrownoutEnabled = Config.Bind(
                "08a - Brownout (slow dim-hold-recover)",
                "Enabled", true,
                "EN: The light slowly dims to about half brightness, holds, then\n" +
                "    recovers. No blinks — feels like the battery is sagging.\n" +
                "UA: Світло плавно тьмяніє приблизно до половини, тримається,\n" +
                "    потім відновлюється. Без миготінь — наче батарея просіла.");
            BrownoutIntervalMin = Config.Bind(
                "08a - Brownout (slow dim-hold-recover)",
                "IntervalMinSeconds", 600f,
                "EN: Minimum seconds between battery sags.\n" +
                "UA: Мінімум секунд між просіданнями.");
            BrownoutIntervalMax = Config.Bind(
                "08a - Brownout (slow dim-hold-recover)",
                "IntervalMaxSeconds", 2400f,
                "EN: Maximum seconds between battery sags.\n" +
                "UA: Максимум секунд між просіданнями.");

            // 08b - Stutter: 0.8–2.5s burst of high-freq Perlin modulation.
            //       Continuous (not binary), like a vibrating loose contact.
            StutterEnabled = Config.Bind(
                "08b - Stutter (high-frequency flutter)",
                "Enabled", true,
                "EN: A short bout of nervous flutter — like a loose, vibrating\n" +
                "    contact.\n" +
                "UA: Короткий нервовий трепет світла — наче торохтить контакт.");
            StutterIntervalMin = Config.Bind(
                "08b - Stutter (high-frequency flutter)",
                "IntervalMinSeconds", 900f,
                "EN: Minimum seconds between flutter bursts.\n" +
                "UA: Мінімум секунд між нервовими спалахами.");
            StutterIntervalMax = Config.Bind(
                "08b - Stutter (high-frequency flutter)",
                "IntervalMaxSeconds", 2700f,
                "EN: Maximum seconds between flutter bursts.\n" +
                "UA: Максимум секунд між нервовими спалахами.");

            // 08c - Restrike: 4-phase LED bulb restart sequence. Rarest of
            //       the bunch; feels like a momentary power hiccup.
            RestrikeEnabled = Config.Bind(
                "08c - Restrike (LED bulb restart)",
                "Enabled", true,
                "EN: The light cuts out, gives a weak strike, cuts again, then\n" +
                "    comes back — like an LED bulb restarting after a power hiccup.\n" +
                "    The rarest event.\n" +
                "UA: Світло гасне, дає слабкий проблиск, знову гасне і повертається\n" +
                "    — наче LED-лампа перезапускається після стрибка живлення.\n" +
                "    Найрідкісніша подія.");
            RestrikeIntervalMin = Config.Bind(
                "08c - Restrike (LED bulb restart)",
                "IntervalMinSeconds", 1200f,
                "EN: Minimum seconds between LED restarts.\n" +
                "UA: Мінімум секунд між перезапусками лампи.");
            RestrikeIntervalMax = Config.Bind(
                "08c - Restrike (LED bulb restart)",
                "IntervalMaxSeconds", 3600f,
                "EN: Maximum seconds between LED restarts.\n" +
                "UA: Максимум секунд між перезапусками лампи.");

            // Presets 09–13 are DEFAULT-mode values for each flashlight type.
            // Active Mode (section 01) adds hardcoded offsets — see ModePresets.For().

            // 09 - Preset Short Range (pistols, revolvers, generic shotguns)
            P_Short_CoreAngle     = Cfg("09 - Preset Short Range — DEFAULT", "Core_Angle",      50f,
                "Pistols/revolvers — width of the bright inner beam (°). / Ширина яскравого центру для пістолетів і револьверів (°).");
            P_Short_CoreIntensity = Cfg("09 - Preset Short Range — DEFAULT", "Core_Intensity",   0.80f,
                "Pistols/revolvers — brightness of the inner beam. / Яскравість центру для пістолетів і револьверів.");
            P_Short_CoreRange     = Cfg("09 - Preset Short Range — DEFAULT", "Core_Range",      18.0f,
                "Pistols/revolvers — how far the inner beam reaches (m). / Як далеко б'є центр для пістолетів і револьверів (м).");
            P_Short_MidAngle      = Cfg("09 - Preset Short Range — DEFAULT", "Mid_Angle",       80f,
                "Pistols/revolvers — width of the middle glow (°). / Ширина середнього світла для пістолетів і револьверів (°).");
            P_Short_MidIntensity  = Cfg("09 - Preset Short Range — DEFAULT", "Mid_Intensity",    0.60f,
                "Pistols/revolvers — brightness of the middle glow. / Яскравість середнього світла для пістолетів і револьверів.");
            P_Short_MidRange      = Cfg("09 - Preset Short Range — DEFAULT", "Mid_Range",       12.4f,
                "Pistols/revolvers — how far the middle glow reaches (m). / Як далеко сягає середнє світло для пістолетів і револьверів (м).");
            P_Short_FallAngle     = Cfg("09 - Preset Short Range — DEFAULT", "Fall_Angle",     169f,
                "Pistols/revolvers — width of the soft outer glow (°). / Ширина м'якого зовнішнього світла для пістолетів і револьверів (°).");
            P_Short_FallIntensity = Cfg("09 - Preset Short Range — DEFAULT", "Fall_Intensity",   0.25f,
                "Pistols/revolvers — brightness of the soft outer glow. / Яскравість м'якого зовнішнього світла для пістолетів і револьверів.");
            P_Short_FallRange     = Cfg("09 - Preset Short Range — DEFAULT", "Fall_Range",       5.6f,
                "Pistols/revolvers — how far the soft outer glow reaches (m). / Як далеко сягає м'яке зовнішнє світло для пістолетів і револьверів (м).");

            // 10 - Preset Medium Range 1 (SMG, generic assault rifle)
            P_Med1_CoreAngle     = Cfg("10 - Preset Medium Range 1 — DEFAULT", "Core_Angle",     50f,    "SMGs/ARs — width of the bright inner beam (°). / Ширина яскравого центру для SMG і автоматів (°).");
            P_Med1_CoreIntensity = Cfg("10 - Preset Medium Range 1 — DEFAULT", "Core_Intensity",  0.80f, "SMGs/ARs — brightness of the inner beam. / Яскравість центру для SMG і автоматів.");
            P_Med1_CoreRange     = Cfg("10 - Preset Medium Range 1 — DEFAULT", "Core_Range",     21.0f,  "SMGs/ARs — how far the inner beam reaches (m). / Як далеко б'є центр для SMG і автоматів (м).");
            P_Med1_MidAngle      = Cfg("10 - Preset Medium Range 1 — DEFAULT", "Mid_Angle",      80f,    "SMGs/ARs — width of the middle glow (°). / Ширина середнього світла для SMG і автоматів (°).");
            P_Med1_MidIntensity  = Cfg("10 - Preset Medium Range 1 — DEFAULT", "Mid_Intensity",   0.60f, "SMGs/ARs — brightness of the middle glow. / Яскравість середнього світла для SMG і автоматів.");
            P_Med1_MidRange      = Cfg("10 - Preset Medium Range 1 — DEFAULT", "Mid_Range",      14.3f,  "SMGs/ARs — how far the middle glow reaches (m). / Як далеко сягає середнє світло для SMG і автоматів (м).");
            P_Med1_FallAngle     = Cfg("10 - Preset Medium Range 1 — DEFAULT", "Fall_Angle",    169f,    "SMGs/ARs — width of the soft outer glow (°). / Ширина м'якого зовнішнього світла для SMG і автоматів (°).");
            P_Med1_FallIntensity = Cfg("10 - Preset Medium Range 1 — DEFAULT", "Fall_Intensity",  0.25f, "SMGs/ARs — brightness of the soft outer glow. / Яскравість м'якого зовнішнього світла для SMG і автоматів.");
            P_Med1_FallRange     = Cfg("10 - Preset Medium Range 1 — DEFAULT", "Fall_Range",      6.7f,  "SMGs/ARs — how far the soft outer glow reaches (m). / Як далеко сягає м'яке зовнішнє світло для SMG і автоматів (м).");

            // 11 - Preset Medium Range 2 (DMR / tight focused)
            P_Med2_CoreAngle     = Cfg("11 - Preset Medium Range 2 — DEFAULT", "Core_Angle",     40f,    "DMRs/precision guns — width of the bright inner beam (°). / Ширина яскравого центру для DMR і точної зброї (°).");
            P_Med2_CoreIntensity = Cfg("11 - Preset Medium Range 2 — DEFAULT", "Core_Intensity",  0.80f, "DMRs/precision guns — brightness of the inner beam. / Яскравість центру для DMR і точної зброї.");
            P_Med2_CoreRange     = Cfg("11 - Preset Medium Range 2 — DEFAULT", "Core_Range",     21.0f,  "DMRs/precision guns — how far the inner beam reaches (m). / Як далеко б'є центр для DMR і точної зброї (м).");
            P_Med2_MidAngle      = Cfg("11 - Preset Medium Range 2 — DEFAULT", "Mid_Angle",      75f,    "DMRs/precision guns — width of the middle glow (°). / Ширина середнього світла для DMR і точної зброї (°).");
            P_Med2_MidIntensity  = Cfg("11 - Preset Medium Range 2 — DEFAULT", "Mid_Intensity",   0.60f, "DMRs/precision guns — brightness of the middle glow. / Яскравість середнього світла для DMR і точної зброї.");
            P_Med2_MidRange      = Cfg("11 - Preset Medium Range 2 — DEFAULT", "Mid_Range",      14.3f,  "DMRs/precision guns — how far the middle glow reaches (m). / Як далеко сягає середнє світло для DMR і точної зброї (м).");
            P_Med2_FallAngle     = Cfg("11 - Preset Medium Range 2 — DEFAULT", "Fall_Angle",    169f,    "DMRs/precision guns — width of the soft outer glow (°). / Ширина м'якого зовнішнього світла для DMR і точної зброї (°).");
            P_Med2_FallIntensity = Cfg("11 - Preset Medium Range 2 — DEFAULT", "Fall_Intensity",  0.25f, "DMRs/precision guns — brightness of the soft outer glow. / Яскравість м'якого зовнішнього світла для DMR і точної зброї.");
            P_Med2_FallRange     = Cfg("11 - Preset Medium Range 2 — DEFAULT", "Fall_Range",      6.7f,  "DMRs/precision guns — how far the soft outer glow reaches (m). / Як далеко сягає м'яке зовнішнє світло для DMR і точної зброї (м).");

            // 12 - Preset Wide Range (shotguns, wide-cone weapons)
            P_Wide_CoreAngle     = Cfg("12 - Preset Wide Range — DEFAULT", "Core_Angle",     50f,    "Shotguns — width of the bright inner beam (°). / Ширина яскравого центру для дробовиків (°).");
            P_Wide_CoreIntensity = Cfg("12 - Preset Wide Range — DEFAULT", "Core_Intensity",  0.80f, "Shotguns — brightness of the inner beam. / Яскравість центру для дробовиків.");
            P_Wide_CoreRange     = Cfg("12 - Preset Wide Range — DEFAULT", "Core_Range",     24.0f,  "Shotguns — how far the inner beam reaches (m). / Як далеко б'є центр для дробовиків (м).");
            P_Wide_MidAngle      = Cfg("12 - Preset Wide Range — DEFAULT", "Mid_Angle",      90f,    "Shotguns — width of the middle glow (°). / Ширина середнього світла для дробовиків (°).");
            P_Wide_MidIntensity  = Cfg("12 - Preset Wide Range — DEFAULT", "Mid_Intensity",   0.70f, "Shotguns — brightness of the middle glow. / Яскравість середнього світла для дробовиків.");
            P_Wide_MidRange      = Cfg("12 - Preset Wide Range — DEFAULT", "Mid_Range",      16.1f,  "Shotguns — how far the middle glow reaches (m). / Як далеко сягає середнє світло для дробовиків (м).");
            P_Wide_FallAngle     = Cfg("12 - Preset Wide Range — DEFAULT", "Fall_Angle",    169f,    "Shotguns — width of the soft outer glow (°). / Ширина м'якого зовнішнього світла для дробовиків (°).");
            P_Wide_FallIntensity = Cfg("12 - Preset Wide Range — DEFAULT", "Fall_Intensity",  0.25f, "Shotguns — brightness of the soft outer glow. / Яскравість м'якого зовнішнього світла для дробовиків.");
            P_Wide_FallRange     = Cfg("12 - Preset Wide Range — DEFAULT", "Fall_Range",      7.9f,  "Shotguns — how far the soft outer glow reaches (m). / Як далеко сягає м'яке зовнішнє світло для дробовиків (м).");

            // 13 - Preset Extended Range (snipers, long-range, fallback)
            P_Ext_CoreAngle     = Cfg("13 - Preset Extended Range — DEFAULT", "Core_Angle",     50f,    "Snipers/long-range guns — width of the bright inner beam (°). / Ширина яскравого центру для снайперок і дальнобоїв (°).");
            P_Ext_CoreIntensity = Cfg("13 - Preset Extended Range — DEFAULT", "Core_Intensity",  0.80f, "Snipers/long-range guns — brightness of the inner beam. / Яскравість центру для снайперок і дальнобоїв.");
            P_Ext_CoreRange     = Cfg("13 - Preset Extended Range — DEFAULT", "Core_Range",     24.0f,  "Snipers/long-range guns — how far the inner beam reaches (m). / Як далеко б'є центр для снайперок і дальнобоїв (м).");
            P_Ext_MidAngle      = Cfg("13 - Preset Extended Range — DEFAULT", "Mid_Angle",      80f,    "Snipers/long-range guns — width of the middle glow (°). / Ширина середнього світла для снайперок і дальнобоїв (°).");
            P_Ext_MidIntensity  = Cfg("13 - Preset Extended Range — DEFAULT", "Mid_Intensity",   0.60f, "Snipers/long-range guns — brightness of the middle glow. / Яскравість середнього світла для снайперок і дальнобоїв.");
            P_Ext_MidRange      = Cfg("13 - Preset Extended Range — DEFAULT", "Mid_Range",      16.1f,  "Snipers/long-range guns — how far the middle glow reaches (m). / Як далеко сягає середнє світло для снайперок і дальнобоїв (м).");
            P_Ext_FallAngle     = Cfg("13 - Preset Extended Range — DEFAULT", "Fall_Angle",    169f,    "Snipers/long-range guns — width of the soft outer glow (°). / Ширина м'якого зовнішнього світла для снайперок і дальнобоїв (°).");
            P_Ext_FallIntensity = Cfg("13 - Preset Extended Range — DEFAULT", "Fall_Intensity",  0.25f, "Snipers/long-range guns — brightness of the soft outer glow. / Яскравість м'якого зовнішнього світла для снайперок і дальнобоїв.");
            P_Ext_FallRange     = Cfg("13 - Preset Extended Range — DEFAULT", "Fall_Range",      7.9f,  "Snipers/long-range guns — how far the soft outer glow reaches (m). / Як далеко сягає м'яке зовнішнє світло для снайперок і дальнобоїв (м).");

            // 14 - Pickable (consumable long range flashlight) — DEFAULT.
            //      Rare upgrade pickup; wider/brighter than weapons.
            //      ITS_2050: +2m range + more neutral tint.
            Pick_Angle = Config.Bind(
                "14 - Pickable Flashlight — DEFAULT",
                "Angle", 50f,
                "EN: The big pick-up flashlight — how wide its main beam is (°).\n" +
                "UA: Підбираний ліхтарик — наскільки широкий основний промінь (°).");

            Pick_Intensity = Config.Bind(
                "14 - Pickable Flashlight — DEFAULT",
                "Intensity", 0.70f,
                "EN: The big pick-up flashlight — how bright its main beam is.\n" +
                "UA: Підбираний ліхтарик — наскільки яскравий основний промінь.");

            Pick_Range = Config.Bind(
                "14 - Pickable Flashlight — DEFAULT",
                "Range", 40f,
                "EN: The big pick-up flashlight — how far its main beam reaches (m).\n" +
                "UA: Підбираний ліхтарик — як далеко б'є основний промінь (м).");

            // ─────────────────────────────────────────────────────────────
            // 15 - Pickable Flashlight Rarity — how often the consumable
            //      flashlight spawns in the world. Rarity / частота появи.
            // ─────────────────────────────────────────────────────────────
            Pick_RareWeight = Config.Bind(
                "15 - Pickable Flashlight Rarity",
                "RareWeight", 0.001f,
                "EN: Spawn weight applied to every loot-table entry that drops the\n" +
                "    consumable flashlight. Lower = rarer, higher = more common.\n" +
                "    Reference scale (rolls are random against the table totals):\n" +
                "       0.001  (DEFAULT) — near-zero natural rolls. The guarantee\n" +
                "                          floor below force-promotes one mid-level\n" +
                "                          so the player still gets ~1 per run.\n" +
                "                          Feels like a special upgrade find.\n" +
                "       0.01            — still very rare; ~10× more likely than\n" +
                "                          default but most levels still rely on\n" +
                "                          the guarantee.\n" +
                "       0.1             — noticeably uncommon; 0-2 natural rolls\n" +
                "                          per level on top of the guarantee.\n" +
                "       1.0             — normal item rarity (same weight as a\n" +
                "                          regular pickup); several per level,\n" +
                "                          MaxPerLevel cap kicks in regularly.\n" +
                "       10.0+           — flooded; almost every container.\n" +
                "    The MaxPerLevel cap (section below) clamps over-quota rolls\n" +
                "    by replacing them with a category-matching item, so even at\n" +
                "    very high weights you won't see more flashlights than that\n" +
                "    cap allows.\n" +
                "UA: Вага появи (spawn weight) для consumable-ліхтарика у лут-\n" +
                "    таблицях. Менше = рідше, більше = частіше.\n" +
                "    Орієнтовна шкала (рандом проти суми ваг у таблиці):\n" +
                "       0.001  (DEFAULT) — майже ніколи не випадає природно.\n" +
                "                          Нижній «гарантійний поріг» примусово\n" +
                "                          ставить ~1 ліхтарик на рівень, тож\n" +
                "                          без нього теж не залишишся. Відчуття:\n" +
                "                          рідкісний апгрейд-предмет.\n" +
                "       0.01            — все ще дуже рідко; у ~10× ймовірніше\n" +
                "                          за default, але рівні все одно тримають\n" +
                "                          на гарантії.\n" +
                "       0.1             — помітно нечасто; 0-2 природних випадки\n" +
                "                          на рівень понад гарантію.\n" +
                "       1.0             — звичайна рідкість предмета (як інший\n" +
                "                          ресурс); кілька на рівень, ліміт\n" +
                "                          MaxPerLevel спрацьовує регулярно.\n" +
                "       10.0+           — буквально в кожному ящику.\n" +
                "    Ліміт MaxPerLevel (нижче) обріже зайві випадання, заміняючи\n" +
                "    їх на інший предмет тієї ж категорії — тож навіть при дуже\n" +
                "    високій вазі ти не побачиш більше ліхтариків ніж ліміт.");

            Pick_MaxPerLevel = Config.Bind(
                "15 - Pickable Flashlight Rarity",
                "MaxPerLevel", 2,
                "EN: Hard cap on how many consumable flashlights can appear in a\n" +
                "    single level. Over-quota rolls are replaced with a different\n" +
                "    item from the same loot category (so containers never end up\n" +
                "    empty). 2 (DEFAULT) keeps the upgrade feeling scarce; raise\n" +
                "    to 4-6 for a flashlight-rich run, set to 1 for hardcore.\n" +
                "UA: Жорсткий ліміт скільки consumable-ліхтариків взагалі може\n" +
                "    зʼявитись на рівні. Понад-лімітні випадання заміняються на\n" +
                "    інший предмет тієї ж категорії (контейнери не лишаються\n" +
                "    порожніми). 2 (DEFAULT) тримає апгрейд дефіцитним; постав\n" +
                "    4-6 для забагів-ліхтариків, 1 для хардкору.");

            Pick_GuaranteeFloor = Config.Bind(
                "15 - Pickable Flashlight Rarity",
                "GuaranteeAfterNPickups", 300,
                "EN: If RareWeight is so low that natural rolls don't produce a\n" +
                "    flashlight, after this many non-flashlight pickups in a level\n" +
                "    the NEXT pickup is force-converted into one. Ensures the\n" +
                "    player gets at least one flashlight per run even with the\n" +
                "    default RareWeight=0.001. Set to a very high number (e.g.\n" +
                "    9999) to disable the guarantee — you'll then rely purely on\n" +
                "    RareWeight.\n" +
                "UA: Якщо RareWeight настільки малий, що природні випадання не\n" +
                "    дають ліхтарика, то після стількох не-ліхтариків на рівні\n" +
                "    НАСТУПНИЙ пікап буде примусово замінений на ліхтарик. Це\n" +
                "    гарантує, що гравець отримає хоча б один за пробіг навіть\n" +
                "    при default RareWeight=0.001. Постав велике число (наприклад\n" +
                "    9999) щоб вимкнути гарантію — тоді все залежить лише від\n" +
                "    RareWeight.");

            // 16 - Dust effect — faint particle motes inside the Core cone.
            //      Visible only because Core's spotlight illuminates them,
            //      giving a "dust in the beam" volumetric feel. Toggle-only
            //      for now; tuning lives in BuildDustPS for the moment.
            DustEnabled = Config.Bind(
                "16 - Dust Effect",
                "Enabled", true,
                "EN: If true, adds a particle system inside the flashlight beam.\n" +
                "UA: Якщо true, додає систему частинок в конусі променя.");

            // 99 - Legacy dummies — kept so old configs don't surface orphan warnings.
            I3    = Config.Bind("99 - Legacy", "I3_unused",    0f, "EN: Unused. / UA: Не використовується.");
            I4    = Config.Bind("99 - Legacy", "I4_unused",    0f, "EN: Unused. / UA: Не використовується.");
            I5    = Config.Bind("99 - Legacy", "I5_unused",    0f, "EN: Unused. / UA: Не використовується.");
            R3    = Config.Bind("99 - Legacy", "R3_unused",    0f, "EN: Unused. / UA: Не використовується.");
            R4    = Config.Bind("99 - Legacy", "R4_unused",    0f, "EN: Unused. / UA: Не використовується.");
            R5    = Config.Bind("99 - Legacy", "R5_unused",    0f, "EN: Unused. / UA: Не використовується.");
            Step3 = Config.Bind("99 - Legacy", "Step3_unused", 0f, "EN: Unused. / UA: Не використовується.");
            Step4 = Config.Bind("99 - Legacy", "Step4_unused", 0f, "EN: Unused. / UA: Не використовується.");
            Step5 = Config.Bind("99 - Legacy", "Step5_unused", 0f, "EN: Unused. / UA: Не використовується.");
        }

        // Compact wrapper around Config.Bind so the 45+ per-preset entries stay
        // readable as one-liners.
        private ConfigEntry<float> Cfg(string section, string key, float defaultValue, string desc)
        {
            return Config.Bind(section, key, defaultValue, "EN/UA: " + desc);
        }

        // Reads DEFAULT config values + active mode offsets and writes resolved
        // values into RangedPresets, helmet, and pickable fields. VANILLA still
        // populates them so the helmet synth keeps working.
        internal static void ApplyPreset(FlashLightMode m)
        {
            var adj = ModePresets.For(m);
            RangedPresets.BuildFromConfig(adj.Ranged, noMid: m == FlashLightMode.MINIMAL);

            // Helmet (melee synth).
            HelmetAngle     = MeleeAngle.Value     + adj.HelmetAngleOffset;
            HelmetIntensity = MeleeIntensity.Value + adj.HelmetIntensityOffset;
            HelmetRange     = MeleeRange.Value     + adj.HelmetRangeOffset;
            HelmetFallRange = HelmetRange          + adj.HelmetFallRangeExtra;

            // Pickable (consumable long range flashlight).
            PickAngle     = Mathf.Min(Pick_Angle.Value, 89f);
            PickIntensity = Pick_Intensity.Value;
            PickRange     = Pick_Range.Value + adj.PickRangeOffset;
            PickColor     = adj.PickColorOverride ?? new Color(1.0f, 0.84f, 0.60f);
        }
    }
}
