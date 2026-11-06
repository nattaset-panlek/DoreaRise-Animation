Shader "Custom/AnimeSpeedOverlay"{/* STREAMING_CHUNK: ตั้งค่า Properties สำหรับรับค่าจาก Unity Inspector และ Script */Properties{[Header(Base Settings)]_MainTex ("Base Character Texture (Optional)", 2D) = "white" {}_Color ("Base Color Tint", Color) = (1,1,1,1)    // ถูกควบคุมโดย C# Script ห้ามปรับเอง
    [HideInInspector] _Velocity ("Velocity", Vector) = (0,0,0,0)
    
    [Header(Texture Lines Effect)]
    _SpeedLineTex ("Speed Line Noise Texture", 2D) = "white" {}
    _LineScrollSpeed ("Line Scroll Speed", Range(0, 50)) = 15.0
    _LineColor ("Speed Line Color", Color) = (1, 1, 1, 1)
    _StretchMultiplier ("Stretch Multiplier", Range(0, 2)) = 0.5
}

/* STREAMING_CHUNK: ตั้งค่า SubShader ให้เป็นแบบโปร่งใส (Transparent) และไม่เขียน ZBuffer */
SubShader
{
    // กำหนดให้เป็น Transparent เพื่อให้ทับ Material หลักได้โดยไม่บัง
    Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
    LOD 200
    
    Cull Off 
    ZWrite Off // ปิด ZWrite เพื่อไม่ให้กินทะลุ Material หลัก
    Blend SrcAlpha OneMinusSrcAlpha // เปิดการใช้งาน Alpha Blending

    Pass
    {
        CGPROGRAM
        #pragma vertex vert
        #pragma fragment frag
        #include "UnityCG.cginc"

        /* STREAMING_CHUNK: ประกาศโครงสร้างตัวแปร (Structs) สำหรับรับส่งข้อมูล */
        struct appdata
        {
            float4 vertex : POSITION;
            float2 uv : TEXCOORD0;
            float3 normal : NORMAL;
        };

        struct v2f
        {
            float4 pos : SV_POSITION;
            float2 uv : TEXCOORD0;
            float2 noise_uv : TEXCOORD1;
            float stretchFactor : TEXCOORD2;
        };

        /* STREAMING_CHUNK: ประกาศตัวแปรรับค่าจาก Properties */
        sampler2D _MainTex;
        float4 _MainTex_ST;
        float4 _Color;
        
        sampler2D _SpeedLineTex;
        float4 _SpeedLineTex_ST;
        float _LineScrollSpeed;
        float4 _LineColor;
        float _StretchMultiplier;
        
        float4 _Velocity;

        /* STREAMING_CHUNK: Vertex Shader - คำนวณการยืดรูปร่างไปด้านหลัง */
        v2f vert (appdata v)
        {
            v2f o;
            o.uv = TRANSFORM_TEX(v.uv, _MainTex);
            
            // คำนวณความเร็วในระดับ World Space
            float3 worldVel = _Velocity.xyz;
            float speed = length(worldVel);
            
            // หาว่าตัวละครกำลังมุ่งหน้าไปทางไหน
            float3 moveDir = speed > 0.001 ? normalize(worldVel) : float3(0,1,0);
            float3 worldNormal = UnityObjectToWorldNormal(v.normal);
            
            // ใช้ Dot Product หาด้านหลัง (Trailing Edge)
            float dirDot = dot(worldNormal, moveDir);
            float trailingEdge = saturate(-dirDot); // ค่าจะเป็น 1 ถ้าอยู่ด้านหลังตรงๆ และ 0 ถ้าอยู่ด้านหน้า
            
            /* STREAMING_CHUNK: สร้างความหยัก (Spikes) และยืด Vertex ออกไป */
            // สร้างคลื่นความหยักแบบ Procedural ให้การยืดดูเป็นแฉกๆ ไม่ทื่อ
            float spike = (sin(v.vertex.y * 15.0 + _Time.y * 20.0) + cos(v.vertex.x * 15.0 - _Time.y * 20.0)) * 0.5;
            spike = saturate(spike + 0.5);
            
            // คำนวณระยะทางที่จะต้องยืดออกไป
            float stretch = trailingEdge * spike * _StretchMultiplier * speed;
            
            // แปลง Vertex เป็น World Space แล้วดึงจุดไปในทิศทางตรงข้ามกับการวิ่ง
            float4 worldPos = mul(unity_ObjectToWorld, v.vertex);
            worldPos.xyz -= moveDir * stretch;
            
            // แปลงกลับเป็น Screen Space
            o.pos = mul(UNITY_MATRIX_VP, worldPos);
            
            /* STREAMING_CHUNK: คำนวณ UV ของลายเส้นและค่าความยืดเพื่อส่งไปที่ Fragment */
            // เตรียม UV สำหรับลายเส้นความเร็ว (เลื่อนไปเรื่อยๆ)
            o.noise_uv = TRANSFORM_TEX(v.uv, _SpeedLineTex);
            o.noise_uv.y += _Time.y * _LineScrollSpeed * (speed + 0.1); 
            
            // ส่งค่าพารามิเตอร์เพื่อบอกว่าจุดนี้ถูกยืดออกไปมากแค่ไหน
            // ถ้า speed น้อยมาก stretchFactor จะเป็น 0
            o.stretchFactor = trailingEdge * (speed > 0.1 ? 1.0 : 0.0) * saturate(speed / 2.0);
            
            return o;
        }

        /* STREAMING_CHUNK: Fragment Shader - ระบายสีและกำหนดความโปร่งใส */
        fixed4 frag (v2f i) : SV_Target
        {
            // อ่านค่าสีจาก Texture ชุดตัวละคร (ถ้าใส่ไว้)
            fixed4 baseCol = tex2D(_MainTex, i.uv) * _Color;

            // อ่านค่าสีจาก Texture Noise (ภาพขาวดำลายเส้น)
            fixed4 noiseCol = tex2D(_SpeedLineTex, i.noise_uv);
            
            // สร้างลายเส้นที่คมชัด
            float lineMask = step(0.5, noiseCol.r);
            
            // รวมสีชุด สีที่ตั้งค่าไว้ 
            fixed4 finalColor = baseCol * _LineColor;
            
            // *** หัวใจสำคัญ ***
            // ถ้า i.stretchFactor เป็น 0 (ด้านหน้าตัวละคร) Alpha = 0 (โปร่งใส 100%)
            // ถ้า i.stretchFactor > 0 (ด้านหลังที่โดนยืด) Alpha จะแสดงผล ทำให้เห็นเป็นเส้นๆ
            finalColor.a = lineMask * saturate(i.stretchFactor * 1.5);

            return finalColor;
        }
        ENDCG
    }
}
FallBack "Diffuse"
}