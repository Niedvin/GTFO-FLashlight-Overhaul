using System;
using System.Collections.Generic;
using ItemSetup;
using UnityEngine;

namespace GTFO_SuperFlashlight
{
    // Single authoritative MonoBehaviour for the mod.
    //
    // - EnforceRFState runs every frame (LateUpdate + PreCullEnforcer.OnPreCull)
    //   to keep RF children in sync with the wielded item.
    // - Swap input (1-6 / scroll) instantly darkens the outgoing source and
    //   arms a swap-in-progress flag so the next Enforce picks up correctly.
    // - Helmet synth is the warm head-mounted light used for melee/tools.
    // - Six flicker passes (Perlin baseline + five timed events) modulate the
    //   RF_Core intensity.
    // - DoScan ticks every 1s to lazy-build sandwiches on newly-spawned gear.
    //
    // No FindObjectsOfType in the per-frame hot path — only in DoScan and on
    // explicit swap input.
    public class LightUpdater : MonoBehaviour
    {
        public LightUpdater(IntPtr ptr) : base(ptr) { }

        // Patch-maintained known item list. Populated by Patch_OnWield via
        // RegisterItem; never enumerated via FindObjectsOfType (that was the
        // ~1s frametime hit visible even in the main menu).
        internal static readonly List<ItemEquippable> _knownItems = new List<ItemEquippable>();

        // Throttled recovery scan over _knownItems. Cheap (iterates ~6 items),
        // but kept on a longer interval so the per-tick cost is negligible.
        private const float ScanIntervalSeconds = 3.0f;
        private float _nextScan;

        // Register an item we've seen wielded so EnforceRFState can find it
        // again later. Idempotent.
        internal static void RegisterItem(ItemEquippable? it)
        {
            if (it == null) return;
            for (int i = _knownItems.Count - 1; i >= 0; i--)
            {
                try
                {
                    var k = _knownItems[i];
                    if (k == null) { _knownItems.RemoveAt(i); continue; }
                    if (k == it) return;
                }
                catch { _knownItems.RemoveAt(i); }
            }
            _knownItems.Add(it);
        }

        internal static void ClearKnownItems() => _knownItems.Clear();

        // ── Singleton handle so static helpers can reach the instance ─────
        internal static LightUpdater? Instance;
        private static bool _preCullAttached = false;

        public void Awake() => Instance = this;

        // ── Helmet synth ──────────────────────────────────────────────────
        // Warm head-mounted light used when wielding melee/tool items. Lives
        // on FPSLookCamera so it follows the player's view.
        private static GameObject? _helmetSynthGO;
        private static bool _helmetSynthBuilt = false;

        // (it == trueWielded && gunOrPick && LastFlashlightState)
        internal static bool GunFlashlightActive = false;
        // Match vanilla GTFO: per-item flashlight starts OFF at level load.
        // Player presses F → LFS=true → lights come on.
        internal static bool LastFlashlightState = false;
        internal static float LastWieldChangeTime = -1000f;

        // Mid-swap fallback. Patch_OnWield.Prefix sets this to the incoming
        // item before m_isWielded transitions. Enforce treats it as trueWielded
        // during the transient window and clears it once the poll catches up.
        internal static ItemEquippable? _pendingWieldItem;

        // Sticky record of the most recent resolved wielded type. During the
        // (null, null) gap between OnUnWield and the matching OnWield, this
        // tells Enforce whether we're mid-swap (last was gun → keep synth off)
        // or genuinely on bare-hands. No expiry — the next OnWield resets it.
        internal static bool _lastObservedWasGun = false;

        internal static void SetHelmetSynthActive(bool active)
        {
            if (_helmetSynthGO != null && _helmetSynthGO.activeSelf != active)
                _helmetSynthGO.SetActive(active);
        }

        // Swap-in-progress signal. Set on input/OnUnWield (whichever fires
        // first), cleared by Patch_OnWield.Prefix. While true, Enforce forces
        // every source dark, then the new source lights up in the same pass
        // after the flag clears.
        internal static bool _swapInProgress = false;
        private static float _swapStartedAt = 0f;
        private const float SwapMaxSeconds = 0.8f;

        // Minimum visible dark window — kept short to keep the feel brisk.
        // Even if EndSwap clears _swapInProgress in the same frame as input,
        // this hold guarantees a perceivable OFF before the new source comes up.
        private static float _holdDarkUntil = 0f;
        private static float  _lastWieldedDiagAt  = 0f;
        private static float  _lastStateDiagAt    = 0f;
        private static float  _lastLoopTraceAt    = 0f;
        private const float MinDarkSeconds = 0.15f;

        internal static void BeginSwap()
        {
            // Idempotent across a single swap. The first call (input handler
            // or OnUnWield) anchors _holdDarkUntil; subsequent calls during
            // the same swap must NOT push it forward, otherwise the visible
            // dark window grows by the per-pair OnUnWield latency.
            bool wasInProgress = _swapInProgress;
            _swapInProgress = true;
            _swapStartedAt = Time.unscaledTime;
            if (!wasInProgress)
                _holdDarkUntil = Time.unscaledTime + MinDarkSeconds;
        }

        internal static void EndSwap()
        {
            _swapInProgress = false;
        }

        // Pushes our visible state into GTFO's per-item flashlight field so
        // the inventory icon tracks weapon swaps. Idempotent + re-entry guarded.
        private static ItemEquippable? _iconLastItem;
        private static bool _iconLastState = false;
        private static bool _inDriveIcon = false;

        internal static void DriveIcon(ItemEquippable? item, bool state)
        {
            if (_inDriveIcon) return;
            if (item == null) return;
            if (item == _iconLastItem && state == _iconLastState) return;
            _iconLastItem  = item;
            _iconLastState = state;
            _inDriveIcon = true;
            try { item.SetAttachedFlashlightEnabled(state); }
            catch { }
            finally { _inDriveIcon = false; }
        }

        internal static void ResetIconCache()
        {
            _iconLastItem  = null;
            _iconLastState = false;
        }

