Shader "Mad Machines/Driving Feedback"
{
    Properties
    {
        [HideInInspector] _Mode ("Mode", Float) = 0
        [HideInInspector] _TrackTime ("Track time", Float) = 0
        [HideInInspector] _TrackLifetime ("Track lifetime", Float) = 1
        [HideInInspector] _DstBlend ("Destination blend", Float) = 10
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha [_DstBlend]
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Mode;
                float _TrackTime;
                float _TrackLifetime;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 age : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                half visibility : TEXCOORD1;
                half fog : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                output.visibility = _Mode > 0.5 ? 1.0 :
                    1.0 - smoothstep(0.15, _TrackLifetime, _TrackTime - input.age.x);
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            float EffectHash(float2 position)
            {
                float3 p = frac(position.xyx * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float CloudNoise(float2 position)
            {
                float2 cell = floor(position);
                float2 f = frac(position);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(EffectHash(cell), EffectHash(cell + float2(1, 0)), f.x),
                    lerp(EffectHash(cell + float2(0, 1)), EffectHash(cell + 1.0), f.x), f.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                half opacity;
                half3 color = input.color.rgb;
                if (_Mode > 4.5)
                {
                    float2 p = uv * 2.0 - 1.0;
                    float noise = CloudNoise(p * 4.5) * 0.7 + CloudNoise(p * 13.0) * 0.3;
                    opacity = 1.0 - smoothstep(0.15, 0.95, length(p) + (noise - 0.44) * 0.64);
                    color *= lerp(0.65, 1.75, opacity * noise + opacity * 0.32);
                }
                else if (_Mode > 3.5)
                {
                    float along = 1.0 - uv.x;
                    float width = lerp(0.09, 0.95, smoothstep(0.0, 0.3, along)) * pow(max(0.0, 1.0 - along), 0.78);
                    float across = abs(uv.y * 2.0 - 1.0);
                    float streaks = lerp(0.6, 1.25, CloudNoise(float2(along * 11.0, uv.y * 8.0)));
                    float core = 1.0 - smoothstep(0.0, 0.25, across / max(width, 0.01));
                    opacity = (1.0 - smoothstep(width * 0.16, width + 0.02, across)) *
                        (1.0 - along * along) * streaks;
                    color = lerp(color * 1.6, half3(3.3, 2.65, 1.55), core);
                }
                else if (_Mode > 2.5)
                {
                    float variation = sin(uv.x * 32.0 - _Time.y * 13.0);
                    float across = abs(uv.y * 2.0 - 1.0 + variation * 0.24);
                    opacity = (1.0 - smoothstep(0.16, 0.86, across)) * lerp(0.45, 1.0, variation * 0.5 + 0.5);
                }
                else if (_Mode > 1.5)
                {
                    float across = abs(uv.y * 2.0 - 1.0);
                    opacity = 1.0 - smoothstep(0.12, 1.0, across);
                    color *= lerp(1.15, 2.8, 1.0 - smoothstep(0.0, 0.48, across));
                }
                else if (_Mode > 0.5)
                {
                    float2 p = uv * 2.0 - 1.0;
                    float noise = CloudNoise(p * 4.0) * 0.73 + CloudNoise(p * 10.0) * 0.27;
                    opacity = (1.0 - smoothstep(0.16, 0.98, length(p) + (noise - 0.47) * 0.58)) *
                        lerp(0.49, 1.0, noise);
                    color *= lerp(0.58, 1.13, noise);
                }
                else
                {
                    float across = abs(uv.x * 2.0 - 1.0);
                    float edge = 1.0 - smoothstep(0.71, 1.0, across);
                    float groove = abs(frac(uv.y - across * 0.35) - 0.5);
                    float tread = 1.0 - smoothstep(0.29, 0.43, groove);
                    opacity = edge * lerp(0.47, 1.0, tread);
                }
                // Additive effects fade into the background without adding the fog color.
                bool additive = (_Mode > 1.5 && _Mode < 2.5) || _Mode > 3.5;
                half3 fog = additive ? half3(0, 0, 0) : half3(unity_FogColor.rgb);
                return half4(MixFogColor(color, fog, input.fog), input.color.a * opacity * input.visibility);
            }
            ENDHLSL
        }
    }
}
