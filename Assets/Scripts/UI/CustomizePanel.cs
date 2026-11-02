// 꾸미기 창 (WindowAndCustomizationUI.md, Appearance.md, Equipment.md)
//   앱 안의 UI 패널. 제목 영역을 끌어 옮기고 오른쪽 아래 핸들로 크기를 바꾼다.
//   위치·크기는 앱 설정에 저장해 다음 실행에 복원하고, 앱 화면 밖으로 나가지 않게 보정한다.
//   큰 카테고리는 폴드아웃, 세부 항목은 검색 가능한 드롭다운이다. 고르면 바로 모델에 반영한다(적용 버튼 없음).
//   선택 상태와 실제 출력을 분리한다. 입을 수 없는 의상은 선택을 유지하고 빨간색과 사유를 표시한다.
//
// 보정 정책(구현 선택): 저장하는 값은 사용자가 마지막으로 옮기거나 크기를 바꾼 배치다.
//   앱 창이 작아져서 보정한 배치는 저장하지 않으므로, 앱 창을 다시 키우면 원래 배치로 돌아간다.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class CustomizePanel
{
    const float DefaultWidth = 340, Margin = 12, MinWidth = 240, MinHeight = 160;

    /// <summary>폴드아웃 키 → 처음 펼침 여부(설정에 저장된 값이 없을 때).</summary>
    static readonly (string key, string title, bool open)[] Sections =
    {
        ("basic", "기본 외형", true),
        ("head", "얼굴형", false),
        ("hair", "헤어", true),
        ("eyes", "눈", false),
        ("mouth", "입", false),
        ("clothes", "의상", true),
        ("accessory", "액세서리", false),
        ("weapon", "무기", false),
    };

    /// <summary>Costume.CostumeType → 표시 이름, 폴드아웃.</summary>
    static readonly (string part, string label, string section)[] CostumeParts =
    {
        ("Hat", "모자", "clothes"),
        ("Top", "상의", "clothes"),
        ("Bottom", "하의", "clothes"),
        ("Robe", "로브", "clothes"),
        ("Gloves", "장갑", "clothes"),
        ("Shoes", "신발", "clothes"),
        ("EarDeco", "귀장식", "accessory"),
        ("EyeDeco", "눈장식", "accessory"),
        ("FaceDeco", "얼굴장식", "accessory"),
        ("Weapon", "무기", "weapon"),
    };

    readonly VisualElement host, window, title, popupLayer;
    readonly ScrollView scroll;
    readonly Label error;
    readonly AppSettings settings;
    readonly Action saveSettings;
    readonly Action<Action<EditState>> edit;
    readonly Dictionary<string, Foldout> foldouts = new Dictionary<string, Foldout>();
    readonly List<Action> refreshers = new List<Action>();

    GameData data;
    EditState state;
    AssemblyPlan plan;
    Rect desired;
    Rect shown;

    /// <param name="host">화면 전체 크기의 층(AppUI.WindowLayer)</param>
    /// <param name="edit">장면 상태를 바꾸는 요청. 복사본을 바꿔 조립에 성공하면 반영한다(AppController.Edit).</param>
    public CustomizePanel(VisualElement host, AppSettings settings, Action saveSettings, Action<Action<EditState>> edit)
    {
        this.host = host;
        this.settings = settings;
        this.saveSettings = saveSettings;
        this.edit = edit;

        window = new VisualElement { name = "customize-window" };
        window.style.position = Position.Absolute;
        window.style.backgroundColor = AppUI.PanelBg;
        window.style.overflow = Overflow.Hidden;
        SearchDropdown.SetBorder(window, AppUI.Border, 1, 6);

        title = new VisualElement { name = "customize-title" };
        title.style.flexDirection = FlexDirection.Row;
        title.style.alignItems = Align.Center;
        title.style.height = 32;
        title.style.flexShrink = 0;
        title.style.paddingLeft = 12; title.style.paddingRight = 8;
        title.style.backgroundColor = AppUI.HeaderBg;
        var t = new Label("꾸미기");
        t.style.color = AppUI.Text;
        t.style.fontSize = 14;
        t.style.unityFontStyleAndWeight = FontStyle.Bold;
        t.style.flexGrow = 1;
        t.pickingMode = PickingMode.Ignore;
        title.Add(t);
        window.Add(title);

        error = new Label { name = "customize-error" };
        error.style.color = AppUI.ErrorColor;
        error.style.fontSize = 12;
        error.style.whiteSpace = WhiteSpace.Normal;
        error.style.marginLeft = 12; error.style.marginRight = 12; error.style.marginTop = 6;
        error.style.display = DisplayStyle.None;
        window.Add(error);

        scroll = new ScrollView(ScrollViewMode.Vertical) { name = "customize-scroll" };
        scroll.style.flexGrow = 1;
        scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        scroll.contentContainer.style.paddingLeft = 8;
        scroll.contentContainer.style.paddingRight = 10;
        scroll.contentContainer.style.paddingBottom = 18;
        window.Add(scroll);

        var grip = new Label("◢") { name = "customize-resize" };
        grip.style.position = Position.Absolute;
        grip.style.right = 1; grip.style.bottom = 0;
        grip.style.width = 16; grip.style.height = 16;
        grip.style.fontSize = 11;
        grip.style.color = AppUI.Sub;
        grip.style.unityTextAlign = TextAnchor.LowerRight;
        window.Add(grip);

        host.Add(window);

        // 드롭다운 목록은 꾸미기 창 밖으로 나갈 수 있도록 화면 전체 층에 띄운다.
        popupLayer = new VisualElement { name = "popup-layer", pickingMode = PickingMode.Ignore };
        popupLayer.style.position = Position.Absolute;
        popupLayer.style.left = 0; popupLayer.style.top = 0; popupLayer.style.right = 0; popupLayer.style.bottom = 0;
        host.Add(popupLayer);

        DragHandler(title, (r, d) => new Rect(r.x + d.x, r.y + d.y, r.width, r.height));
        DragHandler(grip, (r, d) => new Rect(r.x, r.y, r.width + d.x, r.height + d.y));
        host.RegisterCallback<GeometryChangedEvent>(e => Layout());

        var saved = settings.customizePanel;
        desired = saved != null && saved.valid && saved.width > 0 && saved.height > 0
            ? new Rect(saved.x, saved.y, saved.width, saved.height)
            : Rect.zero;
        Layout();
    }

    // ------------------------------------------------------------ 배치

    void Layout()
    {
        float w = host.layout.width, h = host.layout.height;
        if (float.IsNaN(w) || float.IsNaN(h) || w <= 0 || h <= 0) return;
        if (desired.width <= 0)
        {
            float dw = Mathf.Min(DefaultWidth, w - Margin * 2);
            desired = new Rect(w - dw - Margin, Margin, dw, h - Margin * 2);
        }
        Show(Clamp(desired, w, h));
    }

    /// <summary>창이 앱 화면보다 크면 먼저 줄이고, 그다음 전체가 화면 안에 들어오게 옮긴다.</summary>
    static Rect Clamp(Rect r, float w, float h)
    {
        float rw = Mathf.Clamp(r.width, Mathf.Min(MinWidth, w), w);
        float rh = Mathf.Clamp(r.height, Mathf.Min(MinHeight, h), h);
        float x = Mathf.Clamp(r.x, 0, w - rw);
        float y = Mathf.Clamp(r.y, 0, h - rh);
        return new Rect(x, y, rw, rh);
    }

    void Show(Rect r)
    {
        shown = r;
        window.style.left = r.x; window.style.top = r.y;
        window.style.width = r.width; window.style.height = r.height;
    }

    /// <summary>요소를 끌면 창 배치를 바꾸고, 놓으면 그 배치를 저장한다.</summary>
    void DragHandler(VisualElement handle, Func<Rect, Vector2, Rect> apply)
    {
        Vector2 start = default;
        Rect from = default;
        int pointer = -1;
        handle.RegisterCallback<PointerDownEvent>(e =>
        {
            if (e.button != 0) return;
            SearchDropdown.CloseAny();
            pointer = e.pointerId;
            start = e.position;
            from = shown;
            handle.CapturePointer(pointer);
            e.StopPropagation();
        });
        handle.RegisterCallback<PointerMoveEvent>(e =>
        {
            if (e.pointerId != pointer || !handle.HasPointerCapture(pointer)) return;
            Vector2 d = (Vector2)e.position - start;
            Show(Clamp(apply(from, d), host.layout.width, host.layout.height));
        });
        void End(int id)
        {
            if (pointer < 0 || id != pointer) return;
            int p = pointer;
            pointer = -1;
            if (handle.HasPointerCapture(p)) handle.ReleasePointer(p);
            desired = shown;
            settings.customizePanel = new PanelLayout { valid = true, x = shown.x, y = shown.y, width = shown.width, height = shown.height };
            saveSettings?.Invoke();
        }
        handle.RegisterCallback<PointerUpEvent>(e => End(e.pointerId));
        handle.RegisterCallback<PointerCaptureOutEvent>(e => End(pointer));
    }

    // ------------------------------------------------------------ 구성

    /// <summary>시트를 받은 뒤 한 번 항목을 만든다.</summary>
    public void Bind(GameData gameData)
    {
        data = gameData;
        scroll.Clear();
        foldouts.Clear();
        refreshers.Clear();

        Header("외형");
        var basic = Section("basic");
        basic.Add(Row("성별", Choice(GenderItems, () => state.appearance.gender, Gender, (s, id) => s.appearance.gender = id), null));
        basic.Add(Row("체형", Choice(BodyTypeItems, () => state.appearance.bodyType, BodyTypeName, (s, id) => s.appearance.bodyType = id), null));
        basic.Add(Row("나이", Choice(AgeItems, () => state.appearance.age.ToString(), id => id + "세",
            (s, id) => s.appearance.age = int.Parse(id)), null));

        AppearanceRow(Section("head"), "얼굴형", HeadItems, () => state.appearance.head, HeadIssue, (s, id) => s.appearance.head = id,
            id => data.Heads.TryGetValue(id ?? "", out var h) ? h.DisplayName : null);
        AppearanceRow(Section("hair"), "헤어스타일", () => GroupItems(data.Hairs, state.appearance.hair, HairVariant, "Default"),
            () => state.appearance.hair, () => GroupIssue(data.Hairs, state.appearance.hair, HairVariant(), "Default"),
            (s, id) => s.appearance.hair = id, id => GameData.Resolve(data.Hairs, id, HairVariant())?.DisplayName);
        AppearanceRow(Section("eyes"), "눈 모양", () => GroupItems(data.Eyes, state.appearance.eyes, HeadVariant, "Head_00"),
            () => state.appearance.eyes, () => GroupIssue(data.Eyes, state.appearance.eyes, HeadVariant(), "Head_00"),
            (s, id) => s.appearance.eyes = id, id => GameData.Resolve(data.Eyes, id, HeadVariant(), "Head_00")?.DisplayName);
        AppearanceRow(Section("mouth"), "입 모양", () => GroupItems(data.Mouths, state.appearance.mouth, HeadVariant, "Head_00"),
            () => state.appearance.mouth, () => GroupIssue(data.Mouths, state.appearance.mouth, HeadVariant(), "Head_00"),
            (s, id) => s.appearance.mouth = id, id => GameData.Resolve(data.Mouths, id, HeadVariant(), "Head_00")?.DisplayName);

        Header("의상");
        foreach (var (part, label, section) in CostumeParts) CostumeRow(Section(section), part, label);

        Refresh(state, plan);
    }

    /// <summary>장면이 바뀐 뒤 선택 필드·상태 표시를 다시 그린다.</summary>
    public void Refresh(EditState current, AssemblyPlan currentPlan)
    {
        state = current;
        plan = currentPlan;
        if (state == null || data == null) return;
        foreach (var r in refreshers) r();
    }

    /// <summary>적용 실패 안내. null 이면 지운다.</summary>
    public void SetError(string message)
    {
        error.text = message ?? "";
        error.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
    }

    void Header(string text)
    {
        var l = new Label(text);
        l.style.color = AppUI.Sub;
        l.style.fontSize = 11;
        l.style.marginTop = 10; l.style.marginLeft = 4; l.style.marginBottom = 2;
        l.style.unityFontStyleAndWeight = FontStyle.Bold;
        scroll.Add(l);
    }

    VisualElement Section(string key)
    {
        if (foldouts.TryGetValue(key, out var existing)) return existing.contentContainer;
        int i = Array.FindIndex(Sections, s => s.key == key);
        var (_, titleText, open) = Sections[i];
        if (settings.sectionsSaved) open = settings.openSections.Contains(key);
        var f = new Foldout { text = titleText, value = open };
        f.style.marginTop = 2;
        var toggleLabel = f.Q<Toggle>()?.Q<Label>();
        if (toggleLabel != null) { toggleLabel.style.color = AppUI.Text; toggleLabel.style.fontSize = 13; }
        // 기본 테마의 펼침 화살표는 어두운 배경에서 거의 보이지 않는다.
        var arrow = f.Q(className: Foldout.checkmarkUssClassName);
        if (arrow != null) arrow.style.unityBackgroundImageTintColor = AppUI.Sub;
        f.contentContainer.style.marginLeft = 4;
        f.contentContainer.style.marginBottom = 4;
        // 펼침 상태는 앱 UI 설정이다. 장면 상태·코디 JSON 에 넣지 않는다.
        f.RegisterValueChangedCallback(e =>
        {
            if (e.target != f) return;
            SaveSections();
        });
        foldouts[key] = f;
        scroll.Add(f);
        return f.contentContainer;
    }

    void SaveSections()
    {
        settings.sectionsSaved = true;
        settings.openSections = new List<string>();
        foreach (var kv in foldouts) if (kv.Value.value) settings.openSections.Add(kv.Key);
        saveSettings?.Invoke();
    }

    static VisualElement Row(string label, VisualElement control, Label status)
    {
        var box = new VisualElement();
        box.style.marginTop = 3; box.style.marginBottom = 3;
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        var l = new Label(label);
        l.style.width = 76;
        l.style.flexShrink = 0;
        l.style.color = AppUI.Sub;
        l.style.fontSize = 12;
        row.Add(l);
        row.Add(control);
        box.Add(row);
        if (status != null) box.Add(status);
        return box;
    }

    static Label StatusLabel()
    {
        var l = new Label();
        l.style.fontSize = 11;
        l.style.whiteSpace = WhiteSpace.Normal;
        l.style.marginLeft = 76; l.style.marginTop = 2;
        l.style.display = DisplayStyle.None;
        return l;
    }

    static void SetStatus(Label l, string text, Color color)
    {
        l.text = text ?? "";
        l.style.color = color;
        l.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
    }

    SearchDropdown Dropdown(Func<List<DropdownItem>> items, Action<DropdownItem> chosen) =>
        new SearchDropdown(() => popupLayer, () => state == null ? null : items(), chosen);

    /// <summary>값 목록이 고정된 선택(성별·체형·나이).</summary>
    SearchDropdown Choice(Func<List<DropdownItem>> items, Func<string> current, Func<string, string> name, Action<EditState, string> set)
    {
        var dd = Dropdown(items, it => edit(s => set(s, it.Id)));
        refreshers.Add(() => { string id = current(); dd.SetValue(id, name(id) ?? id ?? "(없음)", null); });
        return dd;
    }

    void AppearanceRow(VisualElement parent, string label, Func<List<DropdownItem>> items, Func<string> current, Func<string> issue,
        Action<EditState, string> set, Func<string, string> name)
    {
        var status = StatusLabel();
        var dd = Dropdown(items, it => edit(s => set(s, it.Id)));
        refreshers.Add(() =>
        {
            string id = current();
            string problem = issue();
            dd.SetValue(id, name(id) ?? id ?? "(없음)", id, problem != null ? AppUI.ErrorColor : (Color?)null);
            SetStatus(status, problem, AppUI.ErrorColor);
        });
        parent.Add(Row(label, dd, status));
    }

    void CostumeRow(VisualElement parent, string part, string label)
    {
        var status = StatusLabel();
        var dd = Dropdown(() => CostumeItems(part), it => edit(s => SetCostume(s, part, it.Id)));
        refreshers.Add(() =>
        {
            var slot = state.Costume(part);
            var res = plan?.Costumes.Find(c => c.Part == part);
            if (slot == null || (slot.kind != CostumeSlotState.KindResource && string.IsNullOrEmpty(slot.costumeId)))
            {
                dd.SetValue(null, "(착용 안 함)", null, AppUI.Sub);
                SetStatus(status, null, AppUI.Sub);
                return;
            }
            bool resource = slot.kind == CostumeSlotState.KindResource;
            string id = resource ? slot.resource : slot.costumeId;
            string name = resource ? slot.resource : data.Costumes.TryGetValue(slot.costumeId, out var row) ? row.DisplayName : null;
            Color? color = null;
            string text = null;
            Color statusColor = AppUI.ErrorColor;
            if (res != null && res.Status == CostumeStatus.HiddenByOther)
            {
                text = "다른 의상에 가려져 표시하지 않습니다. 선택과 염색은 유지합니다.";
                statusColor = AppUI.WarnColor;
            }
            else if (res != null && res.Status != CostumeStatus.Applied)
            {
                color = AppUI.ErrorColor;
                text = "적용되지 않음: " + res.Detail + " 선택과 염색은 유지합니다.";
            }
            dd.SetValue(resource ? null : id, name ?? id, resource ? "리소스 " + id : id, color);
            SetStatus(status, text, statusColor);
        });
        parent.Add(Row(label, dd, status));
    }

    static void SetCostume(EditState s, string part, string costumeId)
    {
        var slot = s.Costume(part);
        if (costumeId == null)
        {
            if (slot != null) s.costumes.Remove(slot);
            return;
        }
        if (slot == null) s.costumes.Add(slot = new CostumeSlotState { part = part });
        // 다른 의상을 고르면 새 의상의 원본 초기색으로 시작한다(Dye.md 확정).
        if (slot.kind != CostumeSlotState.KindCostume || slot.costumeId != costumeId) slot.dyes.Clear();
        slot.kind = CostumeSlotState.KindCostume;
        slot.costumeId = costumeId;
        slot.resource = null;
    }

    // ------------------------------------------------------------ 후보 목록

    bool Male => string.Equals(state.appearance.gender, "Male", StringComparison.OrdinalIgnoreCase);
    bool Infant => state.appearance.age > 0 && state.appearance.age < PlanResolver.AdultMinAge;
    string HairVariant() => string.IsNullOrEmpty(state.appearance.bodyType) ? "Default" : state.appearance.bodyType;
    string HeadVariant() => state.appearance.head;

    static string Gender(string id) => id == "Male" ? "남성" : id == "Female" ? "여성" : id;

    List<DropdownItem> GenderItems() => new List<DropdownItem>
    {
        new DropdownItem { Id = "Male", Name = "남성" },
        new DropdownItem { Id = "Female", Name = "여성" },
    };

    /// <summary>시트 표시 이름 앞의 정렬 번호(예: "1중간")를 뗀다.</summary>
    string BodyTypeName(string id)
    {
        if (id == null || !data.BodyTypes.TryGetValue(id, out var b) || string.IsNullOrEmpty(b.DisplayName)) return id;
        return b.DisplayName.TrimStart('0', '1', '2', '3', '4', '5', '6', '7', '8', '9').Trim();
    }

    List<DropdownItem> BodyTypeItems()
    {
        var items = new List<DropdownItem>();
        foreach (var b in data.BodyTypes.Values) items.Add(new DropdownItem { Id = b.Id, Name = BodyTypeName(b.Id) });
        return items;
    }

    /// <summary>[제안] 외형 프리셋에 나오는 나이를 유효 범위로 본다. 범위는 미정(Appearance.md).</summary>
    List<DropdownItem> AgeItems()
    {
        var ages = new SortedSet<int>();
        foreach (var p in data.Presets.Values) if (p.Age > 0) ages.Add(p.Age);
        if (state.appearance.age > 0) ages.Add(state.appearance.age);
        var items = new List<DropdownItem>();
        foreach (int a in ages) items.Add(new DropdownItem { Id = a.ToString(), Name = a + "세", Sub = a < PlanResolver.AdultMinAge ? "어린 외형" : null });
        return items;
    }

    /// <summary>후보로 보일 파츠인지. 꾸미기 숨김 행, 현재 성별·나이에 모델이 없는 행, 최소 나이 미달 행은 뺀다.</summary>
    bool Usable(AppearancePartData p) =>
        p != null && !p.HideOnCustomize && p.Model(Male, Infant) != null && (state.appearance.age <= 0 || p.MinAge <= state.appearance.age);

    /// <summary>현재 선택은 후보 조건에 맞지 않아도 목록에 남긴다(선택 보존, 흐리게).</summary>
    List<DropdownItem> HeadItems()
    {
        var items = new List<DropdownItem>();
        foreach (var h in data.Heads.Values)
        {
            bool ok = Usable(h);
            if (ok || h.Id == state.appearance.head) items.Add(new DropdownItem { Id = h.Id, Name = h.DisplayName ?? h.Id, Sub = h.Id, Dim = !ok });
        }
        return items;
    }

    List<DropdownItem> GroupItems(Dictionary<string, Dictionary<string, AppearancePartData>> map, string current, Func<string> variant, string fallback)
    {
        var items = new List<DropdownItem>();
        string v = variant();
        foreach (var kv in map)
        {
            var p = GameData.Resolve(map, kv.Key, v, fallback);
            bool ok = Usable(p);
            if (ok || kv.Key == current) items.Add(new DropdownItem { Id = kv.Key, Name = p?.DisplayName ?? kv.Key, Sub = kv.Key, Dim = !ok });
        }
        return items;
    }

    /// <summary>선택한 외형 파츠를 지금 외형으로 표시할 수 없는 이유. 문제 없으면 null.</summary>
    string PartIssue(AppearancePartData p, string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (p == null) return "데이터에 없는 ID입니다.";
        if (p.Model(Male, Infant) == null) return "현재 성별·나이에 맞는 모델이 없어 표시하지 않습니다. 선택은 유지합니다.";
        return null;
    }

    string HeadIssue() => PartIssue(data.Heads.TryGetValue(state.appearance.head ?? "", out var h) ? h : null, state.appearance.head);

    string GroupIssue(Dictionary<string, Dictionary<string, AppearancePartData>> map, string id, string variant, string fallback) =>
        PartIssue(GameData.Resolve(map, id, variant, fallback), id);

    List<DropdownItem> CostumeItems(string part)
    {
        var items = new List<DropdownItem> { new DropdownItem { Id = null, Name = "(착용 안 함)" } };
        foreach (var c in data.CostumeList)
        {
            if (c.CostumeType != part) continue;
            bool fits = PlanResolver.CheckCostume(c, Male, Infant, out _, out _) == CostumeStatus.Applied;
            items.Add(new DropdownItem { Id = c.Id, Name = string.IsNullOrEmpty(c.DisplayName) ? c.Id : c.DisplayName, Sub = c.Id, Dim = !fits });
        }
        return items;
    }
}
