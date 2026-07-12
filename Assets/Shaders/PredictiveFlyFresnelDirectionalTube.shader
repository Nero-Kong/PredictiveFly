Shader "PredictiveFly/Fresnel Directional Tube"
{
    Properties
    {
        [HDR] _BaseColor ("Wall Color", Color) = (0.55, 0.9, 1, 0.14)
        [HDR] _FresnelColor ("Fresnel Color", Color) = (0.5, 0.95, 1, 1)
        _FresnelAlpha ("Fresnel Alpha", Range(0, 0.8)) = 0.28
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 2.2
        [HDR] _TopGuideColor ("Top Guide Color", Color) = (0.92, 0.98, 1, 1)
        [HDR] _SideGuideColor ("Side Guide Color", Color) = (0.16, 0.82, 1, 1)
        _GuideAlpha ("Guide Alpha", Range(0, 1)) = 0.78
        _GuideHalfWidth ("Guide Angular Half Width", Range(0.001, 0.02)) = 0.003
        _SideDashPeriod ("Side Dash Period (m)", Float) = 4
        _SideDashDuty ("Side Dash Duty", Range(0.1, 0.9)) = 0.55
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Blend One OneMinusSrcAlpha
            Cull Back
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _FresnelColor;
                float _FresnelAlpha;
                float _FresnelPower;
                half4 _TopGuideColor;
                half4 _SideGuideColor;
                float _GuideAlpha;
                float _GuideHalfWidth;
                float _SideDashPeriod;
                float _SideDashDuty;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.uv = input.uv;
                return output;
            }

            float PeriodicDistance(float value, float center)
            {
                return abs(frac(value - center + 0.5) - 0.5);
            }

            float GuideLineMask(float u, float center)
            {
                float antialias = max(fwidth(u) * 1.5, 0.00005);
                float distanceToLine = PeriodicDistance(u, center);
                return 1.0 - smoothstep(_GuideHalfWidth, _GuideHalfWidth + antialias, distanceToLine);
            }

            float SideDashMask(float distanceMeters)
            {
                float period = max(_SideDashPeriod, 0.25);
                float normalizedDistance = distanceMeters / period;
                float phase = frac(normalizedDistance);
                float antialias = max(fwidth(normalizedDistance) * 1.5, 0.001);
                float fadeIn = smoothstep(0.0, antialias, phase);
                float fadeOut = 1.0 - smoothstep(
                    max(0.0, _SideDashDuty - antialias),
                    min(1.0, _SideDashDuty + antialias),
                    phase);
                return saturate(fadeIn * fadeOut);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half3 normalWS = normalize(input.normalWS);
                half3 viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float fresnel = pow(
                    saturate(1.0 - abs(dot(normalWS, viewDirectionWS))),
                    max(_FresnelPower, 0.5));

                float wallAlpha = saturate(_BaseColor.a + fresnel * _FresnelAlpha);
                half3 wallColor = lerp(_BaseColor.rgb, _FresnelColor.rgb, fresnel);
                half3 premultipliedColor = wallColor * wallAlpha;
                float outputAlpha = wallAlpha;

                float topMask = GuideLineMask(input.uv.x, 0.25);
                float sideMask = max(
                    GuideLineMask(input.uv.x, 0.0),
                    GuideLineMask(input.uv.x, 0.5));
                sideMask *= SideDashMask(input.uv.y);

                float topAlpha = saturate(topMask * _GuideAlpha * _TopGuideColor.a);
                premultipliedColor = _TopGuideColor.rgb * topAlpha
                    + premultipliedColor * (1.0 - topAlpha);
                outputAlpha = topAlpha + outputAlpha * (1.0 - topAlpha);

                float sideAlpha = saturate(sideMask * _GuideAlpha * _SideGuideColor.a);
                premultipliedColor = _SideGuideColor.rgb * sideAlpha
                    + premultipliedColor * (1.0 - sideAlpha);
                outputAlpha = sideAlpha + outputAlpha * (1.0 - sideAlpha);

                return half4(premultipliedColor, outputAlpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
