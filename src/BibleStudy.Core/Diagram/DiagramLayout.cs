namespace BibleStudy.Core.Diagram;

public enum DLineStyle { Baseline, Divider, Hang, Dashed, Stilt }

public enum DTextKind { Subject, Verb, Object, Complement, Modifier, Prep, Conj, Implied, Gloss }

public enum DAnchor { Left, Center, Right }

public sealed record DLine(double X1, double Y1, double X2, double Y2, DLineStyle Style);

/// <summary>A text of the picture. Y is the baseline of the text. A slanted text starts at (X, Y), runs for <see cref="Length"/> along <see cref="Angle"/> (degrees, clockwise).</summary>
public sealed record DText(double X, double Y, string Text, double Size, DTextKind Kind, DAnchor Anchor = DAnchor.Center, double Angle = 0, double Length = 0, DWord? Word = null);

public sealed class DScene
{
    public double Width, Height;
    public List<DLine> Lines = [];
    public List<DText> Texts = [];

    /// <summary>Mirrors the picture left to right (for Hebrew, which is read from the right).</summary>
    public void Mirror()
    {
        for (var i = 0; i < Lines.Count; i++) { var l = Lines[i]; Lines[i] = l with { X1 = Width - l.X1, X2 = Width - l.X2 }; }
        for (var i = 0; i < Texts.Count; i++)
        {
            var t = Texts[i];
            if (t.Angle != 0)
            {
                var rad = t.Angle * Math.PI / 180;
                // the text starts at the lower end of the (mirrored) line and rises to the other end
                Texts[i] = t with { X = Width - (t.X + t.Length * Math.Cos(rad)), Y = t.Y + t.Length * Math.Sin(rad), Angle = -t.Angle };
            }
            else Texts[i] = t with { X = Width - t.X, Anchor = t.Anchor switch { DAnchor.Left => DAnchor.Right, DAnchor.Right => DAnchor.Left, _ => DAnchor.Center } };
        }
    }
}

/// <summary>How words are shown: <see cref="Main"/> is written on the lines, <see cref="Small"/> (may be null) in small letters above the main words of the baseline.</summary>
public sealed record DiagramOptions(Func<DWord, string> Main, Func<DWord, string?> Small, Func<string, string> Implied);

/// <summary>
/// Lays a sentence out as a classical sentence diagram (Reed–Kellogg): subject | verb | object on one baseline, a vertical bar that crosses the line after the subject,
/// a short bar before the object, a slanted bar before a predicate complement, modifiers hanging on slanted lines (the word along the line),
/// prepositions on a slanted line with their object on a shelf, dependent clauses on their own line below (dashed link, the conjunction on it)
/// and coordinated clauses linked by a dashed line with the conjunction on it.
/// </summary>
public sealed class DiagramLayout(DiagramOptions options, Func<string, double, double> measure)
{
    const double FsMain = 19, FsMod = 15, FsSmall = 11;
    const double ClauseTop = 40;   // from the top of a clause to its baseline: room for the small text and the main text
    const double LinkLength = 46;
    static readonly double Angle = 62 * Math.PI / 180;
    static readonly double Cos = Math.Cos(Angle), Sin = Math.Sin(Angle);

    DScene _scene = new();

    string Main(DWord w) => options.Main(w);
    string Main(IEnumerable<DWord> words) => string.Join(" ", words.Select(Main));
    double Tw(string text, double size) => text.Length == 0 ? 0 : measure(text, size);

    public DScene Layout(DSentence s)
    {
        _scene = new DScene();
        var (w, h, _) = Unit(s.Root, 14, 10, true);
        _scene.Width = w + 28; _scene.Height = h + 24;
        if (s.Hebrew) _scene.Mirror();
        return _scene;
    }

    void Line(bool draw, double x1, double y1, double x2, double y2, DLineStyle style) { if (draw) _scene.Lines.Add(new DLine(x1, y1, x2, y2, style)); }

    void Text(bool draw, double x, double y, string text, double size, DTextKind kind, DAnchor anchor = DAnchor.Center, double angle = 0, double length = 0, DWord? word = null)
    {
        if (draw && text.Length > 0) _scene.Texts.Add(new DText(x, y, text, size, kind, anchor, angle, length, word));
    }

