using System.Diagnostics.CodeAnalysis;
using Assets.Scripts;
using Assets.Scripts.Flight.GameView.Cameras;
using HarmonyLib;
using UnityEngine;

namespace Patches
{
    /// <summary>
    ///     Harmony postfix patch for the private <see cref="CameraController.AdjustCameraClipPlanes" />.
    ///     The stock method derives <c>nearClipPlane</c> from the camera's distance to its target, then
    ///     sets <c>farClipPlane = 10000 * Mathf.Min(nearClipPlane, 1f)</c>. In any
    ///     <see cref="FirstPersonCameraController" /> (astronaut eyes or a physical Camera part), that
    ///     distance is near zero, so <c>nearClipPlane</c> collapses to its 0.1 floor and
    ///     <c>farClipPlane</c> collapses to as little as 1000 m, visibly reducing terrain scatter (Juno
    ///     Parallax) and shadow draw distance. This postfix forces a sane floor for <c>farClipPlane</c>
    ///     to make it stay at a normal full-range value in first-person view.
    /// </summary>
    [HarmonyPatch(typeof(CameraController), "AdjustCameraClipPlanes")] // private, so nameof won't work
    internal static class CameraControllerClipPlanePatch
    {
        private const float MinFarClipPlane = 10000f;

        private static readonly int CloudCameraSplitId = Shader.PropertyToID("_CloudCameraSplit");
        private static readonly int CloudCameraSplitFeatherId = Shader.PropertyToID("_CloudCameraSplitFeather");
        private static readonly int CloudCameraSplitModeId = Shader.PropertyToID("_CloudCameraSplitMode");

        // Cahced reflected accessors for the private near/far camera fields on CameraController.
        private static readonly AccessTools.FieldRef<CameraController, Camera> NearCameraRef =
            AccessTools.FieldRefAccess<CameraController, Camera>("_nearCamera");

        private static readonly AccessTools.FieldRef<CameraController, Camera> FarCameraRef =
            AccessTools.FieldRefAccess<CameraController, Camera>("_farCamera");

        // ReSharper disable once UnusedMember.Local
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        [HarmonyPostfix]
        private static void Postfix(CameraController __instance)
        {
            if (__instance is not FirstPersonCameraController) return;
            if (!ModSettings.Instance.FixFirstPersonDrawDistance.Value) return;

            var nearCamera = NearCameraRef(__instance);
            var farCamera = FarCameraRef(__instance);
            if (nearCamera == null || farCamera == null) return;

            // Leave stock's small nearClipPlane value so close-up geometry still renders correctly.
            if (nearCamera.farClipPlane >= MinFarClipPlane) return;

            nearCamera.farClipPlane = MinFarClipPlane;

            // Recompute the far-camera split and cloud shader globals exactly as the stock
            // method would have, using the corrected farClipPlane, so the two-camera compositing
            // (and ground clouds) stay in sync with the restored draw distance.
            // All the magic numbers are from the decompiled code.
            var groundClouds = Game.Instance.Settings.Game.Flight.GroundClouds.Value;
            var overlapMeters = CameraController.CloudCameraOverlapOverrideMeters >= 0f
                ? CameraController.CloudCameraOverlapOverrideMeters
                : (groundClouds ? 100f : 5f);
            farCamera.nearClipPlane = nearCamera.farClipPlane - overlapMeters;

            var splitMode = CameraController.CloudCameraSplitModeOverride >= 0
                ? CameraController.CloudCameraSplitModeOverride
                : (groundClouds ? 2 : 0);

            Shader.SetGlobalFloat(CloudCameraSplitId, 0.5f * (nearCamera.farClipPlane + farCamera.nearClipPlane));
            Shader.SetGlobalFloat(CloudCameraSplitFeatherId,
                0.5f * (nearCamera.farClipPlane - farCamera.nearClipPlane));
            Shader.SetGlobalFloat(CloudCameraSplitModeId, splitMode);
        }
    }
}