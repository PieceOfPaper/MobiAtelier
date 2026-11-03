// 빠른 조작 안내와 자세한 조작 안내 (ControlsHelp.md)
//   빠른 안내(C): 카메라 영역 왼쪽 아래. 마우스 그림(해당 버튼 강조)과 짧은 글자, 정보 아이콘.
//     그림·글자는 입력을 막지 않는다. 정보 아이콘만 입력을 받는다.
//   자세한 안내: 비모달. 밖의 카메라 영역은 계속 조작할 수 있고, 안의 클릭·휠은 카메라로 넘기지 않는다.
//     X·정보 아이콘 재선택·ESC 로 접는다. 바깥 카메라 조작만으로는 닫지 않는다.
//   첫 사용: '확인했어요'를 누른 적이 없으면 첫 정상 진입에 펼친다. 확인 여부만 앱 UI 설정에 보관한다.
using System;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class ControlsHelpView
{
    public readonly VisualElement Quick;
    readonly VisualElement detail;
    readonly AppSettings settings;
    readonly Action saveSettings;

    public bool DetailOpen => UIAnim.IsShown(detail);

    static readonly (IconKind[] icons, string what, string desc)[] Rows =
    {
        (new[] { IconKind.MouseLeft, IconKind.MouseRight }, "왼쪽 또는 오른쪽 버튼 드래그", "캐릭터 주위를 돌아봅니다"),
        (new[] { IconKind.MouseBoth }, "왼쪽+오른쪽 버튼 동시 드래그", "화면을 좌우·위아래로 옮깁니다"),
        (new[] { IconKind.MouseWheel }, "마우스 휠", "카메라 거리를 조절합니다"),
        (null, "0.5x · 1x · 3x", "광각·기본·망원 화각을 선택합니다. 휠 거리 조절과 별개입니다"),
        (new[] { IconKind.Eye }, "눈 아이콘 · F1", "전체 UI를 숨기거나 다시 표시합니다"),
        (new[] { IconKind.ChevronLeft }, "가장자리 탭", "꾸미기 패널만 열거나 접습니다"),
        (new[] { IconKind.Undo, IconKind.Redo }, "패널 위쪽 되돌리기·다시 실행", "꾸미기 편집만 되돌립니다. Ctrl+Z·Ctrl+Y도 사용할 수 있습니다"),
        (new[] { IconKind.Menu }, "메뉴 아이콘 · ESC", "시스템 메뉴를 열거나 닫습니다"),
    };

    public ControlsHelpView(VisualElement viewport, AppSettings settings, Action saveSettings)
    {
        this.settings = settings;
        this.saveSettings = saveSettings;

        Quick = new VisualElement { name = "controls-quick", pickingMode = PickingMode.Ignore };
        Quick.style.position = Position.Absolute;
        Quick.style.left = 12; Quick.style.bottom = 12;
        Quick.style.flexDirection = FlexDirection.Row;
        Quick.style.alignItems = Align.Center;
        Quick.style.backgroundColor = IconButtons.Bg;
        Quick.style.paddingLeft = 8; Quick.style.paddingRight = 4; Quick.style.paddingTop = 3; Quick.style.paddingBottom = 3;
        AppUI.Round(Quick, 9);
        AddHint(new[] { IconKind.MouseLeft, IconKind.MouseRight }, "회전");
        AddHint(new[] { IconKind.MouseBoth }, "화면 이동");
        AddHint(new[] { IconKind.MouseWheel }, "거리");
        var info = IconButtons.Make(IconKind.Info, ToggleDetail, () => "조작 안내", 28, 18);
        info.style.backgroundColor = new Color(0, 0, 0, 0);
        info.style.marginLeft = 2;
        Quick.Add(info);
        viewport.Add(Quick);

        detail = new VisualElement { name = "controls-detail" };
        detail.style.position = Position.Absolute;
        detail.style.left = 12;
        detail.style.bottom = 12 + 40;
        detail.style.width = 400;
        detail.style.maxWidth = new Length(100, LengthUnit.Percent);
        detail.style.maxHeight = new Length(80, LengthUnit.Percent);
        detail.style.backgroundColor = AppUI.PanelBg;
        detail.style.paddingLeft = 14; detail.style.paddingRight = 8; detail.style.paddingTop = 8; detail.style.paddingBottom = 10;
        AppUI.Round(detail, 8);
        SearchDropdown.SetBorder(detail, AppUI.Border, 1, 8);
        detail.style.display = DisplayStyle.None;
        // 안내 위 휠은 안내 스크롤에만 쓴다(카메라는 IsPointerOver 로 막힌다).

        var head = new VisualElement();
        head.style.flexDirection = FlexDirection.Row;
        head.style.alignItems = Align.Center;
        var title = new Label("조작 안내");
        title.style.color = AppUI.Text; title.style.fontSize = 15; title.style.unityFontStyleAndWeight = FontStyle.Bold; title.style.flexGrow = 1;
        head.Add(title);
        var close = IconButtons.Make(IconKind.Close, CloseDetail, () => "닫기 (ESC)", 26, 14);
        close.style.backgroundColor = new Color(0, 0, 0, 0);
        head.Add(close);
        detail.Add(head);

        var scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.style.flexShrink = 1;
        scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        foreach (var (icons, what, desc) in Rows)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 6;
            var iconBox = new VisualElement();
            iconBox.style.flexDirection = FlexDirection.Row;
            iconBox.style.width = 50; iconBox.style.flexShrink = 0;
            if (icons != null) foreach (var k in icons) iconBox.Add(new IconElement(k, 22));
            else
            {
                var l = new Label("0.5x");
                l.style.color = AppUI.Text; l.style.fontSize = 11;
                iconBox.Add(l);
            }
            row.Add(iconBox);
            var texts = new VisualElement();
            texts.style.flexShrink = 1;
            var a = new Label(what);
            a.style.color = AppUI.Text; a.style.fontSize = 12; a.style.whiteSpace = WhiteSpace.Normal;
            var b = new Label(desc);
            b.style.color = AppUI.Sub; b.style.fontSize = 12; b.style.whiteSpace = WhiteSpace.Normal;
            texts.Add(a); texts.Add(b);
            row.Add(texts);
            scroll.Add(row);
        }
        var notesLabel = new Label("버튼·패널·목록 위에서 시작한 드래그와 휠은 그 UI만 조작합니다. 카메라는 남은 화면에서 조작합니다. 꾸미기 실행 취소는 카메라 회전·거리·화각을 되돌리지 않습니다.");
        notesLabel.style.color = AppUI.Sub; notesLabel.style.fontSize = 11; notesLabel.style.whiteSpace = WhiteSpace.Normal; notesLabel.style.marginTop = 10;
        scroll.Add(notesLabel);
        detail.Add(scroll);

        var buttons = new VisualElement();
        buttons.style.flexDirection = FlexDirection.Row;
        buttons.style.justifyContent = Justify.FlexEnd;
        buttons.style.marginTop = 8;
        var ok = new Button(Acknowledge) { text = "확인했어요" };
        ok.style.height = 28; ok.style.minWidth = 96;
        buttons.Add(ok);
        detail.Add(buttons);
        viewport.Add(detail);
    }

    void AddHint(IconKind[] icons, string text)
    {
        foreach (var k in icons) Quick.Add(new IconElement(k, 24));
        var l = new Label(text) { pickingMode = PickingMode.Ignore };
        l.style.color = AppUI.Text; l.style.fontSize = 12;
        l.style.marginLeft = 2; l.style.marginRight = 10;
        Quick.Add(l);
    }

    /// <summary>자세한 안내의 아래 끝을 빠른 안내 바로 위에 맞춘다. quickBottom 은 빠른 안내의 아래 여백.</summary>
    public void SetDetailBottom(float quickBottom)
    {
        detail.style.bottom = quickBottom + (float.IsNaN(Quick.layout.height) ? 34 : Quick.layout.height) + 8;
    }

    /// <summary>첫 정상 진입: 안내를 확인한 적이 없으면 펼친다.</summary>
    public void ShowOnFirstUse()
    {
        if (!settings.helpAcknowledged) OpenDetail();
    }

    public void ToggleDetail()
    {
        if (DetailOpen) CloseDetail(); else OpenDetail();
    }

    void OpenDetail()
    {
        UIAnim.SetShown(detail, true, UIAnim.Normal, new Vector2(0, 10));
    }

    public void CloseDetail()
    {
        UIAnim.SetShown(detail, false, UIAnim.Fast, new Vector2(0, 8));
    }

    void Acknowledge()
    {
        CloseDetail();
        if (settings.helpAcknowledged) return;
        settings.helpAcknowledged = true;
        saveSettings?.Invoke();
    }
}
