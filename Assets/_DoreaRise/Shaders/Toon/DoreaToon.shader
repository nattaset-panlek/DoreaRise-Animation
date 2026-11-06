// Anime toon shader for Dorea (URP). Techniques follow the common anime NPR set used by UTS3 / lilToon /
// HoYoverse-style renderers, adapted to albedo-only textures:
//   - painted shadow colour (hue-keeping saturation) + optional core shade + warm terminator band
//   - texture-free SDF-style face shadow driven by the head bone (ToonCharacterLighting)
//   - screen-space depth rim light (fresnel fallback), Kajiya-Kay angel ring for hair
//   - inverted-hull outline on baked smooth normals (ToonOutlineNormalBaker), screen-stable width
Shader "DoreaRise/Anime Toon"
{
    Properties
    {
        [Header(Base)]
        _BaseMap ("Base Map (Albedo)", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        [Toggle(_NORMALMAP)] _UseNormalMap ("Use Normal Map", Float) = 0
        [Normal] _NormalMap ("Normal Map", 2D) = "bump" {}
        _NormalScale ("Normal Scale", Float) = 1.0

        [Header(Cel Shading)]
        _CelThreshold ("Shadow Threshold (half-Lambert)", Range(0, 1)) = 0.5
        _CelSmoothness ("Shadow Edge Softness", Range(0.001, 0.5)) = 0.03
        _ShadowColor ("Shadow Tint", Color) = (0.72, 0.66, 0.78, 1)
        _ShadowSaturation ("Shadow Saturation (keeps hue)", Range(0, 1)) = 0.5
        _ShadeStrength ("Shading Amount", Range(0, 1)) = 1
        [Space]
        _Shadow2Color ("Core Shadow Tint", Color) = (0.82, 0.8, 0.9, 1)
        _Shadow2Threshold ("Core Shadow Threshold", Range(0, 1)) = 0.25
        _Shadow2Smoothness ("Core Shadow Softness", Range(0.001, 0.5)) = 0.08
        _Shadow2Strength ("Core Shadow Strength", Range(0, 1)) = 0
        [Space]
        _TerminatorColor ("Terminator Color (warm edge)", Color) = (1, 0.55, 0.45, 1)
        _TerminatorWidth ("Terminator Width", Range(0.001, 0.5)) = 0.08
        _TerminatorStrength ("Terminator Strength", Range(0, 1)) = 0
        [Space]
        _ReceiveShadowStrength ("Cast Shadow Strength", Range(0, 1)) = 1
        _CastShadowSmoothness ("Cast Shadow Edge Softness", Range(0.001, 0.5)) = 0.1
        [Toggle(_RAMPMAP)] _UseRampMap ("Use Ramp Map", Float) = 0
        [NoScaleOffset] _RampMap ("Ramp Map (left = shadow, right = lit)", 2D) = "white" {}

        [Header(Light)]
        _MaxLightIntensity ("Max Light Intensity", Range(0.5, 3)) = 1.1
        _AdditionalLightStrength ("Point and Spot Light Strength", Range(0, 2)) = 1
        _AmbientScale ("Ambient Strength", Range(0, 1)) = 0.35
        _AmbientFlatten ("Ambient Flatten", Range(0, 1)) = 0.6

        [Header(Specular)]
        _Metallic ("Specular Strength", Range(0, 1)) = 0.1
        _Smoothness ("Specular Size (smoothness)", Range(0.01, 1)) = 0.5
        _SpecularColor ("Specular Color", Color) = (1, 1, 1, 1)
        _SpecularSmoothness ("Specular Edge Softness", Range(0.01, 0.5)) = 0.1

        [Header(Hair Angel Ring)]
        [Toggle(_HAIR_HIGHLIGHT)] _HairHighlight ("Enable Angel Ring", Float) = 0
        _HairHighlightColor ("Ring Color", Color) = (1, 0.97, 0.94, 1)
        _HairHighlightStrength ("Ring Strength", Range(0, 2)) = 0.45
        _HairHighlightExponent ("Ring Thinness", Range(1, 400)) = 90
        _HairHighlightShift ("Ring Shift (up or down)", Range(-1, 1)) = 0
        _HairHighlightSoftness ("Ring Edge Softness", Range(0.001, 0.5)) = 0.08

        [Header(Rim Light)]
        [Toggle(_RIM_DEPTH)] _RimDepth ("Screen-Space Depth Rim", Float) = 1
        _RimColor ("Rim Color", Color) = (1, 1, 1, 1)
        _RimStrength ("Rim Strength", Range(0, 3)) = 0.6
        _RimWidth ("Rim Width (cm)", Range(0, 5)) = 1.2
        _RimDepthThreshold ("Rim Depth Threshold (m)", Range(0.01, 1)) = 0.12
        _RimLightAlign ("Rim Only Toward Light", Range(0, 1)) = 0.7
        _RimAlbedoTint ("Rim Uses Albedo", Range(0, 1)) = 0.4
        _RimPower ("Fresnel Rim Power (fallback)", Range(0.1, 8)) = 4
        _RimThreshold ("Fresnel Rim Threshold (fallback)", Range(0, 1)) = 0.6

        [Header(Face)]
        [Toggle(_FACE_SHADING)] _FaceShading ("Face Shading (needs ToonCharacterLighting)", Float) = 0
        _FaceShadowSoftness ("Face Shadow Softness", Range(0.001, 0.3)) = 0.03
        _FaceShadowOffset ("Face Shadow Offset (+ = more lit)", Range(-0.5, 0.5)) = 0.05
        _FaceMaskInner ("Face Radius Inner (m)", Range(0, 0.5)) = 0.13
        _FaceMaskOuter ("Face Radius Outer (m)", Range(0, 0.5)) = 0.17
        _FaceChinSquash ("Chin Squash (keeps neck out)", Range(1, 4)) = 1.35
        _FaceCastShadowStrength ("Cast Shadow On Face", Range(0, 1)) = 0.35

        [Header(Emission)]
        [HDR] _EmissionColor ("Emission Color", Color) = (0, 0, 0, 1)
        [NoScaleOffset] _EmissionMap ("Emission Mask", 2D) = "white" {}
        _EmissionAlbedoTint ("Emission Uses Albedo", Range(0, 1)) = 1

        [Header(Outline)]
        _OutlineWidth ("Outline Width", Range(0, 10)) = 1.0
        _OutlineColor ("Outline Color", Color) = (0.1, 0.1, 0.1, 1)
        _OutlineAlbedoBlend ("Outline Uses Albedo", Range(0, 1)) = 0.5
        _OutlineRefDistance ("Outline Reference Distance (m)", Range(0.1, 20)) = 2.5
        _OutlineMinScale ("Outline Min Scale (close-up)", Range(0, 2)) = 0.4
        _OutlineMaxScale ("Outline Max Scale (far)", Range(0, 4)) = 1.5
        _OutlineZOffset ("Outline Z Offset (m)", Range(0, 0.05)) = 0.0015
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ToonForwardVertex
            #pragma fragment ToonForwardFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            #pragma shader_feature_local_fragment _NORMALMAP
            #pragma shader_feature_local_fragment _RAMPMAP
            #pragma shader_feature_local_fragment _FACE_SHADING
            #pragma shader_feature_local_fragment _HAIR_HIGHLIGHT
            #pragma shader_feature_local_fragment _RIM_DEPTH

            #include "DoreaToonForwardPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ToonOutlineVertex
            #pragma fragment ToonOutlineFragment
            #pragma multi_compile_fog

            #include "DoreaToonOutlinePass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ToonShadowCasterVertex
            #pragma fragment ToonShadowCasterFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "DoreaToonDepthPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ToonDepthOnlyVertex
            #pragma fragment ToonDepthOnlyFragment

            #include "DoreaToonDepthPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ToonDepthNormalsVertex
            #pragma fragment ToonDepthNormalsFragment

            #include "DoreaToonDepthPasses.hlsl"
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
