// 꾸미기 창 (WindowAndCustomizationUI.md, Appearance.md, Equipment.md)
//   앱 안의 UI 패널. 제목 영역을 끌어 옮기고 오른쪽 아래 핸들로 크기를 바꾼다.
//   위치·크기는 앱 설정에 저장해 다음 실행에 복원하고, 앱 화면 밖으로 나가지 않게 보정한다.
//   큰 카테고리는 폴드아웃, 세부 항목은 검색 가능한 드롭다운이다. 고르면 바로 모델에 반영한다(적용 버튼 없음).
//   선택 상태와 실제 출력을 분리한다. 입을 수 없는 의상은 선택을 유지하고 빨간색과 사유를 표시한다.
//
// 색(사용자 결정 2026-11-02):
//   피부·헤어·눈 색은 게임 색 시트(CharacterSkinColor·HairColor·EyeColor)의 행을 검색 드롭다운으로 고른다.
//   꾸미기 숨김 행은 후보에서 빼고, 현재 선택이면 흐리게 남긴다.
//   의상 염색은 표시 중인 의상의 실제 염색 파트(모델 DyeInfo 에 연결이 있는 Slot0~5)만 견본으로 보이고,
//   견본을 누르면 컬러피커(ColorPicker)를 연다. 색만 바뀌는 편집은 다시 조립하지 않는다(editColors).
//
// 배치(사용자 결정 2026-11-02): 앱 창이 가로로 넓으면 오른쪽 세로 패널, 세로로 길면 아래쪽 가로 패널이 기본이다.
//   가로·세로 배치를 따로 저장하므로 앱 창 비율이 바뀌면 그 비율의 마지막 배치로 바뀐다.
// 보정 정책(구현 선택): 저장하는 값은 사용자가 마지막으로 옮기거나 크기를 바꾼 배치다.
//   앱 창이 작아져서 보정한 배치는 저장하지 않으므로, 앱 창을 다시 키우면 원래 배치로 돌아간다.
// 크기 조절 손잡이는 스크롤 영역 아래의 하단 줄에 둔다(Windows 상태 표시줄의 크기 조절 손잡이와 같은 방식).
//   스크롤바가 손잡이 위에서 끝나므로 서로 가리지 않는다.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class CustomizePanel
{
    const float DefaultWidth = 340, Margin = 12, MinWidth = 240, MinHeight = 160, FooterHeight = 16;

    /// <summary>폴드아웃 키 → 제목, 처음 펼침 여부(설정에 저장된 값이 없을 때). 카테고리는 사용자 결정(2026-11-02).</summary>
    static readonly (string key, string title, bool open)[] Sections =
    {
        ("appearance", "외형", true),
        ("costume", "의상", true),
    };

    /// <summary>Costume.CostumeType → 표시 이름. 순서는 사용자 결정(2026-11-02).</summary>
    static readonly (string part, string label)[] CostumeParts =
    {
        ("Hat", "모자"),
        ("Top", "상의"),
        ("Bottom", "하의"),
        ("Robe", "로브"),
        ("Gloves", "장갑"),
        ("Shoes", "신발"),
        ("EarDeco", "귀장식"),
        ("EyeDeco", "눈장식"),
        ("FaceDeco", "얼굴장식"),
        ("Weapon", "무기"),
    };

    readonly VisualElement host, window, title, popupLayer;
    readonly ScrollView scroll;
    readonly Label error;
    readonly AppSettings settings;
    readonly Action saveSettings;
    readonly Action<Action<EditState>> edit, editColors;
    readonly Func<CharacterView> view;
    readonly Dictionary<string, Foldout> foldouts = new Dictionary<string, Foldout>();
    readonly List<Action> refreshers = new List<Action>();

    GameData data;
    EditState state;
    AssemblyPlan plan;
    /// <summary>가로로 넓은 창·세로로 긴 창의 마지막 배치. 너비 0 이면 기본 배치.</summary>
    Rect desiredWide, desiredTall;
    bool tall;
    Rect shown;

    /// <param name="host">화면 전체 크기의 층(AppUI.WindowLayer)</param>
    /// <param name="edit">장면 상태를 바꾸는 요청. 복사본을 바꿔 조립에 성공하면 반영한다(AppController.Edit).</param>
    /// <param name="editColors">색만 바꾸는 요청. 다시 조립하지 않고 머티리얼 색만 바꾼다(AppController.EditColors).</param>
    /// <param name="view">지금 조립된 캐릭터(염색 파트 조회·미리보기)</param>
    public CustomizePanel(VisualElement host, AppSettings settings, Action saveSettings, Action<Action<EditState>> edit,
        Action<Action<EditState>> editColors, Func<CharacterView> view)
    {
        this.host = host;
        this.settings = settings;
        this.saveSettings = saveSettings;
        // 다른 편집을 시작하면 열려 있던 컬러피커는 지금 색으로 적용하고 닫는다.
        this.edit = change => { ColorPicker.ApplyAny(); edit(change); };
        this.editColors = change => { ColorPicker.ApplyAny(); editColors(change); };
        this.view = view;

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

        // 하단 줄: 스크롤 영역이 여기서 끝나므로 세로 스크롤바가 손잡이를 덮지 않는다.
        var footer = new VisualElement { name = "customize-footer" };
        footer.style.flexDirection = FlexDirection.Row;
        footer.style.justifyContent = Justify.FlexEnd;
        footer.style.height = FooterHeight;
        footer.style.flexShrink = 0;
        footer.style.backgroundColor = AppUI.HeaderBg;
        var grip = new Label("◢") { name = "customize-resize" };
        grip.style.width = 18; grip.style.height = FooterHeight;
        grip.style.fontSize = 12;
        grip.style.color = AppUI.Sub;
        grip.style.unityTextAlign = TextAnchor.LowerRight;
        grip.style.paddingRight = 2;
        grip.RegisterCallback<PointerEnterEvent>(e => grip.style.color = AppUI.Text);
        grip.RegisterCallback<PointerLeaveEvent>(e => grip.style.color = AppUI.Sub);
        footer.Add(grip);
        window.Add(footer);

        host.Add(window);

        // 드롭다운 목록은 꾸미기 창 밖으로 나갈 수 있도록 화면 전체 층에 띄운다.
        popupLayer = new VisualElement { name = "popup-layer", pickingMode = PickingMode.Ignore };
        popupLayer.style.position = Position.Absolute;
        popupLayer.style.left = 0; popupLayer.style.top = 0; popupLayer.style.right = 0; popupLayer.style.bottom = 0;
        host.Add(popupLayer);

        DragHandler(title, (r, d) => new Rect(r.x + d.x, r.y + d.y, r.width, r.height));
        DragHandler(grip, (r, d) => new Rect(r.x, r.y, r.width + d.x, r.height + d.y));
        host.RegisterCallback<GeometryChangedEvent>(e => Layout());

        desiredWide = ToRect(settings.customizePanel);
        desiredTall = ToRect(settings.customizePanelTall);
        Layout();
    }

    // ------------------------------------------------------------ 배치

    static Rect ToRect(PanelLayout l) =>
        l != null && l.valid && l.width > 0 && l.height > 0 ? new Rect(l.x, l.y, l.width, l.height) : Rect.zero;

    void Layout()
    {
        float w = host.layout.width, h = host.layout.height;
        if (float.IsNaN(w) || float.IsNaN(h) || w <= 0 || h <= 0) return;
        tall = h > w;
        var desired = tall ? desiredTall : desiredWide;
        if (desired.width <= 0) desired = DefaultRect(w, h, tall);
        Show(Clamp(desired, w, h));
    }

    /// <summary>가로로 넓은 창: 오른쪽, 폭 340, 높이 전체. 세로로 긴 창: 아래쪽, 폭 전체, 높이 화면의 42%.</summary>
    static Rect DefaultRect(float w, float h, bool tall)
    {
        if (tall)
        {
            float dh = Mathf.Clamp(h * 0.42f, Mathf.Min(220, h - Margin * 2), h - Margin * 2);
            return new Rect(Margin, h - dh - Margin, w - Margin * 2, dh);
        }
        float dw = Mathf.Min(DefaultWidth, w - Margin * 2);
        return new Rect(w - dw - Margin, Margin, dw, h - Margin * 2);
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
            ColorPicker.ApplyAny();
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
            var saved = new PanelLayout { valid = true, x = shown.x, y = shown.y, width = shown.width, height = shown.height };
            if (tall) { desiredTall = shown; settings.customizePanelTall = saved; }
            else { desiredWide = shown; settings.customizePanel = saved; }
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

        var look = Section("appearance");
        look.Add(Row("성별", Choice(GenderItems, () => state.appearance.gender, Gender, (s, id) => s.appearance.gender = id), null));
        look.Add(Row("체형", Choice(BodyTypeItems, () => state.appearance.bodyType, BodyTypeName, (s, id) => s.appearance.bodyType = id), null));
        look.Add(Row("나이", Choice(AgeItems, () => state.appearance.age.ToString(), id => id + "세",
            (s, id) => s.appearance.age = int.Parse(id)), null));
        ColorRow(look, "피부색", () => data.SkinColors, () => state.appearance.skinColor, new[] { "Skin" }, null,
            (s, id) => s.appearance.skinColor = id);

        AppearanceRow(look, "얼굴형", HeadItems, () => state.appearance.head, HeadIssue, (s, id) => s.appearance.head = id,
            id => data.Heads.TryGetValue(id ?? "", out var h) ? h.DisplayName : null);
        AppearanceRow(look, "헤어스타일", () => GroupItems(data.Hairs, state.appearance.hair, HairVariant, "Default"),
            () => state.appearance.hair, () => GroupIssue(data.Hairs, state.appearance.hair, HairVariant(), "Default"),
            (s, id) => s.appearance.hair = id, id => GameData.Resolve(data.Hairs, id, HairVariant())?.DisplayName);
        ColorRow(look, "헤어 색상", () => data.HairColors, () => state.appearance.hairColor, new[] { "Hair", "Hair1" }, HairColorIssue,
            (s, id) => s.appearance.hairColor = id);
        AppearanceRow(look, "눈 모양", () => GroupItems(data.Eyes, state.appearance.eyes, HeadVariant, "Head_00"),
            () => state.appearance.eyes, () => GroupIssue(data.Eyes, state.appearance.eyes, HeadVariant(), "Head_00"),
            (s, id) => s.appearance.eyes = id, id => GameData.Resolve(data.Eyes, id, HeadVariant(), "Head_00")?.DisplayName);
        ColorRow(look, "눈 색상", () => data.EyeColors, () => state.appearance.eyeColor, new[] { "Eye" }, null,
            (s, id) => s.appearance.eyeColor = id);
        AppearanceRow(look, "입 모양", () => GroupItems(data.Mouths, state.appearance.mouth, HeadVariant, "Head_00"),
            () => state.appearance.mouth, () => GroupIssue(data.Mouths, state.appearance.mouth, HeadVariant(), "Head_00"),
            (s, id) => s.appearance.mouth = id, id => GameData.Resolve(data.Mouths, id, HeadVariant(), "Head_00")?.DisplayName);

        var wear = Section("costume");
        foreach (var (part, label) in CostumeParts) CostumeRow(wear, part, label);

        Refresh(state, plan);
    }

    /// <summary>장면이 바뀐 뒤 선택 필드·상태 표시를 다시 그린다.</summary>
    public void Refresh(EditState current, AssemblyPlan currentPlan)
    {
        // 열린 피커는 이전 장면의 파트를 가리킨다. 적용은 편집 전에 이미 했으므로 남은 것은 되돌려 닫는다.
        ColorPicker.CancelAny();
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

    VisualElement Section(string key)
    {
        if (foldouts.TryGetValue(key, out var existing)) return existing.contentContainer;
        int i = Array.FindIndex(Sections, s => s.key == key);
        var (_, titleText, open) = Sections[i];
        if (SavedSectionsValid()) open = settings.openSections.Contains(key);
        var f = new Foldout { text = titleText, value = open };
        f.style.marginTop = 6;
        var toggleLabel = f.Q<Toggle>()?.Q<Label>();
        if (toggleLabel != null) { toggleLabel.style.color = AppUI.Text; toggleLabel.style.fontSize = 14; toggleLabel.style.unityFontStyleAndWeight = FontStyle.Bold; }
        // 기본 테마의 펼침 화살표는 어두운 배경에서 거의 보이지 않는다.
        var arrow = f.Q(className: Foldout.checkmarkUssClassName);
        if (arrow != null) arrow.style.unityBackgroundImageTintColor = AppUI.Sub;
        // 항목 줄은 폴드아웃 제목 글자와 같은 위치에서 시작한다(화살표만큼 들여쓰기).
        f.contentContainer.style.marginLeft = 0;
        f.contentContainer.style.marginBottom = 4;
        if (toggleLabel != null)
            toggleLabel.RegisterCallback<GeometryChangedEvent>(e =>
            {
                float indent = toggleLabel.worldBound.x - f.worldBound.x;
                if (indent > 0 && !float.IsNaN(indent)) f.contentContainer.style.paddingLeft = indent;
            });
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

    /// <summary>저장된 펼침 상태가 지금 카테고리 키를 하나라도 담고 있는지(예전 키만 있으면 기본값을 쓴다).</summary>
    bool SavedSectionsValid()
    {
        if (!settings.sectionsSaved || settings.openSections == null) return false;
        if (settings.openSections.Count == 0) return true;
        foreach (var k in settings.openSections) if (Array.FindIndex(Sections, x => x.key == k) >= 0) return true;
        return false;
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
        var dyes = new VisualElement { name = "dye-parts" };
        dyes.style.flexDirection = FlexDirection.Row;
        dyes.style.flexWrap = Wrap.Wrap;
        dyes.style.alignItems = Align.Center;
        dyes.style.marginLeft = 76; dyes.style.marginTop = 3;
        dyes.style.display = DisplayStyle.None;
        var dd = Dropdown(() => CostumeItems(part), it => edit(s => SetCostume(s, part, it.Id)));
        refreshers.Add(() =>
        {
            var slot = state.Costume(part);
            var res = plan?.Costumes.Find(c => c.Part == part);
            RefreshDyes(dyes, part, label, slot, res);
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
        var box = Row(label, dd, status);
        box.Add(dyes);
        parent.Add(box);
    }

    // ------------------------------------------------------------ 의상 염색

    static readonly Dictionary<string, string> DyeMaterialNames = new Dictionary<string, string>
    {
        { "Cloth", "천" }, { "Leather", "가죽" }, { "Metal", "금속" }, { "Wood", "나무" },
    };

    /// <summary>
    /// 표시 중인 의상의 염색 파트 견본을 다시 만든다. 가려졌거나 적용되지 않은 의상은 파트를 알 수 없으므로 비운다
    /// (저장된 염색값은 그대로 둔다). 파트 이름은 데이터에 없으므로 "파트 n"과 DyeInfo 재질로만 표시한다(Dye.md).
    /// </summary>
    void RefreshDyes(VisualElement box, string part, string label, CostumeSlotState slot, CostumeResult res)
    {
        box.Clear();
        box.style.display = DisplayStyle.None;
        if (slot == null || res == null || res.Status != CostumeStatus.Applied) return;
        box.style.display = DisplayStyle.Flex;
        CostumeData row = null;
        if (slot.kind == CostumeSlotState.KindCostume) data.Costumes.TryGetValue(slot.costumeId ?? "", out row);
        if (row != null && row.DyePolicy == "AlwaysCantDye") { box.Add(Note("염색할 수 없는 의상입니다.")); return; }
        string key = AssemblyPlan.CostumeKey(part);
        var v = view();
        var parts = v ? v.DyeParts(key) : new List<DyePart>();
        if (parts.Count == 0) { box.Add(Note("염색 파트가 없습니다.")); return; }
        foreach (var dp in parts) box.Add(DyeChip(part, label, key, slot, dp));
    }

    static Label Note(string text)
    {
        var l = new Label(text);
        l.style.fontSize = 11;
        l.style.color = AppUI.Sub;
        return l;
    }

    /// <summary>염색 파트 하나의 견본 버튼. 사용자 지정 색이 있는 파트는 테두리를 밝게 한다.</summary>
    VisualElement DyeChip(string part, string label, string key, CostumeSlotState slot, DyePart dp)
    {
        bool user = ColorOverrides.Get(slot.dyes, dp.Slot) != null;
        string mat = dp.DyeMaterial != null && DyeMaterialNames.TryGetValue(dp.DyeMaterial, out var m) ? m : null;
        string name = "파트 " + (dp.Number + 1);

        var chip = new VisualElement { name = "dye-" + dp.Slot, focusable = true };
        chip.style.flexDirection = FlexDirection.Row;
        chip.style.alignItems = Align.Center;
        chip.style.height = 24;
        chip.style.paddingLeft = 4; chip.style.paddingRight = 6;
        chip.style.marginRight = 4; chip.style.marginBottom = 3;
        chip.style.backgroundColor = AppUI.FieldBg;
        SearchDropdown.SetBorder(chip, user ? AppUI.Text : AppUI.Border, 1, 3);
        chip.Add(SearchDropdown.Swatch(dp.Current, 16));
        var l = new Label((dp.Number + 1).ToString());
        l.pickingMode = PickingMode.Ignore;
        l.style.fontSize = 11;
        l.style.color = AppUI.Text;
        l.style.marginLeft = 2;
        chip.Add(l);
        chip.tooltip = $"{name}{(mat != null ? " · " + mat : "")} · {ColorOverrides.ToHex(dp.Current)}{(user ? " · 사용자 지정" : "")}";
        chip.RegisterCallback<PointerEnterEvent>(e => chip.style.backgroundColor = AppUI.FieldHover);
        chip.RegisterCallback<PointerLeaveEvent>(e => chip.style.backgroundColor = AppUI.FieldBg);

        void Open()
        {
            // 피커는 꾸미기 창 바깥(왼쪽 → 오른쪽 → 위 → 아래)에 띄워 창을 가리지 않게 한다. 세로 위치는 견본에 맞춘다.
            var wb = window.worldBound; var cb = chip.worldBound;
            // 넓은 앱 창: 창 왼쪽 가장자리(세로 선) → 피커는 왼쪽. 세로로 긴 앱 창: 창 위쪽 가장자리(가로 선) → 피커는 위.
            var anchor = tall ? new Rect(wb.xMin, wb.yMin, wb.width, 1) : new Rect(wb.xMin, cb.yMin, 1, cb.height);
            ColorPicker.Open(popupLayer, anchor, $"{label} · {name}{(mat != null ? " (" + mat + ")" : "")}", dp.Current,
                c => { var cv = view(); if (cv) cv.SetColor(key, dp.Slot, c); },
                c => editColors(s => SetDye(s, part, dp.Slot, ColorOverrides.ToHex(c))),
                () => { var cv = view(); if (cv && plan != null) cv.ApplyPlanColors(plan); },
                user ? () => editColors(s => SetDye(s, part, dp.Slot, null)) : (Action)null);
        }
        chip.RegisterCallback<ClickEvent>(e => Open());
        chip.RegisterCallback<KeyDownEvent>(e =>
        {
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Space) { Open(); e.StopPropagation(); }
        });
        return chip;
    }

    static void SetDye(EditState s, string part, string slot, string hex)
    {
        var c = s.Costume(part);
        if (c != null) ColorOverrides.Set(c.dyes, slot, hex);
    }

    // ------------------------------------------------------------ 외형 색

    /// <summary>
    /// 색 시트 행을 고르는 줄. 고르면 해당 외형 색 슬롯의 사용자 지정 색(예전 JSON 에 있을 수 있음)을 지운다.
    /// </summary>
    void ColorRow(VisualElement parent, string label, Func<Dictionary<string, ColorData>> map, Func<string> current, string[] slots,
        Func<ColorData, string> mismatch, Action<EditState, string> set)
    {
        var status = StatusLabel();
        var dd = Dropdown(() => ColorItems(map(), current(), mismatch), it => editColors(s =>
        {
            set(s, it.Id);
            foreach (var sl in slots) ColorOverrides.Set(s.appearance.colors, sl, null);
        }));
        refreshers.Add(() =>
        {
            string id = current();
            map().TryGetValue(id ?? "", out var c);
            string userHex = null;
            foreach (var sl in slots) userHex = userHex ?? ColorOverrides.Get(state.appearance.colors, sl);
            if (userHex != null && ColorOverrides.TryParse(userHex, out var uc))
            {
                // 현재 ID 를 비워 두어 같은 시트 색을 다시 골라도 사용자 색을 지울 수 있게 한다.
                dd.SetValue(null, "사용자 지정 색", userHex, null, new[] { uc });
                SetStatus(status, null, AppUI.Sub);
                return;
            }
            string problem = c == null ? (string.IsNullOrEmpty(id) ? null : "데이터에 없는 색 ID입니다.") : mismatch?.Invoke(c);
            dd.SetValue(id, c != null ? ColorName(c) : id ?? "(없음)", c != null ? ColorHex(c) : null, null, c != null ? Swatches(c) : null);
            SetStatus(status, problem, AppUI.WarnColor);
        });
        parent.Add(Row(label, dd, status));
    }

    static string ColorName(ColorData c) => string.IsNullOrEmpty(c.DisplayName) ? c.Id : c.DisplayName;

    static Color[] Swatches(ColorData c)
    {
        var list = new List<Color>();
        if (c.Color1.HasValue) list.Add(PlanResolver.ToColor(c.Color1.Value));
        if (c.Type == "TwoColor" && c.Color2.HasValue) list.Add(PlanResolver.ToColor(c.Color2.Value));
        return list.ToArray();
    }

    static string ColorHex(ColorData c)
    {
        var sw = Swatches(c);
        return sw.Length == 0 ? c.Id : string.Join(" ", Array.ConvertAll(sw, ColorOverrides.ToHex));
    }

    /// <summary>꾸미기 숨김 행은 빼고, 현재 선택이면 흐리게 남긴다. 보조 표시에 HEX 를 써서 코드로도 검색된다.</summary>
    List<DropdownItem> ColorItems(Dictionary<string, ColorData> map, string current, Func<ColorData, string> mismatch)
    {
        var items = new List<DropdownItem>();
        foreach (var c in map.Values)
        {
            if (c.HideOnCustomize && c.Id != current) continue;
            string why = mismatch?.Invoke(c);
            items.Add(new DropdownItem
            {
                Id = c.Id, Name = ColorName(c), Sub = why != null ? why + " " + ColorHex(c) : ColorHex(c),
                Dim = c.HideOnCustomize || why != null, Swatches = Swatches(c),
            });
        }
        return items;
    }

    /// <summary>투톤 색을 한 가지 색 헤어에 고르면 첫 번째 색만 보인다.</summary>
    string HairColorIssue(ColorData c)
    {
        if (c.Type != "TwoColor") return null;
        var hair = GameData.Resolve(data.Hairs, state.appearance.hair, HairVariant());
        return hair != null && hair.HairType != "TwoColor" ? "투톤 헤어용(첫 색만 보임)" : null;
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
