// 검색 가능한 드롭다운 (WindowAndCustomizationUI.md "검색 가능한 드롭다운")
//   선택 필드에 현재 항목 이름을 표시하고, 열면 검색란과 후보 목록을 띄운다.
//   검색: 표시 이름·ID 부분 일치(대소문자 무시). 방향키·Enter 선택, ESC·바깥 클릭으로 닫기.
//   목록을 닫거나 검색을 취소한 것만으로는 선택이 바뀌지 않는다.
//   목록이 열려 있는 동안 화면 전체를 덮는 투명 막을 두어 카메라 입력(드래그·휠)이 넘어가지 않게 한다.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class DropdownItem
{
    /// <summary>선택 값. null 은 비움(착용 안 함 등).</summary>
    public string Id;
    public string Name;
    /// <summary>보조 표시(ID 등). 검색 대상에 포함한다.</summary>
    public string Sub;
    /// <summary>현재 외형에 맞지 않는 후보(흐리게 표시, 선택은 가능).</summary>
    public bool Dim;
    /// <summary>이름 앞에 보일 색 견본(색 선택 항목). null 이면 없음.</summary>
    public Color[] Swatches;
}

public sealed class SearchDropdown : VisualElement
{
    const float RowHeight = 24, MaxPopupHeight = 340, SearchMinCount = 9;
    static SearchDropdown openOne;

    readonly Func<VisualElement> popupHost;
    readonly Func<List<DropdownItem>> source;
    readonly Action<DropdownItem> chosen;
    readonly VisualElement field;
    readonly Label nameLabel, subLabel;
    readonly VisualElement fieldSwatches;

    VisualElement catcher;
    TextField search;
    ListView list;
    Label empty;
    List<DropdownItem> all = new List<DropdownItem>();
    readonly List<DropdownItem> shown = new List<DropdownItem>();
    string currentId;
    bool layoutSeen;

