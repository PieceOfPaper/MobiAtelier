// 아이콘·툴팁 (WindowAndCustomizationUI.md 2.4, ControlsHelp.md)
//   아이콘은 이미지 파일 없이 Painter2D 벡터로 그린다(24×24 설계 격자를 요소 크기에 맞춰 늘린다).
//   런타임 UI Toolkit 은 tooltip 속성을 표시하지 않으므로 포인터를 잠시 올리면 뜨는 간단한 툴팁을 직접 띄운다.
using System;
using UnityEngine;
using UnityEngine.UIElements;

public enum IconKind
{
    Eye, EyeOff, Menu, Undo, Redo, Close, Info,
    ChevronLeft, ChevronRight, ChevronUp, ChevronDown,
    MouseLeft, MouseRight, MouseBoth, MouseWheel,
}

public sealed class IconElement : VisualElement
{
    IconKind kind;
    Color color = AppUI.Text;
    Color accent = new Color(0.45f, 0.7f, 1f);

    public IconKind Kind { get => kind; set { kind = value; MarkDirtyRepaint(); } }
    public Color Color { get => color; set { color = value; MarkDirtyRepaint(); } }
    public Color Accent { get => accent; set { accent = value; MarkDirtyRepaint(); } }

    public IconElement(IconKind kind, float size)
    {
        this.kind = kind;
        pickingMode = PickingMode.Ignore;
        style.width = size; style.height = size;
        style.flexShrink = 0;
        generateVisualContent += Draw;
    }

    void Draw(MeshGenerationContext ctx)
    {
        var r = contentRect;
        float s = Mathf.Min(r.width, r.height) / 24f;
        if (s <= 0 || float.IsNaN(s)) return;
        var o = new Vector2(r.x + (r.width - 24 * s) * 0.5f, r.y + (r.height - 24 * s) * 0.5f);
        Vector2 P(float x, float y) => o + new Vector2(x, y) * s;
        var p = ctx.painter2D;
        p.strokeColor = color;
        p.fillColor = color;
        p.lineWidth = Mathf.Max(1.2f, 2f * s);
        p.lineCap = LineCap.Round;
        p.lineJoin = LineJoin.Round;

        void Line(float x0, float y0, float x1, float y1) { p.BeginPath(); p.MoveTo(P(x0, y0)); p.LineTo(P(x1, y1)); p.Stroke(); }
        void Poly(params float[] xy)
        {
            p.BeginPath(); p.MoveTo(P(xy[0], xy[1]));
            for (int i = 2; i < xy.Length; i += 2) p.LineTo(P(xy[i], xy[i + 1]));
            p.Stroke();
        }
        void Dot(float x, float y, float radius)
        {
            p.BeginPath(); p.Arc(P(x, y), radius * s, Angle.Degrees(0), Angle.Degrees(360)); p.ClosePath(); p.Fill();
        }

        switch (kind)
        {
            case IconKind.Eye:
            case IconKind.EyeOff:
                p.BeginPath();
                p.MoveTo(P(2, 12));
                p.BezierCurveTo(P(6, 5), P(18, 5), P(22, 12));
                p.BezierCurveTo(P(18, 19), P(6, 19), P(2, 12));
                p.ClosePath();
                p.Stroke();
                Dot(12, 12, 3.2f);
                if (kind == IconKind.EyeOff) Line(4, 20, 20, 4);
                break;
            case IconKind.Menu:
                Line(5, 7, 19, 7); Line(5, 12, 19, 12); Line(5, 17, 19, 17);
                break;
            case IconKind.Undo:
            case IconKind.Redo:
            {
                bool redo = kind == IconKind.Redo;
                float X(float x) => redo ? 24 - x : x;
                p.BeginPath();
                p.MoveTo(P(X(19), 18));
                p.BezierCurveTo(P(X(19), 11), P(X(14), 9), P(X(6), 9));
                p.Stroke();
                Poly(X(10), 5, X(6), 9, X(10), 13);
                break;
            }
            case IconKind.Close:
                Line(6, 6, 18, 18); Line(18, 6, 6, 18);
                break;
            case IconKind.Info:
                p.BeginPath(); p.Arc(P(12, 12), 9 * s, Angle.Degrees(0), Angle.Degrees(360)); p.ClosePath(); p.Stroke();
                Dot(12, 7.6f, 1.4f);
                Line(12, 11, 12, 17);
                break;
            case IconKind.ChevronLeft: Poly(15, 5, 8, 12, 15, 19); break;
            case IconKind.ChevronRight: Poly(9, 5, 16, 12, 9, 19); break;
            case IconKind.ChevronUp: Poly(5, 15, 12, 8, 19, 15); break;
            case IconKind.ChevronDown: Poly(5, 9, 12, 16, 19, 9); break;
            case IconKind.MouseLeft:
            case IconKind.MouseRight:
            case IconKind.MouseBoth:
            case IconKind.MouseWheel:
                DrawMouse(p, P, s);
                break;
        }
    }

