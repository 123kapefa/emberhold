// Туман за пределами света. Полноэкранный проход URP (Full Screen Pass Renderer Feature) перед
// прозрачными объектами: по глубине восстанавливает точку мира, считает расстояние по горизонтали
// до ближайшего круга света и высоту точки, накладывает цвет тумана. Параметры задаёт компонент LightFog;
// формула плотности совпадает с FogLook.DensityAt(distanceOutside, height).
// Тот же проход рисует море тумана за краем мира (компонент WorldEdgeFog, #102): та же формула, только
// расстояние — до прямоугольника мира, а небо ниже горизонта целиком под пеленой.
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
            // x — ниже этой высоты туман в полную силу, y — выше неё тумана нет.
            float4 _EmberholdFogHeight;

            // Туман за краем мира: x — непрозрачность, y — 1 / falloff, z — 1, когда включён, w — на сколько блоков
            // внутрь от края он заходит.
            float4 _EmberholdEdgeFogParams;
            float4 _EmberholdEdgeFogColor;
            // Прямоугольник мира: xy — минимум (x, z), zw — максимум.
            float4 _EmberholdEdgeFogRect;
            // x — ниже этой высоты пелена в полную силу, y — выше неё её нет.
            float4 _EmberholdEdgeFogHeight;

            // Как FogLook.HeightFactor: 1 ниже height.x, 0 выше height.y, между ними smoothstep.
            float HeightFactor(float y, float4 height)
            {
                float t = saturate((height.y - y) / max(height.y - height.x, 1e-4));
                return t * t * (3.0 - 2.0 * t);
            }

            // Как WorldEdgeFog.DistanceOutside: снаружи — до ближайшей точки края, внутри — со знаком минус.
            float DistanceOutsideRect(float2 p, float4 rect)
            {
                float2 d = max(rect.xy - p, p - rect.zw);
                return d.x <= 0.0 && d.y <= 0.0 ? max(d.x, d.y) : length(max(d, 0.0));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                bool lightFog = _EmberholdFogParams.z >= 0.5;
                bool edgeFog = _EmberholdEdgeFogParams.z >= 0.5;
                if (!lightFog && !edgeFog)
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
                float3 world = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);

                // Небо бесконечно далеко от любого огня: там туман самый густой, высота не важна.
                float lightDensity = 0.0;
                if (lightFog)
                {
                    float outside = 1e6;
                    float heightFactor = 1.0;
                    if (!sky)
                    {
                        heightFactor = HeightFactor(world.y, _EmberholdFogHeight);
                        int count = (int)_EmberholdLightCount;
                        for (int i = 0; i < EMBERHOLD_MAX_LIGHTS; i++)
                        {
                            if (i >= count)
                                break;
                            float4 circle = _EmberholdLightCircles[i];
                            outside = min(outside, distance(world.xz, circle.xy) - circle.z);
                        }
                    }
                    lightDensity = _EmberholdFogParams.x * (1.0 - exp(-max(outside, 0.0) * _EmberholdFogParams.y)) * heightFactor;
                }

                // Море тумана за краем: земля — по расстоянию за черту у края и по высоте; небо ниже горизонта
                // целиком под пеленой (луч рано или поздно уходит в неё), к горизонту пелена плавно редеет.
                float edgeDensity = 0.0;
                if (edgeFog)
                {
                    if (sky)
                    {
                        float up = normalize(world - _WorldSpaceCameraPos).y;
                        edgeDensity = _EmberholdEdgeFogParams.x * (1.0 - smoothstep(-0.01, 0.08, up));
                    }
                    else
                    {
                        float outside = DistanceOutsideRect(world.xz, _EmberholdEdgeFogRect) + _EmberholdEdgeFogParams.w;
                        edgeDensity = _EmberholdEdgeFogParams.x * (1.0 - exp(-max(outside, 0.0) * _EmberholdEdgeFogParams.y))
                                      * HeightFactor(world.y, _EmberholdEdgeFogHeight);
                    }
                }

                // Два слоя тумана друг на друге: пелена за краем поверх тумана от света.
                float alpha = 1.0 - (1.0 - lightDensity) * (1.0 - edgeDensity);
                if (alpha <= 1e-5)
                    return 0;
                float3 color = (_EmberholdFogColor.rgb * lightDensity * (1.0 - edgeDensity) + _EmberholdEdgeFogColor.rgb * edgeDensity) / alpha;
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
