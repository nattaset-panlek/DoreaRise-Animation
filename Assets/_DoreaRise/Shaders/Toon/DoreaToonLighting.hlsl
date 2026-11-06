#ifndef DOREA_TOON_LIGHTING_INCLUDED
#define DOREA_TOON_LIGHTING_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "DoreaToonInput.hlsl"

struct ToonInput
{
    half3 albedo;
    float3 positionWS;
    half3 normalWS;
    half3 viewDirWS;
    float2 normalizedScreenSpaceUV;
    float2 uv;
};

// ---------------------------------------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------------------------------------

// Same convention URP uses for positionNDC, so it matches _CameraDepthTexture on every platform.
float2 ToonScreenUV(float3 positionWS)
{
    float4 positionCS = TransformWorldToHClip(positionWS);
    float4 ndc = positionCS * 0.5;
    ndc.xy = float2(ndc.x, ndc.y * _ProjectionParams.x) + ndc.w;
    return ndc.xy / ndc.w;
}

// Caps a light's brightness so the lit side shows the painted albedo instead of blowing it out.
half3 ToonLightColor(half3 color)
{
    half peak = max(max(color.r, color.g), color.b);
    return color * min(1.0h, _MaxLightIntensity / max(peak, 0.0001h));
}

// Painted-style shadow albedo: tinted, and pushed toward the albedo's own hue instead of going grey.
// Luminance is kept, only saturation rises (warm skin gets a red-pink shadow, navy cloth a deeper blue).
half3 ToonShadowAlbedo(half3 albedo)
{
    half luma = max(Luminance(albedo), 0.001h);
    half3 saturated = albedo * lerp(1.0h, albedo / luma, _ShadowSaturation);
    saturated *= luma / max(Luminance(saturated), 0.001h);
    return saturate(saturated) * _ShadowColor.rgb;
}

// ---------------------------------------------------------------------------------------------------------
// Face (needs the head frame from ToonCharacterLighting)
// ---------------------------------------------------------------------------------------------------------

// 1 inside the head sphere, 0 outside. Below the centre the sphere is squashed so the neck stays out.
half ToonFaceMask(float3 positionWS)
{
    if (!HasToonHead())
        return 0.0h;

    float3 offset = positionWS - _ToonHeadCenter.xyz;
    float height = dot(offset, _ToonHeadUp.xyz);
    float3 squashed = offset + _ToonHeadUp.xyz * (height * (_FaceChinSquash - 1.0) * step(height, 0.0));
    return 1.0h - smoothstep(_FaceMaskInner, max(_FaceMaskOuter, _FaceMaskInner + 0.001), length(squashed));
}

// SDF-style face shadow without a texture: the terminator is a line across the face that sweeps with the
// light's horizontal angle around the head; the vertical light angle is ignored, like hand-drawn anime faces.
// Returns a signed distance to the terminator (> 0 = lit) on roughly the same scale as half-Lambert.
half ToonFaceLightSignal(float3 positionWS, half3 lightDirWS)
{
    float3 forward = _ToonHeadForward.xyz;
    float3 up = _ToonHeadUp.xyz;
    float3 right = cross(up, forward);

    float3 flatLight = lightDirWS - up * dot(lightDirWS, up);
    float flatLength = length(flatLight);
    flatLight /= max(flatLength, 0.0001);

    float front = dot(flatLight, forward);
    front = lerp(1.0, front, saturate(flatLength * 3.0));   // light from straight above: face fully lit
    float side = dot(flatLight, right);
    side = side / max(abs(side), 0.15);                       // soft sign: no flicker for front/back light

    float across = dot(SafeNormalize(positionWS - _ToonHeadCenter.xyz), right);
    return (half)((across * side + front) * 0.5 + _FaceShadowOffset);
}

// ---------------------------------------------------------------------------------------------------------
// Main light
// ---------------------------------------------------------------------------------------------------------

// Cast shadows read as cel shapes too, with their own (usually weaker) strength on the face.
half ToonCastShadow(half shadowAttenuation, half faceMask)
{
    half softness = max(_CastShadowSmoothness, 0.001h);
    half castLit = smoothstep(0.5h - softness, 0.5h + softness, shadowAttenuation);
    half strength = _ReceiveShadowStrength * lerp(1.0h, _FaceCastShadowStrength, faceMask);
    return lerp(1.0h, castLit, strength);
}

