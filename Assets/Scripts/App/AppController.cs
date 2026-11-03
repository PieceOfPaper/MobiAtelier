// 앱 진입점과 상태 전환 (ProgramFlow.md)
//   시작: 앱 설정 읽기 → 게임 경로 검사(실패 시 경로 지정 팝업) → 데이터 로딩
//     → 마지막 작업 복원 / 없으면 기본 외형 2종 중 무작위 + 초보자 의상 / 읽기 실패면 복구 안내 → 모델 조립 → 일반 화면(패널 닫힘)
//   편집: 꾸미기 패널의 요청은 Edit·EditColors·SetBackground 로 받는다. 성공한 꾸미기 편집만 이력(EditHistory)에 넣는다.
//   시스템 메뉴(SystemMenu.md): 내보내기·가져오기·초기화·옵션·종료. ESC 는 가장 위의 UI 한 곳만 처리한다.
//   카메라 출력 영역: 패널이 열리면 남은 영역만 그린다(Camera.rect). 사용자 구도는 바꾸지 않는다.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public sealed class AppController : MonoBehaviour
{
    public Shader characterShader;
    public Shader gradientShader;
    public PanelSettings panelSettings;

    const string JsonFilter = "JSON (*.json)\0*.json\0";

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
    StudioBackground background;
    readonly EditHistory history = new EditHistory();
    /// <summary>Windows 파일·폴더 대화상자가 열려 있다. 뒤의 ESC·F1·단축키를 막는다.</summary>
    bool dialogOpen;
    /// <summary>저장 실패 안내에서 '저장 없이 종료'를 고른 경우만 true.</summary>
    bool quitWithoutSave;
    /// <summary>앱이 스스로 종료를 정했다(메뉴의 종료, 시작 화면의 종료, 시험 촬영). 창 닫기 요청과 구분한다.</summary>
#pragma warning disable CS0414 // 에디터 빌드에서는 창 닫기를 가로채지 않아 읽지 않는다
    static bool quitApproved;
#pragma warning restore CS0414
    bool helpOffered;

    public EditState State => state;
    public GameData Data => data;

    async void Start()
    {
        EnsureEventSystem();
        var cam = StageBuilder.CreateStage();
        cameraCtl = cam.gameObject.AddComponent<CameraController>();
        cameraCtl.Cam = cam;
        ui = new AppUI(gameObject, panelSettings);
        ui.HiddenChanged += hidden => UpdateCameraArea();
        cameraCtl.IsPointerOverUI = p => ui.IsPointerOver(p);
        cameraCtl.Committed += () =>
        {
            if (state == null) return;
            state.camera = cameraCtl.Save();
            MarkDirty();
            ui.SetLensSelection(cameraCtl.LensMultiplier);
        };
        autosave = new Autosave(() => state);
        autosave.Failed += msg =>
        {
            Debug.LogWarning("[MobiAtelier] 자동 저장 실패: " + msg);
            panel?.SetSaveStatus("자동 저장 실패", true);
            ui.ShowNotice("자동 저장에 실패했습니다. 작업은 그대로 남아 있으며, 메뉴의 내보내기로 보관할 수 있습니다.\n" + msg, true);
        };
        autosave.Saved += () => panel?.SetSaveStatus($"자동 저장됨 {DateTime.Now:HH:mm:ss}", false);
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

    void Update()
    {
        autosave?.Tick(Time.unscaledDeltaTime);
        var kb = Keyboard.current;
        if (state == null || ui == null || kb == null || dialogOpen) return;

        if (kb.escapeKey.wasPressedThisFrame) HandleEscape();
        // F1 은 모달·파일 대화상자 뒤의 UI 를 전환하지 않는다.
        if (kb.f1Key.wasPressedThisFrame && !ui.ModalOpen) ui.ToggleEditingUI();
        // Ctrl+Z / Ctrl+Y: 꾸미기 실행 취소·다시 실행. 텍스트 입력란(검색·HEX)이 입력을 가지면 실행하지 않는다.
        if (kb.ctrlKey.isPressed && !ui.ModalOpen && !ui.TextInputFocused)
        {
            if (kb.zKey.wasPressedThisFrame && !kb.shiftKey.isPressed) Undo();
            else if (kb.yKey.wasPressedThisFrame) Redo();
        }
    }

    /// <summary>
    /// ESC 입력 소유권(SystemMenu.md 4장): 모달(메뉴·옵션·확인) → 드롭다운 → 컬러피커 → 자세한 조작 안내 → 시스템 메뉴 열기.
    /// 패널은 ESC 로 접지 않는다. UI 숨김 중에도 메뉴를 열며, 닫으면 숨김 화면으로 돌아온다(메뉴는 숨김 대상 층이 아니다).
    /// </summary>
    void HandleEscape()
    {
        if (UIEscape.ConsumedRecently) return;
        if (ui.HandleModalEscape()) return;
        if (SearchDropdown.AnyOpen) { SearchDropdown.CloseAny(); return; }
        if (ColorPicker.IsOpen) { ColorPicker.CancelAny(); return; }
        if (!ui.UIHidden && ui.Help != null && ui.Help.DetailOpen) { ui.Help.CloseDetail(); return; }
        OpenMenu();
    }

    void LateUpdate()
    {
        if (background != null && cameraCtl) background.UpdateOccluders(cameraCtl.Cam.transform.position, cameraCtl.Pivot);
    }

    /// <summary>
    /// 종료 요청. 메뉴의 종료 등 앱이 정한 종료는 대기 중 저장 후 통과시킨다.
    /// 공개 빌드에서 Windows 창 닫기·Alt+F4 는 바로 끝내지 않고 시스템 메뉴를 열어 같은 종료 항목으로 연결한다(SystemMenu.md 3장 제안).
    /// 추가 종료 확인 팝업은 띄우지 않는다. 작업 화면에 들어가기 전(경로 지정·복구 안내)이면 그대로 종료한다.
    /// 에디터에서는 플레이 종료·에디터 닫기를 막지 않는다.
    /// </summary>
    bool OnWantsToQuit()
    {
#if !UNITY_EDITOR
        if (!quitApproved && state != null && ui != null)
        {
            if (!ui.ModalOpen && !dialogOpen) OpenMenu();
            return false;
        }
#endif
        if (!quitWithoutSave && autosave != null && autosave.Pending) autosave.Flush();
        return true;
    }

    void OnDestroy()
    {
        Application.wantsToQuit -= OnWantsToQuit;
        ColorPicker.CancelAny();
        SearchDropdown.CloseAny();
        if (view) DestroyImmediate(view.gameObject);
        background?.Dispose();
        bundles?.Dispose();
    }

    static void Quit()
    {
        quitApproved = true;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void MarkDirty()
    {
        autosave.MarkDirty();
        panel?.SetSaveStatus("자동 저장 대기 중", false);
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
                dialogOpen = true;
                string chosen = await WindowsDialogs.PickFolder("설치된 마비노기 모바일 폴더를 선택해 주세요.", InstallValidator.Normalize(p.Input.value));
                dialogOpen = false;
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
        background = new StudioBackground(cameraCtl.Cam, bundles, gradientShader);
        if (data.BackgroundError != null) Debug.LogWarning("[MobiAtelier] 배경 시트를 읽지 못했습니다: " + data.BackgroundError);
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
            case LoadStatus.Ok: ApplyChecked(lr.Value, false, true); break;
            default: ShowRestoreFailed(lr); break;
        }
    }

    /// <summary>새 작업: 기본 외형 2종 중 무작위 + 초보자 상의·하의·신발(Startup.md).</summary>
    void StartNew()
    {
        Apply(StateFactory.NewRandom(data, new System.Random()), true);
    }

    void ShowRestoreFailed(LoadResult<EditState> lr)
    {
        var actions = new List<(string, Action)>();
        var bak = JsonStore.ReadEditState(JsonStore.AutosavePath + ".bak");
        if (bak.Status == LoadStatus.Ok)
            actions.Add(("백업에서 복구", () => { ui.CloseModal(); PreserveBroken(); ApplyChecked(bak.Value, true, false); }));
        actions.Add(("다른 JSON 가져오기", LoadOtherJson));
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
        dialogOpen = true;
        string file = await WindowsDialogs.OpenFile("코디 JSON 가져오기", JsonFilter, JsonFolder());
        dialogOpen = false;
        if (this == null || file == null) return;      // 취소: 안내 화면 유지
        var lr = JsonStore.ReadEditState(file);
        if (lr.Status != LoadStatus.Ok)
        {
            ui.ShowChoice("가져오기 실패", "선택한 JSON 을 읽지 못했습니다.", lr.Error,
                new List<(string, Action)> { ("돌아가기", RestoreOrStart) });
            return;
        }
        ui.CloseModal();
        PreserveBroken();
        ApplyChecked(lr.Value, true, false);
    }

    /// <summary>
    /// 시작 시 작업을 복원하기 전에 지금 설치본으로 복원할 수 없는 항목을 검사한다(SaveLoad.md 부분 복원).
    /// 문제가 있으면 사유를 보이고 부분 복원 / 다른 선택을 고르게 한다. 승인 전에는 자동 저장 파일을 건드리지 않는다.
    /// 승인하면 원본 자동 저장 파일을 따로 보존하고 선택 ID·색은 그대로 둔 채(표시만 하지 않음) 시작한다.
    /// </summary>
    void ApplyChecked(EditState s, bool saveNow, bool fromAutosave)
    {
        var issues = RestoreCheck.Inspect(s, data);
        if (issues.Count == 0) { Apply(s, saveNow); return; }
        var actions = new List<(string, Action)>
        {
            ("부분 복원", () =>
            {
                ui.CloseModal();
                if (fromAutosave) PreservePartial();
                Apply(s, saveNow);
            }),
        };
        if (fromAutosave)
        {
            var bak = JsonStore.ReadEditState(JsonStore.AutosavePath + ".bak");
            if (bak.Status == LoadStatus.Ok && RestoreCheck.Inspect(bak.Value, data).Count == 0)
                actions.Add(("백업에서 복구", () => { ui.CloseModal(); PreservePartial(); Apply(bak.Value, true); }));
        }
        actions.Add(("다른 JSON 가져오기", LoadOtherJson));
        actions.Add(("새 작업 시작", () => { ui.CloseModal(); if (fromAutosave) PreservePartial(); StartNew(); }));
        actions.Add(("종료", Quit));
        ui.ShowChoice("일부 항목을 복원할 수 없습니다",
            "작업의 일부 항목을 지금 설치된 게임 데이터로 표시할 수 없습니다. 부분 복원하면 그 항목의 선택 ID·색은 보존하고 표시만 하지 않습니다.",
            RestoreCheck.Describe(issues), actions);
    }

    /// <summary>부분 복원·새 작업으로 자동 저장을 덮어쓰기 전에 원본을 보존한다.</summary>
    void PreservePartial()
    {
        try
        {
            string src = JsonStore.AutosavePath;
            if (!File.Exists(src)) return;
            string dst = Path.Combine(JsonStore.DataDir, $"autosave.partial-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(src, dst, false);
            Debug.Log("[MobiAtelier] 부분 복원 전 자동 저장 파일을 보존했습니다: " + dst);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            Debug.LogWarning("[MobiAtelier] 자동 저장 파일 보존 실패: " + e.Message);
        }
    }

    /// <summary>MobiAtelier 실행 파일이 있는 폴더(SaveLoad.md).</summary>
    public static string AppFolder() =>
        Application.isEditor ? Directory.GetCurrentDirectory() : Path.GetDirectoryName(Application.dataPath);

    /// <summary>내보내기·가져오기 대화상자의 시작 폴더: 최근 내보내기에 성공한 폴더, 없거나 지워졌으면 프로그램 폴더.</summary>
    string JsonFolder() => Directory.Exists(settings.recentJsonFolder ?? "") ? settings.recentJsonFolder : AppFolder();

    // ------------------------------------------------------------ 조립

    /// <summary>시작 시 작업을 적용한다. 실패하면 새 작업·종료를 안내한다.</summary>
    void Apply(EditState newState, bool saveNow)
    {
        ui.ShowLoading("캐릭터를 조립하는 중...");
        try
        {
            Build(newState);
            string bgError = background.Apply(StudioBackground.Resolve(data, state.background));
            if (bgError != null) Debug.LogWarning("[MobiAtelier] " + bgError);
            ui.HideLoading();
            EnsurePanel();
            FrameCamera(state.camera);
            history.Clear();
            UpdateHistoryButtons();
            panel.SetError(bgError);
            panel.Refresh(state, plan);
            if (saveNow) autosave.Flush();
            if (!helpOffered) { helpOffered = true; ui.Help?.ShowOnFirstUse(); }
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

    /// <summary>캐릭터 크기로 기준점·기본 거리를 정하고 저장된 시점(없으면 기본 구도)을 적용한다.</summary>
    void FrameCamera(CameraState saved)
    {
        var b = view.VisibleBounds();
        cameraCtl.Pivot = new Vector3(0, b.size.y > 0.1f ? b.center.y : 0.9f, 0);
        cameraCtl.FitSize(b.size.y, Mathf.Max(b.size.x, b.size.z));
        cameraCtl.Load(saved);
        state.camera = cameraCtl.Save();
        ui.SetLensSelection(cameraCtl.LensMultiplier);
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
        panel = new CustomizePanel(ui.WindowLayer, settings, SaveSettings, Edit, EditColors, () => view,
            () => background?.CurrentId, SetBackground, Undo, Redo);
        panel.Bind(data);
        ui.AddViewControls(cameraCtl.SetLensMultiplier, () => state != null && !ui.ModalOpen && !dialogOpen, OpenMenu, settings, SaveSettings);
        panel.CameraAreaChanged += area => UpdateCameraArea();
        UpdateCameraArea();
    }

    /// <summary>
    /// 카메라 출력 영역(WindowAndCustomizationUI.md 3장). 패널 닫힘·전체 UI 숨김은 전체, 열림은 패널을 뺀 영역.
    /// 회전·팬·거리·화각과 기준점은 그대로 두고 화면비만 바뀐다(FitSize·ResetView 를 부르지 않는다).
    /// </summary>
    void UpdateCameraArea()
    {
        if (panel == null || !cameraCtl) return;
        var area = panel.CameraArea;
        if (area.width <= 0 || area.height <= 0) { cameraCtl.Cam.rect = new Rect(0, 0, 1, 1); return; }   // 첫 배치 전
        ui.SetCameraArea(area, panel.BottomToolInset);
        cameraCtl.Cam.rect = ui.UIHidden ? new Rect(0, 0, 1, 1) : ui.ToViewportRect(area);
    }

    /// <summary>
    /// 배경 선택(Background.md). 장면 상태이므로 자동 저장·코디 JSON 에 들어간다(SaveLoad.md 확정 원칙).
    /// 캐릭터는 다시 조립하지 않는다. 카메라 시점은 바꾸지 않는다. 꾸미기 이력에 넣는다(UndoRedo.md).
    /// </summary>
    void SetBackground(string id)
    {
        if (state == null || background == null || data == null || ui.ModalOpen) return;
        var b = data.Background(id);
        if (!StudioBackground.Selectable(b)) return;
        var before = state;
        string error = background.Apply(b, true);
        if (background.LastFailed)
        {
            // 새 배경을 준비하지 못했다. 이전 배경·장면·저장·이력을 그대로 두고 같은 항목을 다시 고를 수 있게 한다.
            panel.SetError("배경을 바꾸지 못해 이전 배경을 유지합니다. " + error);
            panel.Refresh(state, plan);
            return;
        }
        var next = state.Clone();
        next.camera = cameraCtl.Save();
        next.background = background.CurrentId;
        state = next;
        MarkDirty();
        if (history.Record(before, state)) UpdateHistoryButtons();
        panel.SetError(error);
        panel.Refresh(state, plan);
    }

    void SaveSettings()
    {
        try { settings.Save(); } catch (Exception e) { Debug.LogWarning("[MobiAtelier] 앱 설정 저장 실패: " + e.Message); }
    }

    /// <summary>
    /// 꾸미기 패널의 편집 요청(ProgramFlow.md 2장). 현재 상태의 복사본을 바꿔 조립에 성공하면 장면·선택 UI 를 갱신하고 자동 저장·이력에 넣는다.
    /// 조립에 실패하면 이전 선택과 장면을 유지한다. 카메라 시점·기준점은 바꾸지 않는다.
    /// </summary>
    void Edit(Action<EditState> change)
    {
        if (state == null || data == null || ui.ModalOpen) return;
        var before = state;
        var next = state.Clone();
        next.camera = cameraCtl.Save();
        change(next);
        try
        {
            Build(next);
            panel.SetError(null);
            MarkDirty();
            if (history.Record(before, state)) UpdateHistoryButtons();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            panel.SetError("변경을 적용하지 못해 이전 선택을 유지합니다. " + e.Message);
        }
        panel.Refresh(state, plan);
    }

    /// <summary>
    /// 색만 바꾸는 편집(외형 색 시트 선택, 의상 염색). 모델은 그대로 두고 계획을 다시 풀어 머티리얼 색만 바꾼다.
    /// 색 슬롯 연결(DyeInfo)은 조립 때 정해졌으므로 다시 조립할 필요가 없다.
    /// </summary>
    void EditColors(Action<EditState> change)
    {
        if (state == null || data == null || ui.ModalOpen) return;
        if (!view) { Edit(change); return; }
        var before = state;
        var next = state.Clone();
        next.camera = cameraCtl.Save();
        change(next);
        try
        {
            var nextPlan = PlanResolver.Resolve(next, data, files);
            view.ApplyPlanColors(nextPlan);
            view.Plan = nextPlan;
            state = next;
            plan = nextPlan;
            panel.SetError(null);
            MarkDirty();
            if (history.Record(before, state)) UpdateHistoryButtons();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            view.ApplyPlanColors(plan);
            panel.SetError("색을 적용하지 못해 이전 색을 유지합니다. " + e.Message);
        }
        panel.Refresh(state, plan);
    }

    // ------------------------------------------------------------ 꾸미기 이력 (UndoRedo.md)

    void Undo() => StepHistory(true);
    void Redo() => StepHistory(false);

    /// <summary>
    /// 이력의 꾸미기 값(외형·의상·염색·배경)만 복원한다. 현재 카메라(회전·팬·거리·화각)는 유지한다.
    /// 복원에 실패하면 장면·이력을 그대로 두고 이유를 표시한다.
    /// </summary>
    void StepHistory(bool back)
    {
        if (state == null || data == null || ui.ModalOpen || dialogOpen) return;
        var target = back ? history.PeekUndo() : history.PeekRedo();
        if (target == null) return;
        SearchDropdown.CloseAny();
        ColorPicker.CancelAny();
        var current = state;
        var next = target.Clone();
        next.camera = cameraCtl.Save();
        next.appVersion = current.appVersion;
        string label = back ? "실행 취소" : "다시 실행";
        // 배경을 먼저 준비한다. 실패하면 아무것도 바꾸지 않는다.
        string bgError = null, previousBg = background.CurrentId;
        bool bgChanged = current.background != next.background;
        if (bgChanged)
        {
            bgError = background.Apply(StudioBackground.Resolve(data, next.background), true);
            if (background.LastFailed)
            {
                panel.SetError(label + "의 배경을 준비하지 못해 현재 상태를 유지합니다. " + bgError);
                panel.Refresh(state, plan);
                return;
            }
        }
        try
        {
            if (!EditHistory.SameCharacter(current, next)) Build(next);
            else state = next;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            if (bgChanged) background.Apply(StudioBackground.Resolve(data, previousBg));
            panel.SetError(label + "를 적용하지 못해 현재 상태를 유지합니다. " + e.Message);
            panel.Refresh(state, plan);
            return;
        }
        if (back) history.CommitUndo(current); else history.CommitRedo(current);
        UpdateHistoryButtons();
        MarkDirty();
        panel.SetError(bgError);
        panel.Refresh(state, plan);
    }

    void UpdateHistoryButtons() => panel?.SetHistory(history.CanUndo, history.CanRedo);

    /// <summary>
    /// 작업 전체 교체(가져오기·초기화). 새 상태를 조립한 뒤에만 장면을 바꾸고 이력을 비운다.
    /// 실패하면 기존 장면·작업·이력을 유지한다. 바꾸기 전 대기 중인 자동 저장을 먼저 써서 직전 작업이 백업(.bak)에 남게 한다.
    /// </summary>
    bool ReplaceWork(EditState next, bool defaultCamera, out string error)
    {
        error = null;
        SearchDropdown.CloseAny();
        ColorPicker.CancelAny();
        state.camera = cameraCtl.Save();
        autosave.Flush();
        try
        {
            Build(next);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            error = e.Message;
            return false;
        }
        string bgError = background.Apply(StudioBackground.Resolve(data, state.background));
        FrameCamera(defaultCamera ? null : state.camera);
        history.Clear();
        UpdateHistoryButtons();
        panel.SetError(bgError);
        panel.Refresh(state, plan);
        autosave.Flush();
        return true;
    }

    // ------------------------------------------------------------ 시스템 메뉴 (SystemMenu.md)

    void OpenMenu()
    {
        if (state == null || dialogOpen) return;
        SearchDropdown.CloseAny();
        // 열린 컬러피커는 다른 작업 시작 정책(Dye.md)대로 미리보던 색을 적용하고 닫는다.
        ColorPicker.ApplyAny();
        ui.ShowSystemMenu(new List<(string, string, Action, bool)>
        {
            ("내보내기(Export)", "현재 외형·의상·염색·배경·카메라를 JSON 파일로 보관합니다", Export, false),
            ("가져오기(Import)", "JSON 파일을 검사하고 작업을 교체합니다. 성공하면 꾸미기 이력이 초기화됩니다", Import, false),
            ("초기화", "현재 성별 기본 코디·기본 카메라로 돌아갑니다. 배경은 유지합니다", ConfirmReset, true),
            ("옵션", "게임 설치 경로를 바꾸고 적용 후 다시 시작합니다", OpenOptions, false),
            ("종료", "대기 중 작업을 저장한 뒤 종료합니다", QuitFromMenu, true),
        }, ui.CloseModal);
    }

    /// <summary>내보내기(SaveLoad.md). 취소하면 메뉴로 돌아온다. 이력은 지우지 않는다.</summary>
    async void Export()
    {
        if (state == null || dialogOpen) return;
        dialogOpen = true;
        string file = await WindowsDialogs.SaveFile("코디 JSON 내보내기", JsonFilter, JsonFolder(), $"MobiAtelier_{DateTime.Now:yyyyMMdd_HHmmss}.json");
        dialogOpen = false;
        if (this == null || file == null) return;
        try
        {
            var s = state.Clone();
            s.camera = cameraCtl.Save();
            s.appVersion = Application.version;
            JsonStore.WriteUserFile(file, JsonUtility.ToJson(s, true));
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
        {
            ui.ShowChoice("내보내기 실패", "파일을 쓰지 못했습니다. 쓰기 권한이 있는 다른 폴더를 선택해 주세요.", e.Message,
                new List<(string, Action)> { ("돌아가기", OpenMenu) }, OpenMenu);
            return;
        }
        settings.recentJsonFolder = Path.GetDirectoryName(file);
        SaveSettings();
        ui.CloseModal();
        ui.ShowNotice("내보냈습니다: " + Path.GetFileName(file), false);
    }

    /// <summary>가져오기(SaveLoad.md). 성공하면 작업을 교체하고 이력을 비운다. 취소·실패는 기존 작업·이력을 유지한다.</summary>
    async void Import()
    {
        if (state == null || dialogOpen) return;
        dialogOpen = true;
        string file = await WindowsDialogs.OpenFile("코디 JSON 가져오기", JsonFilter, JsonFolder());
        dialogOpen = false;
        if (this == null || file == null) return;
        var lr = JsonStore.ReadEditState(file);
        if (lr.Status != LoadStatus.Ok)
        {
            string msg = lr.Status == LoadStatus.Unsupported ? "지원하지 않는 형식의 파일입니다." : "선택한 JSON 을 읽지 못했습니다.";
            ui.ShowChoice("가져오기 실패", msg + " 현재 작업을 유지합니다.", lr.Error,
                new List<(string, Action)> { ("돌아가기", OpenMenu) }, OpenMenu);
            return;
        }
        var issues = RestoreCheck.Inspect(lr.Value, data);
        if (issues.Count == 0) { FinishImport(lr.Value, file, false); return; }
        // 부분 복원 / 취소. 승인 전에는 현재 작업·자동 저장·이력을 그대로 둔다. 선택한 JSON 파일은 고치지 않는다.
        ui.ShowChoice("일부 항목을 복원할 수 없습니다",
            "선택한 파일의 일부 항목을 지금 설치된 게임 데이터로 표시할 수 없습니다. 부분 복원하면 그 항목의 선택 ID·색은 보존하고 표시만 하지 않습니다.",
            RestoreCheck.Describe(issues),
            new List<(string, Action)> { ("취소", OpenMenu), ("부분 복원", () => FinishImport(lr.Value, file, true)) }, OpenMenu);
    }

    void FinishImport(EditState s, string file, bool partial)
    {
        ui.CloseModal();
        ui.ShowLoading("캐릭터를 조립하는 중...");
        bool ok = ReplaceWork(s, false, out string error);
        ui.HideLoading();
        if (!ok)
        {
            ui.ShowChoice("가져오기 실패", "선택한 코디를 적용하지 못해 현재 작업을 유지합니다.", error,
                new List<(string, Action)> { ("돌아가기", OpenMenu) }, OpenMenu);
            return;
        }
        ui.ShowNotice((partial ? "일부 항목을 제외하고 가져왔습니다: " : "가져왔습니다: ") + Path.GetFileName(file), false);
    }

    /// <summary>코디 초기화 확인(Reset.md). 취소·X·바깥 클릭·ESC 는 메뉴로 돌아간다.</summary>
    void ConfirmReset()
    {
        ui.ShowChoice("코디 초기화",
            "현재 코디를 초기화하시겠습니까? 외형·의상·염색·카메라가 기본 상태로 돌아가며 실행 취소할 수 없습니다.",
            "배경과 패널·옵션 설정은 유지합니다.",
            new List<(string, Action)> { ("취소", OpenMenu), ("초기화", DoReset) }, OpenMenu);
    }

    void DoReset()
    {
        EditState next;
        try { next = StateFactory.ResetFor(data, state); }
        catch (Exception e)
        {
            ui.ShowChoice("초기화 실패", "기본 외형을 찾지 못해 현재 작업을 유지합니다.", e.Message,
                new List<(string, Action)> { ("돌아가기", OpenMenu) }, OpenMenu);
            return;
        }
        ui.CloseModal();
        ui.ShowLoading("캐릭터를 조립하는 중...");
        bool ok = ReplaceWork(next, true, out string error);
        ui.HideLoading();
        if (!ok)
        {
            ui.ShowChoice("초기화 실패", "기본 코디를 조립하지 못해 현재 작업을 유지합니다.", error,
                new List<(string, Action)> { ("돌아가기", OpenMenu) }, OpenMenu);
            return;
        }
        ui.ShowNotice("코디를 초기화했습니다.", false);
    }

    // ------------------------------------------------------------ 옵션 (Options.md)

    bool SameInstallPath(string input) =>
        string.Equals(InstallValidator.Normalize(input ?? ""), settings.installPath ?? "", StringComparison.OrdinalIgnoreCase);

    void OpenOptions()
    {
        if (state == null || dialogOpen) return;
        ui.ShowOptions(settings.installPath, SameInstallPath, ApplyInstallPath,
            async p =>
            {
                p.SetBusy(true, "폴더 선택 중...");
                dialogOpen = true;
                string chosen = await WindowsDialogs.PickFolder("설치된 마비노기 모바일 폴더를 선택해 주세요.", InstallValidator.Normalize(p.Input.value));
                dialogOpen = false;
                if (this == null) return;
                p.SetBusy(false);
                p.Result.text = "";
                if (!string.IsNullOrEmpty(chosen)) p.Input.value = chosen;    // 취소하면 입력값 유지
                AppUI.SyncOptionsApply(p, SameInstallPath);
            },
            OpenMenu);
    }

    /// <summary>
    /// 경로 적용(Options.md 2장): 검사 → 현재 작업 저장 → 설정 기록 → 다시 시작.
    /// 어느 단계든 실패하면 현재 실행·경로·작업·이력을 유지하고 이유를 표시한다.
    /// </summary>
    async void ApplyInstallPath(AppUI.PathPopup p, string input)
    {
        if (p.Busy || SameInstallPath(input)) return;
        string path = InstallValidator.Normalize(input);
        p.SetBusy(true, "검사 중...");
        var r = await Task.Run(() => InstallValidator.Check(path));
        if (this == null) return;
        if (!r.Ok)
        {
            p.SetBusy(false);
            p.SetError(r.Message, r.Detail);
            AppUI.SyncOptionsApply(p, SameInstallPath);
            return;
        }
        p.SetBusy(true, "현재 작업을 저장하는 중...");
        state.camera = cameraCtl.Save();
        if (!autosave.Flush())
        {
            p.SetBusy(false);
            p.SetError("현재 작업을 저장하지 못해 다시 시작하지 않았습니다.", autosave.LastError);
            AppUI.SyncOptionsApply(p, SameInstallPath);
            return;
        }
        string old = settings.installPath;
        settings.installPath = path;
        try { settings.Save(); }
        catch (Exception e)
        {
            settings.installPath = old;
            p.SetBusy(false);
            p.SetError("설정을 기록하지 못해 다시 시작하지 않았습니다. 다시 시도해 주세요.", e.Message);
            AppUI.SyncOptionsApply(p, SameInstallPath);
            return;
        }
        if (!Restart())
        {
            settings.installPath = old;
            SaveSettings();
            p.SetBusy(false);
            p.SetError("다시 시작하지 못했습니다. 이전 경로와 작업을 유지합니다.", null);
            AppUI.SyncOptionsApply(p, SameInstallPath);
        }
    }

    /// <summary>
    /// 앱 내부 재시작: 앱 씬을 다시 불러와 시작 흐름(경로 검사·작업 복원)을 처음부터 실행한다.
    /// 새 실행은 패널 닫힘·빈 이력으로 시작한다. 번들·장면은 OnDestroy 에서 정리한다.
    /// </summary>
    bool Restart()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.buildIndex < 0) return false;
        ui.CloseModal();
        ui.ShowLoading("다시 시작하는 중...");
        SceneManager.LoadScene(scene.buildIndex);
        return true;
    }

    // ------------------------------------------------------------ 종료 (SystemMenu.md 3장)

    /// <summary>메뉴의 종료. 추가 확인 없이 저장 후 종료한다. 저장 실패 시 종료하지 않고 복구 선택을 보인다.</summary>
    void QuitFromMenu()
    {
        if (state != null) state.camera = cameraCtl.Save();
        if (autosave.Flush()) { Quit(); return; }
        ui.ShowChoice("저장 실패", "작업을 저장하지 못해 종료하지 않았습니다. 작업은 그대로 남아 있습니다.", autosave.LastError,
            new List<(string, Action)>
            {
                ("저장 없이 종료", () => { quitWithoutSave = true; Quit(); }),
                ("내보내기", Export),
                ("다시 시도", QuitFromMenu),
            }, OpenMenu);
    }

    /// <summary>시험용: -maScreenshot &lt;파일&gt; [-maShotDelay 초] 이면 시작 후 일정 시간 뒤 화면을 저장하고 종료한다.</summary>
    /// 시험용 화면 상태: -maShotUI open,menu,options,hidden,nohelp,edit,bg,lens3,undo (쉼표로 여러 개). 촬영 -maShotUIWait 초(기본 1) 전에 적용한다.
    System.Collections.IEnumerator ShotAndQuit(string file)
    {
        float delay = float.TryParse(CommandLine.Value("-maShotDelay"), out var d) ? d : 8f;
        // -maShotUIWait 초: UI 상태 적용 후 촬영까지 기다리는 시간(기본 1초). 연출 중간 프레임 확인용.
        float uiWait = float.TryParse(CommandLine.Value("-maShotUIWait"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var uw) ? uw : 1f;
        yield return new WaitForSecondsRealtime(Mathf.Max(0, delay - uiWait));
        string shotUI = CommandLine.Value("-maShotUI");
        if (shotUI != null && state != null && panel != null)
        {
            var opts = new HashSet<string>(shotUI.Split(','));
            if (opts.Contains("nohelp")) ui.Help?.CloseDetail();
            // edit: 상의를 벗긴다, bg: 배경을 바꾼다, undo: 한 번 실행 취소(이력 동작 확인용)
            if (opts.Contains("edit")) Edit(s => s.costumes.RemoveAll(c => c.part == "Top"));
            if (opts.Contains("bg")) SetBackground("BG_Gradation2");
            if (opts.Contains("lens3")) cameraCtl.SetLensMultiplier(3f);
            if (opts.Contains("undo")) Undo();
            if (opts.Contains("open")) panel.SetOpen(true);
            if (opts.Contains("hidden")) ui.ToggleEditingUI();
            if (opts.Contains("menu")) OpenMenu();
            if (opts.Contains("options")) OpenOptions();
        }
        yield return new WaitForSecondsRealtime(Mathf.Min(uiWait, delay));
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(file);
        yield return null; yield return null;
        quitApproved = true;
        Application.Quit();
    }

    /// <summary>화면 구석 메모. 의상 미적용·가림은 꾸미기 패널에 표시하므로 여기에는 조립 중 알게 된 문제만 쓴다.</summary>
    static IEnumerable<string> Notes(AssemblyPlan plan) => plan.Notes;
}
