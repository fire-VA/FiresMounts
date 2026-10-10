using HarmonyLib;
using UnityEngine;

namespace FiresMounts
{
    /// <summary>
    /// Once a Fires mount is SADDLED, the WHOLE animal offers "mount" instead of "pet" — no more hunting for the
    /// tiny saddle collider.
    ///
    /// Why it takes a patch: the interact ray (Player.FindHoverObject) resolves the first collider whose own
    /// GameObject has a Hoverable, else the collider's attachedRigidbody GO. Every horse collider (bones + root
    /// capsule) shares the ROOT rigidbody, so any body hit resolves to the root — where Character is the Hoverable
    /// (its GetHoverText delegates to Tameable) and Tameable is the Interactable. The Sadle lives on a child node,
    /// so it's only ever hit by aiming at its small sphere. We therefore retarget Tameable itself: hover shows the
    /// ride prompt, and Use delegates to the Sadle mount RPC.
    ///
    /// Scoped to our mounts (the horse; from 0.1.108 the flyers and the Mini Fader too, Fire 10-08: "e to mount") AND only while
    /// saddled — an unsaddled Fires mount (E = follow / stay), a vanilla Lox, and any other mod's Tameable keep their normal
    /// pet/tame behaviour. Alt+Use (rename) is left vanilla, and the saddle's own sphere still removes the saddle via Alt+Use.
    /// </summary>
    internal static class FiresMountHover
    {
        internal static bool IsOurSaddledMount(Tameable t)
        {
            if (t == null || t.m_saddle == null) return false;
            if (t.GetComponent<FiresMountController>() == null && t.GetComponent<FiresFlyerFlight>() == null
                && t.GetComponent<FiresFaderRide>() == null) return false;   // our mounts only
            var nv = t.GetComponent<ZNetView>();
            return nv != null && nv.IsValid() && nv.GetZDO().GetBool(ZDOVars.s_haveSaddleHash);
        }
    }

    /// <summary>Swap the "[Use] Pet" hint for the ride prompt on a saddled Fires mount (name/status/rename stay).</summary>
    [HarmonyPatch(typeof(Tameable), nameof(Tameable.GetHoverText))]
    internal static class FiresMountHoverTextPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Tameable __instance, ref string __result)
        {
            if (string.IsNullOrEmpty(__result) || !FiresMountHover.IsOurSaddledMount(__instance)) return;
            // Replace the whole localized pet-hint fragment (markup + key + word), NOT the bare word — a renamed
            // horse could otherwise contain the pet word and get corrupted. Localize both the same way vanilla did.
            string petHint = Localization.instance.Localize("\n[<color=yellow><b>$KEY_Use</b></color>] $hud_pet");
            string rideHint = Localization.instance.Localize("\n[<color=yellow><b>$KEY_Use</b></color>] $hud_ride");
            if (__result.Contains(petHint)) __result = __result.Replace(petHint, rideHint);
        }
    }

    /// <summary>Use on a saddled Fires mount mounts it (delegates to the Sadle) instead of petting.</summary>
    [HarmonyPatch(typeof(Tameable), nameof(Tameable.Interact))]
    internal static class FiresMountInteractPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Tameable __instance, Humanoid user, bool hold, bool alt, ref bool __result)
        {
            if (hold || alt) return true;                              // hold-repeat + Alt(rename) stay vanilla
            if (!FiresMountHover.IsOurSaddledMount(__instance)) return true;
            var sadle = __instance.m_saddle;
            if (sadle == null) return true;
            __result = sadle.Interact(user, hold, alt);                // RequestControl RPC = mount
            return false;                                             // skip the pet action
        }
    }
}
