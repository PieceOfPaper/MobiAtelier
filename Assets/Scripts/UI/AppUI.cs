// 앱 공통 오버레이 UI (UI Toolkit, 코드로 구성).
//   경로 지정 팝업(Startup.md), 로딩 표시, 복구·오류 안내, 화면 구석의 상태 메모.
// 꾸미기 창은 별도 작업에서 추가한다.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class AppUI
{
    public readonly UIDocument Document;
    readonly VisualElement root, layer, notes;
    VisualElement modal;
    Label loading;

    static readonly Color Dim = new Color(0, 0, 0, 0.55f);
    static readonly Color PanelBg = new Color(0.16f, 0.17f, 0.2f, 0.98f);
    static readonly Color Text = new Color(0.93f, 0.93f, 0.95f);
    static readonly Color Sub = new Color(0.7f, 0.72f, 0.76f);
    static readonly Color ErrorColor = new Color(1f, 0.45f, 0.42f);

    public AppUI(GameObject host, PanelSettings settings)
    {
        Document = host.AddComponent<UIDocument>();
        Document.panelSettings = settings;
        root = Document.rootVisualElement;
        root.style.flexGrow = 1;
        root.pickingMode = PickingMode.Ignore;
        ApplySystemFont(root);

        layer = new VisualElement { name = "app-layer", pickingMode = PickingMode.Ignore };
        Fill(layer);
        root.Add(layer);

        notes = new VisualElement { name = "notes", pickingMode = PickingMode.Ignore };
        notes.style.position = Position.Absolute;
        notes.style.left = 12; notes.style.bottom = 10; notes.style.right = 12;
        root.Add(notes);
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
        return picked != null && picked != root && picked != layer;
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
