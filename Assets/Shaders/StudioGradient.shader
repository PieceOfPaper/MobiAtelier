// 스튜디오 색 배경(Background.md). 화면 네 모서리 색을 화면 좌표로 섞는 스카이박스다.
// 카메라를 돌려도 배경은 화면에 고정된다. 색은 sRGB 값끼리 섞는다(UI 정점색 그라데이션과 같은 방식) [추정].
Shader "MobiAtelier/StudioGradient"
{
    Properties
    {
        _LeftBottom ("Left Bottom", Color) = (1,1,1,1)
        _RightBottom ("Right Bottom", Color) = (1,1,1,1)
        _LeftUpper ("Left Upper", Color) = (1,1,1,1)
        _RightUpper ("Right Upper", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _LeftBottom, _RightBottom, _LeftUpper, _RightUpper;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 screenPos : TEXCOORD0; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            float4 frag (Varyings i) : SV_Target
            {
                float2 uv = saturate(i.screenPos.xy / i.screenPos.w);
                float3 bottom = lerp(LinearToSRGB(_LeftBottom.rgb), LinearToSRGB(_RightBottom.rgb), uv.x);
                float3 top = lerp(LinearToSRGB(_LeftUpper.rgb), LinearToSRGB(_RightUpper.rgb), uv.x);
                return float4(SRGBToLinear(lerp(bottom, top, uv.y)), 1);
            }
            ENDHLSL
        }
    }
}