        // Invariant after every call:
        //   For every gun/pickable, RF on ⇔ (it == trueWielded && LastFlashlightState).
        //   RF_HelmetSynth on ⇔ !trueWieldedIsGunOrPick && LastFlashlightState.
        //
        // trueWielded comes from polling m_isWielded on cached items; falls
        // back to _pendingWieldItem during the mid-swap window. Runs at
        // LateUpdate + PreCullEnforcer.OnPreCull — patches only mutate inputs.
        internal void EnforceRFState()
        {
            try
            {
                // Swap-in-progress: force darkness on every source. Anchored
                // by input handler (instant OFF at start of swap), held by
                // Patch_OnUnWield, cleared by Patch_OnWield.Prefix. Safety
                // expiry covers swaps that don't terminate cleanly.
                float tNow = Time.unscaledTime;
                if (_swapInProgress && tNow - _swapStartedAt > SwapMaxSeconds)
                    _swapInProgress = false;

                // Force-dark also while min-dark hold is active. Even if
                // GTFO's swap chain completes on the same frame as input
                // and EndSwap clears _swapInProgress, the hold guarantees
                // a perceivable OFF window before the new source comes on.
                bool forceDark = _swapInProgress || tNow < _holdDarkUntil;
                if (forceDark)
                {
                    if (Time.unscaledTime - _lastStateDiagAt > 10f)
                    {
                        _lastStateDiagAt = Time.unscaledTime;
                        Plugin.Logger.LogInfo("[RF] state FORCE_DARK swapInProgress=" + _swapInProgress
                            + " holdUntil-now=" + (_holdDarkUntil - tNow).ToString("F2"));
                    }
                    if (_helmetSynthGO != null && _helmetSynthGO.activeSelf)
                        _helmetSynthGO.SetActive(false);
                    foreach (var it in _knownItems)
                    {
                        if (it == null) continue;
                        Light? lt = null;
                        try { lt = it.m_lt; } catch { }
                        if (lt == null) continue;
                        if (lt.transform.Find(LightSandwich.L1) == null) continue;
                        var coreT = lt.transform.Find(LightSandwich.RFCORE);
                        if (coreT != null && coreT.gameObject.activeSelf)
                            LightSandwich.SyncEnabled(lt.transform, false);
                    }
                    GunFlashlightActive = false;
                    // Do NOT DriveIcon(false) here. Some weapons respond to
                    // SetAttachedFlashlightEnabled(false) by SetActive'ing
                    // their m_lt GameObject false, which then doesn't reliably
                    // get reactivated on swap-back. Letting GTFO's per-item
                    // state stay as the player left it keeps the m_lt subtree
                    // active throughout the swap.
                    //
                    // Reset the icon cache so the first DriveIcon call after
                    // we exit force-dark fires fresh and re-asserts the state
                    // (GTFO may have toggled its own per-item flag during the
                    // swap animation).
                    ResetIconCache();
                    return;
                }

                // Poll cached items for an m_isWielded=true entry.
                ItemEquippable? trueWielded = null;
                bool trueWieldedIsGunOrPick = false;
                foreach (var it in _knownItems)
                {
                    if (it == null) continue;
                    bool w;
                    try { w = it.m_isWielded; } catch { continue; }
                    if (!w) continue;
                    trueWielded = it;
                    try
                    {
                        trueWieldedIsGunOrPick = it.TryCast<global::Gear.BulletWeapon>() != null
                                              || it.TryCast<ConsumableFlashlight>() != null;
                    }
                    catch { trueWieldedIsGunOrPick = false; }
                    break;
                }

                // Pending-fallback during the mid-swap gap. Cleared once the
                // poll catches up.
                if (trueWielded == null && _pendingWieldItem != null)
                {
                    trueWielded = _pendingWieldItem;
                    try
                    {
                        trueWieldedIsGunOrPick = trueWielded.TryCast<global::Gear.BulletWeapon>() != null
                                              || trueWielded.TryCast<ConsumableFlashlight>() != null;
                    }
                    catch { trueWieldedIsGunOrPick = false; }
                }
                else if (trueWielded != null && trueWielded == _pendingWieldItem)
                {
                    _pendingWieldItem = null;
                }

                if (trueWielded != null
                    && Patch_ItemWield.CurrentWieldedItem != trueWielded)
                {
                    Patch_ItemWield.CurrentWieldedItem = trueWielded;
                }

                // Stays sticky during gap frames so the swap-limbo check below
                // works without a time-based deadline.
                if (trueWielded != null)
                    _lastObservedWasGun = trueWieldedIsGunOrPick;

                // During the (null, null) gap between OnUnWield and OnWield,
                // if the previous wielded was a gun, suppress the synth — we're
                // mid-swap, not transitioning to bare-hands.
                bool inSwapLimbo = trueWielded == null
                                && _pendingWieldItem == null
                                && _lastObservedWasGun;

                GunFlashlightActive = trueWieldedIsGunOrPick && LastFlashlightState;
                bool synthShouldBeOn = !inSwapLimbo && !trueWieldedIsGunOrPick && LastFlashlightState;

                // Diagnostic: dump overall state every 10s.
                if (Time.unscaledTime - _lastStateDiagAt > 10f)
                {
                    _lastStateDiagAt = Time.unscaledTime;
                    try
                    {
                        var sb = new System.Text.StringBuilder(256);
                        sb.Append("[RF] state LFS=").Append(LastFlashlightState)
                          .Append(" trueW=").Append(trueWielded != null && trueWielded.gameObject != null ? trueWielded.gameObject.name : "null")
                          .Append(" pendW=").Append(_pendingWieldItem != null && _pendingWieldItem.gameObject != null ? _pendingWieldItem.gameObject.name : "null")
                          .Append(" cw=").Append(Patch_ItemWield.CurrentWieldedItem != null && Patch_ItemWield.CurrentWieldedItem.gameObject != null ? Patch_ItemWield.CurrentWieldedItem.gameObject.name : "null")
                          .Append(" swapInProgress=").Append(_swapInProgress)
                          .Append(" known=[");
                        bool first = true;
                        foreach (var ki in _knownItems)
                        {
                            if (ki == null) continue;
                            if (!first) sb.Append(',');
                            first = false;
                            string nm = "?";
                            try { nm = ki.gameObject != null ? ki.gameObject.name : "?"; } catch { }
                            bool wi = false; try { wi = ki.m_isWielded; } catch { }
                            bool hasLt = false; try { hasLt = ki.m_lt != null; } catch { }
                            bool hasL1 = false;
                            try { hasL1 = ki.m_lt != null && ki.m_lt.transform.Find(LightSandwich.L1) != null; } catch { }
                            sb.Append(nm).Append("(w=").Append(wi)
                              .Append(",lt=").Append(hasLt)
                              .Append(",L1=").Append(hasL1).Append(")");
                        }
                        sb.Append("]");
                        Plugin.Logger.LogInfo(sb.ToString());
                    }
                    catch { }
                }
                if (_helmetSynthGO != null && _helmetSynthGO.activeSelf != synthShouldBeOn)
                    _helmetSynthGO.SetActive(synthShouldBeOn);

                // Per-item RF enforcement.
                //
                // We unconditionally apply the desired state for the wielded
                // item every frame — the previous `currentlyEnabled` check
                // skipped SyncEnabled when activeSelf reported "on", but GTFO
                // can SetActive an ancestor mid-swap which leaves the RF child
                // activeSelf=true while activeInHierarchy=false (no visible
                // beam, no state-mismatch detection). For non-wielded items
                // we only fire when the change is needed so we don't write
                // every frame for a dozen stowed weapons.
                bool logLoopOnce = Time.unscaledTime - _lastLoopTraceAt > 10f;
                if (logLoopOnce) _lastLoopTraceAt = Time.unscaledTime;
                foreach (var it in _knownItems)
                {
                    if (it == null) { if (logLoopOnce) Plugin.Logger.LogInfo("[RF] loop: it=null skip"); continue; }
                    bool isGunOrPick;
                    string itName = "?"; try { itName = it.gameObject != null ? it.gameObject.name : "?"; } catch { }
                    try
                    {
                        isGunOrPick = it.TryCast<global::Gear.BulletWeapon>() != null
                                   || it.TryCast<ConsumableFlashlight>() != null;
                    }
                    catch { if (logLoopOnce) Plugin.Logger.LogInfo("[RF] loop " + itName + ": cast threw, skip"); continue; }
                    if (!isGunOrPick) { if (logLoopOnce) Plugin.Logger.LogInfo("[RF] loop " + itName + ": !isGunOrPick skip"); continue; }

                    Light? lt = null;
                    try { lt = it.m_lt; } catch { }
                    if (lt == null) { if (logLoopOnce) Plugin.Logger.LogInfo("[RF] loop " + itName + ": lt=null skip"); continue; }

                    bool isWielded       = (it == trueWielded);
                    bool shouldBeEnabled = isWielded && LastFlashlightState;

                    // GTFO's m_lt is SHARED across all weapons — one Light
                    // object that moves between them on wield. If this item is
                    // non-wielded and shares m_lt with the wielded item, our
                    // disable path would kill the very RF children the wielded
                    // path just enabled. Defer to the wielded iteration.
                    if (!isWielded && trueWielded != null)
                    {
                        Light? wlt = null;
                        try { wlt = trueWielded.m_lt; } catch { }
                        if (wlt != null && wlt.GetInstanceID() == lt.GetInstanceID())
                        {
                            if (logLoopOnce) Plugin.Logger.LogInfo("[RF] loop " + itName + ": shares m_lt with wielded — skip");
                            continue;
                        }
                    }
                    if (logLoopOnce) Plugin.Logger.LogInfo("[RF] loop " + itName + ": isWielded=" + isWielded + " shouldBeEnabled=" + shouldBeEnabled + " hasL1=" + (lt.transform.Find(LightSandwich.L1) != null));

                    // Defensive recovery: if the wielded item has no sandwich
                    // (race where GTFO recreated m_lt between our last build
                    // and this frame), build it now so SyncEnabled has children
                    // to toggle.
                    if (lt.transform.Find(LightSandwich.L1) == null)
                    {
                        if (isWielded) LightSandwich.ApplySandwich(lt, it);
                        else continue;
                    }

                    if (isWielded)
                    {
                        // Walk up from m_lt and activate any inactive ancestor
                        // up to the owning ItemEquippable. Some weapons leave
                        // an ancestor (e.g. the flashlight-attach node, or
                        // m_lt's own GameObject) SetActive(false) after stow
                        // and don't reactivate on re-wield. RF children's
                        // activeSelf would be true but activeInHierarchy false,
                        // so they never actually render. m_lt itself emits
                        // nothing (cullingMask=0), so activating its subtree
                        // is visually safe.
                        if (shouldBeEnabled)
                        {
                            Transform? cur = lt.transform;
                            GameObject? ownerGO = null;
                            try { ownerGO = it.gameObject; } catch { }
                            int safety = 16;
                            while (cur != null && safety-- > 0)
                            {
                                if (!cur.gameObject.activeSelf)
                                    cur.gameObject.SetActive(true);
                                if (ownerGO != null && cur.gameObject == ownerGO) break;
                                cur = cur.parent;
                            }
                        }
                        // Always converge to the target state — self-heals
                        // any desync from SetActive cycles on the weapon GO.
                        LightSandwich.SyncEnabled(lt.transform, shouldBeEnabled);

                        // Diagnostic: sample wielded RF_Core state every 0.3s
                        // unconditionally so we can see the "icon on, beam
                        // invisible" state if it appears between SyncEnabled
                        // writes.
                        if (shouldBeEnabled && Time.unscaledTime - _lastWieldedDiagAt > 10f)
                        {
                            _lastWieldedDiagAt = Time.unscaledTime;
                            int step = 0;
                            try
                            {
                                step = 1;
                                var coreT = lt.transform.Find(LightSandwich.RFCORE);
                                step = 2;
                                var l1T   = lt.transform.Find(LightSandwich.L1);
                                step = 3;
                                var l2T   = lt.transform.Find(LightSandwich.L2);
                                step = 4;
                                bool ltGoAct = lt.gameObject.activeInHierarchy;
                                step = 5;
                                bool ltEn = lt.enabled;
                                step = 6;
                                Vector3 ltPos = lt.transform.position;
                                Vector3 ltFwd = lt.transform.forward;
                                int ltId = lt.GetInstanceID();
                                string s = "[RF] wielded=" + itName
                                    + " ltID=" + ltId
                                    + " ltGO_actHier=" + ltGoAct
                                    + " ltEnabled=" + ltEn
                                    + " ltPos=(" + ltPos.x.ToString("F1") + "," + ltPos.y.ToString("F1") + "," + ltPos.z.ToString("F1") + ")"
                                    + " ltFwd=(" + ltFwd.x.ToString("F2") + "," + ltFwd.y.ToString("F2") + "," + ltFwd.z.ToString("F2") + ")";
                                step = 7;
                                // Look up cached preset by m_lt's instance ID.
                                string presetName = "?";
                                if (LightSandwich._lightPreset.TryGetValue(ltId, out var presetInfo))
                                    presetName = presetInfo.Name ?? "Unnamed";
                                s += " preset=\"" + presetName + "\"";

                                if (coreT != null)
                                {
                                    var cl = coreT.GetComponent<Light>();
                                    step = 8;
                                    s += " | RF_Core: enabled=" + (cl != null ? cl.enabled.ToString() : "noL")
                                       + " int=" + (cl != null ? cl.intensity.ToString("F2") : "?")
                                       + " rng=" + (cl != null ? cl.range.ToString("F1") : "?")
                                       + " ang=" + (cl != null ? cl.spotAngle.ToString("F0") : "?")
                                       + " cull=" + (cl != null ? cl.cullingMask.ToString() : "?")
                                       + " cookie=" + (cl != null && cl.cookie != null ? "Y" : "N");
                                    if (cl != null)
                                    {
                                        Color cc = cl.color;
                                        s += " color=(" + cc.r.ToString("F2") + "," + cc.g.ToString("F2") + "," + cc.b.ToString("F2") + ")";
                                    }
                                    step = 9;
                                }
                                if (l1T != null)
                                {
                                    var cl = l1T.GetComponent<Light>();
                                    step = 10;
                                    s += " | L1: actHier=" + l1T.gameObject.activeInHierarchy
                                       + " enabled=" + (cl != null ? cl.enabled.ToString() : "noL")
                                       + " int=" + (cl != null ? cl.intensity.ToString("F2") : "?");
                                }
                                if (l2T != null)
                                {
                                    var cl = l2T.GetComponent<Light>();
                                    step = 11;
                                    s += " | L2: actHier=" + l2T.gameObject.activeInHierarchy
                                       + " enabled=" + (cl != null ? cl.enabled.ToString() : "noL")
                                       + " int=" + (cl != null ? cl.intensity.ToString("F2") : "?");
                                }
                                step = 12;
                                Plugin.Logger.LogInfo(s);
                            }
                            catch (System.Exception ex)
                            {
                                Plugin.Logger.LogWarning("[RF] wielded diag THREW at step=" + step + " ex=" + ex.GetType().Name + " msg=" + ex.Message);
                            }
                        }
                    }
                    else
                    {
                        var core = lt.transform.Find(LightSandwich.RFCORE);
                        if (core == null) continue;
                        var cl = core.GetComponent<Light>();
                        bool currentlyEnabled = core.gameObject.activeSelf
                                             && (cl == null || cl.enabled);
                        if (currentlyEnabled == shouldBeEnabled) continue;
                        LightSandwich.SyncEnabled(lt.transform, shouldBeEnabled);
                    }
                }

                // Bone-follow: copy the wielded gun's animated bone pose onto
                // m_lt so the beam tracks the weapon model through reload /
                // swap / inspect animations. m_lt is normally parented to the
                // camera holder (a stable transform that ignores per-weapon
                // animation), so without this the beam stays locked forward.
                //
                // Runs in both LateUpdate (via this method) and OnPreCull
                // (PreCullEnforcer → Enforce again), both after the Animator
                // Update pass. Pickable consumable flashlight is intentionally
                // skipped — its m_lt is already inside its own moving
                // hierarchy and the user reports its position is correct.
                try
                {
                    if (Plugin.AttachLightToWeaponBone != null
                        && Plugin.AttachLightToWeaponBone.Value
                        && trueWielded != null
                        && trueWieldedIsGunOrPick
                        && trueWielded.TryCast<global::Gear.BulletWeapon>() != null)
                    {
                        Light? boneLt = null;
                        try { boneLt = trueWielded.m_lt; } catch { }
                        if (boneLt != null)
                        {
                            var bone = WeaponBoneTracker.Resolve(trueWielded);
                            if (bone != null)
                                boneLt.transform.SetPositionAndRotation(bone.position, bone.rotation);
                        }
                    }
                }
                catch { }

                // Keep the inventory icon in sync with the visible state.
                if (trueWielded != null)
                    DriveIcon(trueWielded, LastFlashlightState);
            }
            catch { }
        }

