// 캐릭터 표시용 URP 셰이더. 게임 원본 셰이더 대신 쓴다.
// 염색식(원본 MMN/CH/Standard 픽셀 셰이더와 같은 계산):
//   g  = LinearToSRGB(sample(_BaseMap))          텍스처 원래 바이트/255
//   hi = SRGBToLinear(max((g - 0.5) * 2, 0))     0.5 위: 흰 하이라이트
//   w  = 1 - min(g * 2, 1), w *= (w >= 1/128)    0.5 아래: 채널별 염색 가중치
//   albedo = saturate((_DyeColor1*w.r + _DyeColor2*w.g + _DyeColor3*w.b) * 1.347368 + hi)
// 조명은 원본이 아니라 단순 툰 근사다. _AlbedoOnly = 1 이면 조명 없이 알베도만 출력한다.
// 빛은 전역 _MobiCharLightDir(xyz 빛 쪽 방향, w = 1 이면 사용)·_MobiCharLightColor 를 쓰고, 없으면 URP 주광을 쓴다.
//   배경 환경 볼륨이 주광을 바꿔도 캐릭터는 앱 기본 조명을 유지한다(EnvironmentVolume.cs).
// 캐릭터 몸의 그늘은 이 셰이더의 툰 명암과 헤어 그림자 메시(Hair_Shadow)로만 그린다.
// Unity 실시간 그림자(그림자 맵)는 받지 않는다(이중 그림자 방지, 2026-11-02). 드리우기(ShadowCaster)는 바닥 그림자용으로 남긴다.
// _UseTexAlpha = 1 이면 텍스처 알파를 투명도로 쓴다(눈 하이라이트·그림자 등 반투명 재질).
// 몸꾸밈(_TattooOn = 1): 원본 MMN/CH/Skin_Body 픽셀 셰이더의 문신 칸을 옮겼다(2026-11-12 디스어셈블 확인).
//   uv' = 회전(_TattooMapRotation 도, 중심 0.5)( uv / s + (1 - 1/s) * 0.5 - p * 0.1 ),  _TattooMapScalePosition = (s.x, s.y, p.x, p.y)
//   문신: t.rgb * (_DyeColor1 * 0.7 + 0.3)          (_DyeColor1 = 피부색)
//   흉터(_IsScarMode = 1): 텍스처를 HSV 로 바꿔 명도를 피부 밝기에 맞추고 피부색 쪽으로 물들인다.
//   albedo = lerp(albedo * _TintColor, 꾸밈색, t.a).
//   문신(흉터 아님)의 발광·반사(2026-11-19 디스어셈블 확인): 꾸밈색 += 발광맵(같은 uv).rgb × _TattooEmissionIntensity × (1 − 낮 × _IsEnableTattooEmissionAtNight)
//     + (_TattooSpecula ≥ 0.01 ? pow(max(N·V, 0), _TattooSpecula) × _TattooSpeculaIntensity : 0).  V = 카메라 쪽 방향. 조명 전에 알베도에 더한다.
//     원본의 낮 값은 전역 셰이더 값이다. 앱은 낮(1)으로 본다 — "밤에만 발광" 문신은 발광하지 않는다 [추정: 전역 값의 뜻].
// 무기 오라(WeaponGradeOutline 패스, Equipment.md "무기 오라"): 원본 MMN/CH/Weapon 의 TwoPassOutline 패스를 옮겼다(2026-11-18 디스어셈블 확인).
//   정점: 월드 위치 + 월드 법선 × 0.007, 앞면 컬링·깊이 쓰기 없음·알파 블렌드.
//   _WeaponGradeColor.w = 등급 번호(Rarity). 8·9(전설+·전설++): sin(|uv − 0.5| × 12 − 2t)를 BlendMin~Max 로 smoothstep 해 두 색을 섞는다.
//   4 이상: _WeaponGradeColor. 4 미만: 그리지 않는다. 색 × 4, 알파 = max(pow(sat(노이즈1 × 노이즈2), 3.8), 0.02)
//   노이즈 = _Global_Weapon_Texture.r (uv × 5 + frac(0.1t) + 1), (uv + frac(−0.2t) + 1).
//   이 패스는 외곽선 전용 재질에서만 켠다(CharacterAssembler.ConvertMaterial 은 끈다).
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
        _TattooOn ("Tattoo On", Float) = 0
        _TattooMap ("Tattoo Map", 2D) = "black" {}
        _TattooMapScalePosition ("Tattoo Scale Position", Vector) = (1,1,0,0)
        _TattooMapRotation ("Tattoo Rotation", Float) = 0
        _IsScarMode ("Is Scar Mode", Float) = 0
        _TattooEmissionMap ("Tattoo Emission Map", 2D) = "black" {}
        _TattooEmissionIntensity ("Tattoo Emission Intensity", Float) = 0
        _IsEnableTattooEmissionAtNight ("Tattoo Emission At Night Only", Float) = 0
        _TattooSpecula ("Tattoo Specular Power", Float) = 0
        _TattooSpeculaIntensity ("Tattoo Specular Intensity", Float) = 0
        _Alpha ("Alpha", Range(0,1)) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
        _ZWrite ("ZWrite", Float) = 1
        _WeaponGradeColor ("Weapon Grade Color (w = Rarity)", Vector) = (0,0,0,0)
        _WeaponGradeColor2 ("Weapon Grade Color 2", Color) = (0,0,0,0)
        _OutlineColorBlendMin ("Outline Color Blend Min", Float) = 0
        _OutlineColorBlendMax ("Outline Color Blend Max", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #pragma target 3.0
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_TattooMap); SAMPLER(sampler_TattooMap);
        TEXTURE2D(_TattooEmissionMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float _IsDyable;
            float4 _DyeColor1, _DyeColor2, _DyeColor3, _TintColor, _ShadeColor;
            float _AlbedoOnly, _UseTexAlpha, _Alpha, _Cull, _SrcBlend, _DstBlend, _ZWrite;
            float4 _TattooMapScalePosition;
            float _TattooOn, _TattooMapRotation, _IsScarMode;
            float _TattooEmissionIntensity, _IsEnableTattooEmissionAtNight, _TattooSpecula, _TattooSpeculaIntensity;
            float4 _WeaponGradeColor, _WeaponGradeColor2;
            float _OutlineColorBlendMin, _OutlineColorBlendMax;
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
            float4 _MobiCharLightDir, _MobiCharLightColor;

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

            float3 ScarColor (float3 t)
            {
                float3 skin = _DyeColor1.rgb;
                float v = max(skin.r, max(skin.g, skin.b));
                // RGB → HSV(원본과 같은 근사, 0.0001 보정)
                float4 k = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = lerp(float4(t.bg, k.wz), float4(t.gb, k.xy), step(t.b, t.g));
                float4 q = lerp(float4(p.xyw, t.r), float4(t.r, p.yzx), step(p.x, t.r));
                float d = q.x - min(q.w, q.y);
                float h = abs(q.z + (q.w - q.y) / (6.0 * d + 0.0001));
                float sat = d / (q.x + 0.0001);
                float nv = saturate(v * 0.28 + q.x - 0.08);
                float3 hue = saturate(abs(frac(h + float3(1.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0);
                float3 c = nv * ((hue - 1.0) * sat + 1.0);
                c = lerp(t, c, saturate((sat - 0.48) * 25.0));
                float y = 1.0 - v * 0.1;
                return c * (skin * y + (1.0 - y));
            }

            float4 Tattoo (float2 uv, float ndv)
            {
                float2 sc = _TattooMapScalePosition.xy;
                float2 u = uv / sc + (1.0 - 1.0 / sc) * 0.5 - _TattooMapScalePosition.zw * 0.1 - 0.5;
                float sn, cs;
                sincos(_TattooMapRotation * 0.0174533, sn, cs);
                u = float2(dot(u, float2(cs, sn)), dot(u, float2(-sn, cs))) + 0.5;
                float4 t = SAMPLE_TEXTURE2D(_TattooMap, sampler_TattooMap, u);
                if (_IsScarMode > 0.5) { t.rgb = ScarColor(t.rgb); return t; }
                float spec = _TattooSpecula >= 0.01 ? pow(max(ndv, 0.0), _TattooSpecula) * _TattooSpeculaIntensity : 0.0;
                float3 em = SAMPLE_TEXTURE2D(_TattooEmissionMap, sampler_TattooMap, u).rgb * _TattooEmissionIntensity * (1.0 - _IsEnableTattooEmissionAtNight);
                t.rgb = t.rgb * (_DyeColor1.rgb * 0.7 + 0.3) + em + spec;
                return t;
            }

            float4 frag (Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                float4 s = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                float3 albedo = (_IsDyable > 0.5) ? DyeAlbedo(s.rgb) : s.rgb;
                albedo *= _TintColor.rgb;
                if (_TattooOn > 0.5)
                {
                    float ndv = dot(normalize(i.normalWS) * (front ? 1.0 : -1.0), normalize(GetWorldSpaceViewDir(i.positionWS)));
                    float4 t = Tattoo(i.uv, ndv);
                    albedo = lerp(albedo, t.rgb, saturate(t.a));
                }
                float alpha = _Alpha * lerp(1.0, s.a, step(0.5, _UseTexAlpha));
                if (_AlbedoOnly > 0.5) return float4(albedo, alpha);

                float3 n = normalize(i.normalWS) * (front ? 1.0 : -1.0);
                Light L = GetMainLight();
                if (_MobiCharLightDir.w > 0.5) { L.direction = normalize(_MobiCharLightDir.xyz); L.color = _MobiCharLightColor.rgb; }
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

        Pass
        {
            Name "WeaponGradeOutline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front ZWrite Off ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            TEXTURE2D(_Global_Weapon_Texture);
            SamplerState sampler_linear_repeat;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert (Attributes v)
            {
                Varyings o;
                float3 n = normalize(TransformObjectToWorldNormal(v.normalOS));
                float3 p = TransformObjectToWorld(v.positionOS.xyz) + n * 0.007;
                // 등급이 없는 재질은 삼각형을 한 점으로 모아 그리지 않는다.
                o.positionCS = _WeaponGradeColor.w < 3.5 ? float4(0, 0, 0, 0) : TransformWorldToHClip(p);
                o.uv = v.uv;
                return o;
            }
            float4 frag (Varyings i) : SV_Target
            {
                float g = _WeaponGradeColor.w;
                if (g < 3.5) return 0;
                float3 c = _WeaponGradeColor.rgb;
                if (abs(g - 8.0) < 0.5 || abs(g - 9.0) < 0.5)
                {
                    float s = sin(length(i.uv - 0.5) * 12.0 - 2.0 * _Time.y);
                    float k = saturate((s - _OutlineColorBlendMin) / max(_OutlineColorBlendMax - _OutlineColorBlendMin, 1e-4));
                    c = lerp(_WeaponGradeColor.rgb, _WeaponGradeColor2.rgb, k * k * (3.0 - 2.0 * k));
                }
                float2 a = i.uv * 5.0 + frac(_Time.y * 0.1) + 1.0;
                float2 b = i.uv + frac(_Time.y * -0.2) + 1.0;
                float nz = saturate(SAMPLE_TEXTURE2D(_Global_Weapon_Texture, sampler_linear_repeat, a).r *
                                    SAMPLE_TEXTURE2D(_Global_Weapon_Texture, sampler_linear_repeat, b).r);
                return float4(c * 4.0, max(pow(nz, 3.8), 0.02));
            }
            ENDHLSL
        }
    }
}