    // ---- hanging modifiers -------------------------------------------------------------------------------------------------

    (double W, double H) HangsFoot(IEnumerable<DMod> mods)
    {
        double w = 0, h = 0;
        foreach (var m in mods) { var (mw, mh) = Foot(m); w += mw; h = Math.Max(h, mh); }
        return (w, h);
    }

    (double W, double H) Foot(DMod m)
    {
        switch (m.Kind)
        {
            case DModKind.Word:
            {
                var len = Math.Max(Tw(Main(m.Word!), FsMod) + 12, 30);
                return (len * Cos + FsMod * 0.9, len * Sin);
            }
            case DModKind.Clause:
            {
                var (cw, ch, _) = Clause(m.Clause!, 0, 0, false);
                return (cw + 12, LinkLength + (ch - ClauseTop));
            }
            default:
            {
                var len = Math.Max(m.Word is null ? 28 : Tw(Main(m.Word), FsMod) + 12, 28);
                var (shelfW, innerH) = Shelf(m.Phrase);
                return (len * Cos + shelfW, len * Sin + innerH + 6);
            }
        }
    }

    (double W, double H) Shelf(DPhrase? p)
    {
        if (p is null) return (14, 0);
        var tw = Tw(Main(p.Head), FsMod + 1) + 14;
        var (hw, hh) = HangsFoot(p.Mods);
        return (Math.Max(tw, hw + 8), hh);
    }

    void DrawHangs(IEnumerable<DMod> mods, double x, double y, bool draw)
    {
        foreach (var m in mods) { DrawMod(m, x, y, draw); x += Foot(m).W; }
    }

    void DrawMod(DMod m, double ax, double ay, bool draw)
    {
        if (!draw) return;
        switch (m.Kind)
        {
            case DModKind.Word:
            {
                var text = Main(m.Word!);
                var len = Math.Max(Tw(text, FsMod) + 12, 30);
                Line(true, ax, ay, ax + len * Cos, ay + len * Sin, DLineStyle.Hang);
                Text(true, ax + 3.5 * Sin, ay - 3.5 * Cos, text, FsMod, DTextKind.Modifier, DAnchor.Left, 62, len, m.Word);
                break;
            }
            case DModKind.Clause:
            {
                var sub = m.Clause!;
                var by = ay + LinkLength;
                Line(true, ax, ay, ax, by, m.Stilt ? DLineStyle.Stilt : DLineStyle.Dashed);
                if (sub.Conj is { } conj) Text(true, ax + 5, ay + 13, Main(conj), FsSmall + 1, DTextKind.Conj, DAnchor.Left, 0, 0, conj);
                Clause(sub, ax, by - ClauseTop, true);
                break;
            }
            default:
            {
                var len = Math.Max(m.Word is null ? 28 : Tw(Main(m.Word), FsMod) + 12, 28);
                var ex = ax + len * Cos; var ey = ay + len * Sin;
                var dashed = m.Kind == DModKind.Conjunct;
                Line(true, ax, ay, ex, ey, dashed ? DLineStyle.Dashed : DLineStyle.Hang);
                if (m.Word is not null) Text(true, ax + 3.5 * Sin, ay - 3.5 * Cos, Main(m.Word), FsMod, m.Kind == DModKind.Conjunct ? DTextKind.Conj : DTextKind.Prep, DAnchor.Left, 62, len, m.Word);
                else if (m.Kind == DModKind.Apposition) Text(true, ax + len * Cos / 2 + 4, ey - 2, "=", FsSmall + 1, DTextKind.Conj, DAnchor.Left);
                var (shelfW, _) = Shelf(m.Phrase);
                Line(true, ex, ey, ex + shelfW, ey, DLineStyle.Hang);
                if (m.Phrase is { } p)
                {
                    var head = Main(p.Head);
                    Text(true, ex + 7 + Tw(head, FsMod + 1) / 2, ey - 5, head, FsMod + 1, DTextKind.Modifier, DAnchor.Center, 0, 0, p.Head.FirstOrDefault());
                    DrawHangs(p.Mods, ex + 8, ey, true);
                }
                break;
            }
        }
    }