        // Called by Patch_OnWield.Prefix — runs BEFORE GTFO's own OnWield body.
        // We do the minimum needed (register the item, lazy-build the sandwich
        // if missing, fix misclassification) and DEFER SuppressRangedSiblings
        // to LateUpdate. Calling SuppressRangedSiblings here zeroed m_lt's
        // intensity/range before GTFO's wield body could read them, which on
        // some weapons (whichever was the first ranged wielded in a session)
        // left GTFO with a stuck "flashlight disabled" internal state for the
        // rest of the run.
        internal static void OnWieldedChanged(ItemEquippable? newWielded)
        {
            LastWieldChangeTime = Time.unscaledTime;
            if (newWielded == null) return;
            RegisterItem(newWielded);
            try
            {
                if (newWielded.m_lt == null) return;
                var lt = newWielded.m_lt;
                bool isBullet = newWielded.TryCast<global::Gear.BulletWeapon>() != null;
                bool isPick   = newWielded.TryCast<ConsumableFlashlight>() != null;

                if (isBullet)
                {
                    if (lt.transform.Find(LightSandwich.L1) == null)
                        LightSandwich.ApplySandwich(lt, newWielded);
                    // Force a fresh resolve: at wield time the weapon's full
                    // prefab tree (including "Flashlight_X(Clone)" descendant)
                    // is guaranteed instantiated, so the keyword matcher can
                    // pick the right preset. If lazy-build at sandwich time
                    // ran against an incomplete tree and cached a null/Default
                    // preset, this corrects it.
                    LightSandwich.EnsureRangedSettings(lt, newWielded, forceResolve: true);
                }
                else if (isPick)
                {
                    if (lt.transform.Find(LightSandwich.L1) == null)
                        LightSandwich.ApplySandwich(lt, newWielded);
                    LightSandwich.EnsurePickableSettings(lt);
                }
            }
            catch { }
        }

