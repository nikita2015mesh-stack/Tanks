Shader "Mad Machines/Driving Feedback"
{
    Properties
    {
        [HideInInspector] _Mode ("Mode", Float) = 0
        [HideInInspector] _TrackTime ("Track time", Float) = 0
        [HideInInspector] _TrackLifetime ("Track lifetime", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
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

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                half opacity;
                if (_Mode > 2.5)
                {
                    float variation = sin(uv.x * 32.0 - _Time.y * 13.0);
                    float across = abs(uv.y * 2.0 - 1.0 + variation * 0.24);
                    opacity = (1.0 - smoothstep(0.16, 0.86, across)) * lerp(0.45, 1.0, variation * 0.5 + 0.5);
                }
                else if (_Mode > 1.5)
                {
                    opacity = 1.0 - smoothstep(0.2, 1.0, abs(uv.y * 2.0 - 1.0));
                }
                else if (_Mode > 0.5)
                {
                    float2 p = uv * 2.0 - 1.0;
                    float r = dot(p, p);
                    float lobes = 0.047 * sin(p.x * 9.0 + p.y * 4.0) + 0.038 * cos(p.y * 13.0 - p.x * 5.0);
                    opacity = 1.0 - smoothstep(0.04, 0.84, r + lobes);
                }
                else
                {
                    float across = abs(uv.x * 2.0 - 1.0);
                    float edge = 1.0 - smoothstep(0.71, 1.0, across);
                    float groove = abs(frac(uv.y - across * 0.35) - 0.5);
                    float tread = 1.0 - smoothstep(0.29, 0.43, groove);
                    opacity = edge * lerp(0.47, 1.0, tread);
                }
                return half4(MixFog(input.color.rgb, input.fog), input.color.a * opacity * input.visibility);
            }
            ENDHLSL
        }
    }
}
