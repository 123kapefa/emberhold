// Туман за пределами света. Полноэкранный проход URP (Full Screen Pass Renderer Feature) перед
// прозрачными объектами: по глубине восстанавливает точку мира, считает расстояние по горизонтали
// до ближайшего круга света и накладывает цвет тумана. Параметры задаёт компонент LightFog;
// формула плотности совпадает с FogLook.DensityAt.
Shader "Emberhold/Fog/LightFog"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "LightFog"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #define EMBERHOLD_MAX_LIGHTS 16

            // x — непрозрачность вдали, y — 1 / falloff, z — 1, когда туман включён.
            float4 _EmberholdFogParams;
            float4 _EmberholdFogColor;
            // xy — центр круга в мире (x, z), z — радиус.
            float4 _EmberholdLightCircles[EMBERHOLD_MAX_LIGHTS];
            float _EmberholdLightCount;

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                if (_EmberholdFogParams.z < 0.5)
                    return 0;

                // Экранные UV из позиции пикселя: так же их видят глубина и матрицы камеры на любой платформе.
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                #if UNITY_REVERSED_Z
                    float depth = SampleSceneDepth(uv);
                    bool sky = depth <= 0.0;
                #else
                    float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(uv));
                    bool sky = depth >= 1.0;
                #endif

                // Небо бесконечно далеко от любого огня: там туман самый густой.
                float outside = 1e6;
                if (!sky)
                {
                    float3 world = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                    int count = (int)_EmberholdLightCount;
                    for (int i = 0; i < EMBERHOLD_MAX_LIGHTS; i++)
                    {
                        if (i >= count)
                            break;
                        float4 circle = _EmberholdLightCircles[i];
                        outside = min(outside, distance(world.xz, circle.xy) - circle.z);
                    }
                }

                float density = _EmberholdFogParams.x * (1.0 - exp(-max(outside, 0.0) * _EmberholdFogParams.y));
                return half4(_EmberholdFogColor.rgb, density);
            }
            ENDHLSL
        }
    }
}
