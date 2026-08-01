using System.Diagnostics.CodeAnalysis;
using ModApi.Settings.Core;

namespace Rendering
{
    /// <summary>
    /// Quality presets exposed by the reference SMAA implementation. Each maps to one of the
    /// <c>SMAA_PRESET_*</c> shader keywords, which control the edge-detection threshold and the
    /// number of search steps used when tracing an edge.
    /// </summary>
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public enum SmaaQualityPreset
    {
        [EnumOption("Fastest. Threshold 0.15 with 4 search steps, and no diagonal or corner detection.")]
        Low,

        [EnumOption("Threshold 0.1 with 8 search steps, and no diagonal or corner detection.")]
        Medium,

        [EnumOption("Recommended. Threshold 0.1 with 16 search steps, plus diagonal and corner detection.")]
        High,

        [EnumOption("Highest quality. Threshold 0.05 with 32 search steps, plus diagonal and corner detection.")]
        Ultra
    }
}
