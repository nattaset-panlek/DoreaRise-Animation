Shader "Dorea/ScrollingTextureTransparent"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color Tint", Color) = (1,1,1,1)
        _Fade ("Overall Opacity", Range(0, 1)) = 1.0
        
        [Header(Edge Fading)]
        _FadeLeft ("Fade Left", Range(0, 0.5)) = 0.0
        _FadeRight ("Fade Right", Range(0, 0.5)) = 0.0
        _FadeBottom ("Fade Bottom", Range(0, 0.5)) = 0.0
        _FadeTop ("Fade Top", Range(0, 0.5)) = 0.0

        [Header(Scrolling)]
        _ScrollSpeedX ("Scroll Speed X", Float) = 1.0
        _ScrollSpeedY ("Scroll Speed Y", Float) = 0.0
    }
    SubShader
    {
        // Setup tags for transparency
        Tags { 
            "Queue"="Transparent" 
            "RenderType"="Transparent" 
            "IgnoreProjector"="True" 
        }
        LOD 100

        // Transparency settings
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off // (Optional) Makes it visible from both sides, remove if you only want it one-sided

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR; // Supports vertex colors (e.g. from Particle Systems)
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float2 originalUV : TEXCOORD1;
                UNITY_FOG_COORDS(2)
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST; // ST contains tiling (xy) and offset (zw)
            float4 _Color;
            float _Fade;
            float _FadeLeft;
            float _FadeRight;
            float _FadeTop;
            float _FadeBottom;
            float _ScrollSpeedX;
            float _ScrollSpeedY;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.originalUV = v.uv; // Store original UV (0 to 1) for edge fading
                
                // Calculate scrolling offset based on time and speed
                float2 scrollOffset = float2(_ScrollSpeedX, _ScrollSpeedY) * _Time.y;
                
                // Apply texture Tiling & Offset from material, then add the time-based scroll
                o.uv = TRANSFORM_TEX(v.uv, _MainTex) + scrollOffset;
                
                // Multiply vertex color with Material Tint Color
                o.color = v.color * _Color;
                
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Sample the texture using the scrolling UVs
                fixed4 col = tex2D(_MainTex, i.uv) * i.color;
                
                // Calculate Edge Fading based on original UVs (0 to 1)
                float edgeX = smoothstep(0.0, max(_FadeLeft, 0.0001), i.originalUV.x) * smoothstep(1.0, 1.0 - max(_FadeRight, 0.0001), i.originalUV.x);
                float edgeY = smoothstep(0.0, max(_FadeBottom, 0.0001), i.originalUV.y) * smoothstep(1.0, 1.0 - max(_FadeTop, 0.0001), i.originalUV.y);
                
                // Apply Edge Fade and Overall Fade
                col.a *= edgeX * edgeY * _Fade;
                
                // Apply standard Unity fog
                UNITY_APPLY_FOG(i.fogCoord, col);
                
                return col;
            }
            ENDCG
        }
    }
}