    /// <param name="popupHost">목록을 띄울 화면 전체 크기의 요소</param>
    /// <param name="source">열 때마다 부르는 후보 목록</param>
    /// <param name="chosen">후보를 고르면 부른다(현재 값과 같아도 부르지 않는다)</param>
    public SearchDropdown(Func<VisualElement> popupHost, Func<List<DropdownItem>> source, Action<DropdownItem> chosen)
    {
        this.popupHost = popupHost;
        this.source = source;
        this.chosen = chosen;
        style.flexGrow = 1;
        style.flexShrink = 1;
        style.minWidth = 0;

        field = new VisualElement { name = "dropdown-field", focusable = true };
        field.style.flexDirection = FlexDirection.Row;
        field.style.alignItems = Align.Center;
        field.style.height = 26;
        field.style.paddingLeft = 8; field.style.paddingRight = 6;
        field.style.backgroundColor = AppUI.FieldBg;
        SetBorder(field, AppUI.Border, 1, 3);
        fieldSwatches = new VisualElement();
        fieldSwatches.style.flexDirection = FlexDirection.Row;
        fieldSwatches.style.flexShrink = 0;
        fieldSwatches.style.display = DisplayStyle.None;
        nameLabel = Ellipsis(new Label());
        nameLabel.style.flexGrow = 1;
        nameLabel.style.color = AppUI.Text;
        nameLabel.style.fontSize = 13;
        subLabel = Ellipsis(new Label());
        subLabel.style.flexShrink = 1;
        subLabel.style.maxWidth = new Length(45, LengthUnit.Percent);
        subLabel.style.color = AppUI.Sub;
        subLabel.style.fontSize = 11;
        subLabel.style.marginLeft = 6;
        var arrow = new Label("▾");
        arrow.style.color = AppUI.Sub;
        arrow.style.marginLeft = 4;
        field.Add(fieldSwatches); field.Add(nameLabel); field.Add(subLabel); field.Add(arrow);
        Add(field);

        field.RegisterCallback<ClickEvent>(e => Open());
        field.RegisterCallback<KeyDownEvent>(e =>
        {
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Space) { Open(); e.StopPropagation(); }
        });
        field.RegisterCallback<PointerEnterEvent>(e => field.style.backgroundColor = AppUI.FieldHover);
        field.RegisterCallback<PointerLeaveEvent>(e => field.style.backgroundColor = AppUI.FieldBg);
    }

    /// <summary>선택 필드 표시. color 가 null 이면 기본 글자색.</summary>
    public void SetValue(string id, string name, string sub, Color? color = null, Color[] swatches = null)
    {
        FillSwatches(fieldSwatches, swatches);
        currentId = id;
        nameLabel.text = name ?? "";
        nameLabel.style.color = color ?? AppUI.Text;
        subLabel.text = sub ?? "";
        subLabel.style.display = string.IsNullOrEmpty(sub) ? DisplayStyle.None : DisplayStyle.Flex;
    }

    public bool IsOpen => catcher != null;

    public static void CloseAny() => openOne?.Close();

    // ------------------------------------------------------------ 목록

    public void Open()
    {
        if (IsOpen) return;
        CloseAny();
        var host = popupHost();
        if (host == null) return;
        all = source() ?? new List<DropdownItem>();
        openOne = this;
        layoutSeen = false;

        catcher = new VisualElement { name = "dropdown-catcher" };
        catcher.style.position = Position.Absolute;
        catcher.style.left = 0; catcher.style.top = 0; catcher.style.right = 0; catcher.style.bottom = 0;
        catcher.RegisterCallback<PointerDownEvent>(e => { if (e.target == catcher) Close(); });
        catcher.RegisterCallback<WheelEvent>(e => { if (e.target == catcher) e.StopPropagation(); });
        // 방향키·Enter·ESC 는 아래 KeyDown 에서 직접 처리한다. 내비게이션 이벤트가 포커스를 옮기거나 목록을 두 번 움직이지 않게 막는다.
        catcher.RegisterCallback<NavigationMoveEvent>(BlockNavigation, TrickleDown.TrickleDown);
        catcher.RegisterCallback<NavigationSubmitEvent>(BlockNavigation, TrickleDown.TrickleDown);
        catcher.RegisterCallback<NavigationCancelEvent>(BlockNavigation, TrickleDown.TrickleDown);
        // 앱 창 크기가 바뀌면 위치가 맞지 않으므로 닫는다(처음 배치는 제외).
        catcher.RegisterCallback<GeometryChangedEvent>(e => { if (layoutSeen) Close(); layoutSeen = true; });

        var box = new VisualElement { name = "dropdown-popup" };
        box.style.position = Position.Absolute;
        box.style.backgroundColor = AppUI.PanelBg;
        box.style.paddingLeft = box.style.paddingRight = 4;
        box.style.paddingTop = box.style.paddingBottom = 4;
        SetBorder(box, AppUI.Border, 1, 4);

        bool withSearch = all.Count >= SearchMinCount;
        if (withSearch)
        {
            search = new TextField();
            search.style.marginLeft = search.style.marginRight = 0;
            search.style.marginBottom = 4;
            search.textEdition.placeholder = "이름 검색";
            search.RegisterValueChangedCallback(e => { Filter(e.newValue); e.StopPropagation(); });
            search.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            box.Add(search);
        }

        list = new ListView(shown, RowHeight, MakeRow, BindRow)
        {
            selectionType = SelectionType.Single,
            virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
        };
        list.style.flexGrow = 1;
        list.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
        box.Add(list);

        empty = new Label("검색 결과가 없습니다.");
        empty.style.color = AppUI.Sub;
        empty.style.fontSize = 12;
        empty.style.paddingLeft = 6; empty.style.paddingTop = 4;
        empty.style.display = DisplayStyle.None;
        box.Add(empty);

        // 필드 아래(공간이 모자라면 위)에 필드 너비 이상으로 띄운다.
        Rect f = field.worldBound;
        Vector2 p = host.WorldToLocal(f.position);
        float hostW = host.layout.width, hostH = host.layout.height;
        float w = Mathf.Min(Mathf.Max(f.width, 260), Mathf.Max(hostW - 8, 120));
        float x = Mathf.Clamp(p.x, 4, Mathf.Max(4, hostW - w - 4));
        float want = Mathf.Min(MaxPopupHeight, all.Count * RowHeight + (withSearch ? 34 : 0) + 12);
        want = Mathf.Max(want, (withSearch ? 34 : 0) + RowHeight * 2 + 12);
        float below = hostH - (p.y + f.height) - 4, above = p.y - 4;
        bool down = below >= want || below >= above;
        float h = Mathf.Max(Mathf.Min(want, down ? below : above), RowHeight + 12);
        box.style.left = x;
        box.style.width = w;
        box.style.height = h;
        box.style.top = down ? p.y + f.height + 2 : Mathf.Max(4, p.y - h - 2);
        catcher.Add(box);
        host.Add(catcher);

        Filter("");
        int cur = shown.FindIndex(i => i.Id == currentId);
        if (cur >= 0)
        {
            list.SetSelectionWithoutNotify(new[] { cur });
            list.schedule.Execute(() => list?.ScrollToItem(cur));
        }
        if (search != null) search.schedule.Execute(() => search?.Focus());
        else list.schedule.Execute(() => list?.Focus());
    }

    public void Close()
    {
        if (catcher == null) return;
        catcher.RemoveFromHierarchy();
        catcher = null; search = null; list = null; empty = null;
        if (openOne == this) openOne = null;
        field.Focus();
    }

    void Filter(string q)
    {
        q = q?.Trim();
        shown.Clear();
        foreach (var it in all)
            if (string.IsNullOrEmpty(q) || Has(it.Name, q) || Has(it.Sub, q)) shown.Add(it);
        list.RefreshItems();
        empty.style.display = shown.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        list.style.display = shown.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
        if (!string.IsNullOrEmpty(q) && shown.Count > 0) list.SetSelectionWithoutNotify(new[] { 0 });
        else list.ClearSelection();
    }

    static bool Has(string s, string q) => s != null && s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

    void OnKey(KeyDownEvent e)
    {
        if (list == null) return;
        switch (e.keyCode)
        {
            case KeyCode.DownArrow: Move(1); break;
            case KeyCode.UpArrow: Move(-1); break;
            case KeyCode.PageDown: Move(10); break;
            case KeyCode.PageUp: Move(-10); break;
            case KeyCode.Return:
            case KeyCode.KeypadEnter:
                if (list.selectedIndex >= 0) Choose(list.selectedIndex);
                else if (shown.Count == 1) Choose(0);
                break;
            case KeyCode.Escape: Close(); break;
            default: return;
        }
        e.StopPropagation();
    }

    void BlockNavigation(EventBase e)
    {
        e.StopPropagation();
        catcher?.focusController?.IgnoreEvent(e);
    }

    void Move(int delta)
    {
        if (shown.Count == 0) return;
        int i = list.selectedIndex < 0 ? (delta > 0 ? 0 : shown.Count - 1) : Mathf.Clamp(list.selectedIndex + delta, 0, shown.Count - 1);
        list.SetSelectionWithoutNotify(new[] { i });
        list.ScrollToItem(i);
    }

    void Choose(int index)
    {
        if (index < 0 || index >= shown.Count) return;
        var it = shown[index];
        Close();
        if (it.Id != currentId) chosen?.Invoke(it);
    }

    VisualElement MakeRow()
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.height = RowHeight;
        row.style.paddingLeft = 6; row.style.paddingRight = 6;
        var sw = new VisualElement { name = "swatches" };
        sw.style.flexDirection = FlexDirection.Row;
        sw.style.flexShrink = 0;
        row.Add(sw);
        var n = Ellipsis(new Label { name = "name" });
        n.style.flexGrow = 1;
        n.style.fontSize = 13;
        var s = Ellipsis(new Label { name = "sub" });
        s.style.flexShrink = 1;
        s.style.maxWidth = new Length(50, LengthUnit.Percent);
        s.style.fontSize = 11;
        s.style.color = AppUI.Sub;
        s.style.marginLeft = 6;
        row.Add(n); row.Add(s);
        row.RegisterCallback<ClickEvent>(e => { if (row.userData is int i) Choose(i); });
        return row;
    }

    void BindRow(VisualElement row, int index)
    {
        row.userData = index;
        var it = shown[index];
        var n = row.Q<Label>("name");
        n.text = it.Name ?? it.Id ?? "";
        n.style.color = it.Dim ? AppUI.Sub : AppUI.Text;
        n.style.unityFontStyleAndWeight = it.Id == currentId ? FontStyle.Bold : FontStyle.Normal;
        row.Q<Label>("sub").text = it.Sub ?? "";
        FillSwatches(row.Q("swatches"), it.Swatches);
    }

    // ------------------------------------------------------------ 스타일

    /// <summary>색 견본 사각형을 채운다. 없으면 숨긴다.</summary>
    static void FillSwatches(VisualElement box, Color[] colors)
    {
        box.Clear();
        bool any = colors != null && colors.Length > 0;
        box.style.display = any ? DisplayStyle.Flex : DisplayStyle.None;
        if (!any) return;
        box.style.marginRight = 6;
        foreach (var c in colors) box.Add(Swatch(c, 14));
    }

    public static VisualElement Swatch(Color c, float size)
    {
        var e = new VisualElement { pickingMode = PickingMode.Ignore };
        e.style.width = size; e.style.height = size;
        e.style.marginRight = 2;
        e.style.backgroundColor = new Color(c.r, c.g, c.b, 1);
        SetBorder(e, AppUI.Border, 1, 2);
        return e;
    }

    static Label Ellipsis(Label l)
    {
        l.style.overflow = Overflow.Hidden;
        l.style.textOverflow = TextOverflow.Ellipsis;
        l.style.whiteSpace = WhiteSpace.NoWrap;
        l.style.unityTextAlign = TextAnchor.MiddleLeft;
        l.style.paddingLeft = 0; l.style.paddingRight = 0;
        l.style.marginLeft = 0; l.style.marginRight = 0;
        return l;
    }

    public static void SetBorder(VisualElement e, Color c, float width, float radius)
    {
        e.style.borderLeftColor = e.style.borderRightColor = e.style.borderTopColor = e.style.borderBottomColor = c;
        e.style.borderLeftWidth = e.style.borderRightWidth = e.style.borderTopWidth = e.style.borderBottomWidth = width;
        e.style.borderTopLeftRadius = e.style.borderTopRightRadius = e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = radius;
    }
}
