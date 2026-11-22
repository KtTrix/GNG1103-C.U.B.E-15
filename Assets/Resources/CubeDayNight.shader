// Day / night shading for a sphere (the hub globe, the Moon) driven only by a Sun direction, so it does not depend on the
// scene's lights, ambient light or baked lighting. Day side: the picture. Night side: dark, optionally with a night-lights picture.
// Lives in a Resources folder so it is always included in the headset build.
Shader "CUBE15/DayNight"
{
    Properties
    {
        _DayMap ("Day picture", 2D) = "white" {}
        _NightMap ("Night lights picture", 2D) = "black" {}
        _SunDir ("Sun direction (world, toward the Sun)", Vector) = (0, 0, 1, 0)
        _DayBrightness ("Day brightness", Range(0, 2)) = 1
        _NightFloor ("Night side brightness", Range(0, 1)) = 0.04
        _NightFloorTint ("Night side tint", Color) = (0.62, 0.72, 1, 1)
        _NightTint ("Night lights tint", Color) = (1, 0.86, 0.6, 1)
        _NightStrength ("Night lights strength", Range(0, 4)) = 1
        _Soft ("Terminator softness", Range(0.001, 0.5)) = 0.1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_DayMap);   SAMPLER(sampler_DayMap);
            TEXTURE2D(_NightMap); SAMPLER(sampler_NightMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _DayMap_ST;
                float4 _SunDir;
                float _DayBrightness;
                float _NightFloor;
                float4 _NightFloorTint;
                float4 _NightTint;
                float _NightStrength;
                float _Soft;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = TRANSFORM_TEX(i.uv, _DayMap);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 n = normalize(i.normalWS);
                float d = dot(n, normalize(_SunDir.xyz));
                float k = smoothstep(-_Soft, _Soft, d);                      // 1 = day, 0 = night
                half3 day = SAMPLE_TEXTURE2D(_DayMap, sampler_DayMap, i.uv).rgb * _DayBrightness;
                half3 lights = SAMPLE_TEXTURE2D(_NightMap, sampler_NightMap, i.uv).rgb * _NightTint.rgb * _NightStrength;
                half3 col = day * lerp(_NightFloor * _NightFloorTint.rgb, half3(1, 1, 1), k) + lights * (1.0 - k);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
