// Копия LitForwardPass.hlsl из URP 17.6 с отличиями: текстура материала кладётся по трём проекциям мира
// (без растяжения на крутых склонах), вблизи камеры добавляется мелкая деталь из альфа-канала, вдали рисунок
// упрощается, и трава сменяется текстурой камня по весу из UV3.x. Трава окрашена оттенком своего места
// (по локациям) из цвета вершины, а у Топей, Мёртвого города и Гор своя текстура травы — по весам из UV4.
// На верх блоков земли и камня рядом с травой заходит трава (_GrassMap)
// по маске соседей из UV3.y и месту в ячейке из UV3.zw. Граница травы везде одна: резкая, рваная,
// с зубцами-травинками и тёмной каймой. У мешей без UV3 веса 0, и остаётся своя текстура материала.
#ifndef EMBERHOLD_GROUND_FORWARD_PASS_INCLUDED
#define EMBERHOLD_GROUND_FORWARD_PASS_INCLUDED

// Для трёх проекций текстуры нужна точка в мире.
#ifndef REQUIRES_WORLD_SPACE_POS_INTERPOLATOR
    #define REQUIRES_WORLD_SPACE_POS_INTERPOLATOR 1
#endif
#include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"

// Текстура камня для перехода гладкой земли из травы в камень. Текстуры не входят в UnityPerMaterial,
// поэтому SRP Batcher не ломается.
TEXTURE2D(_RockMap);
SAMPLER(sampler_RockMap);
// Текстура травы для бахромы травы на краю блоков земли и камня рядом с травой (вес в UV3.y).
TEXTURE2D(_GrassMap);
SAMPLER(sampler_GrassMap);
// Трава локаций: Топи, Мёртвый город, Горы (веса в UV4.xyz, остаток — обычная трава). Сэмплер общий с _GrassMap.
TEXTURE2D(_GrassSwampMap);
TEXTURE2D(_GrassDeadMap);
TEXTURE2D(_GrassMountainMap);
// Цвет камня перехода гладкой земли — общее свойство, его ставит VoxelWorldView (цвет материала камня).
half4 _GroundRockColor;

// Оттенок травы своего места из цвета вершины: rgb — множитель, a — насыщенность (1 — как есть).
half3 TintGrass(half3 albedo, half4 tint)
{
    half luma = dot(albedo, half3(0.3h, 0.59h, 0.11h));
    return lerp(luma.xxx, albedo, tint.a) * tint.rgb;
}

// Сколько блоков занимает текстура по каждой проекции.
#define EMBERHOLD_GROUND_TILE 8.0
// Мелкая деталь из альфа-канала текстуры ложится во столько раз мельче рисунка и видна только вблизи:
// полностью ближе DETAIL_NEAR, гаснет к DETAIL_FAR (дистанция камеры 8–80).
#define EMBERHOLD_DETAIL_SCALE 4.0
#define EMBERHOLD_DETAIL_NEAR 14.0
#define EMBERHOLD_DETAIL_FAR 32.0
// Вдали рисунок упрощается к среднему цвету текстуры: меньше ряби, читаются крупные пятна.
#define EMBERHOLD_FLAT_NEAR 55.0
#define EMBERHOLD_FLAT_FAR 130.0
#define EMBERHOLD_FLAT_AMOUNT 0.55

// Вес проекций по нормали.
half3 TriplanarWeights(half3 normalWS)
{
    half3 w = pow(abs(normalWS), 4.0h);
    return w / max(w.x + w.y + w.z, 1e-4h);
}

// Три проекции одной текстуры в точке p (уже в плитках), с производными для мипов.
half4 SampleProjections(TEXTURE2D_PARAM(tex, samplerTex), float3 p, float3 dx, float3 dy, half3 w)
{
    return SAMPLE_TEXTURE2D_GRAD(tex, samplerTex, p.zy, dx.zy, dy.zy) * w.x
         + SAMPLE_TEXTURE2D_GRAD(tex, samplerTex, p.xz, dx.xz, dy.xz) * w.y
         + SAMPLE_TEXTURE2D_GRAD(tex, samplerTex, p.xy, dx.xy, dy.xy) * w.z;
}

