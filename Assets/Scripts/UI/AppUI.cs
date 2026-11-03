// 앱 공통 오버레이 UI (UI Toolkit, 코드로 구성). 배치 기준은 WindowAndCustomizationUI.md 2장.
//   A 왼쪽 위 눈 아이콘(전체 UI 숨김, F1), B 오른쪽 위 시스템 메뉴 아이콘(ESC),
//   C 카메라 영역 왼쪽 아래 빠른 조작 안내(ControlsHelpView), D 카메라 영역 하단 중앙 화각 0.5x·1x·3x.
//   꾸미기 패널(CustomizePanel, E~J)은 WindowLayer 에 붙는다. 시스템 메뉴·옵션·확인·경로 지정·복구 안내는 모달 층에 뜬다.
//   층 순서(아래→위): 카메라 영역 도구 → 꾸미기 패널·드롭다운 → 눈·메뉴 아이콘 → 모달·로딩·알림 → 툴팁.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class AppUI
{
    public readonly UIDocument Document;
    readonly VisualElement root, viewport, windows, topButtons, layer, notes;
    VisualElement modal;
    Action modalEscape;
    Label loading, notice;
    IVisualElementScheduledItem noticeHide;
    VisualElement lensControls, motionControls;
    Button eyeButton, menuButton;
    ControlsHelpView help;
    readonly List<(float multiplier, Button button)> lensButtons = new List<(float, Button)>();
    Func<bool> canUseViewControls;
    bool uiHidden;
    float toolInset;

    internal static readonly Color Dim = new Color(0, 0, 0, 0.55f);
    internal static readonly Color PanelBg = new Color(0.16f, 0.17f, 0.2f, 0.98f);
    internal static readonly Color HeaderBg = new Color(0.12f, 0.13f, 0.15f, 1f);
    internal static readonly Color FieldBg = new Color(0.22f, 0.23f, 0.27f, 1f);
    internal static readonly Color FieldHover = new Color(0.27f, 0.28f, 0.33f, 1f);
    internal static readonly Color Border = new Color(0.33f, 0.35f, 0.4f, 1f);
    internal static readonly Color Text = new Color(0.93f, 0.93f, 0.95f);
    internal static readonly Color Sub = new Color(0.7f, 0.72f, 0.76f);
    internal static readonly Color ErrorColor = new Color(1f, 0.45f, 0.42f);
    internal static readonly Color WarnColor = new Color(0.95f, 0.78f, 0.4f);
    internal static readonly Color Accent = new Color(0.25f, 0.42f, 0.7f);

    /// <summary>F1·눈 아이콘으로 전체 UI 를 숨기거나 되돌렸다.</summary>
    public event Action<bool> HiddenChanged;

    public AppUI(GameObject host, PanelSettings settings)
    {
        Document = host.AddComponent<UIDocument>();
        Document.panelSettings = settings;
        root = Document.rootVisualElement;
        root.style.flexGrow = 1;
        root.pickingMode = PickingMode.Ignore;
        ApplySystemFont(root);

        viewport = new VisualElement { name = "camera-area", pickingMode = PickingMode.Ignore };
        Fill(viewport);
        root.Add(viewport);

        notes = new VisualElement { name = "notes", pickingMode = PickingMode.Ignore };
        notes.style.position = Position.Absolute;
        notes.style.left = 12; notes.style.top = CustomizePanel.TopBar;
        notes.style.maxWidth = new Length(60, LengthUnit.Percent);
        viewport.Add(notes);

        windows = new VisualElement { name = "window-layer", pickingMode = PickingMode.Ignore };
        Fill(windows);
        root.Add(windows);

        topButtons = new VisualElement { name = "top-buttons", pickingMode = PickingMode.Ignore };
        Fill(topButtons);
        root.Add(topButtons);

        layer = new VisualElement { name = "app-layer", pickingMode = PickingMode.Ignore };
        Fill(layer);
        root.Add(layer);

        Tips.Init(root);
    }

    /// <summary>꾸미기 패널을 붙이는 화면 전체 층(모달·로딩보다 아래).</summary>
    public VisualElement WindowLayer => windows;
    public bool UIHidden => uiHidden;

    // ------------------------------------------------------------ 보기 도구 (A·B·C·D)

    /// <summary>꾸미기 진입 뒤 한 번 만든다.</summary>
    public void AddViewControls(Action<float> setLens, Func<bool> canUse, Action openMenu, AppSettings settings, Action saveSettings)
    {
        if (eyeButton != null) return;
        canUseViewControls = canUse;

        // A: 전체 UI 숨김. 숨김 중에도 이 버튼만 남는다.
        eyeButton = IconButtons.Make(IconKind.Eye, ToggleEditingUI, () => uiHidden ? "UI 표시 (F1)" : "UI 숨기기 (F1)");
        eyeButton.style.position = Position.Absolute;
        eyeButton.style.left = 12; eyeButton.style.top = 12;
        topButtons.Add(eyeButton);

        // B: 시스템 메뉴. 패널 열림과 무관하게 같은 자리에 둔다.
        menuButton = IconButtons.Make(IconKind.Menu, () => { if (canUseViewControls?.Invoke() != false) openMenu(); }, () => "시스템 메뉴 (ESC)");
        menuButton.style.position = Position.Absolute;
        menuButton.style.right = 12; menuButton.style.top = 12;
        topButtons.Add(menuButton);

        // D: 화각. 휠 거리 조절과 별개다(Camera.md).
        lensControls = new VisualElement { name = "lens-controls" };
        lensControls.style.position = Position.Absolute;
        lensControls.style.flexDirection = FlexDirection.Row;
        lensControls.style.backgroundColor = IconButtons.Bg;
        lensControls.style.paddingLeft = lensControls.style.paddingRight = lensControls.style.paddingTop = lensControls.style.paddingBottom = 3;
        Round(lensControls, 9);
        foreach (float multiplier in new[] { 0.5f, 1f, 3f })
        {
            float selected = multiplier;
            string text = multiplier == 0.5f ? "0.5x" : multiplier == 3f ? "3x" : "1x";
            string desc = multiplier == 0.5f ? "광각" : multiplier == 3f ? "망원" : "기본";
            var button = new Button(() => { if (canUseViewControls?.Invoke() != false) setLens(selected); }) { text = text };
            button.style.width = 46; button.style.height = 28;
            button.style.marginLeft = button.style.marginRight = 1;
            button.style.marginTop = button.style.marginBottom = 0;
            button.style.fontSize = 13;
            button.style.color = Text;
            button.style.borderTopWidth = button.style.borderBottomWidth = button.style.borderLeftWidth = button.style.borderRightWidth = 0;
            Round(button, 7);
            UIAnim.SmoothColor(button, UIAnim.Normal);
            Tips.Attach(button, () => $"{text} {desc} 화각. 휠로 조절하는 카메라 거리는 유지합니다.");
            lensButtons.Add((multiplier, button));
            lensControls.Add(button);
        }
        viewport.Add(lensControls);
        SetLensSelection(1f);

        // C: 빠른 조작 안내와 자세한 안내
        help = new ControlsHelpView(viewport, settings, saveSettings);
        viewport.RegisterCallback<GeometryChangedEvent>(e => PlaceViewTools());
        help.Quick.RegisterCallback<GeometryChangedEvent>(e => PlaceViewTools());
        lensControls.RegisterCallback<GeometryChangedEvent>(e => PlaceViewTools());
    }

    public ControlsHelpView Help => help;

    /// <summary>T: 소셜 모션 버튼. 카메라 영역 오른쪽 아래(SocialMotion.md).</summary>
    public void AddMotionControl(VisualElement control)
    {
        if (motionControls != null) return;
        motionControls = control;
        control.style.position = Position.Absolute;
        viewport.Add(control);
        control.RegisterCallback<GeometryChangedEvent>(e => PlaceViewTools());
        PlaceViewTools();
    }

    /// <summary>화각 버튼은 카메라 영역 하단 중앙에서 조금 위. 좁은 영역에서 빠른 안내와 겹치면 그 위로 올린다.</summary>
    void PlaceViewTools()
    {
        if (lensControls == null) return;
        float w = viewport.layout.width, lw = lensControls.layout.width;
        if (float.IsNaN(w) || float.IsNaN(lw)) return;
        float baseBottom = 12 + toolInset, bottom = baseBottom + 10;
        var q = help?.Quick;
        if (q != null)
        {
            q.style.bottom = baseBottom;
            float qRight = 12 + q.layout.width + 8;
            if ((w - lw) * 0.5f < qRight) bottom = baseBottom + q.layout.height + 10;
        }
        float lensLeft = Mathf.Max(8, (w - lw) * 0.5f);
        lensControls.style.left = lensLeft;
        lensControls.style.bottom = bottom;
        help?.SetDetailBottom(baseBottom);
        // 모션 버튼: 오른쪽 아래. 좁아서 화각과 겹치면 화각 위 줄로 올린다.
        if (motionControls != null)
        {
            float mw = motionControls.layout.width, lh = lensControls.layout.height;
            float mBottom = baseBottom;
            if (!float.IsNaN(mw) && w - 12 - mw < lensLeft + lw + 8) mBottom = bottom + (float.IsNaN(lh) ? 34 : lh) + 8;
            motionControls.style.right = 12;
            motionControls.style.bottom = mBottom;
        }
    }

    /// <summary>카메라 출력 영역(UI 좌표). 보기 도구·안내가 이 영역을 따라간다.</summary>
    public void SetCameraArea(Rect r, float bottomInset)
    {
        toolInset = bottomInset;
        viewport.style.left = r.x; viewport.style.top = r.y;
        viewport.style.right = StyleKeyword.Auto; viewport.style.bottom = StyleKeyword.Auto;
        viewport.style.width = r.width; viewport.style.height = r.height;
    }

    /// <summary>UI 좌표의 사각형을 카메라 viewport rect(0~1, 왼쪽 아래 원점)로 바꾼다.</summary>
    public Rect ToViewportRect(Rect r)
    {
        float w = root.layout.width, h = root.layout.height;
        if (float.IsNaN(w) || float.IsNaN(h) || w <= 0 || h <= 0) return new Rect(0, 0, 1, 1);
        return new Rect(r.x / w, 1f - (r.y + r.height) / h, r.width / w, r.height / h);
    }

    public void SetLensSelection(float multiplier)
    {
        foreach (var (value, button) in lensButtons)
            button.style.backgroundColor = Mathf.Approximately(value, multiplier) ? Accent : new Color(0, 0, 0, 0);
    }

    /// <summary>
    /// 전체 UI 숨김/복귀(F1·눈 아이콘). 패널 열림·폴드아웃·컬러피커·자세한 안내는 숨겼다가 그대로 복원한다.
    /// 미확정 염색을 적용·취소하지 않는다. 장면·저장 상태는 바꾸지 않는다.
    /// </summary>
    public void ToggleEditingUI()
    {
        if (eyeButton == null || canUseViewControls?.Invoke() == false) return;
        SearchDropdown.CloseAny();
        Tips.Hide();
        uiHidden = !uiHidden;
        // 꾸미기 층·카메라 영역 도구·메뉴 아이콘을 함께 서서히 숨기거나 보인다.
        UIAnim.SetShown(windows, !uiHidden, UIAnim.Normal);
        UIAnim.SetShown(viewport, !uiHidden, UIAnim.Normal);
        UIAnim.SetShown(menuButton, !uiHidden, UIAnim.Normal);
        IconButtons.IconOf(eyeButton).Kind = uiHidden ? IconKind.EyeOff : IconKind.Eye;
        HiddenChanged?.Invoke(uiHidden);
    }

    static FontDefinition? pathFont;

    /// <summary>
    /// 경로 표시용 글꼴. 맑은 고딕은 역슬래시를 원화 기호(₩)로 그리므로 Segoe UI 를 먼저 쓰고,
    /// 한글 등 Segoe UI 에 없는 글자는 맑은 고딕으로 대체한다. 만들지 못하면 기본 글꼴을 그대로 둔다.
    /// </summary>
    internal static void ApplyPathFont(VisualElement e)
    {
        if (pathFont == null)
        {
            try
            {
                var latin = UnityEngine.TextCore.Text.FontAsset.CreateFontAsset("Segoe UI", "Regular");
                var hangul = UnityEngine.TextCore.Text.FontAsset.CreateFontAsset("Malgun Gothic", "Regular");
                if (latin != null)
                {
                    if (hangul != null)
                    {
                        if (latin.fallbackFontAssetTable == null) latin.fallbackFontAssetTable = new List<UnityEngine.TextCore.Text.FontAsset>();
                        latin.fallbackFontAssetTable.Add(hangul);
                    }
                    pathFont = FontDefinition.FromSDFFont(latin);
                }
            }
            catch (Exception ex) { Debug.LogWarning("[MobiAtelier] 경로 글꼴을 만들지 못했습니다: " + ex.Message); }
        }
        if (pathFont != null) e.style.unityFontDefinition = pathFont.Value;
    }

    static void ApplySystemFont(VisualElement e)
    {
        // 기본 런타임 글꼴에는 한글이 없다. 게임·외부 글꼴을 포함하지 않고 Windows 의 맑은 고딕을 쓴다.
        try
        {
            var fa = UnityEngine.TextCore.Text.FontAsset.CreateFontAsset("Malgun Gothic", "Regular");
            if (fa != null) e.style.unityFontDefinition = FontDefinition.FromSDFFont(fa);
        }
        catch (Exception ex) { Debug.LogWarning("[MobiAtelier] 시스템 글꼴을 만들지 못했습니다: " + ex.Message); }
    }

    static void Fill(VisualElement e)
    {
        e.style.position = Position.Absolute;
        e.style.left = 0; e.style.top = 0; e.style.right = 0; e.style.bottom = 0;
    }

    internal static void Round(VisualElement e, float r)
    {
        e.style.borderTopLeftRadius = e.style.borderTopRightRadius = e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = r;
    }

    /// <summary>화면 좌표(왼쪽 아래 원점)가 UI 요소 위인지.</summary>
    public bool IsPointerOver(Vector2 screenPos)
    {
        var panel = root.panel;
        if (panel == null) return false;
        var p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPos.x, Screen.height - screenPos.y));
        var picked = panel.Pick(p);
        return picked != null && picked != root && picked != layer && picked != windows && picked != viewport && picked != topButtons;
    }

    /// <summary>텍스트 입력란에 포커스가 있는지. 있으면 Ctrl+Z 등 전역 단축키를 실행하지 않는다.</summary>
    public bool TextInputFocused
    {
        get
        {
            var f = root.panel?.focusController?.focusedElement as VisualElement;
            return f != null && (f is TextField || f.GetFirstAncestorOfType<TextField>() != null);
        }
    }

    // ------------------------------------------------------------ 모달

    public bool ModalOpen => modal != null;

    /// <summary>모달을 닫는다. 딤과 창이 짧게 사라진다(그동안의 클릭은 막는다).</summary>
    public void CloseModal()
    {
        if (modal != null) UIAnim.FadeOutAndRemove(modal, UIAnim.Fast);
        modal = null;
        modalEscape = null;
    }

    /// <summary>ESC 를 모달이 처리한다. 모달이 열려 있으면 true(처리할 동작이 없는 필수 화면도 ESC 를 소비한다).</summary>
    public bool HandleModalEscape()
    {
        if (modal == null) return false;
        modalEscape?.Invoke();
        return true;
    }

    /// <param name="onEscape">ESC 동작. null 이면 ESC 로 닫지 않는다(필수 화면).</param>
    /// <param name="onOutside">딤 영역 클릭 동작. null 이면 아무 일도 하지 않는다.</param>
    /// <param name="topRight">true 면 화면 가운데 대신 오른쪽 위(메뉴 아이콘 아래)에 붙인다.</param>
    VisualElement OpenModal(string title, float width = 560, Action onEscape = null, Action onOutside = null, bool topRight = false, Action onClose = null)
    {
        // 다른 모달에서 이어 여는 경우(메뉴 → 옵션 등) 딤은 그대로 두는 것처럼 보이게 이전 것을 바로 떼고 창만 새로 나타낸다.
        bool replacing = modal != null;
        modal?.RemoveFromHierarchy();
        modal = null;
        modalEscape = null;
        HideLoading();
        SearchDropdown.CloseAny();
        Tips.Hide();
        modal = new VisualElement { name = "modal-dim" };
        Fill(modal);
        modal.style.backgroundColor = Dim;
        modal.style.alignItems = topRight ? Align.FlexEnd : Align.Center;
        modal.style.justifyContent = topRight ? Justify.FlexStart : Justify.Center;
        if (topRight) { modal.style.paddingTop = 54; modal.style.paddingRight = 12; }
        modalEscape = onEscape;
        // 바깥 클릭은 놓을 때 처리한다. 누른 채로 딤이 사라져 뒤의 카메라 드래그로 이어지지 않게 한다.
        bool downOnDim = false;
        var thisModal = modal;
        modal.RegisterCallback<PointerDownEvent>(e => downOnDim = e.target == thisModal);
        modal.RegisterCallback<PointerUpEvent>(e =>
        {
            if (downOnDim && e.target == thisModal && modal == thisModal) onOutside?.Invoke();
            downOnDim = false;
        });
        modal.RegisterCallback<WheelEvent>(e => e.StopPropagation());

        var panel = new VisualElement { name = "modal-panel" };
        panel.style.backgroundColor = PanelBg;
        panel.style.width = new Length(Mathf.Min(width, 10000), LengthUnit.Pixel);
        panel.style.maxWidth = new Length(92, LengthUnit.Percent);
        panel.style.maxHeight = new Length(92, LengthUnit.Percent);
        panel.style.paddingLeft = panel.style.paddingRight = 20;
        panel.style.paddingTop = panel.style.paddingBottom = 16;
        Round(panel, 6);
        var head = new VisualElement();
        head.style.flexDirection = FlexDirection.Row;
        head.style.alignItems = Align.Center;
        head.style.marginBottom = 8;
        var t = new Label(title);
        t.style.fontSize = 18; t.style.color = Text; t.style.unityFontStyleAndWeight = FontStyle.Bold; t.style.flexGrow = 1;
        head.Add(t);
        if (onClose != null)
        {
            var x = IconButtons.Make(IconKind.Close, onClose, () => "닫기 (ESC)", 28, 16);
            x.style.backgroundColor = new Color(0, 0, 0, 0);
            head.Add(x);
        }
        panel.Add(head);
        modal.Add(panel);
        layer.Add(modal);
        if (!replacing) UIAnim.FadeIn(modal, UIAnim.Normal);
        UIAnim.FadeIn(panel, UIAnim.Normal, topRight ? new Vector2(0, -8) : new Vector2(0, 10), 0.98f);
        return panel;
    }

    static Label Para(string text, Color color, int size = 13)
    {
        var l = new Label(text);
        l.style.color = color; l.style.fontSize = size; l.style.whiteSpace = WhiteSpace.Normal; l.style.marginBottom = 6;
        return l;
    }

    static VisualElement Row(Justify justify = Justify.FlexEnd)
    {
        var r = new VisualElement();
        r.style.flexDirection = FlexDirection.Row;
        r.style.justifyContent = justify;
        r.style.flexWrap = Wrap.Wrap;
        r.style.marginTop = 8;
        return r;
    }

    static Button Btn(string text, Action onClick)
    {
        var b = new Button(onClick) { text = text };
        b.style.minWidth = 84; b.style.height = 28; b.style.marginLeft = 6;
        return b;
    }

    // ------------------------------------------------------------ 경로 지정 팝업 (시작) · 옵션

    public sealed class PathPopup
    {
        public TextField Input;
        public Label Result;
        public Button Next, Browse, Back;
        bool busy;

        public void SetBusy(bool value, string status = null)
        {
            busy = value;
            Next.SetEnabled(!value); Browse.SetEnabled(!value); Input.SetEnabled(!value);
            Back?.SetEnabled(!value);
            if (value) { Result.style.color = Sub; Result.text = status ?? "검사 중..."; }
        }

        public void SetError(string message, string detail)
        {
            Result.style.color = ErrorColor;
            Result.text = string.IsNullOrEmpty(detail) ? message : message + "\n" + detail;
        }

        public bool Busy => busy;
    }

    PathPopup PathEditor(VisualElement panel, string path, Action<PathPopup> onBrowse, Action<string> onSubmit)
    {
        var popup = new PathPopup();
        var row = Row(Justify.FlexStart);
        row.style.flexWrap = Wrap.NoWrap;
        popup.Input = new TextField { value = path ?? "" };
        ApplyPathFont(popup.Input);
        popup.Input.style.flexGrow = 1; popup.Input.style.flexShrink = 1; popup.Input.style.marginLeft = 0;
        popup.Browse = Btn("폴더 선택", () => onBrowse(popup));
        row.Add(popup.Input); row.Add(popup.Browse);
        panel.Add(row);
        popup.Result = Para("", ErrorColor);
        popup.Result.style.marginTop = 6;
        panel.Add(popup.Result);
        popup.Input.RegisterCallback<KeyDownEvent>(e =>
        {
            if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && !popup.Busy) onSubmit(popup.Input.value);
        }, TrickleDown.TrickleDown);
        popup.Input.schedule.Execute(() => popup.Input.Focus());
        return popup;
    }

    /// <summary>시작 경로 검사 실패 팝업(Startup.md). 필수 화면이라 ESC·바깥 클릭으로 닫지 않는다.</summary>
    public PathPopup ShowPathPopup(string path, string message, string detail, Action<string> onNext, Action<PathPopup> onBrowse, Action onQuit)
    {
        var panel = OpenModal("마비노기 모바일 설치 경로");
        panel.Add(Para("설치 폴더를 찾지 못했거나 필요한 파일을 읽을 수 없습니다. 설치된 마비노기 모바일 폴더를 선택해 주세요.", Sub));
        PathPopup popup = null;
        popup = PathEditor(panel, path, onBrowse, v => onNext(v));
        if (!string.IsNullOrEmpty(message)) popup.SetError(message, detail);
        var buttons = Row();
        buttons.Add(Btn("종료", onQuit));
        popup.Next = Btn("다음", () => { if (!popup.Busy) onNext(popup.Input.value); });
        buttons.Add(popup.Next);
        panel.Add(buttons);
        return popup;
    }

    /// <summary>
    /// 옵션 화면(Options.md). 지금은 게임 설치 경로만 있다. 입력만 한 경로는 적용하지 않는다.
    /// 돌아가기·ESC 는 시스템 메뉴로 돌아간다. 정규화한 경로가 현재와 같으면 적용 버튼을 끈다.
    /// </summary>
    public PathPopup ShowOptions(string current, Func<string, bool> sameAsCurrent, Action<PathPopup, string> onApply, Action<PathPopup> onBrowse, Action onBack)
    {
        var panel = OpenModal("옵션", 600, onBack, null, false, onBack);
        var section = new Label("게임 설치 경로");
        section.style.color = Text; section.style.fontSize = 14; section.style.unityFontStyleAndWeight = FontStyle.Bold;
        panel.Add(section);
        var currentLabel = Para("현재 경로: " + (current ?? "(없음)"), Sub, 12);
        ApplyPathFont(currentLabel);
        panel.Add(currentLabel);
        PathPopup popup = null;
        popup = PathEditor(panel, current, onBrowse, v => { if (popup.Next.enabledSelf) onApply(popup, v); });
        panel.Add(Para("경로 변경은 다시 시작한 뒤 적용됩니다. 현재 작업은 저장 후 복원되며, 실행 취소·다시 실행 이력은 초기화됩니다.", Sub, 12));
        var buttons = Row();
        popup.Back = Btn("돌아가기", onBack);
        buttons.Add(popup.Back);
        popup.Next = Btn("적용 후 다시 시작", () => { if (!popup.Busy) onApply(popup, popup.Input.value); });
        popup.Next.style.minWidth = 140;
        buttons.Add(popup.Next);
        panel.Add(buttons);
        void Sync() { if (!popup.Busy) popup.Next.SetEnabled(!sameAsCurrent(popup.Input.value)); }
        popup.Input.RegisterValueChangedCallback(e => Sync());
        Sync();
        return popup;
    }

    /// <summary>폴더 선택 대화상자 뒤에 적용 버튼 상태를 다시 맞춘다.</summary>
    public static void SyncOptionsApply(PathPopup popup, Func<string, bool> sameAsCurrent)
    {
        if (popup?.Next != null && !popup.Busy) popup.Next.SetEnabled(!sameAsCurrent(popup.Input.value));
    }

    // ------------------------------------------------------------ 시스템 메뉴 (SystemMenu.md)

    /// <summary>
    /// 시스템 메뉴. 항목: 내보내기(Export)·가져오기(Import)·초기화·옵션·종료. 초기화·종료는 간격을 두어 구분한다.
    /// ESC·X·바깥 클릭은 메뉴만 닫는다. 처음 선택(포커스)을 종료에 두지 않는다.
    /// </summary>
    public void ShowSystemMenu(IList<(string label, string desc, Action action, bool gapBefore)> items, Action onClose)
    {
        var panel = OpenModal("메뉴", 300, onClose, onClose, true, onClose);
        foreach (var (label, desc, action, gap) in items)
        {
            var b = new Button(action) { text = "" };
            b.style.flexDirection = FlexDirection.Column;
            b.style.alignItems = Align.FlexStart;
            b.style.marginLeft = b.style.marginRight = 0;
            b.style.marginTop = gap ? 14 : 4;
            b.style.paddingLeft = 12; b.style.paddingRight = 12; b.style.paddingTop = 7; b.style.paddingBottom = 7;
            b.style.backgroundColor = FieldBg;
            b.style.borderTopWidth = b.style.borderBottomWidth = b.style.borderLeftWidth = b.style.borderRightWidth = 0;
            Round(b, 6);
            var l = new Label(label);
            l.style.color = Text; l.style.fontSize = 14; l.pickingMode = PickingMode.Ignore;
            b.Add(l);
            if (!string.IsNullOrEmpty(desc))
            {
                var d = new Label(desc);
                d.style.color = Sub; d.style.fontSize = 11; d.style.whiteSpace = WhiteSpace.Normal; d.pickingMode = PickingMode.Ignore;
                b.Add(d);
            }
            UIAnim.SmoothColor(b);
            b.RegisterCallback<PointerEnterEvent>(e => b.style.backgroundColor = FieldHover);
            b.RegisterCallback<PointerLeaveEvent>(e => b.style.backgroundColor = FieldBg);
            panel.Add(b);
        }
    }

    // ------------------------------------------------------------ 안내·선택

    /// <param name="onEscape">ESC·X·바깥 클릭 동작. null 이면 버튼으로만 닫는다(필수 안내).</param>
    public void ShowChoice(string title, string message, string detail, IList<(string label, Action action)> actions, Action onEscape = null)
    {
        var panel = OpenModal(title, 560, onEscape, onEscape, false, onEscape);
        panel.Add(Para(message, Text));
        if (!string.IsNullOrEmpty(detail)) panel.Add(Para(detail, Sub, 12));
        var buttons = Row();
        foreach (var (label, action) in actions) buttons.Add(Btn(label, action));
        panel.Add(buttons);
    }

    /// <summary>
    /// 화면 위쪽 가운데의 짧은 알림. 오류는 사용자가 닫거나 다음 알림이 올 때까지 남긴다.
    /// UI 숨김 중에도 보인다(자동 저장 실패 등 필수 안내).
    /// </summary>
    public void ShowNotice(string text, bool isError, float seconds = 4f)
    {
        if (notice == null)
        {
            notice = new Label { name = "notice" };
            notice.style.position = Position.Absolute;
            notice.style.top = 14;
            notice.style.alignSelf = Align.Center;
            notice.style.left = new Length(20, LengthUnit.Percent);
            notice.style.right = new Length(20, LengthUnit.Percent);
            notice.style.unityTextAlign = TextAnchor.MiddleCenter;
            notice.style.whiteSpace = WhiteSpace.Normal;
            notice.style.fontSize = 13;
            notice.style.paddingLeft = notice.style.paddingRight = 12;
            notice.style.paddingTop = notice.style.paddingBottom = 6;
            Round(notice, 6);
            notice.RegisterCallback<PointerUpEvent>(e => HideNotice());
            notice.style.display = DisplayStyle.None;      // 처음 표시도 나타나는 연출을 쓰도록 숨긴 채 붙인다
            layer.Add(notice);
        }
        noticeHide?.Pause();
        notice.text = text;
        notice.style.color = isError ? new Color(1f, 0.85f, 0.83f) : Text;
        notice.style.backgroundColor = isError ? new Color(0.45f, 0.12f, 0.12f, 0.95f) : new Color(0.1f, 0.11f, 0.13f, 0.92f);
        notice.BringToFront();
        UIAnim.SetShown(notice, true, UIAnim.Normal, new Vector2(0, -8));
        noticeHide = isError ? null : notice.schedule.Execute(HideNotice).StartingIn((long)(seconds * 1000));
    }

    public void HideNotice()
    {
        noticeHide?.Pause();
        noticeHide = null;
        if (notice != null) UIAnim.SetShown(notice, false, UIAnim.Normal, new Vector2(0, -8));
    }

    // ------------------------------------------------------------ 로딩·메모

    public void ShowLoading(string text)
    {
        if (loading == null)
        {
            loading = new Label { name = "loading", pickingMode = PickingMode.Ignore };
            loading.style.position = Position.Absolute;
            loading.style.left = 0; loading.style.right = 0; loading.style.top = new Length(45, LengthUnit.Percent);
            loading.style.unityTextAlign = TextAnchor.MiddleCenter;
            loading.style.fontSize = 16; loading.style.color = Text;
            layer.Add(loading);
        }
        loading.text = text;
        UIAnim.SetShown(loading, true, UIAnim.Fast);
    }

    public void HideLoading()
    {
        if (loading != null) UIAnim.SetShown(loading, false, UIAnim.Fast);
    }

    public void SetNotes(IEnumerable<string> lines)
    {
        notes.Clear();
        if (lines == null) return;
        foreach (var s in lines)
        {
            var l = Para(s, Sub, 12);
            l.pickingMode = PickingMode.Ignore;
            l.style.marginBottom = 2;
            notes.Add(l);
        }
    }
}
