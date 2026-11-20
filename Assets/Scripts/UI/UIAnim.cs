// UI 연출(부드러운 전환). UI Toolkit 의 스타일 트랜지션(opacity·translate·scale·background-color)을 쓴다.
//   외부 트윈 라이브러리를 쓰지 않는다. 시간은 짧게(0.1~0.25초) 두어 조작을 기다리게 하지 않는다.
//   보이기: display 를 켜고 투명·약간 비켜난 상태에서 다음 배치 뒤 제자리로 옮긴다.
//   숨기기: 투명하게 만든 뒤 끝나면 display 를 끈다. 사라지는 동안의 클릭은 막는다.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public static class UIAnim
{
    public const float Fast = 0.12f, Normal = 0.18f, Slow = 0.24f;

    static readonly Dictionary<VisualElement, IVisualElementScheduledItem> pendingHide = new Dictionary<VisualElement, IVisualElementScheduledItem>();
    static readonly HashSet<VisualElement> blocked = new HashSet<VisualElement>();

    /// <summary>요소에 트랜지션을 건다. props 예: "opacity", "translate", "scale", "background-color".</summary>
    public static void Transition(VisualElement e, float seconds, params string[] props)
    {
        var names = new List<StylePropertyName>();
        var times = new List<TimeValue>();
        var easing = new List<EasingFunction>();
        foreach (var p in props)
        {
            names.Add(new StylePropertyName(p));
            times.Add(new TimeValue(seconds, TimeUnit.Second));
            easing.Add(new EasingFunction(EasingMode.EaseOutCubic));
        }
        e.style.transitionProperty = new StyleList<StylePropertyName>(names);
        e.style.transitionDuration = new StyleList<TimeValue>(times);
        e.style.transitionTimingFunction = new StyleList<EasingFunction>(easing);
    }

    /// <summary>hover 색 전환 등 배경색만 부드럽게 바꾼다.</summary>
    public static void SmoothColor(VisualElement e, float seconds = Fast) => Transition(e, seconds, "background-color");

    /// <summary>스타일이 한 번 적용된 뒤(다음 배치) 실행한다. 같은 프레임에 바꾸면 트랜지션이 시작되지 않는다.</summary>
    static void AfterLayout(VisualElement e, Action a)
    {
        bool done = false;
        EventCallback<GeometryChangedEvent> cb = null;
        void Run() { if (done) return; done = true; e.UnregisterCallback(cb); a(); }
        cb = _ => Run();
        e.RegisterCallback(cb);
        e.schedule.Execute(Run).StartingIn(40);
    }

    /// <summary>방금 화면에 붙인 요소를 투명·offset 위치에서 제자리로 나타나게 한다.</summary>
    public static void FadeIn(VisualElement e, float seconds = Normal, Vector2 offset = default, float fromScale = 1f)
    {
        Transition(e, seconds, "opacity", "translate", "scale");
        e.style.opacity = 0;
        e.style.translate = new Translate(offset.x, offset.y);
        if (fromScale != 1f) e.style.scale = new Scale(new Vector2(fromScale, fromScale));
        AfterLayout(e, () =>
        {
            e.style.opacity = 1;
            e.style.translate = new Translate(0, 0);
            e.style.scale = new Scale(Vector2.one);
        });
    }

    /// <summary>요소를 서서히 사라지게 한 뒤 계층에서 뗀다. 사라지는 동안의 클릭·휠은 막는다.</summary>
    public static void FadeOutAndRemove(VisualElement e, float seconds = Fast, Vector2 offset = default)
    {
        if (e == null || e.parent == null) return;
        Block(e);
        Transition(e, seconds, "opacity", "translate");
        e.style.opacity = 0;
        if (offset != default) e.style.translate = new Translate(offset.x, offset.y);
        e.schedule.Execute(() => e.RemoveFromHierarchy()).StartingIn((long)(seconds * 1000) + 20);
    }

    /// <summary>
    /// display 로 켜고 끄는 요소의 표시를 부드럽게 바꾼다. 현재 의도한 상태는 IsShown 으로 확인한다
    /// (사라지는 중에도 display 는 켜져 있으므로 display 로 판단하지 않는다).
    /// </summary>
    public static void SetShown(VisualElement e, bool show, float seconds = Normal, Vector2 offset = default)
    {
        if (pendingHide.TryGetValue(e, out var p)) { p.Pause(); pendingHide.Remove(e); }
        if (show)
        {
            Unblock(e);
            bool wasHidden = e.resolvedStyle.display == DisplayStyle.None || e.style.display == DisplayStyle.None;
            e.style.display = DisplayStyle.Flex;
            if (wasHidden) FadeIn(e, seconds, offset);
            else { Transition(e, seconds, "opacity", "translate"); e.style.opacity = 1; e.style.translate = new Translate(0, 0); }
            return;
        }
        if (e.style.display == DisplayStyle.None) return;
        Block(e);
        Transition(e, seconds, "opacity", "translate");
        e.style.opacity = 0;
        if (offset != default) e.style.translate = new Translate(offset.x, offset.y);
        pendingHide[e] = e.schedule.Execute(() =>
        {
            pendingHide.Remove(e);
            e.style.display = DisplayStyle.None;
            Unblock(e);
        }).StartingIn((long)(seconds * 1000) + 20);
    }

    public static bool IsShown(VisualElement e) => e.style.display != DisplayStyle.None && !pendingHide.ContainsKey(e);

    static void Block(VisualElement e)
    {
        if (!blocked.Add(e)) return;
        e.RegisterCallback<PointerDownEvent>(Stop, TrickleDown.TrickleDown);
        e.RegisterCallback<PointerUpEvent>(Stop, TrickleDown.TrickleDown);
        e.RegisterCallback<ClickEvent>(Stop, TrickleDown.TrickleDown);
    }

    static void Unblock(VisualElement e)
    {
        if (!blocked.Remove(e)) return;
        e.UnregisterCallback<PointerDownEvent>(Stop, TrickleDown.TrickleDown);
        e.UnregisterCallback<PointerUpEvent>(Stop, TrickleDown.TrickleDown);
        e.UnregisterCallback<ClickEvent>(Stop, TrickleDown.TrickleDown);
    }

    static void Stop(EventBase ev) => ev.StopImmediatePropagation();

    /// <summary>0~1 진행도에 쓰는 감속 곡선.</summary>
    public static float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        float u = 1 - t;
        return 1 - u * u * u;
    }
}

