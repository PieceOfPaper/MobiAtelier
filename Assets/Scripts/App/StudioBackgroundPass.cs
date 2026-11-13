// 게임 배경 셰이더의 전용 패스를 그리는 렌더 패스(Background.md).
//   스튜디오 3D 배경의 원본 셰이더는 LightMode 가 BG·SkyLit·ExcludeDecal·Decal 인 패스만 가진다.
//   URP 기본 렌더러는 이 태그를 그리지 않으므로, 카메라마다 이 패스를 끼워 넣어 그린다.
//   Decal(벽 조명 데칼, MMN/FX/ObjectDecalLight)은 그리지 않는다. D3D12 빌드에서 그리면 종료할 때 D3D12Core.dll 에서
//   접근 위반으로 죽었다(벨벳 의상실, 3회 재현, D3D11 에서는 정상, 2026-11-02 확인).
//   불투명 큐(~2500)는 불투명 다음에, 반투명 큐(2501~)는 스카이박스 다음에 그린다. 구름(MMN/CutScene/Sky_Clouds, 큐 2601)은
//   깊이를 쓰지 않아 먼저 그리면 뒤의 그라데이션 스카이박스가 덮었다(공간의 틈: 빛, 2026-11-14 확인).
//   바닥 반사(MMN/Special/PlanarReflection): 게임 스크립트 PlanarReflections 가 반사 카메라로 그린 텍스처를 _PlanarReflectionTexture 로
//   넘기고 _LowOptionEnable 을 0 으로 둔다. 앱은 StudioReflectionPass 가 같은 텍스처를 불투명 전에 그린다(Background.md 4장).
//   렌더러 에셋(PC_Renderer)의 Renderer Feature 목록은 고치지 않는다.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>배경 패스 등록. 에디터 어셈블리가 URP 를 참조하지 않아도 부를 수 있게 패스 타입과 분리한다.</summary>
public static class StudioBackgroundRendering
{
    public static void Install() => StudioBackgroundPass.Install();

    /// <summary>바닥 반사를 켜고 끈다. planeY 는 반사 평면 높이(월드 y). 끄면 반사 텍스처를 그리지 않는다.</summary>
    public static void SetReflection(bool active, float planeY = 0f)
    {
        StudioReflectionPass.Active = active;
        StudioReflectionPass.PlaneY = planeY;
    }

    /// <summary>반사 바닥 렌더러를 표시하는 렌더링 레이어 비트. 반사 텍스처에서 바닥 자신을 뺀다.</summary>
    public const uint ReflectorLayerBit = 1u << 7;
}

sealed class StudioBackgroundPass : ScriptableRenderPass
{
    static readonly ShaderTagId[] OpaqueTags = { new ShaderTagId("BG"), new ShaderTagId("ExcludeDecal"), new ShaderTagId("SkyLit") };

    static StudioBackgroundPass opaque, transparent;
    static bool hooked;

    readonly string sampleName;
    readonly ShaderTagId[] tags;
    readonly RenderQueueRange range;
    readonly SortingCriteria sorting;

    class PassData { public RendererListHandle List; }

    StudioBackgroundPass(string name, ShaderTagId[] tags, RenderQueueRange range, SortingCriteria sorting, RenderPassEvent evt)
    {
        sampleName = name;    // 일반 빌드에서는 profilingSampler 를 쓰지 않는다(이름 조회에서 null 예외가 났다).
        this.tags = tags;
        this.range = range;
        this.sorting = sorting;
        renderPassEvent = evt;
    }

    /// <summary>모든 카메라 렌더링에 배경 패스를 끼운다. 여러 번 불러도 한 번만 건다.</summary>
    public static void Install()
    {
        if (hooked) return;
        hooked = true;
        opaque = new StudioBackgroundPass("MobiAtelier Background", OpaqueTags, RenderQueueRange.opaque, SortingCriteria.CommonOpaque, RenderPassEvent.AfterRenderingOpaques);
        transparent = new StudioBackgroundPass("MobiAtelier Background Transparent", OpaqueTags, RenderQueueRange.transparent, SortingCriteria.CommonTransparent, RenderPassEvent.AfterRenderingSkybox);
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
    }