// Текстура по трём проекциям мира: на крутых склонах не растягивается.
// detailFade — сколько мелкой детали добавить (1 вблизи), flatFade — насколько упростить рисунок (1 вдали).
// Производные позиции (dpdx, dpdy) считаются снаружи: функцию зовут и внутри ветвления, где своих производных нет.
half3 SampleTriplanar(TEXTURE2D_PARAM(tex, samplerTex), float3 positionWS, half3 normalWS, float3 dpdx, float3 dpdy,
    half detailFade, half flatFade)
{
    half3 w = TriplanarWeights(normalWS);
    float3 p = positionWS / EMBERHOLD_GROUND_TILE;
    float3 dx = dpdx / EMBERHOLD_GROUND_TILE;
    float3 dy = dpdy / EMBERHOLD_GROUND_TILE;
    half3 near = SampleProjections(TEXTURE2D_ARGS(tex, samplerTex), p, dx, dy, w).rgb;
    // Та же текстура в 5,3 раза крупнее: её яркость относительно плитки даёт пятна светлее и темнее,
    // и повтор плитки не читается.
    const float macro = 5.3;
    half3 far = SampleProjections(TEXTURE2D_ARGS(tex, samplerTex), p / macro + float3(0.37, 0.61, 0.13),
        dx / macro, dy / macro, w).rgb;
    half lumNear = dot(near, half3(0.3h, 0.59h, 0.11h));
    half lumFar = dot(far, half3(0.3h, 0.59h, 0.11h));
    half variation = clamp(0.55h + 0.45h * lumFar / max((lumNear + lumFar) * 0.5h, 0.05h), 0.8h, 1.2h);

    // Вдали — к среднему цвету текстуры (последний мип), пятна от крупного слоя остаются.
    half3 average = SAMPLE_TEXTURE2D_LOD(tex, samplerTex, float2(0.5, 0.5), 12).rgb;
    half3 color = lerp(near, average, flatFade * EMBERHOLD_FLAT_AMOUNT) * variation;

    // Вблизи — мелкая деталь из альфы (0,5 — без изменений), только там, где её видно.
    UNITY_BRANCH
    if (detailFade > 0.001h)
    {
        const float s = EMBERHOLD_DETAIL_SCALE;
        half detail = SampleProjections(TEXTURE2D_ARGS(tex, samplerTex), p * s + float3(0.21, 0.47, 0.83),
            dx * s, dy * s, w).a;
        color *= lerp(1.0h, clamp(detail * 2.0h, 0.7h, 1.3h), detailFade);
    }
    return color;
}

// Трава своего места: обычная трава meadow смешивается с травой локаций по весам kinds (x — топь,
// y — мёртвый город, z — горы). Текстуры локаций умножаются на scale (цвет материала, как у meadow).
half3 MixGrassKinds(half3 meadow, half3 kinds, half3 scale, float3 positionWS, half3 normalWS, float3 dpdx, float3 dpdy,
    half detailFade, half flatFade)
{
    kinds = saturate(kinds);
    half3 color = meadow * saturate(1.0h - kinds.x - kinds.y - kinds.z);
    UNITY_BRANCH
    if (kinds.x > 0.001h)
        color += kinds.x * scale * SampleTriplanar(TEXTURE2D_ARGS(_GrassSwampMap, sampler_GrassMap), positionWS, normalWS,
            dpdx, dpdy, detailFade, flatFade);
    UNITY_BRANCH
    if (kinds.y > 0.001h)
        color += kinds.y * scale * SampleTriplanar(TEXTURE2D_ARGS(_GrassDeadMap, sampler_GrassMap), positionWS, normalWS,
            dpdx, dpdy, detailFade, flatFade);
    UNITY_BRANCH
    if (kinds.z > 0.001h)
        color += kinds.z * scale * SampleTriplanar(TEXTURE2D_ARGS(_GrassMountainMap, sampler_GrassMap), positionWS, normalWS,
            dpdx, dpdy, detailFade, flatFade);
    return color;
}