        // Attach PreCullEnforcer to FPSLookCamera so Enforce runs again right
        // before render, catching late-frame state changes.
        private void TryAttachPreCullEnforcer()
        {
            if (_preCullAttached) return;
            try
            {
                Camera? fpsCam = null;
                var allCams = Camera.allCameras;
                if (allCams != null)
                {
                    foreach (var c in allCams)
                    {
                        if (c == null) continue;
                        var nm = c.name;
                        if (!string.IsNullOrEmpty(nm)
                            && nm.IndexOf("FPSLookCamera", StringComparison.OrdinalIgnoreCase) >= 0)
                        { fpsCam = c; break; }
                    }
                }
                if (fpsCam == null) return;

                if (fpsCam.gameObject.GetComponent<PreCullEnforcer>() == null)
                    fpsCam.gameObject.AddComponent<PreCullEnforcer>();
                _preCullAttached = true;
            }
            catch { }
        }

        // Build the warm helmet-mounted light shown when wielding melee/tools.
        private static void EnsureHelmetSynth()
        {
            if (_helmetSynthBuilt) return;

            Camera? fpsCam = null;
            var allCams = Camera.allCameras;
            if (allCams != null)
                foreach (var c in allCams)
                    if (c != null && c.name.IndexOf("FPSLookCamera", StringComparison.OrdinalIgnoreCase) >= 0)
                    { fpsCam = c; break; }

            if (fpsCam == null) return;
            _helmetSynthBuilt = true;

            var go = new GameObject("RF_HelmetSynth");
            go.transform.SetParent(fpsCam.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.hideFlags = HideFlags.HideAndDontSave;

            var warm = new Color(1.0f, 0.88f, 0.65f);

            // Main 80° cone.
            var l = go.AddComponent<Light>();
            l.type           = LightType.Spot;
            l.spotAngle      = 80f;
            l.innerSpotAngle = 0f;
            l.intensity      = Plugin.HelmetIntensity * 0.55f;
            l.range          = Plugin.HelmetRange;
            l.shadows        = LightShadows.None;
            l.cullingMask    = -1;
            l.color          = warm;
            try { l.cookie = LightSandwich.GetSmoothCookie(); } catch { }

            // Wide 140° falloff. Its range may exceed the main cone
            // (HelmetFallRange), giving a soft fade past the main range
            // instead of a hard "wall" at its end.
            var goFall = new GameObject("RF_HelmetSynth_Fall");
            goFall.transform.SetParent(go.transform, false);
            goFall.transform.localPosition = Vector3.zero;
            goFall.transform.localRotation = Quaternion.identity;
            goFall.hideFlags = HideFlags.HideAndDontSave;
            var lFall = goFall.AddComponent<Light>();
            lFall.type           = LightType.Spot;
            lFall.spotAngle      = 140f;
            lFall.innerSpotAngle = 0f;
            lFall.intensity      = Plugin.HelmetIntensity * 0.18f;
            lFall.range          = Plugin.HelmetFallRange;
            lFall.shadows        = LightShadows.None;
            lFall.cullingMask    = -1;
            lFall.color          = warm;
            try { lFall.cookie = LightSandwich.GetSmoothCookie(); } catch { }

            _helmetSynthGO = go;
            RegisterFlickerBaseline(l);
            RegisterFlickerRef(l);

            go.SetActive(!GunFlashlightActive && LastFlashlightState);
        }

        // ══════════════════════════════════════════════════════════════════
        // Flicker animations (Perlin tremor + rare blink + horror blink).
        // ══════════════════════════════════════════════════════════════════
        private static readonly Dictionary<int, float> _rfCoreBaselineIntensity = new Dictionary<int, float>();
        private static readonly List<Light> _rfCoreRefs = new List<Light>();

        internal static void RegisterFlickerBaseline(Light l)
        {
            if (l == null) return;
            _rfCoreBaselineIntensity[l.GetInstanceID()] = l.intensity;
        }

        internal static void RegisterFlickerRef(Light l)
        {
            if (l == null) return;
            if (!_rfCoreRefs.Contains(l)) _rfCoreRefs.Add(l);
        }

        private void ApplyFlicker()
        {
            if (!Plugin.FlickerEnabled.Value) return;
            if (_rfCoreRefs.Count == 0) return;
            float speed = Plugin.FlickerSpeed.Value;
            float amp   = Plugin.FlickerAmplitude.Value;
            float t     = Time.time * speed;
            for (int i = _rfCoreRefs.Count - 1; i >= 0; i--)
            {
                // Per-element try/catch: in IL2CPP, a destroyed Light wrapper
                // can still pass the `== null` check but throw on property
                // access. Drop the dead ref and keep iterating.
                try
                {
                    var l = _rfCoreRefs[i];
                    if (l == null) { _rfCoreRefs.RemoveAt(i); continue; }
                    int id = l.GetInstanceID();
                    if (!_rfCoreBaselineIntensity.TryGetValue(id, out var baseline)) continue;
                    float noise = Mathf.PerlinNoise(t, id * 0.0001f) * 2f - 1f;
                    l.intensity = Mathf.Max(0f, baseline * (1f + noise * amp));
                }
                catch { _rfCoreRefs.RemoveAt(i); }
            }
        }

        // Rare flicker (bad-contact bursts).
        private float _rfNextBurstTime = 0f;
        private bool  _rfBurstActive   = false;
        private int   _rfBlinksLeft    = 0;
        private float _rfNextBlinkTime = 0f;
        private bool  _rfBlinkOn       = true;

        private void ApplyRareFlicker()
        {
            if (!Plugin.RareFlickerEnabled.Value) return;
            if (_rfCoreRefs.Count == 0) return;
            float now = Time.unscaledTime;
            if (!_rfBurstActive)
            {
                if (_rfNextBurstTime == 0f)
                {
                    _rfNextBurstTime = now + UnityEngine.Random.Range(
                        Plugin.RareFlickerIntervalMin.Value, Plugin.RareFlickerIntervalMax.Value);
                    return;
                }
                if (now >= _rfNextBurstTime)
                {
                    _rfBurstActive   = true;
                    _rfBlinksLeft    = UnityEngine.Random.Range(3, 8);
                    _rfNextBlinkTime = now + UnityEngine.Random.Range(0.05f, 0.10f);
                    _rfBlinkOn       = false;
                    ApplyBurstState();
                }
                return;
            }
            if (now >= _rfNextBlinkTime)
            {
                _rfBlinkOn = !_rfBlinkOn;
                _rfBlinksLeft--;
                if (_rfBlinksLeft <= 0)
                {
                    _rfBurstActive   = false;
                    _rfBlinkOn       = true;
                    ApplyBurstState();
                    _rfNextBurstTime = now + UnityEngine.Random.Range(
                        Plugin.RareFlickerIntervalMin.Value, Plugin.RareFlickerIntervalMax.Value);
                }
                else
                {
                    _rfNextBlinkTime = now + UnityEngine.Random.Range(0.05f, 0.10f);
                    ApplyBurstState();
                }
            }
        }

        private void ApplyBurstState()
        {
            for (int i = _rfCoreRefs.Count - 1; i >= 0; i--)
            {
                try
                {
                    var l = _rfCoreRefs[i];
                    if (l == null) { _rfCoreRefs.RemoveAt(i); continue; }
                    int id = l.GetInstanceID();
                    if (!_rfCoreBaselineIntensity.TryGetValue(id, out var baseline)) continue;
                    l.intensity = _rfBlinkOn ? baseline : 0f;
                }
                catch { _rfCoreRefs.RemoveAt(i); }
            }
        }

        // Horror flicker — many blinks with one long dark pause.
        private float _horNextBurstTime = 0f;
        private bool  _horBurstActive   = false;
        private int   _horBlinksLeft    = 0;
        private float _horNextBlinkTime = 0f;
        private bool  _horBlinkOn       = true;
        private int   _horLongPauseAt   = -1;

        private void ApplyHorrorFlicker()
        {
            if (!Plugin.HorrorFlickerEnabled.Value) return;
            if (_rfCoreRefs.Count == 0) return;
            float now = Time.unscaledTime;
            if (!_horBurstActive)
            {
                if (_horNextBurstTime == 0f)
                {
                    _horNextBurstTime = now + UnityEngine.Random.Range(
                        Plugin.HorrorFlickerIntervalMin.Value, Plugin.HorrorFlickerIntervalMax.Value);
                    return;
                }
                if (now >= _horNextBurstTime)
                {
                    _horBurstActive   = true;
                    _horBlinksLeft    = UnityEngine.Random.Range(6, 15);
                    _horBlinkOn       = false;
                    _horLongPauseAt   = UnityEngine.Random.Range(2, _horBlinksLeft - 1);
                    _horNextBlinkTime = now + UnityEngine.Random.Range(0.04f, 0.12f);
                    ApplyHorrorState();
                }
                return;
            }
            if (now >= _horNextBlinkTime)
            {
                _horBlinkOn = !_horBlinkOn;
                _horBlinksLeft--;
                if (_horBlinksLeft <= 0)
                {
                    _horBurstActive   = false;
                    _horBlinkOn       = true;
                    ApplyHorrorState();
                    _horNextBurstTime = now + UnityEngine.Random.Range(
                        Plugin.HorrorFlickerIntervalMin.Value, Plugin.HorrorFlickerIntervalMax.Value);
                }
                else
                {
                    bool isLong = !_horBlinkOn && (_horBlinksLeft == _horLongPauseAt);
                    float dur;
                    if (isLong)            dur = UnityEngine.Random.Range(0.45f, 1.20f);
                    else if (!_horBlinkOn) dur = UnityEngine.Random.Range(0.04f, 0.15f);
                    else                   dur = UnityEngine.Random.Range(0.06f, 0.22f);
                    _horNextBlinkTime = now + dur;
                    ApplyHorrorState();
                }
            }
        }

        private void ApplyHorrorState()
        {
            for (int i = _rfCoreRefs.Count - 1; i >= 0; i--)
            {
                try
                {
                    var l = _rfCoreRefs[i];
                    if (l == null) { _rfCoreRefs.RemoveAt(i); continue; }
                    int id = l.GetInstanceID();
                    if (!_rfCoreBaselineIntensity.TryGetValue(id, out var baseline)) continue;
                    l.intensity = _horBlinkOn ? baseline : 0f;
                }
                catch { _rfCoreRefs.RemoveAt(i); }
            }
        }

        // Apply a multiplier to every registered RF_Core baseline. Shared by
        // the brownout/stutter/restrike passes so dead-ref pruning lives in
        // one place.
        private void ApplyIntensityMultiplier(float m)
        {
            for (int i = _rfCoreRefs.Count - 1; i >= 0; i--)
            {
                try
                {
                    var l = _rfCoreRefs[i];
                    if (l == null) { _rfCoreRefs.RemoveAt(i); continue; }
                    int id = l.GetInstanceID();
                    if (!_rfCoreBaselineIntensity.TryGetValue(id, out var baseline)) continue;
                    l.intensity = Mathf.Max(0f, baseline * m);
                }
                catch { _rfCoreRefs.RemoveAt(i); }
            }
        }

        // Brownout — slow dim → hold → recover (smoothstep, no blinks).
        private float _boNextTriggerTime = 0f;
        private bool  _boActive           = false;
        private float _boStartTime        = 0f;
        private float _boDimEnd           = 0f;
        private float _boHoldEnd          = 0f;
        private float _boRecoverEnd       = 0f;
        private float _boDimTarget        = 0.4f;

        private void ApplyBrownoutFlicker()
        {
            if (!Plugin.BrownoutEnabled.Value) return;
            if (_rfCoreRefs.Count == 0) return;
            float now = Time.unscaledTime;

            if (!_boActive)
            {
                if (_boNextTriggerTime == 0f)
                {
                    _boNextTriggerTime = now + UnityEngine.Random.Range(
                        Plugin.BrownoutIntervalMin.Value, Plugin.BrownoutIntervalMax.Value);
                    return;
                }
                if (now >= _boNextTriggerTime)
                {
                    _boActive       = true;
                    _boStartTime    = now;
                    _boDimEnd       = now + UnityEngine.Random.Range(1.5f, 3.0f);
                    _boHoldEnd      = _boDimEnd  + UnityEngine.Random.Range(1.5f, 4.5f);
                    _boRecoverEnd   = _boHoldEnd + UnityEngine.Random.Range(1.5f, 3.0f);
                    _boDimTarget    = UnityEngine.Random.Range(0.35f, 0.55f);
                }
                return;
            }

            float mult;
            if (now < _boDimEnd)
            {
                float t = (now - _boStartTime) / (_boDimEnd - _boStartTime);
                float s = t * t * (3f - 2f * t);              // smoothstep
                mult = Mathf.Lerp(1.0f, _boDimTarget, s);
            }
            else if (now < _boHoldEnd)
            {
                // Tiny low-frequency wander while held low so the dim doesn't
                // look statically frozen.
                float wander = (Mathf.PerlinNoise(now * 1.5f, 0.13f) - 0.5f) * 0.06f;
                mult = Mathf.Max(0.05f, _boDimTarget + wander);
            }
            else if (now < _boRecoverEnd)
            {
                float t = (now - _boHoldEnd) / (_boRecoverEnd - _boHoldEnd);
                float s = t * t * (3f - 2f * t);
                mult = Mathf.Lerp(_boDimTarget, 1.0f, s);
            }
            else
            {
                _boActive          = false;
                _boNextTriggerTime = now + UnityEngine.Random.Range(
                    Plugin.BrownoutIntervalMin.Value, Plugin.BrownoutIntervalMax.Value);
                return; // let Perlin (ApplyFlicker) take over next frame
            }

            ApplyIntensityMultiplier(mult);
        }

        // Stutter — short burst of high-frequency continuous flutter.
        private float _stNextTriggerTime = 0f;
        private bool  _stActive           = false;
        private float _stStartTime        = 0f;
        private float _stEndTime          = 0f;
        private float _stSpeed            = 30f;
        private float _stAmplitude        = 0.65f;
        private float _stSeed             = 0f;

        private void ApplyStutterFlicker()
        {
            if (!Plugin.StutterEnabled.Value) return;
            if (_rfCoreRefs.Count == 0) return;
            float now = Time.unscaledTime;

            if (!_stActive)
            {
                if (_stNextTriggerTime == 0f)
                {
                    _stNextTriggerTime = now + UnityEngine.Random.Range(
                        Plugin.StutterIntervalMin.Value, Plugin.StutterIntervalMax.Value);
                    return;
                }
                if (now >= _stNextTriggerTime)
                {
                    _stActive    = true;
                    _stStartTime = now;
                    _stEndTime   = now + UnityEngine.Random.Range(0.8f, 2.5f);
                    _stSpeed     = UnityEngine.Random.Range(25f, 45f);
                    _stAmplitude = UnityEngine.Random.Range(0.50f, 0.80f);
                    _stSeed      = UnityEngine.Random.Range(0f, 1000f);
                }
                return;
            }

            if (now >= _stEndTime)
            {
                _stActive          = false;
                _stNextTriggerTime = now + UnityEngine.Random.Range(
                    Plugin.StutterIntervalMin.Value, Plugin.StutterIntervalMax.Value);
                return;
            }

            // High-frequency Perlin → unipolar [0..1] noise.
            float n = Mathf.PerlinNoise(now * _stSpeed, _stSeed);
            // Envelope: fade-in over first 0.10s, fade-out over last 0.30s so
            // the burst's edges don't look like hard step transitions.
            float envIn  = Mathf.Clamp01((now - _stStartTime) / 0.10f);
            float envOut = Mathf.Clamp01((_stEndTime - now)   / 0.30f);
            float env    = Mathf.Min(envIn, envOut);
            // Modulate around 1.0 by ±amplitude, attenuated by envelope.
            float mult = 1.0f + (n * 2f - 1f) * _stAmplitude * env;
            ApplyIntensityMultiplier(mult);
        }

        // Restrike — LED bulb restart, 4-phase one-shot:
        //   0: full OFF · 1: 35–50% weak strike · 2: OFF again · 3: restore + decaying tail
        private float _rsNextTriggerTime = 0f;
        private int   _rsPhase            = -1;
        private float _rsPhaseEnd         = 0f;
        private float _rsRecoverEnd       = 0f;
        private float _rsRecoverStart     = 0f;
        private float _rsStrikeLevel      = 0.40f;

        private void ApplyRestrikeFlicker()
        {
            if (!Plugin.RestrikeEnabled.Value) return;
            if (_rfCoreRefs.Count == 0) return;
            float now = Time.unscaledTime;

            if (_rsPhase < 0)
            {
                if (_rsNextTriggerTime == 0f)
                {
                    _rsNextTriggerTime = now + UnityEngine.Random.Range(
                        Plugin.RestrikeIntervalMin.Value, Plugin.RestrikeIntervalMax.Value);
                    return;
                }
                if (now >= _rsNextTriggerTime)
                {
                    _rsPhase        = 0;
                    _rsPhaseEnd     = now + UnityEngine.Random.Range(0.15f, 0.40f);
                    _rsStrikeLevel  = UnityEngine.Random.Range(0.35f, 0.50f);
                    ApplyIntensityMultiplier(0f);
                }
                return;
            }

            // Phase 0 → 1: weak strike
            if (_rsPhase == 0)
            {
                ApplyIntensityMultiplier(0f);
                if (now >= _rsPhaseEnd)
                {
                    _rsPhase    = 1;
                    _rsPhaseEnd = now + UnityEngine.Random.Range(0.05f, 0.10f);
                }
                return;
            }
            // Phase 1 → 2: back to off
            if (_rsPhase == 1)
            {
                ApplyIntensityMultiplier(_rsStrikeLevel);
                if (now >= _rsPhaseEnd)
                {
                    _rsPhase    = 2;
                    _rsPhaseEnd = now + UnityEngine.Random.Range(0.10f, 0.25f);
                }
                return;
            }
            // Phase 2 → 3: recovery with decaying noise tail
            if (_rsPhase == 2)
            {
                ApplyIntensityMultiplier(0f);
                if (now >= _rsPhaseEnd)
                {
                    _rsPhase        = 3;
                    _rsRecoverStart = now;
                    _rsRecoverEnd   = now + UnityEngine.Random.Range(0.35f, 0.65f);
                }
                return;
            }
            // Phase 3: full restore + decaying high-freq noise
            if (_rsPhase == 3)
            {
                if (now >= _rsRecoverEnd)
                {
                    _rsPhase           = -1;
                    _rsNextTriggerTime = now + UnityEngine.Random.Range(
                        Plugin.RestrikeIntervalMin.Value, Plugin.RestrikeIntervalMax.Value);
                    return; // hand control back to Perlin
                }
                float p = (now - _rsRecoverStart) / (_rsRecoverEnd - _rsRecoverStart);
                // Noise amplitude decays from 0.35 → 0 over the recovery window.
                float amp = (1f - p) * 0.35f;
                float n = Mathf.PerlinNoise(now * 28f, 17.3f);
                float mult = Mathf.Max(0.20f, 1.0f + (n * 2f - 1f) * amp);
                ApplyIntensityMultiplier(mult);
            }
        }

        public void LateUpdate()
        {
            // Per-frame state pass; OnPreCull repeats it right before render.
            EnforceRFState();

            // Re-suppress the wielded gun's vanilla siblings — GTFO can
            // un-hide them mid-frame and the dot would bleed through.
            var cw = Patch_ItemWield.CurrentWieldedItem;
            if (cw != null)
            {
                try
                {
                    if (cw.TryCast<global::Gear.BulletWeapon>() != null
                        && cw.m_lt != null
                        && cw.m_lt.transform.Find(LightSandwich.L1) != null)
                    {
                        LightSandwich.SuppressRangedSiblings(cw.m_lt, cw);
                    }
                }
                catch { }
            }
        }

        public void Update()
        {
            // F toggle — mutates LFS only; Enforce picks it up on the next pass.
            try
            {
                if (Input.GetKeyDown(KeyCode.F))
                    LastFlashlightState = !LastFlashlightState;
            }
            catch { }

            // Swap-input instant-OFF. Pressing 1-6 or scrolling the wheel
            // anchors the dark transition to the input frame (not to the end
            // of GTFO's swap animation when OnUnWield/OnWield would fire). Arms
            // _swapInProgress so the next few Enforce passes don't race
            // and re-enable the outgoing source while m_isWielded still
            // reports "true" on the old item.
            try
            {
                bool swapInput = false;
                for (int k = (int)KeyCode.Alpha1; k <= (int)KeyCode.Alpha6; k++)
                {
                    if (Input.GetKeyDown((KeyCode)k)) { swapInput = true; break; }
                }
                if (!swapInput && Mathf.Abs(Input.mouseScrollDelta.y) > 0.01f)
                    swapInput = true;
                if (swapInput)
                {
                    // Instant darkness on whichever source was lit.
                    if (_helmetSynthGO != null && _helmetSynthGO.activeSelf)
                        _helmetSynthGO.SetActive(false);
                    var cw = Patch_ItemWield.CurrentWieldedItem;
                    if (cw != null)
                    {
                        try
                        {
                            var lt = cw.m_lt;
                            if (lt != null && lt.transform.Find(LightSandwich.L1) != null)
                                LightSandwich.SyncEnabled(lt.transform, false);
                        }
                        catch { }
                    }
                    BeginSwap();
                }
            }
            catch { }

            // Perlin is the always-on baseline; the others override intensity
            // during their time windows. Order matters — the last active write
            // this frame wins.
            ApplyFlicker();
            ApplyRareFlicker();
            ApplyHorrorFlicker();
            ApplyBrownoutFlicker();
            ApplyStutterFlicker();
            ApplyRestrikeFlicker();

            // Both are no-ops once satisfied.
            if (_helmetSynthGO == null) _helmetSynthBuilt = false;
            if (!_helmetSynthBuilt) EnsureHelmetSynth();
            TryAttachPreCullEnforcer();

            // Drift the dust motes. Safe to call before pool is built (no-op).
            try { DustEffect.Tick(Time.deltaTime); }
            catch (System.Exception ex)
            {
                // Don't spam — first 3 throws only.
                if (DustEffect._tickThrowLogBudget > 0)
                {
                    DustEffect._tickThrowLogBudget--;
                    Plugin.Logger.LogWarning("[RF] DustEffect.Tick threw: "
                        + ex.GetType().Name + " " + ex.Message);
                }
            }

            // Throttled scanner.
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + ScanIntervalSeconds;
            if (!Plugin.Enabled.Value) return;
            DoScan();
        }

        // Periodic recovery sweep over patch-tracked items. No scene-wide
        // enumeration: we iterate the small list of items we've observed via
        // OnWield. The lazy-build patches (Patch_LightType / set_enabled /
        // set_intensity) handle the common case; this is a safety net for
        // races where the sandwich was built before the owner was reachable.
        private void DoScan()
        {
            try
            {
                if (_knownItems.Count == 0) return;

                for (int idx = _knownItems.Count - 1; idx >= 0; idx--)
                {
                    ItemEquippable? it;
                    try { it = _knownItems[idx]; } catch { _knownItems.RemoveAt(idx); continue; }
                    if (it == null) { _knownItems.RemoveAt(idx); continue; }
                    GameObject? go;
                    try { go = it.gameObject; } catch { _knownItems.RemoveAt(idx); continue; }
                    if (go == null) { _knownItems.RemoveAt(idx); continue; }

                    bool isPickable = it.TryCast<ConsumableFlashlight>() != null;
                    bool isRanged   = it.TryCast<global::Gear.BulletWeapon>() != null;
                    if (!isPickable && !isRanged) continue;

                    var l = it.m_lt;
                    if (l == null) continue;
                    if (l.type != LightType.Spot && l.type != LightType.Point) continue;

                    if (l.transform.Find(LightSandwich.L1) == null)
                        LightSandwich.ApplySandwich(l, it);
                    else if (isPickable)
                        // Re-stamp pickable values if the sandwich was built
                        // with Ranged classification (lazy patch before reparent).
                        LightSandwich.EnsurePickableSettings(l);
                    else if (isRanged)
                    {
                        // Mirror image — recover from pickable-classified ranged.
                        //
                        // GTFO's m_lt is SHARED across all weapons. If we
                        // EnsureRangedSettings(l, it) for non-wielded items here,
                        // we'd re-stamp the shared Light's RF children with the
                        // non-wielded weapon's preset — overwriting the correct
                        // preset the wielded weapon just set. Only re-stamp for
                        // the currently wielded ranged item (the wield path
                        // already handles the swap moment via OnWieldedChanged).
                        var cw = Patch_ItemWield.CurrentWieldedItem;
                        if (cw == it)
                            LightSandwich.EnsureRangedSettings(l, it, forceResolve: false);
                    }
                }
            }
            catch { }
        }
    }

