// 앱 공통 오버레이 UI (UI Toolkit, 코드로 구성).
//   경로 지정 팝업(Startup.md), 로딩 표시, 복구·오류 안내, 화면 구석의 상태 메모.
//   꾸미기 창(CustomizePanel)은 WindowLayer 에 붙는다. 모달은 그 위에 뜬다.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class AppUI
{
    public readonly UIDocument Document;
    readonly VisualElement root, windows, layer, notes;
    VisualElement modal;
    Label loading;
    VisualElement viewControls, lensControls;
    Button visibilityButton;
    readonly List<(float multiplier, Button button)> lensButtons = new List<(float, Button)>();
    Func<bool> canUseViewControls;
    bool editingUIHidden;

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

    public AppUI(GameObject host, PanelSettings settings)
    {
        Document = host.AddComponent<UIDocument>();
        Document.panelSettings = settings;
        root = Document.rootVisualElement;
        root.style.flexGrow = 1;
        root.pickingMode = PickingMode.Ignore;
        ApplySystemFont(root);

        windows = new VisualElement { name = "window-layer", pickingMode = PickingMode.Ignore };
        Fill(windows);
        root.Add(windows);

        layer = new VisualElement { name = "app-layer", pickingMode = PickingMode.Ignore };
        Fill(layer);
        root.Add(layer);

        notes = new VisualElement { name = "notes", pickingMode = PickingMode.Ignore };
        notes.style.position = Position.Absolute;
        notes.style.left = 12; notes.style.bottom = 10;
        notes.style.maxWidth = new Length(50, LengthUnit.Percent);
        root.Insert(0, notes);      // 메모는 꾸미기 창 아래에 둔다
    }

    /// <summary>꾸미기 창을 붙이는 화면 전체 층(모달·로딩보다 아래).</summary>
    public VisualElement WindowLayer => windows;

    /// <summary>꾸미기 진입 뒤 한 번 만든다. 숨김 중에도 복귀 버튼은 남긴다.</summary>
    public void AddViewControls(Action<float> setLens, Func<bool> canUse)
    {
        if (viewControls != null) return;
        canUseViewControls = canUse;
        viewControls = new VisualElement { name = "view-controls", pickingMode = PickingMode.Ignore };
        viewControls.style.position = Position.Absolute;
        viewControls.style.left = 12; viewControls.style.top = 12; viewControls.style.right = 12;
        viewControls.style.flexDirection = FlexDirection.Row;
        viewControls.style.flexWrap = Wrap.Wrap;
        viewControls.style.alignItems = Align.FlexStart;
        visibilityButton = Btn("UI 숨기기 (F1)", ToggleEditingUI);
        visibilityButton.style.marginLeft = 0;
        visibilityButton.tooltip = "꾸미기 UI를 숨기거나 다시 표시합니다. 카메라 조작은 유지됩니다.";
        viewControls.Add(visibilityButton);

        lensControls = new VisualElement { name = "lens-controls", pickingMode = PickingMode.Ignore };
        lensControls.style.flexDirection = FlexDirection.Row;
        lensControls.style.flexWrap = Wrap.Wrap;
        foreach (float multiplier in new[] { 0.5f, 1f, 3f })
        {
            float selected = multiplier;
            string text = multiplier == 0.5f ? "0.5x 광각" : multiplier == 3f ? "3x 망원" : "1x 기본";
            var button = Btn(text, () => { if (canUseViewControls?.Invoke() != false) setLens(selected); });
            button.tooltip = "렌즈 화각 변경. 휠로 조절하는 카메라 거리는 유지합니다.";
            lensButtons.Add((multiplier, button));
            lensControls.Add(button);
        }
        viewControls.Add(lensControls);
        // 모달 층보다 아래에 둬 팝업의 입력 차단을 유지한다.
        root.Insert(root.IndexOf(layer), viewControls);
        SetLensSelection(1f);
    }

    public void SetLensSelection(float multiplier)
    {
        foreach (var (value, button) in lensButtons)
            button.style.backgroundColor = Mathf.Approximately(value, multiplier) ? new Color(0.25f, 0.42f, 0.7f) : FieldBg;
    }

    public void ToggleEditingUI()
    {
        if (viewControls == null || canUseViewControls?.Invoke() == false) return;
        SearchDropdown.CloseAny();
        editingUIHidden = !editingUIHidden;
        windows.style.display = editingUIHidden ? DisplayStyle.None : DisplayStyle.Flex;
        notes.style.display = editingUIHidden ? DisplayStyle.None : DisplayStyle.Flex;
        lensControls.style.display = editingUIHidden ? DisplayStyle.None : DisplayStyle.Flex;
        visibilityButton.text = editingUIHidden ? "UI 표시 (F1)" : "UI 숨기기 (F1)";
        // 열린 컬러피커와 미확정 색 미리보기는 창과 함께 숨겼다가 그대로 복원한다.
        // 장면·배치·색·저장 상태는 바꾸지 않는다.
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

    /// <summary>화면 좌표(왼쪽 아래 원점)가 UI 요소 위인지.</summary>
    public bool IsPointerOver(Vector2 screenPos)
    {
        var panel = root.panel;
        if (panel == null) return false;
        var p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPos.x, Screen.height - screenPos.y));
        var picked = panel.Pick(p);
        return picked != null && picked != root && picked != layer && picked != windows;
    }

    public bool ModalOpen => modal != null;

    public void CloseModal()
    {
        modal?.RemoveFromHierarchy();
        modal = null;
    }

    VisualElement OpenModal(string title, float width = 560)
    {
        CloseModal();
        HideLoading();
        modal = new VisualElement { name = "modal-dim" };
        Fill(modal);
        modal.style.backgroundColor = Dim;
        modal.style.alignItems = Align.Center;
        modal.style.justifyContent = Justify.Center;
        var panel = new VisualElement { name = "modal-panel" };
        panel.style.backgroundColor = PanelBg;
        panel.style.width = new Length(Mathf.Min(width, 10000), LengthUnit.Pixel);
        panel.style.maxWidth = new Length(92, LengthUnit.Percent);
        panel.style.paddingLeft = panel.style.paddingRight = 20;
        panel.style.paddingTop = panel.style.paddingBottom = 16;
        panel.style.borderTopLeftRadius = panel.style.borderTopRightRadius = panel.style.borderBottomLeftRadius = panel.style.borderBottomRightRadius = 6;
        var t = new Label(title);
        t.style.fontSize = 18; t.style.color = Text; t.style.marginBottom = 8; t.style.unityFontStyleAndWeight = FontStyle.Bold;
        panel.Add(t);
        modal.Add(panel);
        layer.Add(modal);
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
        r.style.marginTop = 8;
        return r;
    }

    static Button Btn(string text, Action onClick)
    {
        var b = new Button(onClick) { text = text };
        b.style.minWidth = 84; b.style.height = 28; b.style.marginLeft = 6;
        return b;
    }

    // ------------------------------------------------------------ 경로 지정 팝업

    public sealed class PathPopup
    {
        public TextField Input;
        public Label Result;
        public Button Next, Browse;
        bool busy;

        public void SetBusy(bool value, string status = null)
        {
            busy = value;
            Next.SetEnabled(!value); Browse.SetEnabled(!value); Input.SetEnabled(!value);
            if (value) { Result.style.color = Sub; Result.text = status ?? "검사 중..."; }
        }

        public void SetError(string message, string detail)
        {
            Result.style.color = ErrorColor;
            Result.text = string.IsNullOrEmpty(detail) ? message : message + "\n" + detail;
        }

        public bool Busy => busy;
    }

    public PathPopup ShowPathPopup(string path, string message, string detail, Action<string> onNext, Action<PathPopup> onBrowse, Action onQuit)
    {
        var panel = OpenModal("마비노기 모바일 설치 경로");
        panel.Add(Para("설치 폴더를 찾지 못했거나 필요한 파일을 읽을 수 없습니다. 설치된 마비노기 모바일 폴더를 선택해 주세요.", Sub));
        var popup = new PathPopup();
        var row = Row(Justify.FlexStart);
        popup.Input = new TextField { value = path ?? "" };
        popup.Input.style.flexGrow = 1; popup.Input.style.marginLeft = 0;
        popup.Browse = Btn("폴더 선택", () => onBrowse(popup));
        row.Add(popup.Input); row.Add(popup.Browse);
        panel.Add(row);
        popup.Result = Para("", ErrorColor);
        popup.Result.style.marginTop = 6;
        panel.Add(popup.Result);
        if (!string.IsNullOrEmpty(message)) popup.SetError(message, detail);
        var buttons = Row();
        buttons.Add(Btn("종료", onQuit));
        popup.Next = Btn("다음", () => { if (!popup.Busy) onNext(popup.Input.value); });
        buttons.Add(popup.Next);
        panel.Add(buttons);
        popup.Input.RegisterCallback<KeyDownEvent>(e =>
        {
            if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && !popup.Busy) onNext(popup.Input.value);
        }, TrickleDown.TrickleDown);
        popup.Input.schedule.Execute(() => popup.Input.Focus());
        return popup;
    }

    // ------------------------------------------------------------ 안내·선택

    public void ShowChoice(string title, string message, string detail, IList<(string label, Action action)> actions)
    {
        var panel = OpenModal(title);
        panel.Add(Para(message, Text));
        if (!string.IsNullOrEmpty(detail)) panel.Add(Para(detail, Sub, 12));
        var buttons = Row();
        foreach (var (label, action) in actions) buttons.Add(Btn(label, action));
        panel.Add(buttons);
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
        loading.style.display = DisplayStyle.Flex;
    }

    public void HideLoading()
    {
        if (loading != null) loading.style.display = DisplayStyle.None;
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