    /// <summary>마우스 그림. 해당 버튼·휠을 강조색으로 채운다. 색만으로 구분하지 않도록 옆에 글자를 함께 둔다(ControlsHelp.md).</summary>
    void DrawMouse(Painter2D p, Func<float, float, Vector2> P, float s)
    {
        const float l = 6, rgt = 18, t = 2.5f, b = 21.5f, mid = 9.5f, rad = 6;
        p.fillColor = accent;
        if (kind == IconKind.MouseLeft || kind == IconKind.MouseBoth)
        {
            p.BeginPath();
            p.MoveTo(P(12, t)); p.LineTo(P(12, mid)); p.LineTo(P(l, mid));
            p.ArcTo(P(l, t), P(12, t), rad * s);
            p.ClosePath(); p.Fill();
        }
        if (kind == IconKind.MouseRight || kind == IconKind.MouseBoth)
        {
            p.BeginPath();
            p.MoveTo(P(12, t)); p.LineTo(P(12, mid)); p.LineTo(P(rgt, mid));
            p.ArcTo(P(rgt, t), P(12, t), rad * s);
            p.ClosePath(); p.Fill();
        }
        // 몸통
        p.BeginPath();
        p.MoveTo(P(12, t));
        p.ArcTo(P(rgt, t), P(rgt, b), rad * s);
        p.ArcTo(P(rgt, b), P(l, b), rad * s);
        p.ArcTo(P(l, b), P(l, t), rad * s);
        p.ArcTo(P(l, t), P(12, t), rad * s);
        p.ClosePath();
        p.Stroke();
        p.BeginPath(); p.MoveTo(P(l, mid)); p.LineTo(P(rgt, mid)); p.Stroke();
        p.BeginPath(); p.MoveTo(P(12, t)); p.LineTo(P(12, mid)); p.Stroke();
        if (kind == IconKind.MouseWheel)
        {
            var keep = p.strokeColor;
            p.strokeColor = accent;
            p.lineWidth = Mathf.Max(2f, 3f * s);
            p.BeginPath(); p.MoveTo(P(12, 4.5f)); p.LineTo(P(12, 7.5f)); p.Stroke();
            p.strokeColor = keep;
        }
    }
}

/// <summary>포인터를 잠시 올리면 뜨는 툴팁. AppUI 가 Init 으로 최상위 요소를 넘긴다.</summary>
public static class Tips
{
    static VisualElement root;
    static Label tip;
    static VisualElement owner;
    static IVisualElementScheduledItem pending;
    static int pointerFrame = -10;

    /// <summary>방금 포인터로 눌러 생긴 포커스인지(그때는 툴팁·포커스 테두리를 띄우지 않는다).</summary>
    public static bool FocusFromPointer => Time.frameCount - pointerFrame <= 2;

    public static void Init(VisualElement topRoot)
    {
        root = topRoot;
        tip = new Label { name = "tooltip", pickingMode = PickingMode.Ignore };
        tip.style.position = Position.Absolute;
        tip.style.backgroundColor = new Color(0.07f, 0.08f, 0.1f, 0.96f);
        tip.style.color = AppUI.Text;
        tip.style.fontSize = 12;
        tip.style.paddingLeft = tip.style.paddingRight = 8;
        tip.style.paddingTop = tip.style.paddingBottom = 4;
        tip.style.borderTopLeftRadius = tip.style.borderTopRightRadius = tip.style.borderBottomLeftRadius = tip.style.borderBottomRightRadius = 4;
        tip.style.maxWidth = 320;
        tip.style.whiteSpace = WhiteSpace.Normal;
        tip.style.display = DisplayStyle.None;
        root.Add(tip);
    }

