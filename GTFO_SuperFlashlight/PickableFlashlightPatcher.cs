using GameData;
using HarmonyLib;
using ItemSetup;
using LevelGeneration;
using UnityEngine;
using System;

namespace GTFO_SuperFlashlight
{
    // Drops the consumable long-range flashlight's spawn weight to a sliver
    // so natural rolls almost never produce one. Patch_LGPickupSetupAsConsumable
    // force-promotes one after enough non-flashlight pickups have passed so the
    // level always has at least one.
    [HarmonyPatch(typeof(GameDataInit), nameof(GameDataInit.Initialize))]
    internal static class Patch_GameDataInit_RareFlashlight
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            if (!Plugin.Enabled.Value) return;
            float rareWeight = Plugin.Pick_RareWeight.Value;
            try
            {
                // Resolve flashlight item's persistentID by name substring.
                uint flashlightId = 0;
                var itemBlocks = GameDataBlockBase<ItemDataBlock>.Wrapper?.Blocks;
                if (itemBlocks != null)
                {
                    foreach (var ib in itemBlocks)
                    {
                        if (ib == null || ib.name == null) continue;
                        if (ib.name.IndexOf(PickableFlashlightTracker.FlashlightItemName,
                                            StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            flashlightId = ((GameDataBlockBase<ItemDataBlock>)(object)ib).persistentID;
                            break;
                        }
                    }
                }
                if (flashlightId == 0) return;

                var blocks = GameDataBlockBase<ConsumableDistributionDataBlock>.Wrapper?.Blocks;
                if (blocks == null) return;

                foreach (var b in blocks)
                {
                    if (b == null || b.SpawnData == null) continue;
                    for (int i = 0; i < b.SpawnData.Count; i++)
                    {
                        var sd = b.SpawnData[i];
                        if (sd == null) continue;
                        if (sd.ItemID == flashlightId)
                            sd.Weight = rareWeight;
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning($"[FO] Rare flashlight weight patch err: {e.Message}");
            }
        }
    }

    // Tracks how many flashlight pickups have spawned in the current level
    // and decides when to force-promote one (guarantee floor) or replace an
    // over-quota roll. Matches the consumable flashlight by ItemDataBlock
    // name substring "FlashlightMedium" (the real block is
    // "CONSUMABLE_FlashlightMedium", id=30 in this build).
    internal static class PickableFlashlightTracker
    {
        internal const string FlashlightItemName = "FlashlightMedium";
        internal const string FallbackItemName   = "GlowStick";

        // User-tunable via config (section 15). Hard cap protects against a
        // misconfigured loot table; guarantee floor force-spawns one if natural
        // rolls keep missing.
        internal static int MaxPerLevel            => Plugin.Pick_MaxPerLevel.Value;
        internal static int GuaranteeAfterNPickups => Plugin.Pick_GuaranteeFloor.Value;

        private static int _flashlightsSpawned = 0;
        private static int _nonFlashlightPickups = 0;
        private static bool _guaranteeFired = false;

        internal static void Reset()
        {
            _flashlightsSpawned   = 0;
            _nonFlashlightPickups = 0;
            _guaranteeFired       = false;
        }

        internal static int FlashlightsSpawned => _flashlightsSpawned;

        // Existing cap path — true ⇒ this flashlight roll is permitted.
        internal static bool TryConsume()
        {
            if (_flashlightsSpawned < MaxPerLevel)
            {
                _flashlightsSpawned++;
                return true;
            }
            return false;
        }

        // Triggered once per level: after GuaranteeAfterNPickups non-flashlight
        // pickups pass without a natural flashlight roll, force the next one to
        // be a flashlight so the level always contains at least one.
        internal static bool ShouldPromoteToFlashlight()
        {
            if (_guaranteeFired) return false;
            if (_flashlightsSpawned > 0) return false;
            _nonFlashlightPickups++;
            if (_nonFlashlightPickups < GuaranteeAfterNPickups) return false;
            _guaranteeFired   = true;
            _flashlightsSpawned++;
            return true;
        }
    }

    // The per-level counter is reset from Plugin.Load's SceneManager.sceneLoaded.
    // (LG_Factory.Build couldn't be resolved by HarmonyX in this IL2CPP build.)

    // Main spawn intercept. Both LG_PickupItem.SetupAsConsumable overloads end
    // in (..., uint itemIDOverride); the 2-arg form is the common funnel that
    // the 3-arg form routes through.
    [HarmonyPatch(typeof(LG_PickupItem), nameof(LG_PickupItem.SetupAsConsumable),
                  new Type[] { typeof(int), typeof(uint) })]
    internal static class Patch_LGPickupSetupAsConsumable
    {
        [HarmonyPrefix]
        static void Prefix(LG_PickupItem __instance, int randomSeed, ref uint itemIDOverride)
        {
            if (!Plugin.Enabled.Value) return;

            var block = GameDataBlockBase<ItemDataBlock>.GetBlock(itemIDOverride);
            if (block == null) return;

            bool isFlashlight = LooksLikeFlashlight(block);

            if (isFlashlight)
            {
                // Natural flashlight roll. Apply cap: replace over-quota ones.
                if (!PickableFlashlightTracker.TryConsume())
                {
                    uint replacement = FindReplacement(block);
                    if (replacement != 0) itemIDOverride = replacement;
                }
                return;
            }

            // Non-flashlight: maybe trigger the guarantee promotion.
            if (PickableFlashlightTracker.ShouldPromoteToFlashlight())
            {
                uint flashlightId = FindFlashlightItemID(block);
                if (flashlightId != 0) itemIDOverride = flashlightId;
            }
        }

        internal static bool LooksLikeFlashlight(ItemDataBlock b)
        {
            if (b == null || b.name == null) return false;
            return b.name.IndexOf(PickableFlashlightTracker.FlashlightItemName,
                                  StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static uint FindReplacement(ItemDataBlock original)
        {
            var allBlocks = GameDataBlockBase<ItemDataBlock>.Wrapper.Blocks;
            if (allBlocks == null) return 0;

            foreach (var b in allBlocks)
            {
                if (b == null || b.name == null) continue;
                if (b.inventorySlot == original.inventorySlot && !LooksLikeFlashlight(b))
                    return ((GameDataBlockBase<ItemDataBlock>)(object)b).persistentID;
            }
            return 0;
        }

        // Look up the flashlight item block. Prefers matching the original's
        // inventory slot so the promotion doesn't break slot-restricted
        // containers; falls back to any flashlight block if no slot match.
        private static uint FindFlashlightItemID(ItemDataBlock original)
        {
            var allBlocks = GameDataBlockBase<ItemDataBlock>.Wrapper.Blocks;
            if (allBlocks == null) return 0;

            uint slotMatch = 0, anyMatch = 0;
            foreach (var b in allBlocks)
            {
                if (b == null || b.name == null) continue;
                if (!LooksLikeFlashlight(b)) continue;
                uint pid = ((GameDataBlockBase<ItemDataBlock>)(object)b).persistentID;
                if (anyMatch == 0) anyMatch = pid;
                if (b.inventorySlot == original.inventorySlot)
                {
                    slotMatch = pid;
                    break;
                }
            }
            return slotMatch != 0 ? slotMatch : anyMatch;
        }
    }

    // Pickable visual override is applied via ApplySandwichInternal (when the
    // owner is a known ConsumableFlashlight) plus LightSandwich.EnsurePickableSettings
    // in DoScan as a recovery path. ItemPartFlashlight.Setup doesn't exist in
    // this IL2CPP build, so we don't have a one-shot setup hook to use.
}
