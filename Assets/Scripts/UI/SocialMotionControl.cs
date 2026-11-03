// 소셜 모션 버튼 (SocialMotion.md, WindowAndCustomizationUI.md 항목 T)
//   카메라 출력 영역 오른쪽 아래. 누르면 검색 목록이 열리고, 고르면 바로 재생한다.
//   재생 중에는 "▶ 이름"과 ■(정지)를 보인다. 같은 항목을 다시 고르면 처음부터 재생한다.
//   저장·이력과 관계없는 보기 도구다.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class SocialMotionControl
{
    public readonly VisualElement Root;
    readonly SearchDropdown dropdown;
    readonly Button stop;

    /// <param name="popupHost">목록을 띄울 화면 전체 크기의 요소</param>
    /// <param name="items">열 때마다 부르는 후보 목록</param>
    /// <param name="play">고른 소셜 모션 ID</param>
    /// <param name="stopAction">정지</param>
    /// <param name="canUse">모달·대화상자 중에는 false</param>
    public SocialMotionControl(Func<VisualElement> popupHost, Func<List<DropdownItem>> items, Action<string> play, Action stopAction, Func<bool> canUse)
    {
        Root = new VisualElement { name = "motion-controls" };
        Root.style.flexDirection = FlexDirection.Row;
        Root.style.alignItems = Align.Center;
        Root.style.backgroundColor = IconButtons.Bg;
        Root.style.paddingLeft = Root.style.paddingRight = Root.style.paddingTop = Root.style.paddingBottom = 3;
        AppUI.Round(Root, 9);

        dropdown = new SearchDropdown(popupHost, () => canUse() ? items() : null, item => { if (item?.Id != null) play(item.Id); });
        dropdown.RememberScroll = true;
        dropdown.style.width = 210;
        dropdown.style.flexGrow = 0;
        Root.Add(dropdown);
        Tips.Attach(dropdown, () => "게임 모션 메뉴의 행동을 재생합니다. 저장되지 않습니다.");

        stop = new Button(() => { if (canUse()) stopAction(); }) { text = "■" };
        stop.style.width = 28; stop.style.height = 26;
        stop.style.marginLeft = 3; stop.style.marginRight = 0;
        stop.style.marginTop = stop.style.marginBottom = 0;
        stop.style.fontSize = 12;
        stop.style.color = AppUI.Text;
        stop.style.backgroundColor = AppUI.FieldBg;
        stop.style.borderTopWidth = stop.style.borderBottomWidth = stop.style.borderLeftWidth = stop.style.borderRightWidth = 0;
        AppUI.Round(stop, 6);
        UIAnim.SmoothColor(stop, UIAnim.Normal);
        Tips.Attach(stop, () => "모션 정지");
        Root.Add(stop);
        SetIdle();
    }

    /// <summary>재생 중 표시. 같은 항목을 다시 골라도 재생되도록 선택 값은 비워 둔다.</summary>
    public void SetPlaying(string label)
    {
        dropdown.SetValue(null, "▶ " + label, null);
        stop.style.display = DisplayStyle.Flex;
    }

    public void SetIdle()
    {
        dropdown.SetValue(null, "모션", null, AppUI.Sub);
        stop.style.display = DisplayStyle.None;
    }
}