/// <summary>
/// 좌우로 미는 켜기/끄기 스위치(체크 상자 대신). 누르거나 Space·Enter 로 바꾼다. 손잡이가 좌우로 미끄러지고 바탕색이 바뀐다.
/// 줄 오른쪽 끝에 붙고, 스위치 왼쪽 글자로 켬/끔을 함께 보인다(색만으로 상태를 전하지 않는다).
/// </summary>
public sealed class SlideSwitch : VisualElement
{
    const float W = 40, H = 22, Knob = 16, Pad = 3;
    readonly VisualElement track, knob;
    readonly Label text;
    readonly string onText, offText;
    bool value;

    /// <summary>사용자가 바꾸면 부른다(새 값).</summary>
    public event Action<bool> Changed;

    public bool Value => value;

    public SlideSwitch(string onText = "켬", string offText = "끔")
    {
        this.onText = onText; this.offText = offText;
        focusable = true;
        style.flexDirection = FlexDirection.Row;
        style.alignItems = Align.Center;
        style.justifyContent = Justify.FlexEnd;
        style.flexGrow = 1;
        style.height = 26;
        style.paddingRight = 3;

        text = new Label { pickingMode = PickingMode.Ignore };
        text.style.marginRight = 8;
        text.style.fontSize = 13;
        text.style.unityTextAlign = TextAnchor.MiddleRight;
        Add(text);

        track = new VisualElement { name = "switch-track", pickingMode = PickingMode.Ignore };
        track.style.width = W; track.style.height = H;
        track.style.flexShrink = 0;
        SearchDropdown.SetBorder(track, AppUI.Border, 1, H / 2);
        UIAnim.Transition(track, UIAnim.Fast, "background-color", "border-top-color", "border-bottom-color", "border-left-color", "border-right-color");
        knob = new VisualElement { name = "switch-knob", pickingMode = PickingMode.Ignore };
        knob.style.position = Position.Absolute;
        knob.style.width = Knob; knob.style.height = Knob;
        knob.style.top = (H - 2 - Knob) / 2;
        knob.style.backgroundColor = AppUI.Text;
        SearchDropdown.SetBorder(knob, new Color(0, 0, 0, 0), 0, Knob / 2);
        UIAnim.Transition(knob, UIAnim.Fast, "left");
        track.Add(knob);
        Add(track);

        RegisterCallback<ClickEvent>(e => Toggle());
        RegisterCallback<KeyDownEvent>(e =>
        {
            if (e.keyCode == KeyCode.Space || e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { Toggle(); e.StopPropagation(); }
        });
        RegisterCallback<PointerEnterEvent>(e => { if (!value) track.style.backgroundColor = AppUI.FieldHover; });
        RegisterCallback<PointerLeaveEvent>(e => Apply());
        Apply();
    }

    void Toggle()
    {
        if (!enabledInHierarchy) return;
        value = !value;
        Apply();
        Changed?.Invoke(value);
    }

    /// <summary>값을 바꾸되 Changed 를 부르지 않는다(상태 → 화면 동기화).</summary>
    public void SetValueWithoutNotify(bool v)
    {
        if (value == v) return;
        value = v;
        Apply();
    }

    void Apply()
    {
        track.style.backgroundColor = value ? AppUI.Accent : AppUI.FieldBg;
        var border = value ? AppUI.Accent : AppUI.Border;
        track.style.borderTopColor = track.style.borderBottomColor = track.style.borderLeftColor = track.style.borderRightColor = border;
        knob.style.left = value ? W - 2 - Pad - Knob : Pad;
        text.text = value ? onText : offText;
        text.style.color = value ? AppUI.Text : AppUI.Sub;
    }
}