    // ---- one clause --------------------------------------------------------------------------------------------------------

    sealed class Slot
    {
        public string Text = "";
        public string? Small;
        public DTextKind Kind;
        public DWord? Word;
        public List<DMod> Mods = [];
        public List<DMod> ClauseMods = [];
        public double X, W;
    }

    Slot SlotOf(DPhrase? p, DTextKind kind, IEnumerable<DMod>? extra = null)
    {
        var s = new Slot { Kind = kind, Text = p is null ? "" : Main(p.Head) };
        if (p is not null)
        {
            var small = p.Head.Select(options.Small).Where(x => !string.IsNullOrEmpty(x)).ToList();
            s.Small = small.Count > 0 ? string.Join(" ", small) : null;
            s.Word = p.Head.FirstOrDefault();
            s.Mods.AddRange(p.Mods);
        }
        if (extra is not null) s.Mods.AddRange(extra);
        return s;
    }

    (double W, double H, double Baseline) Clause(DClause c, double x0, double y0, bool draw)
    {
        var by = y0 + ClauseTop;
        var slots = new List<Slot>();
        Slot? subj = null, verb = null, obj = null;
        if (c.Fragment) slots.Add(subj = SlotOf(c.Subject, DTextKind.Subject));
        else
        {
            if (c.Subject is not null) subj = SlotOf(c.Subject, DTextKind.Subject);
            else if (c.ImpliedSubject is not null) subj = new Slot { Kind = DTextKind.Implied, Text = "(" + options.Implied(c.ImpliedSubject) + ")" };
            if (subj is not null) slots.Add(subj);
            if (c.Verb.Count > 0)
            {
                verb = new Slot { Kind = DTextKind.Verb, Text = Main(c.Verb), Word = c.Verb[^1] };
                var small = c.Verb.Select(options.Small).Where(x => !string.IsNullOrEmpty(x)).ToList();
                verb.Small = small.Count > 0 ? string.Join(" ", small) : null;
                verb.Mods.AddRange(c.Mods);
                slots.Add(verb);
            }
            else if (c.Mods.Count > 0)
            {
                // no verb: the dependents hang from the line itself
                var carrier = subj ?? new Slot { Kind = DTextKind.Verb };
                carrier.Mods.AddRange(c.Mods);
                if (subj is null) slots.Add(carrier);
            }
            if (c.Object is not null) slots.Add(obj = SlotOf(c.Object, DTextKind.Object));
            else if (c.Complement is not null) slots.Add(obj = SlotOf(c.Complement, DTextKind.Complement));
        }

        // dependent clauses do not hang beside the other modifiers: they are drawn on their own lines below everything else,
        // so they cannot stretch the baseline of the clause they depend on
        foreach (var s in slots)
        {
            s.ClauseMods = s.Mods.Where(m => m.Kind == DModKind.Clause).ToList();
            s.Mods = s.Mods.Where(m => m.Kind != DModKind.Clause).ToList();
        }

        // widths
        double depth = 0, x = x0 + 6;
        foreach (var s in slots)
        {
            var textW = Math.Max(Tw(s.Text, FsMain), Tw(s.Small ?? "", FsSmall)) + 20;
            var (hw, hh) = HangsFoot(s.Mods);
            s.W = Math.Max(Math.Max(textW, hw + 10 + (s.ClauseMods.Count > 0 ? 14 : 0)), 34);
            s.X = x; x += s.W; depth = Math.Max(depth, hh);
        }
        var total = x - x0 + 6;

        // the rightmost slot first, so that the link of a clause further left never crosses the line of one further right
        double rowTop = ClauseTop + Math.Max(depth, 8) + 22, width = total;
        var hasClauses = false;
        foreach (var s in Enumerable.Reverse(slots))
        {
            foreach (var m in s.ClauseMods)
            {
                hasClauses = true;
                var (cw, ch, _) = Clause(m.Clause!, 0, 0, false);
                var linkX = s.X + s.W - 9;
                if (draw)
                {
                    var top = y0 + rowTop;
                    Line(true, linkX, by, linkX, top + ClauseTop, m.Stilt ? DLineStyle.Stilt : DLineStyle.Dashed);
                    if (m.Clause!.Conj is { } conj) Text(true, linkX + 5, top - 4, Main(conj), FsSmall + 1, DTextKind.Conj, DAnchor.Left, 0, 0, conj);
                    Clause(m.Clause, linkX, top, true);
                }
                width = Math.Max(width, linkX - x0 + cw + 6);
                rowTop += ch + 24;
            }
        }
        if (draw)
        {
            Line(true, x0, by, x0 + total, by, DLineStyle.Baseline);
            foreach (var s in slots)
            {
                var cx = s.X + s.W / 2;
                Text(true, cx, by - 7, s.Text, FsMain, s.Kind, DAnchor.Center, 0, 0, s.Word);
                if (s.Small is not null) Text(true, cx, by - 7 - FsMain - 1, s.Small, FsSmall, DTextKind.Gloss);
                DrawHangs(s.Mods, s.X + 8, by, true);
            }
            if (subj is not null && verb is not null && subj != verb)
                Line(true, verb.X, by - 26, verb.X, by + 14, DLineStyle.Divider);   // subject | verb: the bar crosses the line
            if (verb is not null && obj is not null)
            {
                if (c.Object is not null) Line(true, obj.X, by - 26, obj.X, by, DLineStyle.Divider);   // verb | object: the bar stays above the line
                else Line(true, obj.X, by, obj.X - 13, by - 26, DLineStyle.Divider);                    // verb \ complement: a slanted bar
            }
        }
        return (width, hasClauses ? rowTop - 14 : ClauseTop + Math.Max(depth, 8) + 6, by);
    }