    /// <summary>text 는 띄울 때마다 다시 읽는다(눈 아이콘처럼 상태에 따라 바뀌는 문구).</summary>
    public static void Attach(VisualElement e, Func<string> text)
    {
        e.RegisterCallback<PointerEnterEvent>(_ =>
        {
            Hide();
            owner = e;
            pending = e.schedule.Execute(() => { if (owner == e) Show(e, text()); }).StartingIn(450);
        });
        e.RegisterCallback<PointerLeaveEvent>(_ => { if (owner == e) Hide(); });
        e.RegisterCallback<PointerDownEvent>(_ => { Hide(); pointerFrame = Time.frameCount; }, TrickleDown.TrickleDown);
        // 키보드(Tab)로 포커스가 오면 바로 이름을 보인다. 아이콘만 있는 버튼의 접근 이름 역할을 한다.
        e.RegisterCallback<FocusInEvent>(_ => { if (FocusFromPointer) return; Hide(); owner = e; Show(e, text()); });
        e.RegisterCallback<FocusOutEvent>(_ => { if (owner == e) Hide(); });
        e.RegisterCallback<DetachFromPanelEvent>(_ => { if (owner == e) Hide(); });
    }

    public static void Hide()
    {
        pending?.Pause();
        pending = null;
        owner = null;
        if (tip != null) tip.style.display = DisplayStyle.None;
    }

    static void Show(VisualElement e, string text)
    {
        if (tip == null || root == null || string.IsNullOrEmpty(text) || e.panel == null) return;
        if (e.resolvedStyle.display == DisplayStyle.None) return;
        tip.text = text;
        tip.style.display = DisplayStyle.Flex;
        tip.BringToFront();
        UIAnim.FadeIn(tip, UIAnim.Fast, new Vector2(0, -3));
        tip.style.left = 0; tip.style.top = 0;
        // 크기를 안 다음 위치를 정한다. 기본은 요소 아래, 화면 밖이면 위로 옮긴다.
        tip.schedule.Execute(() =>
        {
            if (owner != e) return;
            var b = e.worldBound;
            var rb = root.worldBound;
            float w = tip.layout.width, h = tip.layout.height;
            float x = Mathf.Clamp(b.center.x - w * 0.5f - rb.x, 4, Mathf.Max(4, rb.width - w - 4));
            float y = b.yMax + 6 - rb.y;
            if (y + h > rb.height - 4) y = b.y - h - 6 - rb.y;
            tip.style.left = x; tip.style.top = Mathf.Max(4, y);
        });
    }
}

public static class IconButtons
{
    public static readonly Color Bg = new Color(0.12f, 0.13f, 0.15f, 0.88f);
    public static readonly Color Hover = new Color(0.22f, 0.24f, 0.29f, 0.95f);

    /// <summary>
    /// 아이콘 버튼. tip 은 툴팁·키보드 포커스 때 보이는 이름이다. 요소 name 에도 같은 이름을 넣는다.
    /// 키보드 포커스를 받으면 테두리를 강조한다(Tab 이동, Enter·Space 로 실행).
    /// </summary>
    public static Button Make(IconKind kind, Action onClick, Func<string> tip, float size = 34, float icon = 20)
    {
        var b = new Button(onClick) { text = "", focusable = true };
        if (tip != null) b.name = "icon-" + tip();
        b.style.width = size; b.style.height = size;
        b.style.paddingLeft = b.style.paddingRight = b.style.paddingTop = b.style.paddingBottom = 0;
        b.style.marginLeft = b.style.marginRight = b.style.marginTop = b.style.marginBottom = 0;
        b.style.alignItems = Align.Center;
        b.style.justifyContent = Justify.Center;
        b.style.backgroundColor = Bg;
        b.style.borderTopWidth = b.style.borderBottomWidth = b.style.borderLeftWidth = b.style.borderRightWidth = 0;
        b.style.borderTopLeftRadius = b.style.borderTopRightRadius = b.style.borderBottomLeftRadius = b.style.borderBottomRightRadius = 7;
        b.Add(new IconElement(kind, icon));
        UIAnim.SmoothColor(b);
        b.RegisterCallback<PointerEnterEvent>(e => { if (b.enabledSelf) b.style.backgroundColor = Hover; });
        b.RegisterCallback<PointerLeaveEvent>(e => b.style.backgroundColor = Bg);
        b.style.borderTopColor = b.style.borderBottomColor = b.style.borderLeftColor = b.style.borderRightColor = AppUI.Accent;
        b.RegisterCallback<FocusInEvent>(e => { if (!Tips.FocusFromPointer) b.style.borderTopWidth = b.style.borderBottomWidth = b.style.borderLeftWidth = b.style.borderRightWidth = 2; });
        b.RegisterCallback<FocusOutEvent>(e => b.style.borderTopWidth = b.style.borderBottomWidth = b.style.borderLeftWidth = b.style.borderRightWidth = 0);
        if (tip != null) Tips.Attach(b, tip);
        return b;
    }

    public static IconElement IconOf(Button b) => b.Q<IconElement>();
}
