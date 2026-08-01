using System;
using Assets.Scripts;
using Assets.Scripts.Cameras;
using ModApi.Settings.Core.Events;
using UnityEngine;

namespace Rendering
{
    /// <summary>
    /// SMAA 1x, applied to the fully composited scene image on the master camera.
    /// </summary>
    /// <remarks>
    /// This is the same injection point the stock FXAA/DLAA effect uses, and it is the only
    /// correct one: the near, far, and scaled-space cameras all render into a single shared
    /// render texture which <see cref="SceneMasterCameraScript" /> then blits to the screen.
    /// Because that blit ignores its <c>source</c> argument and writes the shared texture
    /// directly, this component must run <em>after</em> it in the component order, which
    /// <c>AddComponent</c> guarantees by appending.
    /// </remarks>
    [DisallowMultipleComponent]
    public class SmaaEffect : MonoBehaviour
    {
        private static readonly int BlendTexId = Shader.PropertyToID("_SmaaBlendTex");
        private static readonly int AreaTexId = Shader.PropertyToID("_SmaaAreaTex");
        private static readonly int SearchTexId = Shader.PropertyToID("_SmaaSearchTex");
        private static readonly int RtMetricsId = Shader.PropertyToID("_SmaaRtMetrics");

        private static readonly string[] PresetKeywords =
        {
            "SMAA_PRESET_LOW",
            "SMAA_PRESET_MEDIUM",
            "SMAA_PRESET_HIGH",
            "SMAA_PRESET_ULTRA"
        };

        private Material _material;
        private SceneMasterCameraScript _masterCamera;
        private string _activePresetKeyword;
        private EventHandler<SettingChangedEventArgs<bool>> _enabledChangedHandler;

        /// <summary>
        /// Gets this mod's settings, or null if the category is not registered. The game only
        /// publishes a category once <c>InitializeSettings</c> has run, so a non-null result is
        /// always fully initialized.
        /// </summary>
        private static ModSettings Settings => Game.Instance is null ? null : ModSettings.Instance;

        /// <summary>
        /// Gets a value indicating whether SMAA should run this frame.
        /// </summary>
        public static bool IsRequested
        {
            get
            {
                var settings = Settings;
                return settings is not null && settings.EnableSmaa.Value;
            }
        }

        private void Awake()
        {
            _masterCamera = GetComponent<SceneMasterCameraScript>();
        }

        private void OnEnable()
        {
            // Ordering is guaranteed: ModManager.RegisterModSettings runs while mods are loaded at
            // startup, long before any scene containing SceneMasterCameraScript exists, and the
            // stock Awake this component is attached from dereferences Game.Instance.Settings
            // itself. If the category is somehow still missing, the live toggle would silently stop
            // working, so say so rather than failing quietly.
            var settings = Settings;
            if (settings is null)
            {
                Debug.LogError(
                    "[KellyUtils] Mod settings were unavailable when SMAA was attached; " +
                    "toggling SMAA will not apply until a display setting is changed.");
                return;
            }

            _enabledChangedHandler = OnEnableSmaaChanged;
            settings.EnableSmaa.Changed += _enabledChangedHandler;
        }

        private void OnDisable()
        {
            if (_enabledChangedHandler is null) return;

            var settings = Settings;
            if (settings is not null)
            {
                settings.EnableSmaa.Changed -= _enabledChangedHandler;
            }

            _enabledChangedHandler = null;
        }

        private void OnDestroy()
        {
            if (_material == null) return;
            Destroy(_material);
            _material = null;
        }

        private void OnEnableSmaaChanged(object sender, SettingChangedEventArgs<bool> e)
        {
            // Toggling SMAA changes whether the scene render texture path is required, and whether the
            // stock FXAA/DLAA effect should be suppressed, so re-apply the antialiasing settings
            // immediately instead of waiting for the player to change another setting.
            SceneMasterCameraAccessor.RefreshAntiAliasing(_masterCamera);
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (!IsRequested || !TryPrepareMaterial())
            {
                Graphics.Blit(source, destination);
                return;
            }

            var width = source.width;
            var height = source.height;

            _material.SetVector(RtMetricsId, new Vector4(1f / width, 1f / height, width, height));
            _material.SetTexture(AreaTexId, SmaaResources.AreaTexture);
            _material.SetTexture(SearchTexId, SmaaResources.SearchTexture);

            RenderTexture edges = null;
            RenderTexture blend = null;

            try
            {
                // Linear read/write: edges and blend weights are data, not color.
                edges = RenderTexture.GetTemporary(
                    width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                blend = RenderTexture.GetTemporary(
                    width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);

                edges.filterMode = FilterMode.Bilinear;
                blend.filterMode = FilterMode.Bilinear;

                // Pass 0 discards non-edge pixels, so the target has to start cleared, or it will
                // sample whatever the pooled temporary render texture happened to contain.
                Graphics.SetRenderTarget(edges);
                GL.Clear(false, true, Color.clear);
                Graphics.Blit(source, edges, _material, 0);

                // Pass 1: blending weights from the edge texture plus the two lookup tables.
                Graphics.Blit(edges, blend, _material, 1);

                // Pass 2: blend the original color using those weights.
                _material.SetTexture(BlendTexId, blend);
                Graphics.Blit(source, destination, _material, 2);
            }
            finally
            {
                if (edges != null) RenderTexture.ReleaseTemporary(edges);
                if (blend != null) RenderTexture.ReleaseTemporary(blend);
            }
        }

        private bool TryPrepareMaterial()
        {
            if (!SmaaResources.TryLoad()) return false;

            if (_material == null)
            {
                _material = new Material(SmaaResources.Shader) { hideFlags = HideFlags.HideAndDontSave };
                _activePresetKeyword = null;
            }

            ApplyQualityPreset();
            return true;
        }

        private void ApplyQualityPreset()
        {
            var settings = Settings;
            if (settings is null) return;

            var index = (int)settings.SmaaQuality.Value;
            if (index < 0 || index >= PresetKeywords.Length) index = (int)SmaaQualityPreset.High;

            var keyword = PresetKeywords[index];
            if (keyword == _activePresetKeyword) return;

            foreach (var t in PresetKeywords)
            {
                _material.DisableKeyword(t);
            }

            _material.EnableKeyword(keyword);
            _activePresetKeyword = keyword;
        }
    }
}
