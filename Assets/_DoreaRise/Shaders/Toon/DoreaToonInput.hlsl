#ifndef DOREA_TOON_INPUT_INCLUDED
#define DOREA_TOON_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// Every pass includes this file so UnityPerMaterial stays identical (SRP Batcher).
CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half _NormalScale;

    // Cel shading
    half _CelThreshold;
    half _CelSmoothness;
    half4 _ShadowColor;
    half _ShadowSaturation;
    half4 _Shadow2Color;
    half _Shadow2Threshold;
    half _Shadow2Smoothness;
    half _Shadow2Strength;
    half4 _TerminatorColor;
    half _TerminatorWidth;
    half _TerminatorStrength;
    half _ShadeStrength;
    half _CastShadowSmoothness;
    half _ReceiveShadowStrength;

    // Light response
    half _MaxLightIntensity;
    half _AdditionalLightStrength;
    half _AmbientScale;
    half _AmbientFlatten;

    // Specular
    half _Metallic;
    half _Smoothness;
    half4 _SpecularColor;
    half _SpecularSmoothness;

    // Hair angel ring
    half4 _HairHighlightColor;
    half _HairHighlightStrength;
    half _HairHighlightExponent;
    half _HairHighlightShift;
    half _HairHighlightSoftness;

    // Rim light
    half4 _RimColor;
    half _RimPower;
    half _RimThreshold;
    half _RimStrength;
    half _RimWidth;
    half _RimDepthThreshold;
    half _RimLightAlign;
    half _RimAlbedoTint;

    // Face
    half _FaceShadowSoftness;
    half _FaceShadowOffset;
    half _FaceMaskInner;
    half _FaceMaskOuter;
    half _FaceChinSquash;
    half _FaceCastShadowStrength;

    // Emission
    half4 _EmissionColor;
    half _EmissionAlbedoTint;

    // Outline
    half _OutlineWidth;
    half4 _OutlineColor;
    half _OutlineAlbedoBlend;
    half _OutlineRefDistance;
    half _OutlineMinScale;
    half _OutlineMaxScale;
    half _OutlineZOffset;
CBUFFER_END

TEXTURE2D(_BaseMap);        SAMPLER(sampler_BaseMap);
TEXTURE2D(_NormalMap);      SAMPLER(sampler_NormalMap);
TEXTURE2D(_RampMap);        SAMPLER(sampler_RampMap);
TEXTURE2D(_EmissionMap);    SAMPLER(sampler_EmissionMap);

// Head frame, set per renderer by ToonCharacterLighting through a MaterialPropertyBlock.
// w = 1 when a driver feeds them; 0 means "no head data" and face/hair features fall back.
float4 _ToonHeadCenter;
float4 _ToonHeadForward;
float4 _ToonHeadUp;

half4 SampleToonAlbedo(float2 uv)
{
    return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
}

bool HasToonHead()
{
    return _ToonHeadCenter.w > 0.5;
}

#endif
