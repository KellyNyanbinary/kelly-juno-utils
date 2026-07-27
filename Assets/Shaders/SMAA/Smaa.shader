// Unity built-in-pipeline wrapper around the reference SMAA implementation.
// The algorithm itself lives in the vendored, unmodified SMAA.hlsl (MIT, see LICENSE.txt);
// everything here is just the Unity plumbing needed to drive its three passes.
Shader "Hidden/KellyUtils/SMAA"
{
    Properties
    {
        _MainTex ("Base (RGB)", 2D) = "white" {}
    }

    CGINCLUDE

    #include "UnityCG.cginc"

    sampler2D _MainTex;
    sampler2D _SmaaBlendTex;
    sampler2D _SmaaAreaTex;
    sampler2D _SmaaSearchTex;

    // float4(1/width, 1/height, width, height) of the texture being filtered.
    float4 _SmaaRtMetrics;

    #define SMAA_RT_METRICS _SmaaRtMetrics

    // HLSL3 maps SMAA's texture macros onto sampler2D/tex2Dlod, which is what
    // Unity's built-in pipeline gives us.
    #define SMAA_HLSL_3

    // SMAA_HLSL_3 defaults to a ".ra" swizzle because the DX9 reference loads AreaTex
    // as A8L8. We upload it as a two-channel RG texture instead, so select ".rg".
    #define SMAA_AREATEX_SELECT(s) s.rg

    #include "SMAA.hlsl"

    struct VaryingsEdge
    {
        float4 position : SV_POSITION;
        float2 texcoord : TEXCOORD0;
        float4 offset[3] : TEXCOORD1;
    };

    struct VaryingsBlend
    {
        float4 position : SV_POSITION;
        float2 texcoord : TEXCOORD0;
        float2 pixcoord : TEXCOORD1;
        float4 offset[3] : TEXCOORD2;
    };

    struct VaryingsNeighborhood
    {
        float4 position : SV_POSITION;
        float2 texcoord : TEXCOORD0;
        float4 offset : TEXCOORD1;
    };

    VaryingsEdge VertEdgeDetection(appdata_img v)
    {
        VaryingsEdge o;
        o.position = UnityObjectToClipPos(v.vertex);
        o.texcoord = v.texcoord;
        SMAAEdgeDetectionVS(o.texcoord, o.offset);
        return o;
    }

    float4 FragEdgeDetection(VaryingsEdge i) : SV_Target
    {
        // Non-edge pixels are discarded by SMAA, so the target must be cleared first.
        return float4(SMAAColorEdgeDetectionPS(i.texcoord, i.offset, _MainTex), 0.0, 0.0);
    }

    VaryingsBlend VertBlendingWeights(appdata_img v)
    {
        VaryingsBlend o;
        o.position = UnityObjectToClipPos(v.vertex);
        o.texcoord = v.texcoord;
        SMAABlendingWeightCalculationVS(o.texcoord, o.pixcoord, o.offset);
        return o;
    }

    float4 FragBlendingWeights(VaryingsBlend i) : SV_Target
    {
        // _MainTex is the edges target here. Subsample indices are zero for SMAA 1x.
        return SMAABlendingWeightCalculationPS(
            i.texcoord, i.pixcoord, i.offset,
            _MainTex, _SmaaAreaTex, _SmaaSearchTex,
            float4(0.0, 0.0, 0.0, 0.0));
    }

    VaryingsNeighborhood VertNeighborhoodBlending(appdata_img v)
    {
        VaryingsNeighborhood o;
        o.position = UnityObjectToClipPos(v.vertex);
        o.texcoord = v.texcoord;
        SMAANeighborhoodBlendingVS(o.texcoord, o.offset);
        return o;
    }

    float4 FragNeighborhoodBlending(VaryingsNeighborhood i) : SV_Target
    {
        return SMAANeighborhoodBlendingPS(i.texcoord, i.offset, _MainTex, _SmaaBlendTex);
    }

    ENDCG

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always
        Fog { Mode Off }

        // Pass 0 - Edge detection.
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex VertEdgeDetection
            #pragma fragment FragEdgeDetection
            // Local: these keywords are only ever set per-material, so they must not consume slots
            // from the global keyword budget shared with the game and other mods.
            #pragma multi_compile_local SMAA_PRESET_LOW SMAA_PRESET_MEDIUM SMAA_PRESET_HIGH SMAA_PRESET_ULTRA
            ENDCG
        }

        // Pass 1 - Blending weight calculation.
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex VertBlendingWeights
            #pragma fragment FragBlendingWeights
            #pragma multi_compile_local SMAA_PRESET_LOW SMAA_PRESET_MEDIUM SMAA_PRESET_HIGH SMAA_PRESET_ULTRA
            ENDCG
        }

        // Pass 2 - Neighborhood blending.
        // No preset keywords here: this pass reads the blend weights produced by pass 1 and does not
        // reference any of the preset constants, so compiling it per preset would only produce four
        // identical variants.
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex VertNeighborhoodBlending
            #pragma fragment FragNeighborhoodBlending
            ENDCG
        }
    }

    Fallback Off
}
