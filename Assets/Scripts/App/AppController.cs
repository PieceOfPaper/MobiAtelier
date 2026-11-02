// 앱 진입점과 시작 흐름 (ProgramFlow.md 1장, Startup.md)
//   앱 설정 읽기 → 게임 경로 검사(실패 시 경로 지정 팝업) → 데이터 로딩
//   → 마지막 작업 복원 / 없으면 기본 외형 2종 중 무작위 / 읽기 실패면 복구 안내 → 모델 조립 → 꾸미기
// 꾸미기 창(CustomizePanel)의 편집 요청은 Edit 로 받는다. 저장 대화상자·종료 팝업은 이후 작업에서 붙인다.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UIElements;

public sealed class AppController : MonoBehaviour
{
    public Shader characterShader;
    public PanelSettings panelSettings;

    AppSettings settings;
    AppUI ui;
    CameraController cameraCtl;
    Autosave autosave;
    InstallFiles files;
    GameData data;
    BundleCache bundles;
    CharacterView view;
    EditState state;
    AssemblyPlan plan;
    CustomizePanel panel;

    public EditState State => state;
    public GameData Data => data;

    async void Start()
    {
        EnsureEventSystem();
        var cam = StageBuilder.CreateStage();
        cameraCtl = cam.gameObject.AddComponent<CameraController>();
        cameraCtl.Cam = cam;
        ui = new AppUI(gameObject, panelSettings);
        cameraCtl.IsPointerOverUI = p => ui.IsPointerOver(p);
        cameraCtl.Committed += () =>
        {
            if (state == null) return;
            state.camera = cameraCtl.Save();
            autosave.MarkDirty();
        };
        autosave = new Autosave(() => state);
        autosave.Failed += msg => Debug.LogWarning("[MobiAtelier] 자동 저장 실패: " + msg);
        Application.wantsToQuit += OnWantsToQuit;

        string shot = CommandLine.Value("-maScreenshot");
        if (shot != null) StartCoroutine(ShotAndQuit(shot));

        settings = AppSettings.Load();
        string path = string.IsNullOrEmpty(settings.installPath) ? AppSettings.DefaultInstallPath : settings.installPath;
        ui.ShowLoading("게임 설치 폴더를 확인하는 중...");
        var r = await Task.Run(() => InstallValidator.Check(path));
        if (this == null) return;
        if (r.Ok) OnInstallReady(r, path);
        else ShowPathPopup(path, r);
    }