    // Resolves and caches the animated bone Transform on each weapon model
    // that the shared m_lt should follow. Without this, m_lt stays parented
    // to the camera holder and the beam points straight forward from the
    // view, ignoring reload / swap / inspect animations on the gun model.
    //
    // GTFO weapons have no consistent naming convention for the relevant
    // bone — we probe descendants of the ItemEquippable GameObject for any
    // child whose name contains one of a small keyword list, in priority
    // order. First match wins; ties resolve to the highest-priority keyword.
    // Result is cached per ItemEquippable instance ID; ItemEquippable
    // instances persist across scene loads (see CLAUDE.md item #29), so the
    // cache survives RebuildAll.
    //
    // First resolution per weapon dumps the full candidate list to the log
    // as `[RF] bone-resolve owner=NAME chosen=X candidates=[...]` so we
    // can verify the chosen bone makes sense across different gear.
    internal static class WeaponBoneTracker
    {
        // owner instance ID → resolved bone (or null if not found).
        private static readonly Dictionary<int, Transform?> _cache
            = new Dictionary<int, Transform?>();
        private static readonly HashSet<int> _logged = new HashSet<int>();

        // Substrings searched against descendant GameObject names, in
        // priority order. Lower index wins. Case-insensitive.
        //   "Flashlight" — the parent of the original GTFO Light child
        //                  (e.g. `Flashlight_A(Clone)`); already used by
        //                  RangedPresets.ResolveByOwner — should be the
        //                  best match for a gun-mounted beam direction.
        //   "MuzzleEffect" / "Muzzle" — the firing-point bone, always
        //                  animated, points forward.
        //   "GunMount" / "Barrel" — rare fallbacks.
        private static readonly string[] BoneKeywords =
        {
            "Flashlight",
            "MuzzleEffect",
            "Muzzle",
            "GunMount",
            "Barrel",
        };

