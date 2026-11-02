// 컬러피커 창 (Dye.md "컬러피커와 HEX 코드 입력")
//   색상(H) 막대 + 채도·명도(SV) 사각형 + #RRGGBB 입력. 두 입력은 같은 값을 편집하고 서로 맞춘다.
//   고르는 동안은 미리보기만 한다(머티리얼 값만 바꿈). [적용]으로 장면 상태에 넣고, [취소]·ESC 는 원래 색으로 돌린다.
//   창은 모달이 아니다. 열어 둔 채 카메라를 돌려 색을 확인할 수 있다. 제목 영역을 끌어 옮긴다.
//   다른 편집(드롭다운 선택, 다른 파트의 피커 열기)이 시작되면 열려 있던 피커는 지금 색으로 적용하고 닫는다.
//   잘못되거나 덜 입력된 코드는 색을 바꾸지 않고 오류를 표시한다.
using System;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class ColorPicker
{
    const float Width = 252, SvHeight = 150, HueHeight = 14, Pad = 10, Gap = 8;

    static ColorPicker active;
    static Texture2D hueTex, whiteTex, blackTex;

    readonly VisualElement host, window, sv, svCursor, hue, hueCursor, newSwatch;
    readonly TextField hex;
    readonly Label error;
    readonly Color initial;
    readonly Action<Color> preview, apply;
    readonly Action cancel;
    Color current;
    float h, s, v;

    public static bool IsOpen => active != null;

    /// <summary>열려 있는 피커를 지금 색으로 적용하고 닫는다. 바뀐 것이 없으면 그냥 닫는다.</summary>
    public static void ApplyAny() => active?.Finish(true);

    /// <summary>열려 있는 피커를 원래 색으로 돌리고 닫는다.</summary>
    public static void CancelAny() => active?.Finish(false);

    /// <param name="host">화면 전체 크기의 층</param>
    /// <param name="anchor">피커를 연 요소의 화면 위치(world). 이 옆에 띄운다.</param>
    /// <param name="preview">색이 바뀔 때마다 부른다(장면 상태는 바꾸지 않는 미리보기)</param>
    /// <param name="apply">[적용]. 처음 색과 같으면 부르지 않는다.</param>
    /// <param name="cancel">[취소]·ESC. 미리보기를 되돌린다.</param>
    /// <param name="reset">[기본색]. null 이면 버튼을 숨긴다.</param>
    public static void Open(VisualElement host, Rect anchor, string title, Color initial,
        Action<Color> preview, Action<Color> apply, Action cancel, Action reset)
    {
        ApplyAny();
        SearchDropdown.CloseAny();
        active = new ColorPicker(host, anchor, title, initial, preview, apply, cancel, reset);
    }

    ColorPicker(VisualElement host, Rect anchor, string title, Color initial,
        Action<Color> preview, Action<Color> apply, Action cancel, Action reset)
    {
        this.host = host;
        this.initial = initial;
        this.preview = preview;
        this.apply = apply;
        this.cancel = cancel;
        EnsureTextures();

        window = new VisualElement { name = "color-picker" };
        window.style.position = Position.Absolute;
        window.style.width = Width;
        window.style.backgroundColor = AppUI.PanelBg;
        SearchDropdown.SetBorder(window, AppUI.Border, 1, 6);
        window.style.overflow = Overflow.Hidden;

        // 제목(끌어 옮기기)
        var bar = new VisualElement();
        bar.style.flexDirection = FlexDirection.Row;
        bar.style.alignItems = Align.Center;
        bar.style.height = 28;
        bar.style.paddingLeft = Pad; bar.style.paddingRight = 4;
        bar.style.backgroundColor = AppUI.HeaderBg;
        var t = new Label(title);
        t.pickingMode = PickingMode.Ignore;
        t.style.flexGrow = 1;
        t.style.color = AppUI.Text;
        t.style.fontSize = 12;
        t.style.unityFontStyleAndWeight = FontStyle.Bold;
        t.style.overflow = Overflow.Hidden;
        t.style.textOverflow = TextOverflow.Ellipsis;
        t.style.whiteSpace = WhiteSpace.NoWrap;
        bar.Add(t);
        window.Add(bar);
        DragMove(bar);

        var body = new VisualElement();
        body.style.paddingLeft = Pad; body.style.paddingRight = Pad; body.style.paddingTop = Pad; body.style.paddingBottom = Pad;
        window.Add(body);

        // 채도·명도 사각형: 바탕 = 색상, 위에 흰색(가로)·검은색(세로) 그라데이션을 겹친다.
        float inner = Width - Pad * 2 - 2;
        sv = new VisualElement { name = "sv" };
        sv.style.width = inner; sv.style.height = SvHeight;
        SearchDropdown.SetBorder(sv, AppUI.Border, 1, 0);
        var white = Layer(whiteTex); var black = Layer(blackTex);
        sv.Add(white); sv.Add(black);
        svCursor = Cursor(12);
        sv.Add(svCursor);
        body.Add(sv);
        PointerArea(sv, p =>
        {
            s = Mathf.Clamp01(p.x / sv.layout.width);
            v = 1 - Mathf.Clamp01(p.y / sv.layout.height);
            FromHsv();
        });

        hue = new VisualElement { name = "hue" };
        hue.style.width = inner; hue.style.height = HueHeight;
        hue.style.marginTop = Gap;
        hue.style.backgroundImage = new StyleBackground(hueTex);
        hue.style.backgroundSize = new BackgroundSize(new Length(100, LengthUnit.Percent), new Length(100, LengthUnit.Percent));
        SearchDropdown.SetBorder(hue, AppUI.Border, 1, 0);
        hueCursor = new VisualElement { pickingMode = PickingMode.Ignore };
        hueCursor.style.position = Position.Absolute;
        hueCursor.style.width = 4; hueCursor.style.top = -2; hueCursor.style.bottom = -2;
        SearchDropdown.SetBorder(hueCursor, Color.white, 1, 1);
        hue.Add(hueCursor);
        body.Add(hue);
        PointerArea(hue, p =>
        {
            h = Mathf.Clamp01(p.x / hue.layout.width) * 0.9999f;
            FromHsv();
        });

        // 이전·새 색, HEX
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginTop = Gap;
        var oldSwatch = SearchDropdown.Swatch(initial, 24);
        oldSwatch.tooltip = "이전 색";
        newSwatch = SearchDropdown.Swatch(initial, 24);
        newSwatch.tooltip = "새 색";
        newSwatch.style.marginRight = 8;
        row.Add(oldSwatch); row.Add(newSwatch);
        hex = new TextField { maxLength = 7 };
        hex.style.flexGrow = 1;
        hex.style.marginLeft = 0; hex.style.marginRight = 0;
        hex.textEdition.placeholder = "#RRGGBB";
        var input = hex.Q(className: TextField.inputUssClassName);
        if (input != null)
        {
            input.style.backgroundColor = AppUI.FieldBg;
            input.style.color = AppUI.Text;
            SearchDropdown.SetBorder(input, AppUI.Border, 1, 3);
            var text = input.Q<TextElement>();
            if (text != null) text.style.color = AppUI.Text;
        }
        hex.RegisterValueChangedCallback(e => { e.StopPropagation(); OnHexTyped(e.newValue, false); });
        hex.RegisterCallback<FocusOutEvent>(e => OnHexTyped(hex.value, true));
        hex.RegisterCallback<KeyDownEvent>(e =>
        {
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { OnHexTyped(hex.value, true); e.StopPropagation(); }
        }, TrickleDown.TrickleDown);
        row.Add(hex);
        body.Add(row);

        error = new Label();
        error.style.color = AppUI.ErrorColor;
        error.style.fontSize = 11;
        error.style.whiteSpace = WhiteSpace.Normal;
        error.style.marginTop = 4;
        error.style.display = DisplayStyle.None;
        body.Add(error);

        var buttons = new VisualElement();
        buttons.style.flexDirection = FlexDirection.Row;
        buttons.style.marginTop = Gap;
        if (reset != null)
        {
            var r = Button("기본색", () => { Finish(false); reset(); });
            r.tooltip = "사용자 지정 색을 지우고 원래 색으로 되돌립니다.";
            buttons.Add(r);
        }
        var spacer = new VisualElement();
        spacer.style.flexGrow = 1;
        buttons.Add(spacer);
        buttons.Add(Button("취소", () => Finish(false)));
        var ok = Button("적용", () => Finish(true));
        ok.style.backgroundColor = new Color(0.25f, 0.42f, 0.7f);
        buttons.Add(ok);
        body.Add(buttons);

        window.RegisterCallback<KeyDownEvent>(e =>
        {
            if (e.keyCode != KeyCode.Escape) return;
            Finish(false);
            e.StopPropagation();
        }, TrickleDown.TrickleDown);

        host.Add(window);
        window.RegisterCallback<GeometryChangedEvent>(Place);
        placeAnchor = anchor;

        SetFromColor(initial, true);
    }

    Rect placeAnchor;
    bool placed;

    /// <summary>
    /// 처음 크기가 정해지면 기준 영역 바깥에 둔다. 기본은 왼쪽 → 오른쪽 → 위 → 아래,
    /// 기준 영역이 가로로 넓으면(세로로 긴 앱 창의 아래쪽 꾸미기 창) 위 → 아래 → 왼쪽 → 오른쪽 순으로 들어가는 곳을 고른다.
    /// </summary>
    void Place(GeometryChangedEvent e)
    {
        if (placed) return;
        float w = window.layout.width, hh = window.layout.height;
        float hw = host.layout.width, hhh = host.layout.height;
        if (float.IsNaN(w) || float.IsNaN(hh) || hh <= 0) return;
        placed = true;
        var a = new Rect(host.WorldToLocal(placeAnchor.position), placeAnchor.size);
        var left = (ok: a.xMin - w - 8 >= 4, p: new Vector2(a.xMin - w - 8, a.center.y - hh * 0.5f));
        var right = (ok: a.xMax + 8 + w <= hw - 4, p: new Vector2(a.xMax + 8, a.center.y - hh * 0.5f));
        var above = (ok: a.yMin - hh - 8 >= 4, p: new Vector2(a.center.x - w * 0.5f, a.yMin - hh - 8));
        var below = (ok: a.yMax + 8 + hh <= hhh - 4, p: new Vector2(a.center.x - w * 0.5f, a.yMax + 8));
        var order = a.width > a.height ? new[] { above, below, left, right } : new[] { left, right, above, below };
        Vector2 pos = order[0].p;
        foreach (var o in order) if (o.ok) { pos = o.p; break; }
        MoveTo(pos);
    }

    void MoveTo(Vector2 p)
    {
        float w = window.layout.width, hh = window.layout.height;
        float hw = host.layout.width, hhh = host.layout.height;
        window.style.left = Mathf.Clamp(p.x, 0, Mathf.Max(0, hw - w));
        window.style.top = Mathf.Clamp(p.y, 0, Mathf.Max(0, hhh - hh));
    }

    void DragMove(VisualElement bar)
    {
        Vector2 start = default, from = default;
        int pointer = -1;
        bar.RegisterCallback<PointerDownEvent>(e =>
        {
            if (e.button != 0) return;
            pointer = e.pointerId;
            start = e.position;
            from = new Vector2(window.layout.x, window.layout.y);
            bar.CapturePointer(pointer);
            e.StopPropagation();
        });
        bar.RegisterCallback<PointerMoveEvent>(e =>
        {
            if (e.pointerId != pointer || !bar.HasPointerCapture(pointer)) return;
            MoveTo(from + ((Vector2)e.position - start));
        });
        bar.RegisterCallback<PointerUpEvent>(e =>
        {
            if (e.pointerId != pointer) return;
            if (bar.HasPointerCapture(pointer)) bar.ReleasePointer(pointer);
            pointer = -1;
        });
    }

    /// <summary>누르거나 끄는 동안 영역 안 좌표를 넘긴다.</summary>
    static void PointerArea(VisualElement area, Action<Vector2> onPoint)
    {
        int pointer = -1;
        area.RegisterCallback<PointerDownEvent>(e =>
        {
            if (e.button != 0) return;
            pointer = e.pointerId;
            area.CapturePointer(pointer);
            onPoint(area.WorldToLocal(e.position));
            e.StopPropagation();
        });
        area.RegisterCallback<PointerMoveEvent>(e =>
        {
            if (e.pointerId != pointer || !area.HasPointerCapture(pointer)) return;
            onPoint(area.WorldToLocal(e.position));
        });
        area.RegisterCallback<PointerUpEvent>(e =>
        {
            if (e.pointerId != pointer) return;
            if (area.HasPointerCapture(pointer)) area.ReleasePointer(pointer);
            pointer = -1;
        });
    }

    // ------------------------------------------------------------ 값

    void FromHsv()
    {
        var c = Color.HSVToRGB(h, s, v);
        c.a = 1;
        SetFromColor(c, false);
    }

    /// <summary>색을 바꾸고 표시를 맞춘다. fromOutside 면 HSV 도 색에서 다시 구한다(회색이면 색상 값은 유지).</summary>
    void SetFromColor(Color c, bool fromOutside, bool updateHex = true)
    {
        c.a = 1;
        current = c;
        if (fromOutside)
        {
            Color.RGBToHSV(c, out float nh, out float ns, out float nv);
            if (ns > 0.0001f && nv > 0.0001f) h = nh;
            if (nv > 0.0001f) s = ns;
            v = nv;
        }
        sv.style.backgroundColor = Color.HSVToRGB(h, 1, 1);
        newSwatch.style.backgroundColor = c;
        PositionCursors();
        if (updateHex) hex.SetValueWithoutNotify(ColorOverrides.ToHex(c));
        ShowError(null);
        preview?.Invoke(c);
    }

    void PositionCursors()
    {
        float w = sv.resolvedStyle.width, ht = sv.resolvedStyle.height;
        if (float.IsNaN(w) || w <= 0) { w = Width - Pad * 2 - 2; ht = SvHeight; }
        svCursor.style.left = s * w - 6;
        svCursor.style.top = (1 - v) * ht - 6;
        svCursor.style.borderLeftColor = svCursor.style.borderRightColor = svCursor.style.borderTopColor = svCursor.style.borderBottomColor =
            v > 0.6f && s < 0.4f ? Color.black : Color.white;
        float hw = hue.resolvedStyle.width;
        if (float.IsNaN(hw) || hw <= 0) hw = Width - Pad * 2 - 2;
        hueCursor.style.left = h * hw - 2;
    }

    /// <summary>입력 중에는 맞는 코드일 때만 반영한다. commit(Enter·포커스 이동)이면 잘못된 코드에 오류를 보인다.</summary>
    void OnHexTyped(string text, bool commit)
    {
        string norm = Normalize(text);
        if (norm != null && ColorOverrides.TryParse(norm, out var c))
        {
            SetFromColor(c, true, commit);
            return;
        }
        bool badChar = false;
        foreach (char ch in text ?? "") if (ch != '#' && !Uri.IsHexDigit(ch) && !char.IsWhiteSpace(ch)) badChar = true;
        if (commit || badChar) ShowError("#RRGGBB 형식으로 입력해 주세요(예: #FF00FF). 색은 바꾸지 않았습니다.");
        else ShowError(null);
    }

    /// <summary>앞뒤 공백을 지우고 # 이 없으면 붙인다. 6자리 16진수가 아니면 null.</summary>
    static string Normalize(string text)
    {
        if (text == null) return null;
        text = text.Trim();
        if (!text.StartsWith("#")) text = "#" + text;
        return text.Length == 7 ? text.ToUpperInvariant() : null;
    }

    void ShowError(string msg)
    {
        error.text = msg ?? "";
        error.style.display = string.IsNullOrEmpty(msg) ? DisplayStyle.None : DisplayStyle.Flex;
    }

    void Finish(bool ok)
    {
        if (active != this) return;
        active = null;
        window.RemoveFromHierarchy();
        bool changed = ColorOverrides.ToHex(current) != ColorOverrides.ToHex(initial);
        if (ok && changed) apply?.Invoke(current);
        else cancel?.Invoke();
    }

    // ------------------------------------------------------------ 모양

    static VisualElement Layer(Texture2D tex)
    {
        var e = new VisualElement { pickingMode = PickingMode.Ignore };
        e.style.position = Position.Absolute;
        e.style.left = 0; e.style.top = 0; e.style.right = 0; e.style.bottom = 0;
        e.style.backgroundImage = new StyleBackground(tex);
        e.style.backgroundSize = new BackgroundSize(new Length(100, LengthUnit.Percent), new Length(100, LengthUnit.Percent));
        return e;
    }

    static VisualElement Cursor(float size)
    {
        var c = new VisualElement { pickingMode = PickingMode.Ignore };
        c.style.position = Position.Absolute;
        c.style.width = size; c.style.height = size;
        SearchDropdown.SetBorder(c, Color.white, 2, size * 0.5f);
        return c;
    }

    static Button Button(string text, Action click)
    {
        var b = new Button(click) { text = text };
        b.style.marginLeft = 4; b.style.marginRight = 0;
        b.style.height = 24;
        b.style.paddingLeft = 10; b.style.paddingRight = 10;
        b.style.fontSize = 12;
        b.style.color = AppUI.Text;
        b.style.backgroundColor = AppUI.FieldBg;
        SearchDropdown.SetBorder(b, AppUI.Border, 1, 3);
        return b;
    }

    static void EnsureTextures()
    {
        if (hueTex) return;
        const int n = 256;
        hueTex = NewTex(n, 1);
        whiteTex = NewTex(n, 1);
        blackTex = NewTex(1, n);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)(n - 1);
            hueTex.SetPixel(i, 0, Color.HSVToRGB(t * 0.9999f, 1, 1));
            whiteTex.SetPixel(i, 0, new Color(1, 1, 1, 1 - t));
            // 텍스처 y=0 이 화면 아래쪽이다. 아래로 갈수록 어두워진다(명도 0).
            blackTex.SetPixel(0, i, new Color(0, 0, 0, 1 - t));
        }
        hueTex.Apply(false, true); whiteTex.Apply(false, true); blackTex.Apply(false, true);
    }

    static Texture2D NewTex(int w, int h) => new Texture2D(w, h, TextureFormat.RGBA32, false)
    {
        wrapMode = TextureWrapMode.Clamp,
        filterMode = FilterMode.Bilinear,
        hideFlags = HideFlags.DontSave,
    };
}
