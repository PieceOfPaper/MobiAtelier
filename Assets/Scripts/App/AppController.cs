// 앱 진입점과 상태 전환 (ProgramFlow.md)
//   시작: 앱 설정 읽기 → 게임 경로 검사(실패 시 경로 지정 팝업) → 데이터 로딩
//     → 마지막 작업 복원 / 없으면 기본 외형 2종 중 무작위 + 초보자 의상 / 읽기 실패면 복구 안내 → 모델 조립 → 일반 화면(패널 닫힘)
//   편집: 꾸미기 패널의 요청은 Edit·EditColors·SetBackground 로 받는다. 성공한 꾸미기 편집만 이력(EditHistory)에 넣는다.
//   시스템 메뉴(SystemMenu.md): 내보내기·가져오기·초기화·옵션·종료. ESC 는 가장 위의 UI 한 곳만 처리한다.
//   카메라 출력 영역: 패널이 열리면 남은 영역만 그린다(Camera.rect). 사용자 구도는 바꾸지 않는다.
//   소셜 모션(SocialMotion.md): 카메라 영역의 모션 버튼으로 재생하는 보기 도구. 저장·이력에 넣지 않는다. 다시 조립해도 같은 시점부터 잇는다.
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
    SocialMotionControl motionUI;
    /// <summary>재생 중인 소셜 모션과 고른 행동 이름(Random 은 고른 것). 멈추면 null.</summary>
    SocialActionData motionSocial;
    string motionAction;
    readonly System.Random motionRandom = new System.Random();
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
        if (shot != null)
        {
            Application.runInBackground = true;
            StartCoroutine(ShotAndQuit(shot));
        }

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
        if (!dialogOpen && pending == null) autosave?.Tick(Time.unscaledDeltaTime);
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
        DiscardPending();
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

    /// <summary>
    /// 새 작업: 기본 외형 2종 중 무작위 + 초보자 상의·하의·신발(Startup.md).
    /// 기본 코디에 없는 ID·제한·리소스 누락이 있으면 빈·불완전 상태를 정상 최초 결과로 확정하지 않고 다시 시도·종료를 안내한다.
    /// </summary>
    void StartNew(Action beforeCommit = null)
    {
        ui.ShowLoading("캐릭터를 조립하는 중...");
        Prepared p;
        try { p = Stage(StateFactory.NewRandom(data, new System.Random())); }
        catch (Exception e) { Debug.LogException(e); ui.HideLoading(); ShowStartNewFailed(e.Message, beforeCommit); return; }
        var problems = RestoreCheck.CharacterProblems(p.Plan);
        if (problems.Count > 0) { DiscardPending(); ui.HideLoading(); ShowStartNewFailed(RestoreCheck.Describe(problems), beforeCommit); return; }
        StartWith(p, true, beforeCommit);
    }

    void ShowStartNewFailed(string detail, Action beforeCommit)
    {
        ui.ShowChoice("기본 코디를 준비하지 못했습니다", "기본 외형·초보자 의상을 지금 설치본으로 조립하지 못했습니다. 게임 패치가 끝난 뒤 다시 시도해 주세요.", detail,
            new List<(string, Action)>
            {
                ("다시 시도", () => { ui.CloseModal(); StartNew(beforeCommit); }),
                ("다른 JSON 가져오기", () => LoadOtherJson(beforeCommit)),
                ("종료", Quit),
            });
    }

    void ShowRestoreFailed(LoadResult<EditState> lr)
    {
        var actions = new List<(string, Action)>();
        var bak = JsonStore.ReadEditState(JsonStore.AutosavePath + ".bak");
        if (bak.Status == LoadStatus.Ok)
            actions.Add(("백업에서 복구", () => { ui.CloseModal(); ApplyChecked(bak.Value, true, false, PreserveBroken); }));
        actions.Add(("다른 JSON 가져오기", () => LoadOtherJson(PreserveBroken)));
        actions.Add(("새 작업 시작", () => { ui.CloseModal(); StartNew(PreserveBroken); }));
        actions.Add(("종료", Quit));
        string msg = lr.Status == LoadStatus.Unsupported
            ? "마지막 작업 파일의 형식을 지원하지 않습니다."
            : "마지막 작업 파일을 읽지 못했습니다. 기존 파일은 그대로 보존합니다.";
        ui.ShowChoice("마지막 작업 복원 실패", msg, lr.Error, actions);
    }

    void PreserveBroken()
    {
        string kept = Autosave.PreserveBroken();
        if (kept != null) Debug.Log("[MobiAtelier] 읽지 못한 자동 저장 파일을 보존했습니다: " + kept);
    }

    /// <summary>
    /// 시작 화면(복구 안내)에서 다른 JSON 을 고른다. 취소하면 안내 화면을 그대로 둔다.
    /// beforeApply: 조립·검사·승인을 마친 뒤 장면 교체 직전에 실행한다(기존 자동 저장 파일 보존 등).
    /// </summary>
    async void LoadOtherJson(Action beforeApply = null)
    {
        dialogOpen = true;
        string file = await WindowsDialogs.OpenFile("코디 JSON 가져오기", JsonFilter, JsonFolder());
        dialogOpen = false;
        if (this == null || file == null) return;      // 취소: 안내 화면 유지
        var lr = JsonStore.ReadEditState(file);
        if (lr.Status != LoadStatus.Ok)
        {
            DiscardPending();
            ui.ShowChoice("가져오기 실패", "선택한 JSON 을 읽지 못했습니다.", lr.Error,
                new List<(string, Action)> { ("돌아가기", RestoreOrStart) });
            return;
        }
        ui.CloseModal();
        ApplyChecked(lr.Value, true, false, beforeApply);
    }

    /// <summary>
    /// 시작 시 작업을 복원한다(SaveLoad.md 부분 복원). 새 캐릭터를 화면 밖에서 먼저 조립하고, 계획·조립 결과에서
    /// 지금 설치본으로 복원할 수 없는 항목을 찾는다. 문제가 있으면 사유를 보이고 부분 복원 / 다른 선택을 고르게 한다.
    /// 승인 전에는 자동 저장 파일을 건드리지 않는다. 승인하면 원본 자동 저장 파일을 따로 보존하고
    /// 선택 ID·색은 그대로 둔 채(제한·누락 항목은 표시하지 않고 필수 외형은 공개 기본값으로 대신 표시) 시작한다.
    /// </summary>
    void ApplyChecked(EditState s, bool saveNow, bool fromAutosave, Action beforeCommit = null)
    {
        ui.ShowLoading("캐릭터를 조립하는 중...");
        Prepared p;
        try { p = Stage(s); }
        catch (Exception e)
        {
            Debug.LogException(e);
            ui.HideLoading();
            ShowRestoreChoices("캐릭터를 조립하지 못했습니다", "작업 파일의 캐릭터를 지금 설치본으로 조립하지 못했습니다. 기존 파일은 그대로 보존합니다.",
                e.Message, null, saveNow, fromAutosave, beforeCommit);
            return;
        }
        var issues = InspectPrepared(p);
        if (issues.Count == 0) { StartWith(p, saveNow, beforeCommit); return; }
        ui.HideLoading();
        ShowRestoreChoices("일부 항목을 복원할 수 없습니다",
            "작업의 일부 항목을 지금 설치된 게임 데이터로 표시할 수 없습니다. 부분 복원하면 그 항목의 선택 ID·색은 보존하고 표시만 하지 않습니다(필수 외형은 공개 기본값으로 대신 표시).",
            RestoreCheck.Describe(issues), p, saveNow, fromAutosave, beforeCommit);
    }

    /// <summary>시작 복원의 선택지. prepared 가 있으면 부분 복원을 제공한다(그 외 선택은 준비한 캐릭터를 버린다).</summary>
    void ShowRestoreChoices(string title, string message, string detail, Prepared prepared, bool saveNow, bool fromAutosave, Action beforeCommit)
    {
        var actions = new List<(string, Action)>();
        if (prepared != null)
            actions.Add(("부분 복원", () =>
            {
                ui.CloseModal();
                StartWith(prepared, saveNow, fromAutosave ? PreservePartial : beforeCommit);
            }));
        if (fromAutosave)
        {
            var bak = JsonStore.ReadEditState(JsonStore.AutosavePath + ".bak");
            if (bak.Status == LoadStatus.Ok && BackupLooksClean(bak.Value))
                actions.Add(("백업에서 복구", () => { DiscardPending(); ui.CloseModal(); ApplyChecked(bak.Value, true, false, PreservePartial); }));
        }
        actions.Add(("다른 JSON 가져오기", () => LoadOtherJson(fromAutosave ? PreservePartial : beforeCommit)));
        actions.Add(("새 작업 시작", () => { DiscardPending(); ui.CloseModal(); StartNew(fromAutosave ? PreservePartial : beforeCommit); }));
        actions.Add(("종료", Quit));
        ui.ShowChoice(title, message, detail, actions);
        // 시험 촬영: -maShotUI 에 partial 이 있으면 부분 복원을 고른다(승인 뒤 화면 확인용).
        if (prepared != null && ShotOption("partial")) actions[0].Item2();
    }

    /// <summary>백업 파일이 계획 단계에서 문제가 없는지(조립하지 않는 빠른 검사). 고르면 다시 전체 검사를 한다.</summary>
    bool BackupLooksClean(EditState s)
    {
        try { return RestoreCheck.Inspect(s, data, PlanResolver.Resolve(s, data, files)).Count == 0; }
        catch (Exception) { return false; }
    }

    static bool ShotOption(string name)
    {
        string v = CommandLine.Value("-maShotUI");
        return v != null && CommandLine.Value("-maScreenshot") != null && Array.IndexOf(v.Split(','), name) >= 0;
    }

    /// <summary>부분 복원·새 작업으로 자동 저장을 덮어쓰기 전에 원본을 보존한다.</summary>
    void PreservePartial()
    {
            string src = JsonStore.AutosavePath;
            if (!File.Exists(src)) return;
            string dst = Path.Combine(JsonStore.DataDir, $"autosave.partial-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
            File.Copy(src, dst, false);
            Debug.Log("[MobiAtelier] 부분 복원 전 자동 저장 파일을 보존했습니다: " + dst);
    }

    /// <summary>MobiAtelier 실행 파일이 있는 폴더(SaveLoad.md).</summary>
    public static string AppFolder() =>
        Application.isEditor ? Directory.GetCurrentDirectory() : Path.GetDirectoryName(Application.dataPath);

    /// <summary>내보내기·가져오기 대화상자의 시작 폴더: 최근 내보내기에 성공한 폴더, 없거나 지워졌으면 프로그램 폴더.</summary>
    string JsonFolder() => Directory.Exists(settings.recentJsonFolder ?? "") ? settings.recentJsonFolder : AppFolder();

    // ------------------------------------------------------------ 조립

    /// <summary>시작 시 준비한 작업으로 장면을 연다(검사·승인을 마친 뒤).</summary>
    void StartWith(Prepared p, bool saveNow, Action beforeCommit = null)
    {
        try { beforeCommit?.Invoke(); }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            ui.HideLoading();
            ui.ShowChoice("원본 보존 실패", "원본 자동 저장을 보존하지 못해 작업을 교체하지 않았습니다.", e.Message,
                new List<(string, Action)> { ("다시 시도", () => { ui.CloseModal(); StartWith(p, saveNow, beforeCommit); }), ("종료", Quit) });
            return;
        }
        string bgError = p.Background?.Error;
        if (p.Background != null) background.Commit(p.Background);
        Commit(p);
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

    /// <summary>화면 밖에서 조립을 마쳤지만 아직 장면에 넣지 않은 캐릭터와 그 상태·계획.</summary>
    sealed class Prepared
    {
        public EditState State;
        public AssemblyPlan Plan;
        public CharacterView View;
        public StudioBackground.Prepared Background;
    }

    /// <summary>승인 대기 중인 캐릭터를 두는 위치(카메라에 보이지 않는 곳).</summary>
    static readonly Vector3 StagingPosition = new Vector3(0, -10000, 0);

    /// <summary>복원·가져오기 승인을 기다리는 준비본. 한 번에 하나만 둔다.</summary>
    Prepared pending;

    /// <summary>
    /// 상태를 조립 계획으로 풀고 새 캐릭터를 화면 밖에 조립한다. 기존 장면·상태·이력은 바꾸지 않는다.
    /// 필수 외형의 공개 기본값도 준비하지 못하면(PlanException)·조립이 예외로 끝나면 던진다.
    /// </summary>
    /// <param name="transition">true 면 현재 계획을 직전 장면으로 넘겨 무기 꺼내기·넣기 도입 모션을 붙인다(편집·실행 취소).</param>
    Prepared Prepare(EditState next, bool transition)
    {
        var built = CharacterAssembler.BuildValidated(files, bundles, next, data, characterShader, out var nextPlan, transition ? plan : null);
        built.transform.position = StagingPosition;
        built.gameObject.SetActive(false);
        return new Prepared { State = next, Plan = nextPlan, View = built };
    }

    /// <summary>승인 대기용 준비. 이전 대기본은 버린다.</summary>
    Prepared Stage(EditState next)
    {
        DiscardPending();
        var p = Prepare(next, false);
        try { p.Background = background.Prepare(StudioBackground.Resolve(data, next.background)); }
        catch { Discard(p); throw; }
        return pending = p;
    }

    void Discard(Prepared p)
    {
        if (p == null) return;
        CharacterAssembler.Release(p.View);
        p.Background?.Dispose();
        p.View = null;
        if (pending == p) pending = null;
    }

    void DiscardPending() => Discard(pending);

    static PlanIssue NewBuildFailure(AssemblyPlan before, AssemblyPlan after) =>
        after.Issues.Find(i => i.IsFailure && (before == null || !before.Issues.Exists(o => o.IsFailure && o.Same(i))));

    List<string> InspectPrepared(Prepared p)
    {
        var issues = RestoreCheck.Inspect(p.State, data, p.Plan);
        if (p.Background?.Error != null) issues.Add(p.Background.Error + ". 색 배경으로 대신 표시합니다");
        return issues;
    }

    /// <summary>준비한 캐릭터로 장면을 교체한다. 필수 외형 대체 기록을 상태에 적는다(보관한 원래 선택은 그대로).</summary>
    void Commit(Prepared p)
    {
        var built = p.View;
        if (!built) throw new InvalidOperationException("이미 폐기된 조립 요청입니다.");
        if (pending == p) pending = null;
        built.transform.position = Vector3.zero;
        built.gameObject.SetActive(true);
        // 재생 중인 소셜 모션은 새 캐릭터에서 같은 시점부터 잇는다(SocialMotion.md 4장 6·7).
        var oldMotion = view ? view.GetComponent<CharacterMotion>() : null;
        double motionTime = oldMotion && oldMotion.CurrentAction != null ? oldMotion.ActionTime : -1;
        CharacterAssembler.Release(view);
        view = built;
        state = p.State;
        plan = p.Plan;
        p.View = null;
        RecordSubstitutes(state, plan);
        ui.SetNotes(Notes(plan));
        if (motionSocial != null)
        {
            if (motionTime >= 0) StartMotion(motionSocial, motionAction, motionTime, false);
            else StopMotionUI();
        }
    }

    // ------------------------------------------------------------ 소셜 모션 (SocialMotion.md)

    List<DropdownItem> MotionItems()
    {
        var items = new List<DropdownItem>();
        foreach (var s in data.SocialActions)
            if (SocialMotionLoader.Listed(s))
                items.Add(new DropdownItem { Id = s.Id, Name = s.Id + (string.IsNullOrEmpty(s.DisplayName) ? "" : " - " + s.DisplayName) });
        return items;
    }

    static string MotionLabel(SocialActionData s) => s.Id + (string.IsNullOrEmpty(s.DisplayName) ? "" : " - " + s.DisplayName);

    /// <summary>모션 목록에서 고른 것을 처음부터 재생한다. Random 행동은 고를 때마다 하나를 새로 뽑는다.</summary>
    void PlayMotion(string id)
    {
        var s = data.SocialActions.Find(x => x.Id == id);
        if (s == null || !view) return;
        StartMotion(s, SocialMotionLoader.PickAction(s, motionRandom), 0, true);
    }

    /// <summary>현재 캐릭터에 맞게 읽어 재생한다. 실패하면 알리고 모션을 멈춘다(장면은 그대로).</summary>
    bool StartMotion(SocialActionData s, string actionName, double startTime, bool fadeIn)
    {
        var motion = view ? view.GetComponent<CharacterMotion>() : null;
        if (!motion) { ui.ShowNotice("기본 모션이 없는 캐릭터라 모션을 재생할 수 없습니다.", true); StopMotionUI(); return false; }
        ActionMotion a;
        string error;
        try { a = SocialMotionLoader.Load(files, bundles, data, view, characterShader, s, actionName, null, out error); }
        catch (Exception e) { Debug.LogException(e); a = null; error = e.Message; }
        if (a == null)
        {
            ui.ShowNotice($"{MotionLabel(s)}: {error}", true);
            motion.StopAction(false);
            StopMotionUI();
            return false;
        }
        motionSocial = s;
        motionAction = actionName;
        motion.ActionFinished -= OnMotionFinished;
        motion.ActionFinished += OnMotionFinished;
        motion.PlayAction(a, startTime, fadeIn);
        motionUI?.SetPlaying(MotionLabel(s));
        return true;
    }

    void StopMotion()
    {
        var motion = view ? view.GetComponent<CharacterMotion>() : null;
        StopMotionUI();
        if (motion) motion.StopAction(true);
    }

    void OnMotionFinished()
    {
        var motion = view ? view.GetComponent<CharacterMotion>() : null;
        if (motion && motion.CurrentAction != null) return;   // 다른 모션으로 바뀐 경우
        StopMotionUI();
    }

    void StopMotionUI()
    {
        motionSocial = null;
        motionAction = null;
        motionUI?.SetIdle();
    }

    void EnsurePanel()
    {
        if (panel != null) return;
        panel = new CustomizePanel(ui.WindowLayer, settings, SaveSettings, Edit, EditColors, () => view,
            () => background?.CurrentId, SetBackground, Undo, Redo);
        panel.Bind(data);
        ui.AddViewControls(cameraCtl.SetLensMultiplier, () => state != null && !ui.ModalOpen && !dialogOpen, OpenMenu, settings, SaveSettings);
        if (data.SocialActions.Count > 0)
        {
            motionUI = new SocialMotionControl(() => ui.WindowLayer, MotionItems, PlayMotion, StopMotion, () => state != null && !ui.ModalOpen && !dialogOpen);
            ui.AddMotionControl(motionUI.Root);
        }
        else if (data.SocialError != null) Debug.LogWarning("[MobiAtelier] " + data.SocialError);
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
        Prepared p = null;
        try
        {
            p = Prepare(next, true);
            // 이 편집이 새로 만든 리소스 읽기 실패(모델 정의·프리팹·메시 누락)가 있으면 확정하지 않는다.
            // 이전부터 있던 실패(부분 복원한 항목)는 다른 편집을 막지 않는다. 정상적인 착용 불가·가림은 실패가 아니다.
            var failed = NewBuildFailure(plan, p.Plan);
            if (failed != null)
            {
                Discard(p);
                panel.SetError("변경을 적용하지 못해 이전 선택을 유지합니다. " + failed.Describe());
                panel.Refresh(state, plan);
                return;
            }
            Commit(p);
            panel.SetError(null);
            MarkDirty();
            if (history.Record(before, state)) UpdateHistoryButtons();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Discard(p);
            panel.SetError("변경을 적용하지 못해 이전 선택을 유지합니다. " + e.Message);
        }
        panel.Refresh(state, plan);
    }

    /// <summary>필수 외형 대체 기록(참고용)을 현재 계획으로 다시 쓴다. 원래 선택 필드는 건드리지 않는다.</summary>
    static void RecordSubstitutes(EditState s, AssemblyPlan p)
    {
        if (s?.appearance == null || p == null) return;
        s.appearance.substitutes = new List<AppearanceSubstitute>();
        foreach (var x in p.Substitutes)
            s.appearance.substitutes.Add(new AppearanceSubstitute { part = x.part, selected = x.selected, shown = x.shown, reason = x.reason });
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
            // 공개 시각·호환 기본값·이전 읽기 실패 때문에 표시 모델이 달라졌다면 색만 덮어쓰지 않는다.
            bool sameModels = plan.Parts.Count == nextPlan.Parts.Count;
            for (int i = 0; sameModels && i < plan.Parts.Count; i++)
                sameModels = plan.Parts[i].Key == nextPlan.Parts[i].Key && plan.Parts[i].Model == nextPlan.Parts[i].Model;
            if (!sameModels) { Edit(change); return; }
            CharacterAssembler.CarryBuildResults(plan, nextPlan, view);
            view.ApplyPlanColors(nextPlan);
            view.Plan = nextPlan;
            state = next;
            plan = nextPlan;
            RecordSubstitutes(state, plan);
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
        // 캐릭터·배경을 모두 준비하고 실제 실패를 확인한 뒤 함께 확정한다.
        string bgError = null;
        bool bgChanged = current.background != next.background;
        Prepared prepared = null;
        StudioBackground.Prepared preparedBg = null;
        try
        {
            // 배경만 바뀌어도 현재 공개 판정은 다시 확인한다. 과거 이력이 제한 모델을 되살리지 않는다.
            prepared = Prepare(next, true);
            var failure = NewBuildFailure(plan, prepared.Plan);
            if (failure != null) throw new PlanException(failure.Describe());
            if (bgChanged)
            {
                preparedBg = background.Prepare(StudioBackground.Resolve(data, next.background), true);
                bgError = preparedBg.Error;
                if (preparedBg.Failed) throw new PlanException(bgError);
                background.Commit(preparedBg);
            }
            Commit(prepared);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Discard(prepared);
            panel.SetError(label + "를 적용하지 못해 현재 상태를 유지합니다. " + e.Message);
            panel.Refresh(state, plan);
            return;
        }
        finally { preparedBg?.Dispose(); }
        if (back) history.CommitUndo(current); else history.CommitRedo(current);
        UpdateHistoryButtons();
        MarkDirty();
        panel.SetError(bgError);
        panel.Refresh(state, plan);
    }

    void UpdateHistoryButtons() => panel?.SetHistory(history.CanUndo, history.CanRedo);

    /// <summary>
    /// 작업 전체 교체(가져오기·초기화). 화면 밖에서 조립·검사·승인을 마친 준비본으로만 장면을 바꾸고 이력을 비운다.
    /// 준비·검사에 실패하거나 취소하면 호출자가 준비본을 버리며 기존 장면·작업·자동 저장·이력은 그대로다.
    /// 바꾸기 전 대기 중인 자동 저장을 먼저 써서 직전 작업이 백업(.bak)에 남게 한다.
    /// </summary>
    bool ReplaceWork(Prepared next, bool defaultCamera)
    {
        SearchDropdown.CloseAny();
        ColorPicker.CancelAny();
        state.camera = cameraCtl.Save();
        if (autosave.Pending && !autosave.Flush())
        {
            Discard(next);
            ui.ShowChoice("작업 교체 실패", "현재 작업을 저장하지 못해 기존 작업과 이력을 유지합니다.", autosave.LastError,
                new List<(string, Action)> { ("돌아가기", OpenMenu) }, OpenMenu);
            return false;
        }
        string bgError = next.Background?.Error;
        if (next.Background != null) background.Commit(next.Background);
        Commit(next);
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
        PrepareImport(lr.Value, file);
    }

    // 파일 대화상자와 별개인 가져오기 처리. 자동 시험도 동일한 검사·승인 경로를 호출한다.
    void PrepareImport(EditState imported, string file)
    {
        // 새 캐릭터를 화면 밖에서 먼저 조립해 계획·조립 결과를 검사한다. 승인 전에는 현재 작업·자동 저장·이력을 그대로 둔다.
        ui.CloseModal();
        ui.ShowLoading("캐릭터를 조립하는 중...");
        Prepared p;
        try { p = Stage(imported); }
        catch (Exception e)
        {
            Debug.LogException(e);
            ui.HideLoading();
            ui.ShowChoice("가져오기 실패", "선택한 코디를 조립하지 못해 현재 작업을 유지합니다.", e.Message,
                new List<(string, Action)> { ("돌아가기", OpenMenu) }, OpenMenu);
            return;
        }
        ui.HideLoading();
        var issues = InspectPrepared(p);
        if (issues.Count == 0) { FinishImport(p, file, false); return; }
        // 부분 복원 / 취소. 선택한 JSON 파일은 고치지 않는다. 취소·닫기는 준비한 캐릭터를 버린다.
        void Cancel() { Discard(p); OpenMenu(); }
        ui.ShowChoice("일부 항목을 복원할 수 없습니다",
            "선택한 파일의 일부 항목을 지금 설치된 게임 데이터로 표시할 수 없습니다. 부분 복원하면 그 항목의 선택 ID·색은 보존하고 표시만 하지 않습니다(필수 외형은 공개 기본값으로 대신 표시).",
            RestoreCheck.Describe(issues),
            new List<(string, Action)> { ("취소", Cancel), ("부분 복원", () => FinishImport(p, file, true)) }, Cancel);
    }

    void FinishImport(Prepared p, string file, bool partial)
    {
        ui.CloseModal();
        if (!ReplaceWork(p, false)) return;
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
        Prepared p = null;
        string error = null;
        try
        {
            p = Stage(next);
            // 기본 코디에 없는 ID·제한·리소스 누락이 있으면 불완전한 기본 상태로 확정하지 않는다(Reset.md).
            var problems = RestoreCheck.CharacterProblems(p.Plan);
            if (problems.Count > 0) error = RestoreCheck.Describe(problems);
        }
        catch (Exception e) { Debug.LogException(e); error = e.Message; }
        ui.HideLoading();
        if (error != null)
        {
            Discard(p);
            ui.ShowChoice("초기화 실패", "기본 코디를 조립하지 못해 현재 작업을 유지합니다.", error,
                new List<(string, Action)> { ("돌아가기", OpenMenu) }, OpenMenu);
            return;
        }
        // 초기화는 현재 표시 중인 배경도 유지한다. 새 배경 준비본은 사용하지 않는다.
        p.Background?.Dispose();
        p.Background = null;
        if (!ReplaceWork(p, true)) return;
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
    /// 시험용 화면 상태: -maShotUI open,menu,options,hidden,nohelp,edit,bg,lens3,undo,short,tall,weapon,draw,male,partial,motion:ID (쉼표로 여러 개). 촬영 -maShotUIWait 초(기본 1) 전에 적용한다.
    System.Collections.IEnumerator ShotAndQuit(string file)
    {
        float delay = float.TryParse(CommandLine.Value("-maShotDelay"), out var d) ? d : 8f;
        // -maShotUIWait 초: UI 상태 적용 후 촬영까지 기다리는 시간(기본 1초). 연출 중간 프레임 확인용.
        float uiWait = float.TryParse(CommandLine.Value("-maShotUIWait"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var uw) ? uw : 1f;
        yield return new WaitForSecondsRealtime(Mathf.Max(0, delay - uiWait));
        string shotUI = CommandLine.Value("-maShotUI");
        // 별도 시험 데이터 폴더를 지정한 촬영 실행에서만 파일 대화상자를 생략한다. 실제 가져오기 검사·승인 경로는 같다.
        string shotImport = CommandLine.Value("-maDataDir") != null ? CommandLine.Value("-maShotImport") : null;
        if (shotImport != null && state != null)
        {
            var imported = JsonStore.ReadEditState(shotImport);
            if (imported.Status == LoadStatus.Ok)
            {
                PrepareImport(imported.Value, shotImport);
                if (pending != null && ShotOption("partial")) FinishImport(pending, shotImport, true);
            }
        }
        if (shotUI != null && state != null && panel != null)
        {
            var opts = new HashSet<string>(shotUI.Split(','));
            if (opts.Contains("nohelp")) ui.Help?.CloseDetail();
            // edit: 상의를 벗긴다, bg: 배경을 바꾼다, undo: 한 번 실행 취소(이력 동작 확인용)
            if (opts.Contains("edit")) Edit(s => s.costumes.RemoveAll(c => c.part == "Top"));
            if (opts.Contains("bg")) SetBackground("BG_Gradation2");
            // short·tall: 키 조절값을 -1·+1로 바꾼다(키 슬라이더 확인용)
            if (opts.Contains("short")) Edit(s => s.appearance.heightModifier = -1f);
            if (opts.Contains("tall")) Edit(s => s.appearance.heightModifier = 1f);
            // weapon: 검술사 무기 + 꺼냄(경계) 자세(무기 자세 확인용)
            if (opts.Contains("weapon"))
                Edit(s =>
                {
                    s.costumes.RemoveAll(c => c.part == "Weapon");
                    s.costumes.Add(new CostumeSlotState { part = "Weapon", costumeId = "Fashion_SwordMaster_Weapon_06" });
                    s.weaponPose.stance = WeaponPoseState.Alert;
                });
            // draw: 넣은 무기를 꺼냄(경계)으로 바꾼다(꺼내기 도입 모션 확인용, -maShotUIWait 를 짧게 준다)
            if (opts.Contains("draw"))
            {
                Edit(s =>
                {
                    s.costumes.RemoveAll(c => c.part == "Weapon");
                    s.costumes.Add(new CostumeSlotState { part = "Weapon", costumeId = "Fashion_HighBard_Weapon_06" });
                    s.weaponPose.stance = null;
                });
                Edit(s => s.weaponPose.stance = WeaponPoseState.Alert);
            }
            // male: 성별을 남성으로 바꾼다(필수 외형 호환 대체·실행 취소 확인용)
            if (opts.Contains("male")) Edit(s => s.appearance.gender = "Male");
            if (opts.Contains("lens3")) cameraCtl.SetLensMultiplier(3f);
            // motion:ID : 소셜 모션을 재생한다(모션 버튼 확인용, -maShotUIWait 로 재생 시점을 정한다)
            foreach (var o in opts) if (o.StartsWith("motion:", StringComparison.Ordinal)) PlayMotion(o.Substring("motion:".Length));
            if (opts.Contains("undo")) Undo();
            if (opts.Contains("open")) panel.SetOpen(true);
            if (opts.Contains("weapon") || opts.Contains("draw")) panel.ScrollToEnd();
            if (opts.Contains("hidden")) ui.ToggleEditingUI();
            if (opts.Contains("menu")) OpenMenu();
            if (opts.Contains("options")) OpenOptions();
        }
        yield return new WaitForSecondsRealtime(Mathf.Min(uiWait, delay));
        yield return new WaitForEndOfFrame();
        string report = CommandLine.Value("-maDataDir") != null ? CommandLine.Value("-maShotReport") : null;
        if (report != null)
        {
            var lines = new List<string>
            {
                "allowRestricted=" + Disclosure.AllowRestricted,
                "development=" + Debug.isDebugBuild,
                "pending=" + (pending != null),
                "state=" + (state == null ? "null" : JsonUtility.ToJson(state)),
            };
            if (plan != null)
            {
                foreach (var p in plan.Parts) lines.Add("part=" + p.Key + ":" + p.Model);
                foreach (var i in plan.Issues) lines.Add("issue=" + i.Describe());
            }
            if (view) foreach (var r in view.Renderers) lines.Add("renderer=" + r.partKey + ":" + r.renderer.name);
            File.WriteAllLines(report, lines);
        }
        ScreenCapture.CaptureScreenshot(file);
        yield return null; yield return null;
        quitApproved = true;
        Application.Quit();
    }

    /// <summary>화면 구석 메모. 의상 미적용·가림은 꾸미기 패널에 표시하므로 여기에는 조립 중 알게 된 문제만 쓴다.</summary>
    static IEnumerable<string> Notes(AssemblyPlan plan) => plan.Notes;
}