    // ---- clauses and their links -------------------------------------------------------------------------------------------

    (double W, double H, double Baseline) Unit(DUnit u, double x0, double y0, bool draw)
    {
        if (u.Clause is { } c)
        {
            var lead = 0.0;
            if (c.Conj is { } conj && !c.Fragment)
            {
                var text = Main(conj);
                lead = Tw(text, FsMod) + 18;
                if (draw)
                {
                    Text(true, x0 + 2, y0 + ClauseTop - 7, text, FsMod, DTextKind.Conj, DAnchor.Left, 0, 0, conj);
                    Line(true, x0 + lead - 14, y0 + ClauseTop, x0 + lead, y0 + ClauseTop, DLineStyle.Dashed);
                }
            }
            var (w, h, b) = Clause(c, x0 + lead, y0, draw);
            return (w + lead, h, b);
        }

        // several clauses: stacked, joined by a dashed line on the left with the conjunction written on it
        var colW = u.Conjs.Count == 0 ? 0 : u.Conjs.Max(j => j is null ? 0 : Tw(Main(j), FsMod)) + 26;
        double y = y0, width = 0, first = 0;
        var bases = new List<double>();
        for (var i = 0; i < u.Items.Count; i++)
        {
            var (w, h, b) = Unit(u.Items[i], x0 + colW, y, draw);
            if (i == 0) first = b;
            bases.Add(b); width = Math.Max(width, w); y += h + 18;
        }
        if (draw)
        {
            var xl = x0 + colW - 9;
            for (var i = 0; i + 1 < bases.Count; i++)
            {
                Line(true, xl, bases[i], xl, bases[i + 1], DLineStyle.Dashed);
                Line(true, xl, bases[i], x0 + colW, bases[i], DLineStyle.Dashed);
                Line(true, xl, bases[i + 1], x0 + colW, bases[i + 1], DLineStyle.Dashed);
                if (i < u.Conjs.Count && u.Conjs[i] is { } j) Text(true, x0 + 2, (bases[i] + bases[i + 1]) / 2 + 5, Main(j), FsMod, DTextKind.Conj, DAnchor.Left, 0, 0, j);
            }
        }
        return (width + colW, y - 18 - y0, first);
    }
}