    static void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
    {
        if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;
        var data = cam.GetUniversalAdditionalCameraData();
        if (data == null || data.scriptableRenderer == null) return;
        if (StudioReflectionPass.Active) data.scriptableRenderer.EnqueuePass(StudioReflectionPass.Instance);
        data.scriptableRenderer.EnqueuePass(opaque);
        data.scriptableRenderer.EnqueuePass(transparent);
    }

    public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
    {
        var res = frame.Get<UniversalResourceData>();
        var cam = frame.Get<UniversalCameraData>();
        var rd = frame.Get<UniversalRenderingData>();
        var ld = frame.Get<UniversalLightData>();
        using (var b = graph.AddRasterRenderPass<PassData>(sampleName, out var pd))
        {
            var draw = RenderingUtils.CreateDrawingSettings(new System.Collections.Generic.List<ShaderTagId>(tags), rd, cam, ld, sorting);
            var filter = new FilteringSettings(range);
            pd.List = graph.CreateRendererList(new RendererListParams(rd.cullResults, draw, filter));
            b.UseRendererList(pd.List);
            b.SetRenderAttachment(res.activeColorTexture, 0);
            b.SetRenderAttachmentDepth(res.activeDepthTexture, AccessFlags.ReadWrite);
            b.AllowGlobalStateModification(true);
            b.SetRenderFunc((PassData d, RasterGraphContext c) => c.cmd.DrawRendererList(d.List));
        }
    }
}

/// <summary>
/// 바닥 반사 텍스처. 카메라를 반사 평면으로 뒤집은 행렬로 장면(캐릭터·배경·하늘·구름)을 그려 _PlanarReflectionTexture 에 넣는다.
/// 게임 설정 [확인: 공간의 틈 프리팹 PlanarReflections]: 반사 레이어는 스튜디오 배경 레이어(26) 전체, 클립 평면 오프셋 -0.04, 해상도 자동.
/// 앱은 클립 평면을 바닥 위 +0.04 에 둔다. -0.04 면 바닥 면이 반사에 같은 높이로 그려져 무늬 경계에 점선 자국이 생겼다
/// [확인: 촬영]. 게임 값의 부호 규칙(sideSign)은 [미확인].
/// 앱은 레이어로 거르지 않고 반사 바닥만 뺀다. 컬링은 본 카메라 결과를 쓴다(본 화면 밖 물체는 반사에 빠질 수 있다).
/// </summary>
sealed class StudioReflectionPass : ScriptableRenderPass
{
    internal static bool Active;
    internal static float PlaneY;
    const float ClipPlaneOffset = 0.04f;
    internal static readonly StudioReflectionPass Instance = new StudioReflectionPass();

    static readonly int TextureId = Shader.PropertyToID("_PlanarReflectionTexture");
    static readonly ShaderTagId[] Tags =
    {
        new ShaderTagId("UniversalForward"), new ShaderTagId("UniversalForwardOnly"), new ShaderTagId("SRPDefaultUnlit"),
        new ShaderTagId("BG"), new ShaderTagId("ExcludeDecal"), new ShaderTagId("SkyLit"),
    };

    class PassData
    {
        public RendererListHandle Opaque, Sky, Transparent;
        public Matrix4x4 View, Proj, ReflView, ReflProj;
    }

    StudioReflectionPass() { renderPassEvent = RenderPassEvent.BeforeRenderingOpaques; }