// Шум значений по миру для рваной границы травы и камня.
float GroundHash(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

float GroundValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(GroundHash(i), GroundHash(i + float2(1, 0)), f.x),
                lerp(GroundHash(i + float2(0, 1)), GroundHash(i + float2(1, 1)), f.x), f.y);
}

// Край травы: крупная рваность (насколько шум сдвигает границу) и острые зубцы-травинки поверх неё,
// тёмная кайма травы у края и тень на земле или камне под ним.
#define EMBERHOLD_EDGE_RAGGED 0.6
// Зубцы-травинки: сколько на блок вдоль края и какой глубины, в блоках.
#define EMBERHOLD_TEETH_PER_BLOCK 7.0
#define EMBERHOLD_TEETH_DEPTH 0.28
#define EMBERHOLD_EDGE_RIM 0.12
#define EMBERHOLD_EDGE_RIM_DARK 0.7
#define EMBERHOLD_EDGE_SHADOW 0.1
#define EMBERHOLD_EDGE_SHADOW_DARK 0.72
// Трава на блоке земли или камня: заходит от края с травой в среднем на FRINGE_INSET блока, рваность
// слабее, чем на склонах (±FRINGE_RAGGED / 2 блока), чтобы трава всегда закрывала ребро.
// Внешние углы участка скругляются с радиусом около FRINGE_ROUND блока.
#define EMBERHOLD_FRINGE_INSET 0.2
#define EMBERHOLD_FRINGE_RAGGED 0.2
#define EMBERHOLD_FRINGE_ROUND 0.6

// Сдвиг края травы в точке мира: крупный шум (−0,5..0,5) · ragged и острые зубцы-травинки вдоль края.
// weight — вес травы из вершин (без шума): по его наклону на экране находим направление края в мире,
// зубцы — треугольники вдоль края с вогнутыми боками (острые кончики), разной высоты, глубиной
// до TEETH_DEPTH блока при любом наклоне веса. Где наклон не найти (вес ровный, грань вертикальна), зубцов нет.
float GrassEdgeOffset(float3 positionWS, float ragged, float weight, float3 dpdx, float3 dpdy)
{
    // Шум зависит и от высоты, чтобы на обрывах край тоже был неровным.
    float2 q = positionWS.xz + positionWS.y * float2(0.61, -0.43);
    float noise = GroundValueNoise(q * 0.45) * 0.65 + GroundValueNoise(q * 1.7 + 17.3) * 0.35;
    float offset = (noise - 0.5) * ragged;

    // Наклон веса по X и Z мира из производных на экране: [dpdx.xz; dpdy.xz] · g = (ddx w, ddy w).
    float dwx = ddx(weight);
    float dwy = ddy(weight);
    float det = dpdx.x * dpdy.z - dpdx.z * dpdy.x;
    if (abs(det) < 1e-8)
        return offset;
    float2 g = float2(dwx * dpdy.z - dwy * dpdx.z, dpdx.x * dwy - dpdy.x * dwx) / det;
    float slope = length(g);
    if (slope < 1e-4)
        return offset;
    // Координата вдоль края, слегка изогнутая шумом, — травинки чуть клонятся.
    float along = dot(positionWS.xz, float2(-g.y, g.x) / slope) * EMBERHOLD_TEETH_PER_BLOCK
                + GroundValueNoise(q * 0.9 + 5.7) * 2.5;
    // Два ряда зубцов разной частоты и случайной высоты: травинки неровные, пучками, местами короткие.
    float teeth = 0.0;
    for (int layer = 0; layer < 2; layer++)
    {
        float a = along * (layer == 0 ? 1.0 : 0.61) + layer * 0.37;
        float tooth = 1.0 - abs(2.0 * frac(a) - 1.0);
        float height = GroundHash(float2(floor(a), 7.1 + layer * 3.3));
        teeth = max(teeth, tooth * tooth * height * sqrt(height) * (layer == 0 ? 1.0 : 0.8));
    }
    return offset + teeth * EMBERHOLD_TEETH_DEPTH * slope;
}