// Albedo after cel shading (before light colour). signal > 0 = lit side, lit = final 0..1 light amount.
half3 ToonCelColor(half3 albedo, half signal, half lit, half halfLambert, half castLit, half faceMask)
{
#if defined(_RAMPMAP)
    half rampU = saturate(_CelThreshold + signal) * castLit;
    half3 ramp = SAMPLE_TEXTURE2D(_RampMap, sampler_RampMap, float2(rampU, 0.5)).rgb;
    return albedo * lerp(1.0h, ramp, _ShadeStrength);
#else
    half shade = 1.0h - lerp(1.0h, lit, _ShadeStrength);
    half3 shadowAlbedo = ToonShadowAlbedo(albedo);
    half3 color = lerp(albedo, shadowAlbedo, shade);

    // Second, deeper tone in the core of the form shadow (not used on the face).
    half coreSoftness = max(_Shadow2Smoothness, 0.001h) * 0.5h;
    half core = 1.0h - smoothstep(_Shadow2Threshold - coreSoftness, _Shadow2Threshold + coreSoftness, halfLambert);
    color = lerp(color, shadowAlbedo * _Shadow2Color.rgb, core * shade * _Shadow2Strength * (1.0h - faceMask));

    // Warm, saturated band just inside the shadow edge (fake subsurface, mostly for skin).
    half band = saturate(-signal / max(_TerminatorWidth, 0.001h));
    band = smoothstep(0.0h, 0.35h, band) * (1.0h - smoothstep(0.35h, 1.0h, band)) * castLit;
    color = lerp(color, albedo * _TerminatorColor.rgb, band * _TerminatorStrength * _ShadeStrength);
    return color;
#endif
}

half3 ToonSpecular(half3 albedo, half3 normalWS, half3 viewDirWS, half3 lightDirWS, half lit)
{
    half3 halfDir = SafeNormalize(lightDirWS + viewDirWS);
    half spec = pow(saturate(dot(normalWS, halfDir)), exp2(_Smoothness * 10.0h + 1.0h));
    half softness = max(_SpecularSmoothness, 0.001h) * 0.5h;
    spec = smoothstep(0.5h - softness, 0.5h + softness, spec);
    return spec * lit * _Metallic * _SpecularColor.rgb * albedo;
}

// Angel ring: Kajiya-Kay along the strand direction, stepped into a clean band. Strands are taken as running
// from the crown down (head up axis projected on the surface), so it needs no hair tangents in the mesh.
half3 ToonHairHighlight(half3 normalWS, half3 viewDirWS, half3 lightDirWS, half lit)
{
    half3 up = HasToonHead() ? (half3)_ToonHeadUp.xyz : half3(0.0h, 1.0h, 0.0h);
    half3 strand = up - normalWS * dot(normalWS, up);
    strand = SafeNormalize(strand + normalWS * _HairHighlightShift);

    half3 halfDir = SafeNormalize(lightDirWS + viewDirWS);
    half th = dot(strand, halfDir);
    half kajiya = pow(sqrt(saturate(1.0h - th * th)), _HairHighlightExponent) * smoothstep(-1.0h, 0.0h, th);

    half softness = max(_HairHighlightSoftness, 0.001h);
    half ring = smoothstep(0.5h - softness, 0.5h + softness, kajiya);
    return ring * _HairHighlightStrength * _HairHighlightColor.rgb * lerp(0.2h, 1.0h, lit);
}

// ---------------------------------------------------------------------------------------------------------
// Rim light
// ---------------------------------------------------------------------------------------------------------

// Screen-space depth rim: lit where the scene just outside the silhouette is far behind this pixel, so the rim
// has an even width and never smears over flat areas facing the camera. Falls back to a fresnel rim when this
// pixel is missing from the depth texture (no depth prepass).
half ToonRimMask(float3 positionWS, half3 normalWS, half3 viewDirWS)
{
    half nv = saturate(dot(normalWS, viewDirWS));
    half fresnel = pow(1.0h - nv, _RimPower);
    half fresnelRim = smoothstep(_RimThreshold - 0.05h, _RimThreshold + 0.05h, fresnel);

#if defined(_RIM_DEPTH)
    float fragmentDepth = -TransformWorldToView(positionWS).z;
    float3 screenNormal = normalWS - viewDirWS * dot(normalWS, viewDirWS);
    float3 probeWS = positionWS + screenNormal * (_RimWidth * 0.01);

    float selfDepth = LinearEyeDepth(SampleSceneDepth(ToonScreenUV(positionWS)), _ZBufferParams);
    float probeDepth = LinearEyeDepth(SampleSceneDepth(ToonScreenUV(probeWS)), _ZBufferParams);

    half valid = step(abs(selfDepth - fragmentDepth), 0.05 + fragmentDepth * 0.02);
    half depthRim = smoothstep(_RimDepthThreshold, _RimDepthThreshold * 1.5h + 0.01h, (half)(probeDepth - fragmentDepth));
    depthRim *= saturate((1.0h - nv) * 4.0h);
    return lerp(fresnelRim, depthRim, valid);
#else
    return fresnelRim;
#endif
}