    public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
    {
        var cam = frame.Get<UniversalCameraData>();
        var rd = frame.Get<UniversalRenderingData>();
        var ld = frame.Get<UniversalLightData>();
        var camera = cam.camera;

        var view = camera.worldToCameraMatrix;
        var proj = camera.projectionMatrix;
        var reflView = view * ReflectionMatrix(PlaneY);
        // 평면 아래를 자르는 비스듬한 근평면(반사 카메라 공간의 평면).
        var plane = CameraSpacePlane(reflView, new Vector3(0, PlaneY, 0), Vector3.up, ClipPlaneOffset);
        var reflProj = camera.CalculateObliqueMatrix(plane);

        var desc = cam.cameraTargetDescriptor;
        desc.msaaSamples = 1;
        desc.depthStencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.None;
        desc.depthBufferBits = 0;
        var color = UniversalRenderer.CreateRenderGraphTexture(graph, desc, "_PlanarReflectionTexture", true);
        var depthDesc = cam.cameraTargetDescriptor;
        depthDesc.msaaSamples = 1;
        depthDesc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.None;
        depthDesc.depthStencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.D32_SFloat;
        var depth = UniversalRenderer.CreateRenderGraphTexture(graph, depthDesc, "_PlanarReflectionDepth", true);

        using (var b = graph.AddRasterRenderPass<PassData>("MobiAtelier Planar Reflection", out var pd))
        {
            var tags = new System.Collections.Generic.List<ShaderTagId>(Tags);
            var opaqueDraw = RenderingUtils.CreateDrawingSettings(tags, rd, cam, ld, SortingCriteria.CommonOpaque);
            var transDraw = RenderingUtils.CreateDrawingSettings(tags, rd, cam, ld, SortingCriteria.CommonTransparent);
            uint mask = ~StudioBackgroundRendering.ReflectorLayerBit;
            var opaqueFilter = new FilteringSettings(RenderQueueRange.opaque) { renderingLayerMask = mask };
            var transFilter = new FilteringSettings(RenderQueueRange.transparent) { renderingLayerMask = mask };
            pd.Opaque = graph.CreateRendererList(new RendererListParams(rd.cullResults, opaqueDraw, opaqueFilter));
            pd.Transparent = graph.CreateRendererList(new RendererListParams(rd.cullResults, transDraw, transFilter));
            pd.Sky = graph.CreateSkyboxRendererList(camera, GL.GetGPUProjectionMatrix(reflProj, true), reflView);
            pd.View = view; pd.Proj = proj; pd.ReflView = reflView; pd.ReflProj = reflProj;
            b.UseRendererList(pd.Opaque);
            b.UseRendererList(pd.Transparent);
            b.UseRendererList(pd.Sky);
            b.SetRenderAttachment(color, 0, AccessFlags.Write);
            b.SetRenderAttachmentDepth(depth, AccessFlags.Write);
            b.AllowGlobalStateModification(true);
            b.SetGlobalTextureAfterPass(color, TextureId);
            b.SetRenderFunc((PassData d, RasterGraphContext c) =>
            {
                c.cmd.ClearRenderTarget(true, true, Color.clear);
                c.cmd.SetViewProjectionMatrices(d.ReflView, d.ReflProj);
                c.cmd.SetInvertCulling(true);
                c.cmd.DrawRendererList(d.Opaque);
                c.cmd.DrawRendererList(d.Sky);
                c.cmd.DrawRendererList(d.Transparent);
                c.cmd.SetInvertCulling(false);
                c.cmd.SetViewProjectionMatrices(d.View, d.Proj);
            });
        }
    }

    static Matrix4x4 ReflectionMatrix(float y)
    {
        // 평면 y = PlaneY 에 대한 반사(법선 위쪽).
        var m = Matrix4x4.identity;
        m.m11 = -1f; m.m13 = 2f * y;
        return m;
    }

    static Vector4 CameraSpacePlane(Matrix4x4 worldToCamera, Vector3 pos, Vector3 normal, float offset)
    {
        var p = worldToCamera.MultiplyPoint(pos + normal * offset);
        var n = worldToCamera.MultiplyVector(normal).normalized;
        return new Vector4(n.x, n.y, n.z, -Vector3.Dot(p, n));
    }
}
