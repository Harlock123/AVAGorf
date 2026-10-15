namespace AVAGorf.Engine;

/// <summary>Fly left/right (Dx) and up/down (Dy) about the lower part of the screen, and fire.</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false);

/// <summary>
/// The screen, in pixels: the battle is fought in the column between <see cref="Left"/> and <see cref="Right"/>;
/// the fighter can fly anywhere between <see cref="ZoneTop"/> and <see cref="ZoneBottom"/>.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double Left = 192, Right = 832, Top = 70, Bottom = 768;
    public const double CentreX = (Left + Right) / 2;
    public const double ZoneTop = 566, ZoneBottom = 730;
    public const double ShipHome = 712;
}

public sealed class Ship
{
    public double X = Field.CentreX, Y = Field.ShipHome;
    public bool Alive = true;
}

/// <summary>The fighter's beam: one at a time, and firing again cancels it.</summary>
public sealed class Shot
{
    public const double Speed = 720;
    public double X, Y;
    public bool Dead;
}

public sealed class Bullet
{
    public double X, Y, Vx, Vy;
    /// <summary>A bomb is bigger and slower, and blows a bigger hole in the force field.</summary>
    public bool Bomb, Dead;
}

public enum FoeKind
{
    /// <summary>The Armada's marchers: Variant 0 the top row (worth most) to 2 the bottom rows.</summary>
    Invader,
    /// <summary>A Lancer's ring of fighters, and the Swarm's rank and file (Variant 0 drones, 1 stingers).</summary>
    Fighter,
    /// <summary>A laser ship at the heart of a ring.</summary>
    Lancer,
    /// <summary>The Swarm's leaders.</summary>
    Leader,
    /// <summary>A ship spiralling out of the warp.</summary>
    Warper,
}

public enum FoeState
{
    /// <summary>In its place (in the fleet, the ring or the formation).</summary>
    Holding,
    /// <summary>Peeled off and diving at the fighter.</summary>
    Diving,
    /// <summary>Round from the top, back to its place.</summary>
    Returning,
}

public sealed class Foe
{
    public FoeKind Kind;
    public int Variant, Col, Row;
    public FoeState State = FoeState.Holding;
    public double X, Y, Vx, Vy, Angle, T, Phase;
    public bool Dead;
    public double FireIn, TargetX;
    public int Side = 1, Shots;
    /// <summary>For a Lancer's ring: which Lancer, and where round it this one flies.</summary>
    public int Group;
    public double Orbit;
    /// <summary>For a Warper: how far out from the warp's eye, and which way round.</summary>
    public double Radius, Spin = 1;
    public double Size => Kind switch { FoeKind.Lancer => 20, FoeKind.Leader => 16, FoeKind.Warper => 4 + Math.Min(12, Radius / 14), _ => 14 };
}

/// <summary>A Lancer's laser: a warning line first, then the beam itself, straight down.</summary>
public sealed class Beam
{
    public const double WarnSeconds = 0.75, FireSeconds = 0.6, HalfWidth = 9;
    public double X, Y, T;
    public Foe Owner = null!;
    public bool Firing => T >= WarnSeconds;
    public bool Done => T >= WarnSeconds + FireSeconds;
}

/// <summary>The Overlord's flagship: a hull, a shield band beneath it and the reactor core in the middle.</summary>
public sealed class Dreadnought
{
    public const int BandCols = 22, BandRows = 3, HullCols = 18;
    public const double CellW = 13, BandTop = 44, RowH = 9, HalfWidth = BandCols * CellW / 2;
    public double X = Field.CentreX, Y = 150, Dx = 1, T, FireIn = 1.5, BombIn = 3, RegrowIn = 4, TurnIn = 1.4;
    public readonly bool[,] Band = new bool[BandRows, BandCols];
    public bool CoreAlive = true;
    public int Hits;

    public Dreadnought()
    {
        for (int r = 0; r < BandRows; r++)
            for (int c = 0; c < BandCols; c++)
                Band[r, c] = true;
    }

    /// <summary>The band turns: every column moves one place along.</summary>
    public void Turn()
    {
        for (int r = 0; r < BandRows; r++)
        {
            bool last = Band[r, BandCols - 1];
            for (int c = BandCols - 1; c > 0; c--) Band[r, c] = Band[r, c - 1];
            Band[r, 0] = last;
        }
    }

    /// <summary>The band column under <paramref name="x"/>, or -1.</summary>
    public int ColAt(double x)
    {
        int c = (int)Math.Floor((x - (X - HalfWidth)) / CellW);
        return c is >= 0 and < BandCols ? c : -1;
    }
}

/// <summary>The arched force field over the fighter in the Armada mission: bombs wear it away.</summary>
public sealed class ForceField
{
    public const int Cols = 64, Rows = 3;
    public const double CellW = 9, CellH = 7, LeftX = Field.CentreX - Cols * CellW / 2, Peak = 470, Ends = 540;
    public readonly bool[,] Cells = new bool[Rows, Cols];

    public ForceField()
    {
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                Cells[r, c] = true;
    }

    /// <summary>The top of column <paramref name="c"/>: an arch, highest in the middle.</summary>
    public static double TopOf(int c)
    {
        double u = (c + 0.5) / Cols * 2 - 1;
        return Peak + (Ends - Peak) * u * u;
    }

    public static int ColAt(double x)
    {
        int c = (int)Math.Floor((x - LeftX) / CellW);
        return c is >= 0 and < Cols ? c : -1;
    }

    public int Count => Cells.Cast<bool>().Count(b => b);
}

public enum EffectKind { Score, Burst, Word, Blast }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.7, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