        internal static void ClearAll()
        {
            _cache.Clear();
            _logged.Clear();
        }

        internal static Transform? Resolve(ItemEquippable? owner)
        {
            if (owner == null) return null;
            int oid;
            try { oid = owner.GetInstanceID(); } catch { return null; }

            if (_cache.TryGetValue(oid, out var cached))
            {
                // Unity's overloaded == catches destroyed Transforms.
                if (cached == null) return null;
                try { _ = cached.position; return cached; }
                catch { _cache.Remove(oid); }
            }

            Transform? root = null;
            string ownerName = "?";
            try
            {
                if (owner.gameObject != null)
                {
                    root = owner.gameObject.transform;
                    ownerName = owner.gameObject.name;
                }
            }
            catch { }
            if (root == null) { _cache[oid] = null; return null; }

            Transform? best = null;
            int bestPri = int.MaxValue;
            var matches = new List<string>(8);
            ScanDescendants(root, 0, ref best, ref bestPri, matches);

            if (_logged.Add(oid))
            {
                try
                {
                    string chosen = best != null ? best.gameObject.name : "(none)";
                    string list = matches.Count == 0 ? "" : string.Join(",", matches.ToArray());
                    Plugin.Logger.LogInfo("[RF] bone-resolve owner=" + ownerName
                        + " chosen=" + chosen + " candidates=[" + list + "]");
                }
                catch { }
            }

            _cache[oid] = best;
            return best;
        }

