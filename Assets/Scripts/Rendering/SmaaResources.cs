using System;
using Assets.Scripts;
using UnityEngine;

namespace Rendering
{
    /// <summary>
    ///     Loads and caches the SMAA shader and its two precomputed lookup tables from the mod's
    ///     asset bundle. The lookup tables ship as raw <c>.bytes</c> blobs rather than as imported
    ///     texture assets so that their format, filtering, and color space are set explicitly here
    ///     instead of depending on Unity import settings, which are easy to get silently wrong.
    ///     <para>
    ///         The raw bytes are uploaded in their original row order; this was verified in-game and
    ///         matches how Unity's own Post Processing Stack ships the same tables.
    ///     </para>
    /// </summary>
    internal static class SmaaResources
    {
        private const int AreaTexWidth = 160;
        private const int AreaTexHeight = 560;
        private const int SearchTexWidth = 64;
        private const int SearchTexHeight = 16;

        private static bool _shaderLoadAttempted;
        private static bool _lookupTablesAttempted;

        public static Shader Shader { get; private set; }

        public static Texture2D AreaTexture { get; private set; }

        public static Texture2D SearchTexture { get; private set; }

        private static bool IsAvailable => Shader != null && AreaTexture != null && SearchTexture != null;

        /// <summary>
        ///     Ensures the shader and lookup tables are loaded. Failures are latched so a missing or
        ///     malformed asset logs once rather than once per frame.
        /// </summary>
        public static bool TryLoad()
        {
            return TryLoadShader() && TryBuildLookupTables();
        }

        private static bool TryLoadShader()
        {
            if (_shaderLoadAttempted) return Shader != null;
            _shaderLoadAttempted = true;

            try
            {
                Shader = Mod.Instance.ResourceLoader.LoadAsset<Shader>("Smaa");
                if (Shader == null)
                {
                    Debug.LogError("[KellyUtils] SMAA shader asset 'Smaa' could not be loaded.");
                    return false;
                }

                if (!Shader.isSupported)
                {
                    Debug.LogError("[KellyUtils] SMAA shader is not supported on this platform.");
                    Shader = null;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError("[KellyUtils] Failed to load the SMAA shader: " + ex);
                Shader = null;
                return false;
            }
        }

        private static bool TryBuildLookupTables()
        {
            if (_lookupTablesAttempted) return IsAvailable;
            _lookupTablesAttempted = true;

            try
            {
                AreaTexture = CreateLookupTexture(
                    "AreaTex", AreaTexWidth, AreaTexHeight, TextureFormat.RG16, FilterMode.Bilinear);

                // SMAA samples the search table with point filtering; bilinear here corrupts the
                // encoded search distances.
                SearchTexture = CreateLookupTexture(
                    "SearchTex", SearchTexWidth, SearchTexHeight, TextureFormat.R8, FilterMode.Point);

                return IsAvailable;
            }
            catch (Exception ex)
            {
                Debug.LogError("[KellyUtils] Failed to build the SMAA lookup tables: " + ex);
                ReleaseLookupTables();
                return false;
            }
        }

        private static void ReleaseLookupTables()
        {
            if (AreaTexture != null)
            {
                UnityEngine.Object.Destroy(AreaTexture);
                AreaTexture = null;
            }

            if (SearchTexture != null)
            {
                UnityEngine.Object.Destroy(SearchTexture);
                SearchTexture = null;
            }
        }

        /// <summary>
        ///     Gets the byte size of one pixel in the given format, or zero if the format is not one
        ///     this loader knows how to size. Only the two formats the lookup tables actually use are
        ///     listed, so introducing a third without updating this fails loudly instead of silently
        ///     computing the wrong expected length.
        /// </summary>
        private static int GetBytesPerPixel(TextureFormat format)
        {
            switch (format)
            {
                // Unity's RG16 is 16 bits total (two 8-bit channels), not 16 per channel.
                case TextureFormat.RG16:
                    return 2;
                case TextureFormat.R8:
                    return 1;
                default:
                    return 0;
            }
        }

        private static Texture2D CreateLookupTexture(
            string assetName, int width, int height, TextureFormat format, FilterMode filterMode)
        {
            var bytesPerPixel = GetBytesPerPixel(format);
            if (bytesPerPixel == 0)
            {
                Debug.LogError(
                    "[KellyUtils] SMAA lookup table '" + assetName + "' requests unhandled texture " +
                    "format " + format + ". Add its pixel size to GetBytesPerPixel.");
                return null;
            }

            if (!SystemInfo.SupportsTextureFormat(format))
            {
                Debug.LogError(
                    "[KellyUtils] This system does not support texture format " + format +
                    ", required by the SMAA lookup table '" + assetName + "'. SMAA will be disabled.");
                return null;
            }

            var asset = Mod.Instance.ResourceLoader.LoadAsset<TextAsset>(assetName);
            if (asset == null)
            {
                Debug.LogError("[KellyUtils] SMAA lookup table '" + assetName + "' could not be loaded.");
                return null;
            }

            var expected = width * bytesPerPixel * height;
            var data = asset.bytes;

            if (data == null || data.Length != expected)
            {
                Debug.LogError(
                    "[KellyUtils] SMAA lookup table '" + assetName + "' has " +
                    (data?.Length ?? 0) + " bytes, expected " + expected + ".");
                return null;
            }

            // linear: these are data tables, not color, so they must not be gamma-decoded.
            var texture = new Texture2D(width, height, format, false, true)
            {
                name = "SMAA " + assetName,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = filterMode,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0
            };

            texture.LoadRawTextureData(data);
            texture.Apply(false, true);
            return texture;
        }
    }
}
