// 캐릭터 표시용 URP 셰이더. 게임 원본 셰이더 대신 쓴다.
// 염색식(원본 MMN/CH/Standard 픽셀 셰이더와 같은 계산):
//   g  = LinearToSRGB(sample(_BaseMap))          텍스처 원래 바이트/255
//   hi = SRGBToLinear(max((g - 0.5) * 2, 0))     0.5 위: 흰 하이라이트
//   w  = 1 - min(g * 2, 1), w *= (w >= 1/128)    0.5 아래: 채널별 염색 가중치
//   albedo = saturate((_DyeColor1*w.r + _DyeColor2*w.g + _DyeColor3*w.b) * 1.347368 + hi)
// 조명은 원본이 아니라 단순 툰 근사다. _AlbedoOnly = 1 이면 조명 없이 알베도만 출력한다.
// 캐릭터 몸의 그늘은 이 셰이더의 툰 명암과 헤어 그림자 메시(Hair_Shadow)로만 그린다.
// Unity 실시간 그림자(그림자 맵)는 받지 않는다(이중 그림자 방지, 2026-11-02). 드리우기(ShadowCaster)는 바닥 그림자용으로 남긴다.
// _UseTexAlpha = 1 이면 텍스처 알파를 투명도로 쓴다(눈 하이라이트·그림자 등 반투명 재질).
Shader "MobiAtelier/Character"
{
    Properties
    {
        _BaseMap ("Base Map (DyeMap)", 2D) = "white" {}
        _IsDyable ("Is Dyable", Float) = 1
        _DyeColor1 ("Dye Color 1", Color) = (1,1,1,1)
        _DyeColor2 ("Dye Color 2", Color) = (1,1,1,1)
        _DyeColor3 ("Dye Color 3", Color) = (1,1,1,1)
        _TintColor ("Tint Color", Color) = (1,1,1,1)
        _ShadeColor ("Shade Color", Color) = (0.62,0.55,0.6,1)
        _AlbedoOnly ("Albedo Only", Float) = 0
        _UseTexAlpha ("Use Texture Alpha", Float) = 0
        _Alpha ("Alpha", Range(0,1)) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
        _ZWrite ("ZWrite", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #pragma target 3.0
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float _IsDyable;
            float4 _DyeColor1, _DyeColor2, _DyeColor3, _TintColor, _ShadeColor;
            float _AlbedoOnly, _UseTexAlpha, _Alpha, _Cull, _SrcBlend, _DstBlend, _ZWrite;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 positionWS : TEXCOORD2; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            float3 DyeAlbedo (float3 s)
            {
                float3 g = LinearToSRGB(s);
                float3 hi = SRGBToLinear(max((g - 0.5) * 2.0, 0.0));
                float3 w = 1.0 - min(g * 2.0, 1.0);
                w *= step(1.0 / 128.0, w);
                return saturate((_DyeColor1.rgb * w.r + _DyeColor2.rgb * w.g + _DyeColor3.rgb * w.b) * 1.347368 + hi);
            }

            float4 frag (Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                float4 s = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                float3 albedo = (_IsDyable > 0.5) ? DyeAlbedo(s.rgb) : s.rgb;
                albedo *= _TintColor.rgb;
                float alpha = _Alpha * lerp(1.0, s.a, step(0.5, _UseTexAlpha));
                if (_AlbedoOnly > 0.5) return float4(albedo, alpha);

                float3 n = normalize(i.normalWS) * (front ? 1.0 : -1.0);
                Light L = GetMainLight();
                float ndl = dot(n, L.direction) * 0.5 + 0.5;
                float lit = smoothstep(0.42, 0.58, ndl);
                float3 shade = lerp(_ShadeColor.rgb, 1.0, lit);
                float3 rim = pow(1.0 - saturate(dot(n, normalize(GetWorldSpaceViewDir(i.positionWS)))), 4.0) * 0.12;
                float3 col = albedo * shade * L.color + rim * albedo;
                return float4(col, alpha);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float3 _LightDirection;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            float4 vert (Attributes v) : SV_POSITION
            {
                float3 p = TransformObjectToWorld(v.positionOS.xyz);
                float3 n = TransformObjectToWorldNormal(v.normalOS);
                float4 c = TransformWorldToHClip(ApplyShadowBias(p, n, _LightDirection));
                #if UNITY_REVERSED_Z
                c.z = min(c.z, UNITY_NEAR_CLIP_VALUE);
                #else
                c.z = max(c.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return c;
            }
            half4 frag () : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert (float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half frag () : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            float4 frag (Varyings i) : SV_Target { return float4(NormalizeNormalPerPixel(i.normalWS), 0.0); }
            ENDHLSL
        }
    }
}