        private static void ScanDescendants(Transform t, int depth,
            ref Transform? best, ref int bestPri, List<string> matches)
        {
            // Depth cap protects against pathological hierarchies and
            // anchors search cost at O(small).
            const int MaxDepth = 8;
            if (depth > MaxDepth) return;
            int cc;
            try { cc = t.childCount; } catch { return; }
            for (int i = 0; i < cc; i++)
            {
                Transform? c;
                try { c = t.GetChild(i); } catch { continue; }
                if (c == null) continue;
                string nm;
                try { nm = c.gameObject.name; } catch { continue; }
                if (!string.IsNullOrEmpty(nm) && !LightSandwich.IsRFChild(nm))
                {
                    for (int k = 0; k < BoneKeywords.Length; k++)
                    {
                        if (nm.IndexOf(BoneKeywords[k], StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            matches.Add(nm + "[" + BoneKeywords[k] + "]");
                            if (k < bestPri) { best = c; bestPri = k; }
                            break;
                        }
                    }
                }
                ScanDescendants(c, depth + 1, ref best, ref bestPri, matches);
            }
        }
    }

    // Attached to FPSLookCamera so OnPreCull fires after LateUpdate but before
    // rendering, catching any state change that lands late in the frame.
    public class PreCullEnforcer : MonoBehaviour
    {
        public PreCullEnforcer(IntPtr ptr) : base(ptr) { }
        public void OnPreCull()
        {
            try { LightUpdater.Instance?.EnforceRFState(); } catch { }
        }
    }
}
