#ifndef DOREA_TOON_OUTLINE_PASS_INCLUDED
#define DOREA_TOON_OUTLINE_PASS_INCLUDED

#include "DoreaToonInput.hlsl"

struct OutlineAttributes
{
    float4 positionOS     : POSITION;
    float3 normalOS       : NORMAL;
    float4 tangentOS      : TANGENT;
    float2 uv             : TEXCOORD0;
    float3 smoothNormalTS : TEXCOORD7;    // baked by ToonOutlineNormalBaker; zero when the mesh has none
};

struct OutlineVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv         : TEXCOORD0;
    half fogFactor    : TEXCOORD1;
};

// Hard edges split vertices, so pushing out along the raw normal tears the hull open at seams.
// The baked normal is the average of every vertex at the same position, stored in tangent space so it
// follows skinning.
float3 OutlineNormalWS(float3 normalOS, float4 tangentOS, float3 smoothNormalTS)
{
    VertexNormalInputs normalInputs = GetVertexNormalInputs(normalOS, tangentOS);
    if (dot(smoothNormalTS, smoothNormalTS) < 0.25)
        return normalInputs.normalWS;

    // Same orthonormal frame the baker used.
    float3 normalWS = normalize(normalInputs.normalWS);
    float3 tangentWS = normalize(normalInputs.tangentWS - normalWS * dot(normalWS, normalInputs.tangentWS));
    float3 bitangentWS = cross(normalWS, tangentWS) * (tangentOS.w * GetOddNegativeScale());
    return normalize(tangentWS * smoothNormalTS.x + bitangentWS * smoothNormalTS.y + normalWS * smoothNormalTS.z);
}

// Width is authored in world units at _OutlineRefDistance with a 60 degree FOV. Elsewhere it keeps about the
// same on-screen thickness (also through FOV zooms), clamped so close-ups stay delicate and far shots thin out.
float OutlineScreenScale(float3 positionWS)
{
    if (unity_OrthoParams.w > 0.5)
        return 1.0;

    float eyeDepth = -TransformWorldToView(positionWS).z;
    float tanHalfFov = 1.0 / abs(UNITY_MATRIX_P._m11);
    float scale = eyeDepth * tanHalfFov / (max(_OutlineRefDistance, 0.01) * 0.57735);
    return clamp(scale, _OutlineMinScale, _OutlineMaxScale);
}

// Vertex pushed out to the outline's outer edge. The depth passes use it too, so the depth texture covers the
// drawn line and depth of field / rim light treat the outline as part of the character.
float3 OutlineExpandedPositionWS(float3 positionOS, float3 normalOS, float4 tangentOS, float3 smoothNormalTS)
{
    float3 positionWS = TransformObjectToWorld(positionOS);
    float3 normalWS = OutlineNormalWS(normalOS, tangentOS, smoothNormalTS);
    return positionWS + normalWS * (_OutlineWidth * 0.005 * OutlineScreenScale(positionWS));
}

OutlineVaryings ToonOutlineVertex(OutlineAttributes input)
{
    OutlineVaryings output = (OutlineVaryings)0;

    float3 positionWS = OutlineExpandedPositionWS(input.positionOS.xyz, input.normalOS, input.tangentOS, input.smoothNormalTS);

    // Push the hull slightly away from the camera so it does not poke through inner surfaces.
    float3 positionVS = TransformWorldToView(positionWS);
    positionVS.z -= _OutlineZOffset;

    output.positionCS = TransformWViewToHClip(positionVS);
    output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
    output.fogFactor = ComputeFogFactor(output.positionCS.z);
    return output;
}

// A darker, saturated version of the surface colour reads as a drawn line instead of a black cut-out.
half4 ToonOutlineFragment(OutlineVaryings input) : SV_Target
{
    half3 albedo = SampleToonAlbedo(input.uv).rgb;
    half3 tinted = albedo * _OutlineColor.rgb * 2.0h;
    half3 color = lerp(_OutlineColor.rgb, tinted, _OutlineAlbedoBlend);
    color = MixFog(color, input.fogFactor);
    return half4(color, 1.0h);
}

#endif
