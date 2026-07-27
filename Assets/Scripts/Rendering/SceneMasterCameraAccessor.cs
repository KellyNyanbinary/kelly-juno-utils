using System.Reflection;
using Assets.Scripts;
using Assets.Scripts.Cameras;
using HarmonyLib;
using UnityEngine;

namespace Rendering
{
    /// <summary>
    ///     Reflected access to the private members of <see cref="SceneMasterCameraScript" /> that the
    ///     SMAA integration needs to drive.
    ///     <para>
    ///         Two stock behaviors have to be worked around. First, the composited scene render
    ///         texture (and the master camera itself) is only enabled when the resolution scale is not
    ///         1, a post-process antialiasing mode is selected, or legacy re-entry is on; otherwise
    ///         the scene cameras render straight to the back buffer, and no image effect on the master
    ///         camera ever runs. Second, the stock FXAA/DLAA image effect must be switched off so SMAA
    ///         does not run on top of an already-filtered image.
    ///     </para>
    /// </summary>
    internal static class SceneMasterCameraAccessor
    {
        private static readonly FieldInfo SceneTextureField =
            AccessTools.Field(typeof(SceneMasterCameraScript), "_renderTextureScene");

        private static readonly FieldInfo AntiAliasingEffectField =
            AccessTools.Field(typeof(SceneMasterCameraScript), "_antiAliasingImageEffect");

        private static readonly MethodInfo ApplyAntiAliasingSettingsMethod =
            AccessTools.Method(typeof(SceneMasterCameraScript), "ApplyAntiAliasingSettings");

        private static readonly System.Type RenderTextureDataType =
            AccessTools.Inner(typeof(SceneMasterCameraScript), "RenderTextureData");

        private static readonly MethodInfo SetEnabledMethod =
            RenderTextureDataType == null ? null : AccessTools.Method(RenderTextureDataType, "SetEnabled");

        private static readonly MethodInfo GetEnabledMethod =
            RenderTextureDataType == null ? null : AccessTools.PropertyGetter(RenderTextureDataType, "Enabled");

        /// <summary>
        ///     Gets a value indicating whether every reflected member was resolved. If the game is
        ///     updated and a member is renamed this turns false, and the SMAA integration disables
        ///     itself rather than throwing every frame.
        /// </summary>
        public static bool IsAvailable =>
            SceneTextureField != null &&
            AntiAliasingEffectField != null &&
            ApplyAntiAliasingSettingsMethod != null &&
            SetEnabledMethod != null &&
            GetEnabledMethod != null;

        /// <summary>
        ///     Forces the composited scene render texture and the master camera on, so that image
        ///     effects attached to the master camera are actually executed.
        /// </summary>
        public static void ForceSceneRenderTextureEnabled(SceneMasterCameraScript master)
        {
            if (master == null || !IsAvailable) return;

            var sceneTexture = SceneTextureField.GetValue(master);
            if (sceneTexture == null) return;

            // SetEnabled(true) flags the texture as dirty and forces a reallocation, so only call it
            // when the state actually needs to change.
            if (!(bool)GetEnabledMethod.Invoke(sceneTexture, null))
            {
                SetEnabledMethod.Invoke(sceneTexture, new object[] { true });
            }

            master.enabled = true;
            if (master.Camera != null) master.Camera.enabled = true;

            // Not an override of the player's MSAA choice. Scene MSAA comes from the render
            // texture's descriptor (UpdateTextureAntiAliasing), which is untouched here, so the
            // selected sample count still applies. QualitySettings.antiAliasing only governs
            // back-buffer MSAA, and the stock method just raised it to 2/4/8 on the assumption
            // that the scene renders straight to the back buffer. Forcing the render texture on
            // above invalidates that, leaving a stale multisampled back buffer that nothing but a
            // fullscreen blit draws into. Stock itself clears this to 1 whenever the texture path
            // is active, so this restores the state it would have produced.
            QualitySettings.antiAliasing = 1;
        }

        /// <summary>
        ///     Enables or disables the stock FXAA/DLAA image effect on the master camera.
        /// </summary>
        public static void SetStockPostAntiAliasingEnabled(SceneMasterCameraScript master, bool enabled)
        {
            if (master == null || !IsAvailable) return;

            if (AntiAliasingEffectField.GetValue(master) is Behaviour effect && effect != null)
            {
                effect.enabled = enabled;
            }
        }

        /// <summary>
        ///     Re-applies the stock antialiasing settings from scratch. This re-evaluates both the
        ///     stock FXAA/DLAA effect state and the render texture path (the stock method calls
        ///     <c>UpdateRenderMethod</c> itself), so toggling SMAA off at runtime restores exactly the
        ///     configuration the player's own antialiasing setting asks for.
        /// </summary>
        public static void RefreshAntiAliasing(SceneMasterCameraScript master)
        {
            if (master == null || !IsAvailable || Game.Instance == null) return;

            var setting = Game.Instance.Settings.Quality.Display.AntiAliasing.Value;
            ApplyAntiAliasingSettingsMethod.Invoke(master, new object[] { setting });
        }
    }
}
