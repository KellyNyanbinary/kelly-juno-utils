using System;
using System.Diagnostics.CodeAnalysis;
using Assets.Scripts;
using Assets.Scripts.Flight.GameView;
using Assets.Scripts.Flight.Sim;
using HarmonyLib;
using UnityEngine;

namespace Patches
{
    /// <summary>
    ///     Harmony postfix patch for <see cref="GameViewScript.IsRecenterRequired" />.
    ///     The stock method forces a floating-origin recenter only once the craft's
    ///     <c>FramePosition.sqrMagnitude</c> exceeds <c>2.5e7</c> (~5000 m from origin).
    ///     At that distance, 32-bit float precision loss is already large enough to cause
    ///     visible subpixel jitter on craft parts and MFDs. This postfix promotes the result
    ///     to <c>true</c> whenever the craft drifts past a much tighter, user-configurable
    ///     distance (default 100 m). It never suppresses an existing <c>true</c>, so all stock
    ///     triggers (warp, surface-lock transitions, velocity threshold, and the stock 5000 m
    ///     fallback) remain intact. Recentering is cheap and every downstream consumer of
    ///     <c>IGameViewObject.OnReferenceFrameRecentered</c> already handles arbitrary
    ///     position/velocity deltas, so raising the recenter frequency is safe.
    /// </summary>
    [HarmonyPatch(typeof(GameViewScript), "IsRecenterRequired")] // private, so nameof won't work
    internal static class GameViewScriptRecenterPatch
    {
        // Hard bounds on the setting value in case the settings XML is edited by hand.
        // The lower bound avoids thrashing near the origin; the upper bound matches the
        // stock threshold, so this patch can never make recentering less frequent.
        private const float MinDistanceMeters = 100f;
        private const float MaxDistanceMeters = 5000f;

        // Cached reflected accessor for the private CraftNode field on GameViewScript.
        // AccessTools.FieldRefAccess throws at construction if the field is missing, so
        // any incompatibility with a future game version fails loudly at mod loading rather
        // than silently no-op'ing on every frame.
        private static readonly AccessTools.FieldRef<GameViewScript, CraftNode> CraftNodeRef =
            AccessTools.FieldRefAccess<GameViewScript, CraftNode>("_craftNode");

        // ReSharper disable once UnusedMember.Local
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        [HarmonyPostfix]
        private static void Postfix(GameViewScript __instance, ref bool __result)
        {
            if (__result)
                return;

            var referenceFrame = __instance.ReferenceFrame;
            if (referenceFrame is not { RecenterEnabled: true })
                return;

            var craftNode = CraftNodeRef(__instance);
            if (craftNode == null)
                return;

            var distance = Mathf.Clamp(
                ModSettings.Instance.RecenterDistance.Value,
                MinDistanceMeters,
                MaxDistanceMeters);

            var distanceSqr = distance * distance;

            if (craftNode.FramePosition.sqrMagnitude > distanceSqr)
                __result = true;
        }
    }
}