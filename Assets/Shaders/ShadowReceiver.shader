// 스튜디오 색 배경의 그림자 바닥(Background.md). 주광 그림자만 뒤 화면에 곱한다.
// 게임 그림자 바닥 프리팹의 원본 셰이더(MMN/MMN_ShadowOnly)는 이 앱의 URP 소프트 그림자 필터를 쓰지 않아 그림자 맵 칸이 계단으로 보였다.
// 그래서 바닥 재질만 이 셰이더로 바꾼다. 어둡기는 원본처럼 주광 그림자 세기(shadowStrength)를 따른다 [확인: 빌드 촬영 밝기 비 ≈ 0.55].
Shader "MobiAtelier/ShadowReceiver"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ShadowReceiver"
            Tags { "LightMode" = "UniversalForward" }
            Blend DstColor Zero
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half s = MainLightRealtimeShadow(TransformWorldToShadowCoord(i.positionWS));
                s = lerp(s, 1, GetMainLightShadowFade(i.positionWS));
                return half4(s, s, s, 1);
            }
            ENDHLSL
        }
    }
}
