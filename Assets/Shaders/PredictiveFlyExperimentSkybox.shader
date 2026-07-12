Shader "PredictiveFly/Experiment Sparse Skybox"
{
    Properties
    {
        [HDR] _TopColor ("Top Color", Color) = (0.025, 0.035, 0.055, 1)
        [HDR] _HorizonColor ("Horizon Color", Color) = (0.06, 0.045, 0.035, 1)
        [HDR] _BottomColor ("Bottom Color", Color) = (0.012, 0.016, 0.025, 1)
        [HDR] _StarColor ("Star Color", Color) = (0.65, 0.72, 0.78, 1)
        _HorizonStrength ("Horizon Strength", Range(0, 1)) = 0.55
        _StarDensity ("Star Density", Range(0, 0.25)) = 0.045
        _StarGrid ("Star Grid", Range(8, 64)) = 30
        _StarSize ("Star Size", Range(0.01, 0.15)) = 0.055
        _StarIntensity ("Star Intensity", Range(0, 2)) = 0.7
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
        }

        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            float4 _TopColor;
            float4 _HorizonColor;
            float4 _BottomColor;
            float4 _StarColor;
            float _HorizonStrength;
            float _StarDensity;
            float _StarGrid;
            float _StarSize;
            float _StarIntensity;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = UnityObjectToClipPos(input.positionOS);
                output.direction = input.positionOS.xyz;
                return output;
            }

            float Hash21(float2 value)
            {
                float3 p = frac(value.xyx * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float2 Hash22(float2 value)
            {
                float first = Hash21(value);
                return float2(first, Hash21(value + first + 19.19));
            }

            void GetCubeFaceUv(float3 direction, out float2 uv, out float face)
            {
                float3 axis = abs(direction);
                if (axis.x >= axis.y && axis.x >= axis.z)
                {
                    uv = direction.zy / max(axis.x, 0.0001);
                    face = direction.x >= 0.0 ? 0.0 : 1.0;
                    return;
                }

                if (axis.y >= axis.z)
                {
                    uv = direction.xz / max(axis.y, 0.0001);
                    face = direction.y >= 0.0 ? 2.0 : 3.0;
                    return;
                }

                uv = direction.xy / max(axis.z, 0.0001);
                face = direction.z >= 0.0 ? 4.0 : 5.0;
            }

            float SampleStars(float3 direction)
            {
                float2 uv;
                float face;
                GetCubeFaceUv(direction, uv, face);

                float2 gridPosition = (uv * 0.5 + 0.5) * _StarGrid;
                float2 cell = floor(gridPosition);
                float2 localPosition = frac(gridPosition) - 0.5;
                float2 faceOffset = float2(face * 113.0, face * 47.0);
                float2 key = cell + faceOffset;

                float seed = Hash21(key);
                float exists = step(1.0 - _StarDensity, seed);
                float2 offset = (Hash22(key + 7.31) - 0.5) * 0.62;
                float size = lerp(_StarSize * 0.55, _StarSize, Hash21(key + 23.17));
                float distanceToStar = length(localPosition - offset);
                float antialias = clamp(fwidth(distanceToStar) * 1.5, 0.003, 0.02);
                float disc = 1.0 - smoothstep(size, size + antialias, distanceToStar);
                float brightness = lerp(0.55, 1.0, Hash21(key + 51.73));
                return exists * disc * brightness;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.direction);
                float verticalBlend = smoothstep(-0.35, 0.75, direction.y);
                float3 color = lerp(_BottomColor.rgb, _TopColor.rgb, verticalBlend);
                float horizon = pow(saturate(1.0 - abs(direction.y)), 4.0);
                color = lerp(color, _HorizonColor.rgb, horizon * _HorizonStrength);

                float stars = SampleStars(direction);
                color += stars * _StarColor.rgb * _StarIntensity;
                return float4(color, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
