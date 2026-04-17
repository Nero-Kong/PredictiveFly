Shader "Custom/InfiniteBWGrid" {
    Properties {
        _CellSize ("Cell Size (m)", Float) = 1
        _ColorA ("White", Color) = (1,1,1,1)
        _ColorB ("Black", Color) = (0,0,0,1)
        _FarFadeStart ("Far Fade Start (m)", Float) = 18
        _FarFadeEnd ("Far Fade End (m)", Float) = 55
        _FarColor ("Far Blend Color", Color) = (0.5,0.5,0.5,1)
    }

    SubShader {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }

        Pass {
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                float _CellSize;
                float4 _ColorA;
                float4 _ColorB;
                float _FarFadeStart;
                float _FarFadeEnd;
                float4 _FarColor;
            CBUFFER_END

            Varyings vert (Attributes IN) {
                UNITY_SETUP_INSTANCE_ID (IN);
                Varyings OUT;
                UNITY_TRANSFER_INSTANCE_ID (IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO (OUT);
                VertexPositionInputs posInputs = GetVertexPositionInputs (IN.positionOS.xyz);
                OUT.positionCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                return OUT;
            }

            float CheckerValue (float2 p) {
                float2 c = floor (p);
                return abs (fmod (c.x + c.y, 2.0));
            }

            half4 frag (Varyings IN) : SV_Target {
                UNITY_SETUP_INSTANCE_ID (IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX (IN);

                float cell = max (_CellSize, 1e-4);
                float2 p = IN.positionWS.xz / cell;

                // Derivative-based 4-tap supersampling to reduce distance aliasing/moire.
                float2 dx = ddx (p) * 0.5;
                float2 dy = ddy (p) * 0.5;
                float checker =
                    CheckerValue (p + dx + dy) +
                    CheckerValue (p + dx - dy) +
                    CheckerValue (p - dx + dy) +
                    CheckerValue (p - dx - dy);
                checker *= 0.25;

                float3 col = lerp (_ColorA.rgb, _ColorB.rgb, checker);

                // Fade high-frequency pattern to neutral color in distance.
                float fadeDen = max (0.001, _FarFadeEnd - _FarFadeStart);
                float fadeT = saturate ((distance (IN.positionWS, _WorldSpaceCameraPos.xyz) - _FarFadeStart) / fadeDen);
                col = lerp (col, _FarColor.rgb, fadeT);

                return half4 (col, 1);
            }
            ENDHLSL
        }
    }
}