float GroundSmoothMin(float a, float b, float k)
{
    float h = max(k - abs(a - b), 0.0) / k;
    return min(a, b) - h * h * k * 0.25;
}

// Расстояние в блоках от точки uv верха блока (0..1 по X и Z) до травы соседей по маске (биты — как в
// GrassEdge.cs: стороны −X, +X, −Z, +Z, затем диагонали). От сторон — до ребра, со скруглением там,
// где трава с двух сторон; от диагоналей без соседних сторон — до угла (трава заходит четвертью круга).
float FringeDistance(float mask, float2 uv)
{
    uint m = (uint)(mask + 0.5);
    const float none = 4.0;
    float d = GroundSmoothMin(
        GroundSmoothMin((m & 1u) ? uv.x : none, (m & 2u) ? 1.0 - uv.x : none, EMBERHOLD_FRINGE_ROUND),
        GroundSmoothMin((m & 4u) ? uv.y : none, (m & 8u) ? 1.0 - uv.y : none, EMBERHOLD_FRINGE_ROUND),
        EMBERHOLD_FRINGE_ROUND);
    if ((m & 16u) && !(m & 5u))
        d = min(d, length(uv));
    if ((m & 32u) && !(m & 6u))
        d = min(d, length(uv - float2(1.0, 0.0)));
    if ((m & 64u) && !(m & 9u))
        d = min(d, length(uv - float2(0.0, 1.0)));
    if ((m & 128u) && !(m & 10u))
        d = min(d, length(uv - float2(1.0, 1.0)));
    return d;
}

// Сколько травы в точке: grassEdge — вес травы со сдвигом, трава там, где он больше 0,5. Край резкий
// (сглажен на пиксель). rim — тёмная кайма травы у края, shadow — тень на соседнем материале под краем.
half GrassCover(float grassEdge, out half rim, out half shadow)
{
    float aa = clamp(fwidth(grassEdge), 0.01, 0.1);
    half cover = smoothstep(0.5 - aa, 0.5 + aa, grassEdge);
    rim = 1.0h - smoothstep(0.5 + aa, 0.5 + EMBERHOLD_EDGE_RIM, grassEdge);
    shadow = smoothstep(0.5 - EMBERHOLD_EDGE_SHADOW, 0.5 - aa, grassEdge);
    return cover;
}

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

#if defined(LOD_FADE_CROSSFADE)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

#if defined(_PARALLAXMAP)
#define REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR
#endif

#if (defined(_NORMALMAP) || (defined(_PARALLAXMAP) && !defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR))) || defined(_DETAIL)
#define REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR
#endif

// keep this file in sync with LitGBufferPass.hlsl

struct Attributes
{
    float4 positionOS   : POSITION;
    float3 normalOS     : NORMAL;
    float4 tangentOS    : TANGENT;
    float2 texcoord     : TEXCOORD0;
    float2 staticLightmapUV   : TEXCOORD1;
    float2 dynamicLightmapUV  : TEXCOORD2;
    float4 groundBlend  : TEXCOORD3;
    float4 grassKinds   : TEXCOORD4;
    half4 color         : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float2 uv                       : TEXCOORD0;

#if defined(REQUIRES_WORLD_SPACE_POS_INTERPOLATOR)
    float3 positionWS               : TEXCOORD1;
#endif

    float3 normalWS                 : TEXCOORD2;
#if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR)
    half4 tangentWS                : TEXCOORD3;    // xyz: tangent, w: sign
#endif

#ifdef _ADDITIONAL_LIGHTS_VERTEX
    half4 fogFactorAndVertexLight   : TEXCOORD5; // x: fogFactor, yzw: vertex light
#else
    half  fogFactor                 : TEXCOORD5;
#endif

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    float4 shadowCoord              : TEXCOORD6;
#endif

#if defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
    half3 viewDirTS                : TEXCOORD7;
#endif

    DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 8);
#ifdef DYNAMICLIGHTMAP_ON
    float2  dynamicLightmapUV : TEXCOORD9; // Dynamic lightmap UVs
#endif

#ifdef USE_APV_PROBE_OCCLUSION
    float4 probeOcclusion : TEXCOORD10;
#endif

    // Оттенок травы своего места (цвет вершины): rgb — множитель, a — насыщенность.
    half4 groundColor               : TEXCOORD11;
    // UV3: x — вес камня гладкой земли, y — маска соседей-травы у блоков земли и камня (−1 — вершина травы),
    // zw — место в ячейке.
    float4 groundBlend              : TEXCOORD12;
    // UV4: вид травы своего места — веса травы Топей, Мёртвого города и Гор.
    half3 grassKinds                : TEXCOORD13;

    float4 positionCS               : SV_POSITION;

    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

void InitializeInputData(Varyings input, half3 normalTS, out InputData inputData)
{
    inputData = (InputData)0;

#if defined(REQUIRES_WORLD_SPACE_POS_INTERPOLATOR)
    inputData.positionWS = input.positionWS;
#endif

#if defined(DEBUG_DISPLAY)
    inputData.positionCS = input.positionCS;
#endif

    half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
#if defined(_NORMALMAP) || defined(_DETAIL)
    float sgn = input.tangentWS.w;      // should be either +1 or -1
    float3 bitangent = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
    half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangent.xyz, input.normalWS.xyz);

    #if defined(_NORMALMAP)
    inputData.tangentToWorld = tangentToWorld;
    #endif
    inputData.normalWS = TransformTangentToWorld(normalTS, tangentToWorld);
#else
    inputData.normalWS = input.normalWS;
#endif

    inputData.normalWS = NormalizeNormalPerPixel(inputData.normalWS);
    inputData.viewDirectionWS = viewDirWS;

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    inputData.shadowCoord = input.shadowCoord;
#elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);
#else
    inputData.shadowCoord = float4(0, 0, 0, 0);
#endif
#ifdef _ADDITIONAL_LIGHTS_VERTEX
    inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactorAndVertexLight.x);
    inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
#else
    inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
#endif
                           
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);  

    #if defined(DEBUG_DISPLAY)
    #if defined(DYNAMICLIGHTMAP_ON)
    inputData.dynamicLightmapUV = input.dynamicLightmapUV;
    #endif
    #if defined(LIGHTMAP_ON)
    inputData.staticLightmapUV = input.staticLightmapUV;
    #else
    inputData.vertexSH = input.vertexSH;
    #endif
    #if defined(USE_APV_PROBE_OCCLUSION)
    inputData.probeOcclusion = input.probeOcclusion;
    #endif
    #endif
}

void InitializeBakedGIData(Varyings input, inout InputData inputData)
{
    #if defined(_SCREEN_SPACE_IRRADIANCE)
    inputData.bakedGI = SAMPLE_GI(_ScreenSpaceIrradiance, input.positionCS.xy, inputData.normalWS);
    #elif defined(DYNAMICLIGHTMAP_ON)
    inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.dynamicLightmapUV, input.vertexSH, inputData.normalWS);
    inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
    #elif !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
    inputData.bakedGI = SAMPLE_GI(input.vertexSH,
        GetAbsolutePositionWS(inputData.positionWS),
        inputData.normalWS,
        inputData.viewDirectionWS,
        input.positionCS.xy,
        input.probeOcclusion,
        inputData.shadowMask);
    #else
    inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, inputData.normalWS);
    inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
    #endif
}

///////////////////////////////////////////////////////////////////////////////
//                  Vertex and Fragment functions                            //
///////////////////////////////////////////////////////////////////////////////

