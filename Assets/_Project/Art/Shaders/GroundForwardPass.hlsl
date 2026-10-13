// Копия LitForwardPass.hlsl из URP 17.6 с отличиями: текстура материала кладётся по трём проекциям мира
// (без растяжения на крутых склонах), вблизи камеры добавляется мелкая деталь из альфа-канала, вдали рисунок
// упрощается, и трава сменяется текстурой камня, окрашенной цветом вершины, по весу из UV3.x: граница резкая
// и рваная, с тёмной каймой травы. У мешей без UV3 вес 0, и остаётся только своя текстура материала.
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

// Ширина рваного края (насколько шум сдвигает границу) и тёмная кайма травы у камня.
#define EMBERHOLD_EDGE_RAGGED 0.6
#define EMBERHOLD_EDGE_RIM 0.12
#define EMBERHOLD_EDGE_RIM_DARK 0.7

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
    float2 groundBlend  : TEXCOORD3;
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

    // rgb — цвет, к которому смешивать, a — вес смешивания.
    half4 groundColor               : TEXCOORD11;

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
    output.groundColor = half4(input.color.rgb, input.groundBlend.x);

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
    surfaceData.albedo = baseAlbedo;
    // Камень сэмплируем только там, где он есть: у блоков и в сцене без гладкой земли вес 0.
    half rockWeight = saturate(input.groundColor.a);
    UNITY_BRANCH
    if (rockWeight > 0.001h)
    {
        // Граница резкая и рваная: вес камня из вершин сдвигается шумом по миру и режется по 0,5.
        // Шум зависит и от высоты, чтобы на обрывах край тоже был неровным.
        float2 q = input.positionWS.xz + input.positionWS.y * float2(0.61, -0.43);
        float noise = GroundValueNoise(q * 0.45) * 0.65 + GroundValueNoise(q * 1.7 + 17.3) * 0.35;
        float edge = rockWeight + (noise - 0.5) * EMBERHOLD_EDGE_RAGGED;
        float aa = clamp(fwidth(edge), 0.01, 0.1);
        half rock = smoothstep(0.5 - aa, 0.5 + aa, edge);
        // Тёмная кайма травы у камня — рисованный контур.
        half rim = smoothstep(0.5 - EMBERHOLD_EDGE_RIM, 0.5 - aa, edge);
        baseAlbedo *= lerp(1.0h, EMBERHOLD_EDGE_RIM_DARK, rim);
        half3 rockAlbedo = SampleTriplanar(TEXTURE2D_ARGS(_RockMap, sampler_RockMap), input.positionWS, normalForTex,
            dpdx, dpdy, detailFade, flatFade) * input.groundColor.rgb;
        surfaceData.albedo = lerp(baseAlbedo, rockAlbedo, rock);
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