    void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>()) return;
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<InputSystemUIInputModule>();
    }

    void Update() => autosave?.Tick(Time.unscaledDeltaTime);

    bool OnWantsToQuit()
    {
        if (autosave != null && autosave.Pending) autosave.Flush();
        return true;
    }

    void OnDestroy()
    {
        Application.wantsToQuit -= OnWantsToQuit;
        if (view) DestroyImmediate(view.gameObject);
        bundles?.Dispose();
    }

    static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ------------------------------------------------------------ 경로 검사

    void ShowPathPopup(string path, InstallCheckResult failed)
    {
        AppUI.PathPopup popup = null;
        popup = ui.ShowPathPopup(path, failed?.Message, failed?.Detail,
            async input =>
            {
                popup.SetBusy(true, "검사 중...");
                var r = await Task.Run(() => InstallValidator.Check(input));
                if (this == null) return;
                if (!r.Ok) { popup.SetBusy(false); popup.SetError(r.Message, r.Detail); return; }
                ui.CloseModal();
                OnInstallReady(r, InstallValidator.Normalize(input));
            },
            async p =>
            {
                p.SetBusy(true, "폴더 선택 중...");
                string chosen = await WindowsDialogs.PickFolder("설치된 마비노기 모바일 폴더를 선택해 주세요.", InstallValidator.Normalize(p.Input.value));
                if (this == null) return;
                p.SetBusy(false);
                p.Result.text = "";
                if (!string.IsNullOrEmpty(chosen)) p.Input.value = chosen;    // 취소하면 기존 입력값 유지
            },
            Quit);
    }

    void OnInstallReady(InstallCheckResult r, string path)
    {
        files = r.Files;
        data = r.Data;
        bundles = new BundleCache(files);
        if (settings.installPath != path)
        {
            settings.installPath = path;
            SaveSettings();
        }
        RestoreOrStart();
    }

    // ------------------------------------------------------------ 작업 복원

    void RestoreOrStart()
    {
        var lr = JsonStore.ReadEditState(JsonStore.AutosavePath);
        switch (lr.Status)
        {
            case LoadStatus.Missing: StartNew(); break;
            case LoadStatus.Ok: Apply(lr.Value, false); break;
            default: ShowRestoreFailed(lr); break;
        }
    }

    void StartNew()
    {
        Apply(StateFactory.NewRandom(data, new System.Random()), true);
    }

    void ShowRestoreFailed(LoadResult<EditState> lr)
    {
        var actions = new List<(string, Action)>();
        var bak = JsonStore.ReadEditState(JsonStore.AutosavePath + ".bak");
        if (bak.Status == LoadStatus.Ok)
            actions.Add(("백업에서 복구", () => { ui.CloseModal(); PreserveBroken(); Apply(bak.Value, true); }));
        actions.Add(("다른 JSON 불러오기", LoadOtherJson));
        actions.Add(("새 작업 시작", () => { ui.CloseModal(); PreserveBroken(); StartNew(); }));
        actions.Add(("종료", Quit));
        string msg = lr.Status == LoadStatus.Unsupported
            ? "마지막 작업 파일의 형식을 지원하지 않습니다."
            : "마지막 작업 파일을 읽지 못했습니다. 기존 파일은 그대로 보존합니다.";
        ui.ShowChoice("마지막 작업 복원 실패", msg, lr.Error, actions);
    }

    void PreserveBroken()
    {
        try
        {
            string kept = Autosave.PreserveBroken();
            if (kept != null) Debug.Log("[MobiAtelier] 읽지 못한 자동 저장 파일을 보존했습니다: " + kept);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            Debug.LogWarning("[MobiAtelier] 자동 저장 파일 보존 실패: " + e.Message);
        }
    }

    async void LoadOtherJson()
    {
        string dir = Directory.Exists(settings.recentJsonFolder ?? "") ? settings.recentJsonFolder : AppFolder();
        string file = await WindowsDialogs.OpenFile("코디 JSON 불러오기", "JSON (*.json)\0*.json\0", dir);
        if (this == null || file == null) return;      // 취소: 안내 화면 유지
        var lr = JsonStore.ReadEditState(file);
        if (lr.Status != LoadStatus.Ok)
        {
            ui.ShowChoice("불러오기 실패", "선택한 JSON 을 읽지 못했습니다.", lr.Error,
                new List<(string, Action)> { ("돌아가기", RestoreOrStart) });
            return;
        }
        ui.CloseModal();
        PreserveBroken();
        Apply(lr.Value, true);
    }

    /// <summary>MobiAtelier 실행 파일이 있는 폴더(SaveLoad.md).</summary>
    public static string AppFolder() =>
        Application.isEditor ? Directory.GetCurrentDirectory() : Path.GetDirectoryName(Application.dataPath);

    // ------------------------------------------------------------ 조립

    void Apply(EditState newState, bool saveNow)
    {
        ui.ShowLoading("캐릭터를 조립하는 중...");
        try
        {
            Build(newState);
            var b = view.VisibleBounds();
            cameraCtl.Pivot = new Vector3(0, b.size.y > 0.1f ? b.center.y : 0.9f, 0);
            cameraCtl.FitSize(b.size.y, Mathf.Max(b.size.x, b.size.z));
            cameraCtl.Load(state.camera);
            ui.HideLoading();
            EnsurePanel();
            panel.SetError(null);
            panel.Refresh(state, plan);
            if (saveNow) autosave.Flush();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            ui.ShowChoice("캐릭터를 조립하지 못했습니다", "모델을 읽는 중 오류가 발생했습니다.", e.Message,
                new List<(string, Action)>
                {
                    ("새 작업 시작", () => { ui.CloseModal(); StartNew(); }),
                    ("종료", Quit),
                });
        }
    }

    /// <summary>상태를 조립해 장면을 교체한다. 실패하면 예외를 던지고 기존 장면·상태를 그대로 둔다.</summary>
    void Build(EditState next)
    {
        var nextPlan = PlanResolver.Resolve(next, data, files);
        var built = CharacterAssembler.Build(files, bundles, nextPlan, characterShader);
        if (view) Destroy(view.gameObject);
        view = built;
        state = next;
        plan = nextPlan;
        ui.SetNotes(Notes(plan));
    }

    void EnsurePanel()
    {
        if (panel != null) return;
        panel = new CustomizePanel(ui.WindowLayer, settings, SaveSettings, Edit);
        panel.Bind(data);
    }

    void SaveSettings()
    {
        try { settings.Save(); } catch (Exception e) { Debug.LogWarning("[MobiAtelier] 앱 설정 저장 실패: " + e.Message); }
    }

    /// <summary>
    /// 꾸미기 창의 편집 요청(ProgramFlow.md 2장). 현재 상태의 복사본을 바꿔 조립에 성공하면 장면·선택 UI 를 갱신하고 자동 저장 대상으로 표시한다.
    /// 조립에 실패하면 이전 선택과 장면을 유지한다. 카메라 시점·기준점은 바꾸지 않는다.
    /// </summary>
    void Edit(Action<EditState> change)
    {
        if (state == null || data == null || ui.ModalOpen) return;
        var next = state.Clone();
        next.camera = cameraCtl.Save();
        change(next);
        try
        {
            Build(next);
            panel.SetError(null);
            autosave.MarkDirty();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            panel.SetError("변경을 적용하지 못해 이전 선택을 유지합니다. " + e.Message);
        }
        panel.Refresh(state, plan);
    }

    /// <summary>시험용: -maScreenshot &lt;파일&gt; [-maShotDelay 초] 이면 시작 후 일정 시간 뒤 화면을 저장하고 종료한다.</summary>
    System.Collections.IEnumerator ShotAndQuit(string file)
    {
        float delay = float.TryParse(CommandLine.Value("-maShotDelay"), out var d) ? d : 8f;
        yield return new WaitForSecondsRealtime(delay);
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(file);
        yield return null; yield return null;
        Application.Quit();
    }

    /// <summary>화면 구석 메모. 의상 미적용·가림은 꾸미기 창에 표시하므로 여기에는 조립 중 알게 된 문제만 쓴다.</summary>
    static IEnumerable<string> Notes(AssemblyPlan plan) => plan.Notes;
}