// Used in Standard (Physically Based) shader
Varyings LitPassVertex(Attributes input)
{
    Varyings output = (Varyings)0;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);

    // normalWS and tangentWS already normalize.
    // this is required to avoid skewing the direction during interpolation
    // also required for per-vertex lighting and SH evaluation
    VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);

    half3 vertexLight = VertexLighting(vertexInput.positionWS, normalInput.normalWS);

    half fogFactor = 0;
    #if !defined(_FOG_FRAGMENT)
        fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
    #endif

    output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);

    // already normalized from normal transform to WS.
    output.normalWS = normalInput.normalWS;
#if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR) || defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
    real sign = input.tangentOS.w * GetOddNegativeScale();
    half4 tangentWS = half4(normalInput.tangentWS.xyz, sign);
#endif
#if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR)
    output.tangentWS = tangentWS;
#endif

#if defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
    half3 viewDirWS = GetWorldSpaceNormalizeViewDir(vertexInput.positionWS);
    half3 viewDirTS = GetViewDirectionTangentSpace(tangentWS, output.normalWS, viewDirWS);
    output.viewDirTS = viewDirTS;
#endif

    OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
#ifdef DYNAMICLIGHTMAP_ON
    output.dynamicLightmapUV = input.dynamicLightmapUV.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
#endif
    OUTPUT_SH4(vertexInput.positionWS, output.normalWS.xyz, GetWorldSpaceNormalizeViewDir(vertexInput.positionWS), output.vertexSH, output.probeOcclusion);
#ifdef _ADDITIONAL_LIGHTS_VERTEX
    output.fogFactorAndVertexLight = half4(fogFactor, vertexLight);
#else
    output.fogFactor = fogFactor;
#endif

#if defined(REQUIRES_WORLD_SPACE_POS_INTERPOLATOR)
    output.positionWS = vertexInput.positionWS;
#endif

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    output.shadowCoord = GetShadowCoord(vertexInput);
#endif

    output.positionCS = vertexInput.positionCS;
    output.groundColor = input.color;
    output.groundBlend = input.groundBlend;
    output.grassKinds = input.grassKinds.xyz;

    return output;
}

