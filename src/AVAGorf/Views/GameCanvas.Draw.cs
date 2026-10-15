using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAGorf.Engine;
using Effect = AVAGorf.Engine.Effect;

namespace AVAGorf.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAGorf/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Fire = 0xFFFF8A2A, Cyan = 0xFF6AE0FF;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    private const uint Magenta = 0xFFFF5AD0, Green = 0xFF6AFF8A;

    private bool InGame => Engine.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        DrawBackdrop(dc);
        if (InGame)
        {
            using (dc.PushClip(new Rect(Field.Left, Field.Top, Field.Right - Field.Left, Field.Bottom - Field.Top)))
            {
                if (g.Mission == Mission.Warp && g.Phase != Phase.Clear) DrawVortex(dc);
                if (g.Barrier is { } ff) DrawForceField(dc, ff);
                foreach (var b in g.Beams) DrawBeam(dc, b);
                if (g.Flagship is { CoreAlive: true } d) DrawFlagship(dc, d);
                foreach (var f in g.Foes.Where(f => !f.Dead)) DrawFoe(dc, f);
                if (g.Shot is { } s)
                {
                    dc.FillRectangle(Brush(Alpha(Magenta, 0.35)), new Rect(s.X - 3.5, s.Y - 3, 7, 22));
                    dc.FillRectangle(Brush(0xFFFFF0FF), new Rect(s.X - 1.5, s.Y, 3, 16));
                }
                foreach (var b in g.Bullets)
                {
                    if (b.Bomb)
                    {
                        double p = 0.5 + 0.5 * Math.Sin(_clock * 20 + b.X);
                        dc.DrawEllipse(Brush(Alpha(0xFFFF6A2A, 0.35 + 0.3 * p)), null, new Point(b.X, b.Y), 9, 9);
                        dc.DrawEllipse(Brush(0xFFFFE070), null, new Point(b.X, b.Y), 4.5, 4.5);
                    }
                    else
                    {
                        dc.DrawEllipse(Brush(Alpha(0xFFFF6A4A, 0.3)), null, new Point(b.X, b.Y), 5, 8);
                        dc.DrawLine(Pen(0xFFFFE0E0, 3), new Point(b.X - b.Vx * 0.025, b.Y - 7), new Point(b.X, b.Y + 3));
                    }
                }
                if (g.Ship.Alive) DrawShip(dc, new Point(g.Ship.X, g.Ship.Y), 1);
            }
            // The fighter's zone, marked at its top.
            for (double x = Field.Left + 6; x < Field.Right; x += 16)
                dc.FillRectangle(Brush(0x3040A0FF), new Rect(x, Field.ZoneTop - 1, 8, 2));
            DrawPanels(dc);
            DrawTaunt(dc);
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 360), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 18), 26, Yellow, center: true);
    }

    /// <summary>Deep space, its stars drifting down; the battle's column darker than the panels beside it.</summary>
    private void DrawBackdrop(DrawingContext dc)
    {
        dc.FillRectangle(Brush(0xFF0E0818), new Rect(0, 0, Field.Width, Field.Height));
        bool inGame = InGame;
        if (inGame) dc.FillRectangle(Brush(0xFF020006), new Rect(Field.Left, Field.Top, Field.Right - Field.Left, Field.Bottom - Field.Top));
        double left = inGame ? Field.Left : 0, width = inGame ? Field.Right - Field.Left : Field.Width;
        for (int k = 0; k < 110; k++)
        {
            double speed = 14 + Hash(k, 2, 4) % 40;
            double x = left + Hash(k, 1, 4) % (uint)width;
            double y = (Hash(k, 3, 4) % 768 + _clock * speed) % 768;
            if (inGame && y < Field.Top) continue;
            double tw = 0.5 + 0.5 * Math.Sin(_clock * (2 + k % 5) + k);
            uint colour = (k % 6) switch { 0 => 0xFFFFA0E0, 1 => 0xFFA0C0FF, _ => 0xFFFFFFFF };
            dc.FillRectangle(Brush(Alpha(colour, 0.3 + 0.5 * tw)), new Rect(x, y, 2, 2));
        }
        if (inGame)
        {
            dc.DrawLine(Pen(0xFF4A2A6A, 2), new Point(Field.Left, Field.Top), new Point(Field.Left, Field.Bottom));
            dc.DrawLine(Pen(0xFF4A2A6A, 2), new Point(Field.Right, Field.Top), new Point(Field.Right, Field.Bottom));
        }
    }

    /// <summary>The warp: spiral arms turning about its eye.</summary>
    private void DrawVortex(DrawingContext dc)
    {
        var c = new Point(GameEngine.WarpX, GameEngine.WarpY);
        for (int arm = 0; arm < 4; arm++)
            for (int i = 1; i < 40; i++)
            {
                double r = i * 9, a = arm * Math.PI / 2 + i * 0.22 - _clock * 1.6;
                double t = 1 - i / 40.0;
                dc.DrawEllipse(Brush(Alpha(i % 2 == 0 ? 0xFFB04AFF : 0xFF4A6AFF, 0.5 * t)), null, new Point(c.X + Math.Cos(a) * r * 1.1, c.Y + Math.Sin(a) * r * 0.8), 1.5 + t * 2, 1.5 + t * 2);
            }
        double p = 0.5 + 0.5 * Math.Sin(_clock * 6);
        dc.DrawEllipse(Brush(Alpha(0xFFFFFFFF, 0.5 + 0.4 * p)), null, c, 6, 5);
        dc.DrawEllipse(null, Pen(Alpha(0xFFE06AFF, 0.6), 2), c, 14 + 4 * p, 11 + 3 * p);
    }

    private void DrawForceField(DrawingContext dc, ForceField ff)
    {
        double shimmer = 0.6 + 0.25 * Math.Sin(_clock * 5);
        for (int c = 0; c < ForceField.Cols; c++)
        {
            double top = ForceField.TopOf(c), x = ForceField.LeftX + c * ForceField.CellW;
            for (int r = 0; r < ForceField.Rows; r++)
                if (ff.Cells[r, c])
                    dc.FillRectangle(Brush(Alpha(r == 0 ? 0xFF8AF0FF : 0xFF3AA0E0, shimmer + (Hash(c, r, (int)(_clock * 8)) % 10) * 0.02)),
                        new Rect(x + 0.5, top + r * ForceField.CellH + 0.5, ForceField.CellW - 1, ForceField.CellH - 1));
        }
    }

    private void DrawBeam(DrawingContext dc, Beam b)
    {
        double top = b.Y, bottom = Field.Bottom;
        if (!b.Firing)
        {
            // The warning: a thin flickering line.
            if ((int)(b.T * 16) % 2 == 0)
                for (double y = top; y < bottom; y += 14)
                    dc.FillRectangle(Brush(0xC0FF4A4A), new Rect(b.X - 1, y, 2, 8));
            dc.DrawEllipse(Brush(Alpha(0xFFFF6A4A, 0.6)), null, new Point(b.X, top), 6 + 6 * b.T / Beam.WarnSeconds, 6 + 6 * b.T / Beam.WarnSeconds);
            return;
        }
        double k = Math.Sin(Math.PI * Math.Min(1, (b.T - Beam.WarnSeconds) / Beam.FireSeconds));
        double w = Beam.HalfWidth * (0.6 + 0.4 * k);
        dc.FillRectangle(Brush(Alpha(0xFF6AE0FF, 0.25)), new Rect(b.X - w * 2, top, w * 4, bottom - top));
        dc.FillRectangle(Brush(Alpha(0xFFB0F4FF, 0.85)), new Rect(b.X - w, top, w * 2, bottom - top));
        dc.FillRectangle(Brush(0xFFFFFFFF), new Rect(b.X - w * 0.4, top, w * 0.8, bottom - top));
    }

    // ───────────────────────────── the fighter ─────────────────────────────

    /// <summary>The fighter: a green and white star-fighter with twin prongs and a glowing exhaust.</summary>
    private void DrawShip(DrawingContext dc, Point at, double scale)
    {
        double k = scale;
        Point P(double x, double y) => new(at.X + x * k, at.Y + y * k);
        double flick = 5 + 3 * Math.Sin(_clock * 41);
        Poly(dc, 0xFFFF8AE0, null, 0, P(-6, 12), P(6, 12), P(0, 12 + flick));
        Poly(dc, 0xFF2AB06A, 0xFF0A4A2A, 1.2, P(-21, 10), P(-17, -10), P(-12, 4), P(12, 4), P(17, -10), P(21, 10), P(8, 13), P(-8, 13));
        Poly(dc, 0xFFF0F0FA, 0xFF707090, 1.1, P(0, -18), P(7, -2), P(7, 12), P(-7, 12), P(-7, -2));
        dc.DrawEllipse(Brush(0xFFFF5AD0), null, P(0, 0), 3 * k, 5 * k);
        dc.FillRectangle(Brush(0xFFFFFFFF), new Rect(at.X - 18 * k, at.Y - 14 * k, 2 * k, 6 * k));
        dc.FillRectangle(Brush(0xFFFFFFFF), new Rect(at.X + 16 * k, at.Y - 14 * k, 2 * k, 6 * k));
    }

    // ───────────────────────────── the enemy ─────────────────────────────

    private void DrawFoe(DrawingContext dc, Foe f)
    {
        double k = f.Kind == FoeKind.Warper ? f.Size / 14 : 1;
        using (dc.PushTransform(Matrix.CreateRotation(f.Kind == FoeKind.Warper ? 0 : -f.Angle) * Matrix.CreateTranslation(f.X, f.Y)))
            DrawFoeShape(dc, f.Kind, f.Variant, _clock * 6 + f.Phase, k, f.Kind == FoeKind.Warper ? f.Angle : 0);
    }

    /// <summary>
    /// Each kind of enemy, about (0, 0) and facing down: the Armada's crawlers (three kinds), winged fighters,
    /// the Lancers' orange discs, the Swarm's crested leaders and the warp's spinning star-ships.
    /// </summary>
    private static void DrawFoeShape(DrawingContext dc, FoeKind kind, int variant, double clock, double k, double spin)
    {
        Point P(double x, double y) => new(x * k, y * k);
        double flap = Math.Sin(clock);
        uint body = GameEngine.Colour(new Foe { Kind = kind, Variant = variant });
        switch (kind)
        {
            case FoeKind.Invader:
            {
                bool step = Math.Sin(clock * 0.5) > 0;
                double leg = step ? 4 : -4;
                switch (variant)
                {
                    case 0:
                        // A squid-like scout with waving tentacles.
                        Poly(dc, body, null, 0, P(0, -11), P(9, -3), P(9, 4), P(-9, 4), P(-9, -3));
                        for (int i = -1; i <= 1; i++)
                            dc.DrawLine(Pen(body, 2.4 * k), P(i * 6, 4), P(i * 6 + leg * (i == 0 ? -0.5 : 1), 11));
                        break;
                    case 1:
                        // A crab with raised claws.
                        dc.DrawEllipse(Brush(body), null, P(0, 0), 11 * k, 7 * k);
                        dc.DrawLine(Pen(body, 2.4 * k), P(-10, -2), P(-15, step ? -10 : 4));
                        dc.DrawLine(Pen(body, 2.4 * k), P(10, -2), P(15, step ? -10 : 4));
                        dc.DrawLine(Pen(body, 2 * k), P(-6, 5), P(-8 - leg / 2, 11));
                        dc.DrawLine(Pen(body, 2 * k), P(6, 5), P(8 + leg / 2, 11));
                        break;
                    default:
                        // A broad beetle.
                        Poly(dc, body, null, 0, P(-14, -2), P(-8, -9), P(8, -9), P(14, -2), P(12, 6), P(-12, 6));
                        dc.DrawLine(Pen(body, 2 * k), P(-10, 6), P(-14 + leg, 11));
                        dc.DrawLine(Pen(body, 2 * k), P(10, 6), P(14 - leg, 11));
                        dc.DrawLine(Pen(body, 2 * k), P(-3, 6), P(-3, 10));
                        dc.DrawLine(Pen(body, 2 * k), P(3, 6), P(3, 10));
                        break;
                }
                dc.DrawEllipse(Brush(0xFF100010), null, P(-4, -2), 2 * k, 2 * k);
                dc.DrawEllipse(Brush(0xFF100010), null, P(4, -2), 2 * k, 2 * k);
                break;
            }
            case FoeKind.Lancer:
            {
                // An orange disc with spikes round it and a lens beneath.
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4 + clock * 0.3;
                    Poly(dc, 0xFFFFD04A, null, 0, P(Math.Cos(a - 0.2) * 17, Math.Sin(a - 0.2) * 13), P(Math.Cos(a) * 25, Math.Sin(a) * 19), P(Math.Cos(a + 0.2) * 17, Math.Sin(a + 0.2) * 13));
                }
                dc.DrawEllipse(Brush(body), Pen(0xFF7A3A0A, 1.4 * k), P(0, 0), 18 * k, 13 * k);
                dc.DrawEllipse(Brush(0xFFFFE8B0), null, P(0, -4), 8 * k, 4 * k);
                double p = 0.5 + 0.5 * Math.Sin(clock * 2);
                dc.DrawEllipse(Brush(Alpha(0xFFFF3A3A, 0.6 + 0.4 * p)), null, P(0, 8), 5 * k, 4 * k);
                break;
            }
            case FoeKind.Leader:
            {
                double w = 15 + 2 * flap;
                Poly(dc, 0xFFE02A3A, 0xFF7A0A1A, 1, P(-w, -6), P(-6, -10), P(6, -10), P(w, -6), P(10, 4), P(-10, 4));
                Poly(dc, body, 0xFF8A6A0A, 1.2, P(-10, -8), P(-4, -12), P(4, -12), P(10, -8), P(8, 6), P(3, 12), P(-3, 12), P(-8, 6));
                Poly(dc, 0xFFE02A3A, null, 0, P(-6, -12), P(-3, -17), P(0, -12), P(3, -17), P(6, -12));
                dc.DrawEllipse(Brush(0xFF2A1A6A), null, P(-4, 1), 2.2 * k, 2.2 * k);
                dc.DrawEllipse(Brush(0xFF2A1A6A), null, P(4, 1), 2.2 * k, 2.2 * k);
                break;
            }
            case FoeKind.Warper:
            {
                // A spinning four-pointed star with a hot centre.
                var pts = new Point[8];
                for (int i = 0; i < 8; i++)
                {
                    double a = spin + i * Math.PI / 4, r = i % 2 == 0 ? 14 : 5;
                    pts[i] = P(Math.Cos(a) * r, Math.Sin(a) * r);
                }
                Poly(dc, body, 0xFFFFC0FF, 1, pts);
                dc.DrawEllipse(Brush(0xFFFFFFFF), null, P(0, 0), 3.5 * k, 3.5 * k);
                break;
            }
            default:
            {
                double wing = variant == 1 ? 13 : 12, lift = 6 * flap;
                uint wingColour = variant == 1 ? 0xFF6AE0FF : 0xFFB0D0FF;
                Poly(dc, Alpha(wingColour, 0.95), null, 0, P(-3, -2), P(-wing, -6 - lift), P(-wing - 2, 4 - lift / 2), P(-4, 5));
                Poly(dc, Alpha(wingColour, 0.95), null, 0, P(3, -2), P(wing, -6 - lift), P(wing + 2, 4 - lift / 2), P(4, 5));
                Poly(dc, body, 0xFF101030, 1, P(0, -11), P(5, -5), P(5, 6), P(0, 11), P(-5, 6), P(-5, -5));
                if (variant == 1) Poly(dc, 0xFFFFFFFF, null, 0, P(-2, -10), P(0, -16), P(2, -10));
                dc.DrawEllipse(Brush(0xFFFFFF60), null, P(-2.4, 4), 1.6 * k, 1.6 * k);
                dc.DrawEllipse(Brush(0xFFFFFF60), null, P(2.4, 4), 1.6 * k, 1.6 * k);
                break;
            }
        }
    }

    /// <summary>The Dreadnought: a great dark saucer with a dome, cannons at its sides, the core and the shield band below.</summary>
    private void DrawFlagship(DrawingContext dc, Dreadnought d)
    {
        double hw = Dreadnought.HalfWidth;
        Point P(double x, double y) => new(d.X + x, d.Y + y);
        // The dome and the hull.
        dc.DrawEllipse(Brush(0xFF3A2A6A), Pen(0xFF8A6AE0, 1.5), P(0, -18), 60, 30);
        dc.DrawEllipse(Brush(Alpha(0xFFB0A0FF, 0.35)), null, P(-14, -28), 20, 9);
        Poly(dc, 0xFF4A2A7A, 0xFFA07AF0, 2, P(-hw - 26, 10), P(-hw + 10, -12), P(hw - 10, -12), P(hw + 26, 10), P(hw - 4, 34), P(-hw + 4, 34));
        for (int i = 0; i < 9; i++)
        {
            double x = -hw + 20 + i * (2 * hw - 40) / 8;
            bool lit = (int)(_clock * 4 + i) % 3 == 0;
            dc.FillRectangle(Brush(lit ? 0xFFFFE04A : 0xFF6A5A9A), new Rect(d.X + x - 3, d.Y + 2, 6, 5));
        }
        // The cannons.
        dc.FillRectangle(Brush(0xFFC0C0D8), new Rect(d.X - hw - 10, d.Y + 12, 8, 16));
        dc.FillRectangle(Brush(0xFFC0C0D8), new Rect(d.X + hw + 2, d.Y + 12, 8, 16));
        // The core, pulsing.
        double p = 0.5 + 0.5 * Math.Sin(_clock * 7);
        dc.DrawEllipse(Brush(Alpha(0xFFFF4A4A, 0.4 + 0.3 * p)), null, P(0, 24), 18, 12);
        dc.DrawEllipse(Brush(0xFFFF2A2A), Pen(0xFFFFE0A0, 1.5), P(0, 24), 11, 8);
        dc.DrawEllipse(Brush(0xFFFFFFFF), null, P(-3, 21), 3, 2);
        // The band.
        for (int r = 0; r < Dreadnought.BandRows; r++)
            for (int c = 0; c < Dreadnought.BandCols; c++)
                if (d.Band[r, c])
                {
                    double x = d.X - hw + c * Dreadnought.CellW, y = d.Y + Dreadnought.BandTop + r * Dreadnought.RowH;
                    uint col = (c + r + (int)(_clock * 6)) % 4 == 0 ? 0xFFB0F4FF : r == 0 ? 0xFF4AC0FF : 0xFF2A80D0;
                    dc.FillRectangle(Brush(col), new Rect(x + 0.5, y + 0.5, Dreadnought.CellW - 1, Dreadnought.RowH - 1));
                }
    }

    /// <summary>The Overlord's face: a horned steel mask with burning eyes.</summary>
    private void DrawOverlord(DrawingContext dc, Point at, double k, bool talking)
    {
        Point P(double x, double y) => new(at.X + x * k, at.Y + y * k);
        Poly(dc, 0xFF8A2A3A, null, 0, P(-16, -10), P(-26, -30), P(-10, -16));
        Poly(dc, 0xFF8A2A3A, null, 0, P(16, -10), P(26, -30), P(10, -16));
        Poly(dc, 0xFF6A6A8A, 0xFFC0C0D8, 1.5, P(-18, -16), P(18, -16), P(20, 4), P(10, 22), P(-10, 22), P(-20, 4));
        double glow = 0.6 + 0.4 * Math.Sin(_clock * 9);
        Poly(dc, Alpha(Red, glow), null, 0, P(-14, -6), P(-4, -4), P(-6, 2), P(-14, 0));
        Poly(dc, Alpha(Red, glow), null, 0, P(14, -6), P(4, -4), P(6, 2), P(14, 0));
        double mouth = talking ? 3 + 3 * Math.Abs(Math.Sin(_clock * 18)) : 1.5;
        dc.FillRectangle(Brush(0xFF200008), new Rect(at.X - 8 * k, at.Y + (12 - mouth / 2) * k, 16 * k, mouth * k));
    }

    private void DrawTaunt(DrawingContext dc)
    {
        var g = Engine;
        if (g.Taunt is not { } t) return;
        double a = Math.Min(1, g.TauntTime * 3);
        dc.FillRectangle(Brush(Alpha(0xFF200010, 0.75 * a)), new Rect(Field.Left + 10, Field.Top + 6, Field.Right - Field.Left - 20, 48), 6);
        DrawOverlord(dc, new Point(Field.Left + 40, Field.Top + 32), 0.75, true);
        // It types out, a letter at a time.
        int shown = Math.Min(t.Length, (int)((3 - g.TauntTime) * 40) + 1);
        Text(dc, t[..Math.Max(1, shown)], new Point(Field.Left + 72, Field.Top + 30), 28, Alpha(0xFFFF6A6A, a));
    }

    // ───────────────────────────── effects ─────────────────────────────

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var c = new Point(e.X, e.Y);
        switch (e.Kind)
        {
            case EffectKind.Burst:
            {
                double r = (6 + 30 * t) * e.Size;
                dc.DrawEllipse(null, Pen(Alpha(e.Colour, 1 - t), 2.5), c, r, r);
                for (int k = 0; k < 10; k++)
                {
                    double a = k * Math.PI / 5 + Hash(k, (int)e.X, 1) % 10 * 0.06, d = r * (0.6 + Hash(k, 2, 3) % 10 * 0.06);
                    dc.FillRectangle(Brush(Alpha(k % 2 == 0 ? 0xFFFFF0C0 : e.Colour, 1 - t)), new Rect(c.X + Math.Cos(a) * d - 2, c.Y + Math.Sin(a) * d - 2, 4, 4));
                }
                break;
            }
            case EffectKind.Blast:
            {
                double r = (16 + 70 * Math.Sqrt(t)) * e.Size;
                dc.DrawEllipse(Brush(Alpha(0xFFFFE080, (1 - t) * 0.8)), null, c, r * 0.6, r * 0.6);
                dc.DrawEllipse(null, Pen(Alpha(Fire, 1 - t), 6), c, r, r * 0.8);
                for (int k = 0; k < 18; k++)
                {
                    double a = k * Math.PI / 9 + Hash(k, (int)e.X, 5) % 10 * 0.05, d = r * (0.7 + Hash(k, 3, 5) % 10 * 0.05);
                    uint col = k % 3 == 0 ? 0xFFFFFFFF : k % 3 == 1 ? Fire : Yellow;
                    dc.FillRectangle(Brush(Alpha(col, 1 - t)), new Rect(c.X + Math.Cos(a) * d - 3, c.Y + Math.Sin(a) * d * 0.8 - 3, 6, 6));
                }
                break;
            }
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 24), 26, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 30), 40, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar, panels and screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        Text(dc, "SCORE", new Point(16, 18), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 48), 36, Yellow);
        Text(dc, "HI", new Point(Field.Width - 16, 18), 22, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(Field.Width - 16, 48), 30, Dim, right: true);
        if (g.State is GameState.Playing or GameState.Attract or GameState.Paused)
            Text(dc, $"MISSION {g.Wave}", new Point(Field.Width / 2, 36), 38, White, center: true);
    }

    /// <summary>The spare fighters and your rank on the left; the five missions on the right.</summary>
    private void DrawPanels(DrawingContext dc)
    {
        var g = Engine;
        double lx = Field.Left / 2, rx = Field.Right + (Field.Width - Field.Right) / 2;
        Text(dc, "FIGHTERS", new Point(lx, 120), 30, Dim, center: true);
        int spare = Math.Max(0, g.Lives - (g.Ship.Alive ? 1 : 0));
        for (int i = 0; i < Math.Min(spare, 9); i++)
            DrawShip(dc, new Point(lx - 48 + i % 3 * 48, 170 + i / 3 * 46), 0.9);

        Text(dc, "RANK", new Point(lx, 340), 30, Dim, center: true);
        Text(dc, g.RankName, new Point(lx, 378), 32, Yellow, center: true);
        int stars = Math.Min(5, g.Rank + 1);
        for (int i = 0; i < stars; i++)
        {
            double x = lx + (i - (stars - 1) / 2.0) * 28, y = 416;
            var pts = new Point[10];
            for (int j = 0; j < 10; j++)
            {
                double a = -Math.PI / 2 + j * Math.PI / 5, r = j % 2 == 0 ? 11 : 4.5;
                pts[j] = new Point(x + Math.Cos(a) * r, y + Math.Sin(a) * r);
            }
            Poly(dc, Yellow, null, 0, pts);
        }
        if (g.Mission == Mission.Warp && g.Phase == Phase.Play)
            Text(dc, $"WARP SHIPS {g.WarpToCome + g.Remaining}", new Point(lx, 480), 24, 0xFFE06AFF, center: true);
        if (g.Barrier is { } ff)
            Text(dc, $"FIELD {100 * ff.Count / (ForceField.Rows * ForceField.Cols)}%", new Point(lx, 480), 24, Cyan, center: true);

        Text(dc, "MISSIONS", new Point(rx, 120), 30, Dim, center: true);
        (FoeKind Kind, int Variant)[] icons = [(FoeKind.Invader, 1), (FoeKind.Lancer, 0), (FoeKind.Leader, 0), (FoeKind.Warper, 0), (FoeKind.Lancer, 0)];
        for (int i = 0; i < GameEngine.Missions; i++)
        {
            double y = 168 + i * 50;
            bool now = (int)g.Mission == i;
            if (now) dc.FillRectangle(Brush(0x40FFE14A), new Rect(rx - 86, y - 22, 172, 44), 4);
            if (i == 4)
            {
                dc.DrawEllipse(Brush(0xFF4A2A7A), Pen(0xFFA07AF0, 1), new Point(rx - 60, y), 20, 9);
                dc.DrawEllipse(Brush(0xFFFF2A2A), null, new Point(rx - 60, y + 3), 4, 3);
            }
            else
                using (dc.PushTransform(Matrix.CreateTranslation(rx - 60, y)))
                    DrawFoeShape(dc, icons[i].Kind, icons[i].Variant, _clock * 6 + i, i == 1 ? 0.6 : 1, _clock * 3);
            Text(dc, GameEngine.NameOf((Mission)i)[4..], new Point(rx - 32, y), 24, now ? Yellow : White);
        }
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = 470;
        var (w, h) = (Math.Min(Field.Right - Field.Left, Math.Max(msg.Length * 28, (g.SubMessage?.Length ?? 0) * 15) + 60), g.SubMessage != null ? 110.0 : 74.0);
        dc.FillRectangle(Brush(0xC0100820), new Rect(cx - w / 2, cy - 48, w, h), 6);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 12 ? 44 : 56, g.Phase == Phase.Dying ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 36), 28, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        Text(dc, "AVA GORF", new Point(cx, 100), 120, Magenta, center: true);
        Text(dc, "AFTER THE 1981 ARCADE CLASSIC", new Point(cx, 172), 28, Dim, center: true);
        DrawOverlord(dc, new Point(cx, 248), 2, (int)(_clock / 2) % 2 == 0);
        Text(dc, "\"YOU WILL NEVER REACH MY DREADNOUGHT\"", new Point(cx, 318), 28, 0xFFFF6A6A, center: true);
        string[] what = ["MARCHING FLEET", "LASER SHIPS", "DIVING FORMATION", "OUT OF THE VORTEX", "HIT THE CORE"];
        double[] xs = [cx - 360, cx - 180, cx, cx + 180, cx + 360];
        (FoeKind Kind, int Variant)[] icons = [(FoeKind.Invader, 0), (FoeKind.Lancer, 0), (FoeKind.Leader, 0), (FoeKind.Warper, 0)];
        for (int i = 0; i < 5; i++)
        {
            var at = new Point(xs[i], 384);
            if (i < 4)
                using (dc.PushTransform(Matrix.CreateTranslation(at.X, at.Y)))
                    DrawFoeShape(dc, icons[i].Kind, icons[i].Variant, _clock * 6 + i, 1.4, _clock * 3);
            else
            {
                dc.DrawEllipse(Brush(0xFF4A2A7A), Pen(0xFFA07AF0, 1.5), at, 38, 16);
                dc.DrawEllipse(Brush(0xFFFF2A2A), null, new Point(at.X, at.Y + 5), 7, 5);
            }
            Text(dc, $"{i + 1}. {GameEngine.NameOf((Mission)i)[4..]}", new Point(xs[i], 428), 26, Yellow, center: true);
            Text(dc, what[i], new Point(xs[i], 454), 20, Dim, center: true);
        }
        Text(dc, "RISE FROM CADET TO MARSHAL - EACH ROUND OF FIVE IS FASTER", new Point(cx, 506), 25, White, center: true);
        Text(dc, "FIRING AGAIN CANCELS YOUR SHOT IN FLIGHT", new Point(cx, 536), 25, White, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(cx, 600), 46, Yellow, center: true);
        Text(dc, "ARROWS: FLY    SPACE: FIRE    P: PAUSE    M: SOUND", new Point(cx, 664), 26, Dim, center: true);
        Text(dc, $"EXTRA FIGHTER AT {GameEngine.FirstExtraLife} AND EVERY {GameEngine.ExtraLifeEvery}", new Point(cx, 700), 24, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "TOP DEFENDERS", new Point(cx, 130), 72, Magenta, center: true);
        Text(dc, "RANK NAME    SCORE   WAVE", new Point(cx, 196), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 240 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 700), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
