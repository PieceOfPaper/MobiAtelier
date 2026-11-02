// 게임 배경 셰이더의 전용 패스를 그리는 렌더 패스(Background.md).
//   스튜디오 3D 배경의 원본 셰이더는 LightMode 가 BG·SkyLit·ExcludeDecal·Decal 인 패스만 가진다.
//   URP 기본 렌더러는 이 태그를 그리지 않으므로, 카메라마다 이 패스를 끼워 넣어 그린다.
//   Decal(벽 조명 데칼, MMN/FX/ObjectDecalLight)은 그리지 않는다. D3D12 빌드에서 그리면 종료할 때 D3D12Core.dll 에서
//   접근 위반으로 죽었다(벨벳 의상실, 3회 재현, D3D11 에서는 정상, 2026-11-02 확인).
//   렌더러 에셋(PC_Renderer)의 Renderer Feature 목록은 고치지 않는다.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>배경 패스 등록. 에디터 어셈블리가 URP 를 참조하지 않아도 부를 수 있게 패스 타입과 분리한다.</summary>
public static class StudioBackgroundRendering
{
    public static void Install() => StudioBackgroundPass.Install();
}

sealed class StudioBackgroundPass : ScriptableRenderPass
{
    static readonly ShaderTagId[] OpaqueTags = { new ShaderTagId("BG"), new ShaderTagId("ExcludeDecal"), new ShaderTagId("SkyLit") };

    static StudioBackgroundPass opaque;
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
        opaque = new StudioBackgroundPass("MobiAtelier Background", OpaqueTags, RenderQueueRange.all, SortingCriteria.CommonOpaque, RenderPassEvent.AfterRenderingOpaques);
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
    }

    static void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
    {
        if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;
        var data = cam.GetUniversalAdditionalCameraData();
        if (data == null || data.scriptableRenderer == null) return;
        data.scriptableRenderer.EnqueuePass(opaque);
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