// Used in Standard (Physically Based) shader
void LitPassFragment(
    Varyings input
    , out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

#if defined(_PARALLAXMAP)
#if defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
    half3 viewDirTS = input.viewDirTS;
#else
    half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
    half3 viewDirTS = GetViewDirectionTangentSpace(input.tangentWS, input.normalWS, viewDirWS);
#endif
    ApplyPerPixelDisplacement(viewDirTS, input.uv);
#endif

    SurfaceData surfaceData;
    InitializeStandardLitSurfaceData(input.uv, surfaceData);
    // Своя текстура материала и текстура камня кладутся по трём проекциям мира, а не по UV меша.
    half3 normalForTex = normalize(input.normalWS);
    float3 dpdx = ddx(input.positionWS);
    float3 dpdy = ddy(input.positionWS);
    // Чем ближе камера, тем больше детали; вдали рисунок проще.
    float cameraDistance = distance(GetCameraPositionWS(), input.positionWS);
    half detailFade = 1.0h - saturate((cameraDistance - EMBERHOLD_DETAIL_NEAR) / (EMBERHOLD_DETAIL_FAR - EMBERHOLD_DETAIL_NEAR));
    half flatFade = saturate((cameraDistance - EMBERHOLD_FLAT_NEAR) / (EMBERHOLD_FLAT_FAR - EMBERHOLD_FLAT_NEAR));
    half3 baseAlbedo = SampleTriplanar(TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap), input.positionWS, normalForTex,
        dpdx, dpdy, detailFade, flatFade) * _BaseColor.rgb;
    // Трава (гладкая земля и блоки травы) — в оттенке своего места.
    if (input.groundBlend.y < -0.5)
        baseAlbedo = TintGrass(MixGrassKinds(baseAlbedo, input.grassKinds, _BaseColor.rgb, input.positionWS, normalForTex,
            dpdx, dpdy, detailFade, flatFade), input.groundColor);
    surfaceData.albedo = baseAlbedo;
    // Граница травы с камнем резкая и рваная, с зубцами-травинками: вес травы (1 − камень) сдвигается шумом
    // по миру и режется по 0,5. У травы тёмная кайма — рисованный контур, на камне под краем тень.
    // Край считаем до ветвления: в нём производные (ddx, fwidth), а вес камня меняется от пикселя к пикселю,
    // и внутри ветки соседние пиксели могли бы пойти разными путями.
    half rockWeight = saturate(input.groundBlend.x);
    half rockRim, rockShadow;
    half rockGrass = GrassCover(1.0 - rockWeight
        + GrassEdgeOffset(input.positionWS, EMBERHOLD_EDGE_RAGGED, 1.0 - rockWeight, dpdx, dpdy), rockRim, rockShadow);
    // Камень сэмплируем только там, где он есть: у блоков и в сцене без гладкой земли вес 0.
    UNITY_BRANCH
    if (rockWeight > 0.001h)
    {
        baseAlbedo *= lerp(1.0h, EMBERHOLD_EDGE_RIM_DARK, rockRim);
        half3 rockAlbedo = SampleTriplanar(TEXTURE2D_ARGS(_RockMap, sampler_RockMap), input.positionWS, normalForTex,
            dpdx, dpdy, detailFade, flatFade) * _GroundRockColor.rgb;
        rockAlbedo *= lerp(1.0h, EMBERHOLD_EDGE_SHADOW_DARK, rockShadow);
        surfaceData.albedo = lerp(rockAlbedo, baseAlbedo, rockGrass);
    }

    // Трава с соседей на верх блока земли или камня: та же рваная бахрома с зубцами, от края с травой внутрь блока.
    // Маска одна на весь треугольник, поэтому пиксели одного квадрата идут в ветку вместе и производные внутри верны.
    UNITY_BRANCH
    if (input.groundBlend.y > 0.5)
    {
        float toGrass = -FringeDistance(input.groundBlend.y, input.groundBlend.zw);
        half rim, shadow;
        half grass = GrassCover(0.5 + EMBERHOLD_FRINGE_INSET + toGrass
            + GrassEdgeOffset(input.positionWS, EMBERHOLD_FRINGE_RAGGED, toGrass, dpdx, dpdy), rim, shadow);
        half3 meadow = SampleTriplanar(TEXTURE2D_ARGS(_GrassMap, sampler_GrassMap), input.positionWS,
            normalForTex, dpdx, dpdy, detailFade, flatFade);
        half3 grassAlbedo = TintGrass(MixGrassKinds(meadow, input.grassKinds, 1.0h, input.positionWS, normalForTex,
            dpdx, dpdy, detailFade, flatFade), input.groundColor);
        grassAlbedo *= lerp(1.0h, EMBERHOLD_EDGE_RIM_DARK, rim);
        half3 groundAlbedo = surfaceData.albedo * lerp(1.0h, EMBERHOLD_EDGE_SHADOW_DARK, shadow);
        surfaceData.albedo = lerp(groundAlbedo, grassAlbedo, grass);
    }

#ifdef LOD_FADE_CROSSFADE
    LODFadeCrossFade(input.positionCS);
#endif

    InputData inputData;
    InitializeInputData(input, surfaceData.normalTS, inputData);
    SETUP_DEBUG_TEXTURE_DATA(inputData, UNDO_TRANSFORM_TEX(input.uv, _BaseMap));

#if defined(_DBUFFER)
    ApplyDecalToSurfaceData(input.positionCS, surfaceData, inputData);
#endif

    InitializeBakedGIData(input, inputData);

    half4 color = UniversalFragmentPBR(inputData, surfaceData);
    color.rgb = MixFog(color.rgb, inputData.fogCoord);
    color.a = OutputAlpha(color.a, IsSurfaceTypeTransparent());

    outColor = color;

#ifdef _WRITE_RENDERING_LAYERS
    outRenderingLayers = EncodeMeshRenderingLayer();
#endif
}

#endif
