#ifndef DOREA_TOON_DEPTH_PASSES_INCLUDED
#define DOREA_TOON_DEPTH_PASSES_INCLUDED

#include "DoreaToonInput.hlsl"
#include "DoreaToonOutlinePass.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

struct DepthAttributes
{
    float4 positionOS     : POSITION;
    float3 normalOS       : NORMAL;
    float4 tangentOS      : TANGENT;
    float3 smoothNormalTS : TEXCOORD7;
};

struct DepthVaryings
{
    float4 positionCS : SV_POSITION;
    half3 normalWS    : TEXCOORD0;
};

// ShadowCaster ---------------------------------------------------------------------------------------------

// Directional lights use _LightDirection for the normal bias; spot/point lights use _LightPosition.
float3 _LightDirection;
float3 _LightPosition;

DepthVaryings ToonShadowCasterVertex(DepthAttributes input)
{
    DepthVaryings output = (DepthVaryings)0;

    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
    float3 lightDirectionWS = _LightDirection;
#endif

    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
    output.positionCS = ApplyShadowClamping(positionCS);
    return output;
}

half4 ToonShadowCasterFragment(DepthVaryings input) : SV_Target
{
    return 0;
}

// DepthOnly (depth prepass: feeds _CameraDepthTexture, read by the depth rim light and depth of field) -------
// The silhouette is drawn at the outline's outer edge so the line counts as character depth; otherwise depth of
// field blurs the outline together with the background. This prepass depth is a few mm in front of the real
// surface, so it must not be used for depth priming (Depth Priming Mode stays Disabled on the renderer).

DepthVaryings ToonDepthOnlyVertex(DepthAttributes input)
{
    DepthVaryings output = (DepthVaryings)0;
    output.positionCS = TransformWorldToHClip(
        OutlineExpandedPositionWS(input.positionOS.xyz, input.normalOS, input.tangentOS, input.smoothNormalTS));
    return output;
}

half ToonDepthOnlyFragment(DepthVaryings input) : SV_Target
{
    return input.positionCS.z;
}

// DepthNormals (SSAO and other screen-space effects) ------------------------------------------------------

DepthVaryings ToonDepthNormalsVertex(DepthAttributes input)
{
    DepthVaryings output = (DepthVaryings)0;
    output.positionCS = TransformWorldToHClip(
        OutlineExpandedPositionWS(input.positionOS.xyz, input.normalOS, input.tangentOS, input.smoothNormalTS));
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    return output;
}

half4 ToonDepthNormalsFragment(DepthVaryings input) : SV_Target
{
    return half4(NormalizeNormalPerPixel(input.normalWS), 0.0h);
}

#endif
