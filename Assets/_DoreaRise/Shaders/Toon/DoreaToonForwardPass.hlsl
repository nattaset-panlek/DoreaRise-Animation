#ifndef DOREA_TOON_FORWARD_PASS_INCLUDED
#define DOREA_TOON_FORWARD_PASS_INCLUDED

#include "DoreaToonLighting.hlsl"

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float4 tangentOS  : TANGENT;
    float2 uv         : TEXCOORD0;
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 uv         : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    half3 normalWS    : TEXCOORD2;
    half4 tangentWS   : TEXCOORD3;    // w = bitangent sign
    half fogFactor    : TEXCOORD4;
};

Varyings ToonForwardVertex(Attributes input)
{
    Varyings output = (Varyings)0;

    VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

    output.positionCS = positionInputs.positionCS;
    output.positionWS = positionInputs.positionWS;
    output.normalWS = normalInputs.normalWS;
    output.tangentWS = half4(normalInputs.tangentWS, input.tangentOS.w * GetOddNegativeScale());
    output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
    output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
    return output;
}

half4 ToonForwardFragment(Varyings input) : SV_Target
{
    half4 albedo = SampleToonAlbedo(input.uv);
    half3 normalWS = normalize(input.normalWS);

#if defined(_NORMALMAP)
    half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv), _NormalScale);
    half3 tangentWS = normalize(input.tangentWS.xyz);
    half3 bitangentWS = cross(normalWS, tangentWS) * input.tangentWS.w;
    normalWS = normalize(TransformTangentToWorld(normalTS, half3x3(tangentWS, bitangentWS, normalWS)));
#endif

    ToonInput toon;
    toon.albedo = albedo.rgb;
    toon.positionWS = input.positionWS;
    toon.normalWS = normalWS;
    toon.viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
    toon.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    toon.uv = input.uv;

    half3 color = ToonShade(toon);
    color = MixFog(color, input.fogFactor);
    return half4(color, albedo.a);
}

#endif
