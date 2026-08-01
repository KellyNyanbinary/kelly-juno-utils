using System.Diagnostics.CodeAnalysis;
using Assets.Scripts.Cameras;
using HarmonyLib;
using JetBrains.Annotations;
using Rendering;

namespace Patches
{
    /// <summary>
    /// Wires SMAA into <see cref="SceneMasterCameraScript" />, the camera that composites the
    /// near, far, and scaled-space cameras and blits the result to the screen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three hooks are needed. <c>Awake</c> attaches the effect. <c>UpdateRenderMethod</c>
    /// normally disables both the shared scene render texture and the master camera unless the
    /// resolution scale is not 1 or a post-process antialiasing mode is selected, which would
    /// stop any image effect on that camera from running at all, so it is forced back on.
    /// <c>ApplyAntiAliasingSettings</c> re-enables the stock FXAA/DLAA effect whenever the
    /// player changes a display setting, so it is suppressed again to avoid filtering an
    /// already-filtered image.
    /// </para>
    /// <para>
    /// MSAA is deliberately left alone: it is resolved through the scene render texture's
    /// descriptor, so selecting MSAA in the stock settings and enabling SMAA here yields both.
    /// </para>
    /// </remarks>
    [UsedImplicitly]
    [HarmonyPatch(typeof(SceneMasterCameraScript))]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    [SuppressMessage("ReSharper", "UnusedMember.Local")]
    internal static class SceneMasterCameraSmaaPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch("Awake")]
        private static void AwakePostfix(SceneMasterCameraScript __instance)
        {
            if (!SceneMasterCameraAccessor.IsAvailable) return;

            // AddComponent appends, which puts SmaaEffect.OnRenderImage after the master camera's own
            // blit in the image effect chain. That ordering is required: the stock blit ignores its
            // source argument and writes the shared scene texture, discarding anything produced by an
            // earlier effect.
            if (__instance.GetComponent<SmaaEffect>() == null)
            {
                __instance.gameObject.AddComponent<SmaaEffect>();
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("UpdateRenderMethod")]
        private static void UpdateRenderMethodPostfix(SceneMasterCameraScript __instance)
        {
            if (!SmaaEffect.IsRequested) return;
            SceneMasterCameraAccessor.ForceSceneRenderTextureEnabled(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch("ApplyAntiAliasingSettings")]
        private static void ApplyAntiAliasingSettingsPostfix(SceneMasterCameraScript __instance)
        {
            if (!SmaaEffect.IsRequested) return;
            SceneMasterCameraAccessor.SetStockPostAntiAliasingEnabled(__instance, false);
        }
    }
}