half3 ToonRimColor(half3 albedo, half3 normalWS, half3 lightDirWS, half3 lightColor)
{
    half towardLight = saturate(dot(normalWS, lightDirWS) * 0.5h + 0.5h);
    half align = lerp(1.0h, smoothstep(0.3h, 0.7h, towardLight), _RimLightAlign);
    half3 tint = lerp(half3(1.0h, 1.0h, 1.0h), albedo, _RimAlbedoTint);
    return _RimColor.rgb * tint * lightColor * (_RimStrength * align);
}

// ---------------------------------------------------------------------------------------------------------
// Ambient and additional lights
// ---------------------------------------------------------------------------------------------------------

// Flattening the probe toward its average keeps ambient from drawing a second, smooth "3D" gradient.
half3 ToonAmbient(half3 albedo, half3 normalWS)
{
    half3 sh = lerp(SampleSH(normalWS), SampleSH(half3(0.0h, 0.0h, 0.0h)), _AmbientFlatten);
    return max(sh, 0.0h) * albedo * _AmbientScale;
}

half3 ToonAdditionalLight(Light light, ToonInput input)
{
    half halfLambert = dot(input.normalWS, light.direction) * 0.5h + 0.5h;
    half softness = max(_CelSmoothness, 0.05h);
    half lit = smoothstep(_CelThreshold - softness, _CelThreshold + softness, halfLambert);
    lit *= smoothstep(0.3h, 0.7h, light.shadowAttenuation);
    return input.albedo * ToonLightColor(light.color * light.distanceAttenuation) * lit;
}

half3 ToonAdditionalLights(ToonInput input)
{
    half3 total = 0.0h;
#if defined(_ADDITIONAL_LIGHTS)
    // The cluster light loop macros read these two fields from a variable named inputData.
    InputData inputData = (InputData)0;
    inputData.positionWS = input.positionWS;
    inputData.normalizedScreenSpaceUV = input.normalizedScreenSpaceUV;
    half4 shadowMask = half4(1.0h, 1.0h, 1.0h, 1.0h);
    uint lightCount = GetAdditionalLightsCount();

  #if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        Light light = GetAdditionalLight(lightIndex, input.positionWS, shadowMask);
        total += ToonAdditionalLight(light, input);
    }
  #endif

    LIGHT_LOOP_BEGIN(lightCount)
        Light light = GetAdditionalLight(lightIndex, input.positionWS, shadowMask);
        total += ToonAdditionalLight(light, input);
    LIGHT_LOOP_END
#endif
    return total * _AdditionalLightStrength;
}

// ---------------------------------------------------------------------------------------------------------
// Full shading
// ---------------------------------------------------------------------------------------------------------

half3 ToonShade(ToonInput input)
{
    // This overload also fades shadows out toward the shadow distance.
    Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS), input.positionWS, half4(1.0h, 1.0h, 1.0h, 1.0h));
    half3 lightDir = mainLight.direction;
    half3 lightColor = ToonLightColor(mainLight.color * mainLight.distanceAttenuation);

    half faceMask = 0.0h;
    half halfLambert = dot(input.normalWS, lightDir) * 0.5h + 0.5h;
    half signal = halfLambert - _CelThreshold;
    half softness = max(_CelSmoothness, 0.001h) * 0.5h;
    half3 ambientNormal = input.normalWS;

#if defined(_FACE_SHADING)
    faceMask = ToonFaceMask(input.positionWS);
    if (faceMask > 0.0h)
    {
        signal = lerp(signal, ToonFaceLightSignal(input.positionWS, lightDir), faceMask);
        softness = lerp(softness, max(_FaceShadowSoftness, 0.001h), faceMask);
        ambientNormal = SafeNormalize(lerp(input.normalWS, SafeNormalize(input.positionWS - _ToonHeadCenter.xyz), faceMask));
    }
#endif

    half castLit = ToonCastShadow(mainLight.shadowAttenuation, faceMask);
    half lit = min(smoothstep(-softness, softness, signal), castLit);

    half3 color = ToonCelColor(input.albedo, signal, lit, halfLambert, castLit, faceMask) * lightColor;
    color += ToonAmbient(input.albedo, ambientNormal);
    color += ToonSpecular(input.albedo, input.normalWS, input.viewDirWS, lightDir, lit) * lightColor;

#if defined(_HAIR_HIGHLIGHT)
    color += ToonHairHighlight(input.normalWS, input.viewDirWS, lightDir, lit) * lightColor;
#endif

    color += ToonAdditionalLights(input);
    color += ToonRimMask(input.positionWS, input.normalWS, input.viewDirWS)
           * ToonRimColor(input.albedo, input.normalWS, lightDir, lightColor);

    half3 emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb * _EmissionColor.rgb;
    color += emission * lerp(half3(1.0h, 1.0h, 1.0h), input.albedo, _EmissionAlbedoTint);
    return color;
}

#endif
