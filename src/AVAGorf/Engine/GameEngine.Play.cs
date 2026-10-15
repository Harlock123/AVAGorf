namespace AVAGorf.Engine;

public enum Mission
{
    /// <summary>A marching fleet, and a force field over the fighter.</summary>
    Armada,
    /// <summary>Two rings of fighters round laser ships.</summary>
    Lancers,
    /// <summary>A swaying formation that peels off to dive.</summary>
    Swarm,
    /// <summary>Ships spiralling out of a vortex.</summary>
    Warp,
    /// <summary>The Overlord's flagship.</summary>
    Dreadnought,
}

public sealed partial class GameEngine
{
    public const int Missions = 5;
    public const double ShipSpeed = 290, BulletSpeed = 240;
    public const double StartSeconds = 2.6, ReadySeconds = 1.6, DyingSeconds = 2.2, ClearSeconds = 2.2, DreadnoughtDownSeconds = 3.4;
    public const int FirstExtraLife = 15000, ExtraLifeEvery = 40000;
    public const double LoopSeconds = 0.7;
    /// <summary>The eye of the warp.</summary>
    public const double WarpX = Field.CentreX, WarpY = 300;
    public const int ArmadaCols = 8, ArmadaRows = 5;

    public static readonly string[] RankNames = ["CADET", "LIEUTENANT", "CAPTAIN", "COLONEL", "MARSHAL"];

    public int Wave { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public Ship Ship { get; } = new();
    /// <summary>The fighter's beam, while it's in the air.</summary>
    public Shot? Shot { get; private set; }
    public List<Bullet> Bullets { get; } = [];
    public List<Foe> Foes { get; } = [];
    public List<Beam> Beams { get; } = [];
    public ForceField? Barrier { get; private set; }
    public Dreadnought? Flagship { get; private set; }
    public ShipPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    public double WaveTime { get; private set; }
    /// <summary>What the Overlord has to say, and for how much longer.</summary>
    public string? Taunt { get; private set; }
    public double TauntTime { get; private set; }
    /// <summary>For tests: nothing moves in on, fires at or dives at the fighter by itself.</summary>
    internal bool Calm { get; set; }

    private int _nextExtra, _toCome;
    private double _diveIn, _fireIn, _spawnIn, _stepIn, _fleetX, _fleetY;
    private int _fleetDir = 1;
    private bool _firePrev, _ready;

    public Mission Mission => (Mission)((Wave - 1) % Missions);
    /// <summary>0 for a Cadet, going up a rank each time round the five missions.</summary>
    public int Rank => (Wave - 1) / Missions;
    public string RankName => RankNames[Math.Min(Rank, RankNames.Length - 1)];
    public double Pace => Math.Min(1.8, 1 + 0.16 * Rank);
    public int Remaining => Foes.Count(f => !f.Dead);
    /// <summary>How many more ships the warp has still to send out.</summary>
    public int WarpToCome => _toCome;
    /// <summary>The Swarm's formation drifts from side to side.</summary>
    public double Sway => Math.Sin(WaveTime * 0.55) * 50;
    private int MaxBullets => Math.Min(12, 4 + 2 * Rank + (Mission == Mission.Dreadnought ? 3 : 0));

    public static string NameOf(Mission m) => m switch
    {
        Mission.Armada => "THE ARMADA", Mission.Lancers => "THE LANCERS", Mission.Swarm => "THE SWARM",
        Mission.Warp => "THE WARP", _ => "THE DREADNOUGHT",
    };

    public static int Points(Foe f)
    {
        bool flying = f.State != FoeState.Holding;
        return f.Kind switch
        {
            FoeKind.Invader => f.Variant switch { 0 => 30, 1 => 20, _ => 10 },
            FoeKind.Fighter => f.Variant == 1 ? (flying ? 80 : 40) : (flying ? 100 : 50),
            FoeKind.Lancer => 300,
            FoeKind.Leader => flying ? 200 : 60,
            _ => f.Radius < 100 ? 300 : f.Radius < 200 ? 150 : 80,
        };
    }

    /// <summary>The flagship's core is worth more the higher your rank.</summary>
    public int CorePoints => 1500 + 500 * Rank;

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Wave = 1;
        _nextExtra = FirstExtraLife;
        Taunt = null;
        LayOut();
        SetPhase(Phase.Start);
    }

    private void BeginWave(int wave)
    {
        bool promoted = wave > 1 && (wave - 1) % Missions == 0;
        Wave = wave;
        LayOut();
        SetPhase(Phase.Start);
        _ready = false;
        Say($"MISSION {wave}", NameOf(Mission), StartSeconds);
        Play(Sfx.MissionStart);
        if (promoted)
        {
            Speak($"SO, YOU ARE A {RankName} NOW. IT WILL NOT SAVE YOU.");
            Play(Sfx.RankUp);
        }
        else
            Speak(Mission switch
            {
                Mission.Armada => $"MY ARMADA WILL CRUSH YOU, {RankName}",
                Mission.Lancers => "MEET MY LANCERS. THEY BURN.",
                Mission.Swarm => "THE SWARM HUNGERS",
                Mission.Warp => "FALL INTO THE WARP",
                _ => "NOW FACE MY DREADNOUGHT",
            });
    }

    private void Speak(string text, double seconds = 3)
    {
        Taunt = text;
        TauntTime = seconds;
        Play(Sfx.Taunt);
    }

    private void LayOut()
    {
        Shot = null;
        Bullets.Clear();
        Foes.Clear();
        Beams.Clear();
        Effects.Clear();
        Barrier = null;
        Flagship = null;
        WaveTime = 0;
        _diveIn = 2.5;
        _fireIn = 1.5;
        _spawnIn = 0.5;
        _stepIn = 0;
        _fleetX = _fleetY = 0;
        _fleetDir = 1;
        _toCome = 0;
        ResetShip();
        switch (Mission)
        {
            case Mission.Armada:
                Barrier = new ForceField();
                for (int r = 0; r < ArmadaRows; r++)
                    for (int c = 0; c < ArmadaCols; c++)
                        Add(FoeKind.Invader, r == 0 ? 0 : r < 3 ? 1 : 2, c, r);
                break;
            case Mission.Lancers:
                for (int g = 0; g < 2; g++)
                {
                    var l = Add(FoeKind.Lancer, 0, 0, 0);
                    l.Group = g;
                    l.FireIn = 2 + g * 1.6;
                    for (int i = 0; i < 6; i++)
                    {
                        var f = Add(FoeKind.Fighter, 0, i, 0);
                        f.Group = g;
                        f.Orbit = i * Math.PI / 3;
                    }
                }
                break;
            case Mission.Swarm:
                for (int c = 0; c < 8; c++)
                {
                    if (c is 2 or 5) Add(FoeKind.Leader, 0, c, 0);
                    if (c is >= 1 and <= 6) Add(FoeKind.Fighter, 1, c, 1);
                    Add(FoeKind.Fighter, 0, c, 2);
                    Add(FoeKind.Fighter, 0, c, 3);
                }
                break;
            case Mission.Warp:
                _toCome = 16 + 2 * Rank;
                break;
            case Mission.Dreadnought:
                Flagship = new Dreadnought { Y = -120 };
                break;
        }
        foreach (var f in Foes) (f.X, f.Y) = Slot(f);
    }

    private Foe Add(FoeKind kind, int variant, int col, int row)
    {
        var f = new Foe { Kind = kind, Variant = variant, Col = col, Row = row, Phase = _rng.NextDouble() * Math.PI * 2 };
        Foes.Add(f);
        return f;
    }

    private void ResetShip()
    {
        Ship.X = Field.CentreX;
        Ship.Y = Field.ShipHome;
        Ship.Alive = true;
    }

    /// <summary>Where a foe belongs when it's in its place.</summary>
    public (double X, double Y) Slot(Foe f)
    {
        switch (f.Kind)
        {
            case FoeKind.Invader:
                return (Field.CentreX + (f.Col - (ArmadaCols - 1) / 2.0) * 54 + _fleetX, 132 + f.Row * 38 + _fleetY);
            case FoeKind.Lancer:
                return LancerCentre(f.Group);
            case FoeKind.Fighter when Mission == Mission.Lancers:
            {
                var (cx, cy) = LancerCentre(f.Group);
                double a = f.Orbit + WaveTime * 1.3 * (f.Group == 0 ? 1 : -1);
                return (cx + Math.Cos(a) * 62, cy + Math.Sin(a) * 50);
            }
            default:
                return (Field.CentreX + (f.Col - 3.5) * 50 + Sway, 128 + f.Row * 38);
        }
    }

    private (double X, double Y) LancerCentre(int g) =>
        (Field.CentreX + (g == 0 ? -150 : 150) + Math.Sin(WaveTime * 0.45 + g * Math.PI) * 70, 220 + Math.Sin(WaveTime * 0.7 + g) * 30);

    // ───────────────────────────── a step of play ─────────────────────────────

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        if (TauntTime > 0) { TauntTime -= dt; if (TauntTime <= 0) Taunt = null; }
        switch (Phase)
        {
            case Phase.Start:
                MoveShip(c, dt, canFire: false);
                if (Mission != Mission.Armada) WaveTime += dt;
                MoveFoes(dt, active: false);
                MoveFlagship(dt, active: false);
                if (PhaseTime > (_ready ? ReadySeconds : StartSeconds))
                {
                    SetPhase(Phase.Play);
                    if (State == GameState.Playing) Music(true);
                }
                break;
            case Phase.Play:
                WaveTime += dt;
                MoveShip(c, dt, canFire: true);
                MoveShot(dt);
                MoveFoes(dt, active: true);
                MoveFlagship(dt, active: true);
                MoveBeams(dt);
                MoveBullets(dt);
                Hits();
                if (Phase == Phase.Play) Progress();
                break;
            case Phase.Dying:
                MoveShot(dt);
                MoveFoes(dt, active: false);
                MoveFlagship(dt, active: false);
                MoveBullets(dt);
                Beams.Clear();
                if (PhaseTime > DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    Respawn();
                }
                break;
            case Phase.Clear:
                MoveBullets(dt);
                if (PhaseTime > (Mission == Mission.Dreadnought ? DreadnoughtDownSeconds : ClearSeconds)) BeginWave(Wave + 1);
                break;
        }
    }

    // ───────────────────────────── the fighter ─────────────────────────────

    private void MoveShip(Controls c, double dt, bool canFire)
    {
        bool fresh = c.Fire && !_firePrev;
        _firePrev = c.Fire;
        var s = Ship;
        if (!s.Alive) return;
        s.X = Math.Clamp(s.X + Math.Sign(c.Dx) * ShipSpeed * dt, Field.Left + 20, Field.Right - 20);
        s.Y = Math.Clamp(s.Y + Math.Sign(c.Dy) * ShipSpeed * 0.8 * dt, Field.ZoneTop + 14, Field.ZoneBottom);
        // One beam at a time: firing again cancels the one in the air.
        if (canFire && fresh)
        {
            Shot = new Shot { X = s.X, Y = s.Y - 20 };
            Play(Sfx.Fire);
        }
    }

    private void MoveShot(double dt)
    {
        if (Shot is not { } s) return;
        for (int k = 0; k < 4 && !s.Dead; k++)
        {
            s.Y -= Shot.Speed * dt / 4;
            if (s.Y < Field.Top - 10) s.Dead = true;
            else if (Phase == Phase.Play) s.Dead = ShotHits(s);
        }
        if (s.Dead) Shot = null;
    }

    private bool ShotHits(Shot s)
    {
        foreach (var f in Foes)
        {
            if (f.Dead) continue;
            double r = f.Size;
            if (Math.Abs(f.X - s.X) > r + 2 || Math.Abs(f.Y - s.Y) > r) continue;
            Kill(f);
            return true;
        }
        if (Flagship is { CoreAlive: true } d && Math.Abs(s.X - d.X) <= Dreadnought.HalfWidth + 6)
        {
            double ly = s.Y - d.Y;
            int col = d.ColAt(s.X);
            // Up through the band, bottom row first.
            if (col >= 0)
                for (int r = Dreadnought.BandRows - 1; r >= 0; r--)
                {
                    double top = Dreadnought.BandTop + r * Dreadnought.RowH;
                    if (d.Band[r, col] && ly <= top + Dreadnought.RowH && ly > top - 8)
                    {
                        d.Band[r, col] = false;
                        Spark(s.X, d.Y + top + 4, 0xFF6AE0FF);
                        AddScore(10);
                        Play(Sfx.ShieldChip);
                        return true;
                    }
                }
            // The core, through a hole; the hull stops anything else.
            if (Math.Abs(s.X - d.X) < 11 && ly <= 34 && ly > 10)
            {
                DestroyFlagship(d);
                return true;
            }
            if (ly <= 30 && ly > 0)
            {
                Spark(s.X, s.Y, 0xFFFFA040);
                Play(Sfx.ShieldChip);
                return true;
            }
        }
        return false;
    }

    private void Kill(Foe f)
    {
        int points = Points(f);
        f.Dead = true;
        AddScore(points);
        bool big = f.Kind is FoeKind.Lancer or FoeKind.Leader || points >= 150;
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = f.X, Y = f.Y, Life = big ? 0.8 : 0.5, Size = big ? 1.5 : 1, Colour = Colour(f) });
        if (f.State != FoeState.Holding || big)
            Effects.Add(new Effect { Kind = EffectKind.Score, X = f.X, Y = f.Y - 14, Text = points.ToString(), Life = 0.9, Colour = points >= 200 ? 0xFFFFE04A : 0xFFFFFFFF });
        Play(big ? Sfx.BigHit : Sfx.Hit);
        if (f.Kind == FoeKind.Lancer) Beams.RemoveAll(b => b.Owner == f);
    }

    private void DestroyFlagship(Dreadnought d)
    {
        d.CoreAlive = false;
        AddScore(CorePoints);
        Effects.Add(new Effect { Kind = EffectKind.Blast, X = d.X, Y = d.Y + 20, Life = 2.6, Size = 2.4 });
        Effects.Add(new Effect { Kind = EffectKind.Word, X = d.X, Y = d.Y + 90, Text = CorePoints.ToString(), Life = 2.4, Colour = 0xFFFFE04A });
        Play(Sfx.CoreHit);
        Play(Sfx.Explode);
        Speak("NOOO... I WILL RETURN!", 3);
    }

    public static uint Colour(Foe f) => f.Kind switch
    {
        FoeKind.Invader => f.Variant switch { 0 => 0xFFFF5AD0, 1 => 0xFF5AE0FF, _ => 0xFF7AFF6A },
        FoeKind.Lancer => 0xFFFF9A2A,
        FoeKind.Leader => 0xFFFFD84A,
        FoeKind.Warper => 0xFFE06AFF,
        _ => f.Variant == 1 ? 0xFFD06AFF : 0xFF4AA8FF,
    };

    private void Spark(double x, double y, uint colour) =>
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = x, Y = y, Life = 0.25, Size = 0.4, Colour = colour });

    // ───────────────────────────── the enemy ─────────────────────────────

    private void MoveFoes(double dt, bool active)
    {
        if (Mission == Mission.Armada) MoveFleet(dt, active);
        foreach (var f in Foes)
        {
            if (f.Dead) continue;
            f.T += dt;
            double px = f.X, py = f.Y;
            if (f.Kind == FoeKind.Warper) Spiral(f, dt, active);
            else if (f.Kind == FoeKind.Invader) (f.X, f.Y) = Slot(f);
            else
                switch (f.State)
                {
                    case FoeState.Holding:
                        (f.X, f.Y) = Slot(f);
                        f.Angle = 0;
                        break;
                    case FoeState.Diving:
                        Dive(f, dt, active);
                        break;
                    case FoeState.Returning:
                        Return(f, dt);
                        break;
                }
            if (f.State != FoeState.Diving) (f.Vx, f.Vy) = ((f.X - px) / dt, (f.Y - py) / dt);
        }
        if (active && !Calm)
        {
            switch (Mission)
            {
                case Mission.Armada: FleetFire(dt); break;
                case Mission.Lancers: LancerFire(dt); Launch(dt); break;
                case Mission.Swarm: Launch(dt); break;
                case Mission.Warp: Spawn(dt); break;
            }
        }
    }

    /// <summary>The Armada marches from side to side, stepping down at each edge, and quicker the fewer are left.</summary>
    private void MoveFleet(double dt, bool active)
    {
        if (!active || Calm) return;
        var alive = Foes.Where(f => !f.Dead).ToList();
        if (alive.Count == 0) return;
        double left = alive.Count / (double)(ArmadaRows * ArmadaCols);
        double speed = (34 + 120 * (1 - left)) * Pace;
        _fleetX += _fleetDir * speed * dt;
        double minX = alive.Min(f => Slot(f).X), maxX = alive.Max(f => Slot(f).X);
        if (minX < Field.Left + 22 && _fleetDir < 0 || maxX > Field.Right - 22 && _fleetDir > 0)
        {
            _fleetDir = -_fleetDir;
            _fleetY += 16;
        }
        _stepIn -= dt;
        if (_stepIn <= 0)
        {
            _stepIn = 0.15 + 0.5 * left;
            Play(Sfx.Step);
        }
        // They wear the force field away as they come down into it.
        if (Barrier is { } ff)
            foreach (var f in alive)
                for (int c = ForceField.ColAt(f.X - 14); c <= ForceField.ColAt(f.X + 14) && c >= 0; c++)
                    for (int r = 0; r < ForceField.Rows; r++)
                        if (ff.Cells[r, c] && f.Y + 12 >= ForceField.TopOf(c) + r * ForceField.CellH) ff.Cells[r, c] = false;
    }

    private void FleetFire(double dt)
    {
        _fireIn -= dt;
        if (_fireIn > 0 || Bullets.Count >= MaxBullets) return;
        _fireIn = (0.7 + 0.6 * _rng.NextDouble()) / Pace;
        // The lowest of a column, usually one near the fighter.
        var bottoms = Foes.Where(f => !f.Dead).GroupBy(f => f.Col).Select(g => g.MaxBy(f => f.Row)!).ToList();
        if (bottoms.Count == 0) return;
        var f = _rng.NextDouble() < 0.6 ? bottoms.MinBy(b => Math.Abs(b.X - Ship.X))! : bottoms[_rng.Next(bottoms.Count)];
        bool bomb = _rng.NextDouble() < 0.25;
        Bullets.Add(new Bullet { X = f.X, Y = f.Y + 12, Vy = BulletSpeed * Pace * (bomb ? 0.7 : 1), Bomb = bomb });
        Play(Sfx.AlienFire);
    }

    /// <summary>Each Lancer now and then warns, then fires its laser straight down.</summary>
    private void LancerFire(double dt)
    {
        foreach (var l in Foes.Where(f => !f.Dead && f.Kind == FoeKind.Lancer))
        {
            if (Beams.Any(b => b.Owner == l)) continue;
            l.FireIn -= dt;
            if (l.FireIn > 0) continue;
            l.FireIn = (3.4 + 1.6 * _rng.NextDouble()) / Pace;
            Beams.Add(new Beam { X = l.X, Y = l.Y + 18, Owner = l });
            Play(Sfx.BeamWarn);
        }
    }

    private void MoveBeams(double dt)
    {
        foreach (var b in Beams)
        {
            bool was = b.Firing;
            b.T += dt;
            if (b.Firing && !was) Play(Sfx.Beam);
        }
        Beams.RemoveAll(b => b.Done || b.Owner.Dead);
    }

    /// <summary>Every so often one peels off to dive (in the Swarm, the last few without a break).</summary>
    private void Launch(double dt)
    {
        var holding = Foes.Where(f => !f.Dead && f.State == FoeState.Holding && f.Kind is FoeKind.Fighter or FoeKind.Leader).ToList();
        if (holding.Count == 0) return;
        int flying = Foes.Count(f => !f.Dead && f.State == FoeState.Diving);
        if (Mission == Mission.Swarm && Remaining <= 4)
        {
            if (flying < 2) StartDive(holding[_rng.Next(holding.Count)]);
            return;
        }
        _diveIn -= dt;
        if (_diveIn > 0 || flying >= Math.Min(6, 2 + Rank)) return;
        _diveIn = Math.Max(0.6, 2.2 - 0.3 * Rank) * (0.6 + 0.8 * _rng.NextDouble());
        StartDive(holding[_rng.Next(holding.Count)]);
    }

    private void StartDive(Foe f)
    {
        f.State = FoeState.Diving;
        f.T = 0;
        f.Side = f.X < Field.CentreX ? -1 : 1;
        f.TargetX = Ship.X;
        f.Shots = (f.Kind == FoeKind.Leader ? 3 : 2) + (Rank >= 1 ? 1 : 0);
        f.FireIn = 0.2 + _rng.NextDouble() * 0.4;
        Play(Sfx.Dive);
    }

    /// <summary>A loop up and out, then down at the fighter in an arc, firing, and on off the bottom.</summary>
    private void Dive(Foe f, double dt, bool active)
    {
        double speed = (210 + Math.Min(f.T, 2) * 20) * Pace;
        double heading = Math.Atan2(f.Vy, f.Vx);
        if (f.T < LoopSeconds)
        {
            heading = -Math.PI / 2 + f.Side * Math.PI * f.T / LoopSeconds;
            speed = 170;
        }
        else if (f.Y < Ship.Y - 130)
        {
            f.TargetX += Math.Clamp(Ship.X - f.TargetX, -160 * dt, 160 * dt);
            double want = Math.Atan2(Ship.Y + 60 - f.Y, f.TargetX - f.X);
            double turn = Math.Atan2(Math.Sin(want - heading), Math.Cos(want - heading));
            heading += Math.Clamp(turn, -2.4 * dt, 2.4 * dt);
            heading = Math.Clamp(heading, 0.35, Math.PI - 0.35);
        }
        (f.Vx, f.Vy) = (Math.Cos(heading) * speed, Math.Sin(heading) * speed);
        f.X += f.Vx * dt;
        f.Y += f.Vy * dt;
        if (f.X < Field.Left + 14 && f.Vx < 0 || f.X > Field.Right - 14 && f.Vx > 0) { f.Vx = -f.Vx; f.X = Math.Clamp(f.X, Field.Left + 14, Field.Right - 14); }
        f.Angle = Math.Atan2(f.Vx, f.Vy);
        if (active) Shoot(f, dt);
        if (f.Y > Field.Bottom + 24)
        {
            f.Y = Field.Top - 40;
            f.T = 0;
            f.State = FoeState.Returning;
        }
    }

    private void Return(Foe f, double dt)
    {
        double speed = 180 * Pace;
        var (tx, ty) = Slot(f);
        double dx = tx - f.X, dy = ty - f.Y, d = Math.Sqrt(dx * dx + dy * dy);
        f.Angle = 0;
        if (d < speed * dt + 1)
        {
            (f.X, f.Y) = (tx, ty);
            f.State = FoeState.Holding;
            f.T = 0;
        }
        else
        {
            f.X += dx / d * speed * dt;
            f.Y += dy / d * speed * dt;
        }
    }

    private void Shoot(Foe f, double dt)
    {
        if (Calm || f.Shots <= 0) return;
        f.FireIn -= dt;
        if (f.FireIn > 0 || f.Y < 200 || f.Y > Ship.Y - 160 || Bullets.Count >= MaxBullets) return;
        f.Shots--;
        f.FireIn = (0.25 + 0.3 * _rng.NextDouble()) / Pace;
        AimedBullet(f.X, f.Y + 12, 110);
    }

    private void AimedBullet(double x, double y, double maxVx, bool bomb = false)
    {
        double vy = BulletSpeed * Pace * (bomb ? 0.75 : 1);
        double t = Math.Max(0.3, (Ship.Y - y) / vy);
        double vx = Math.Clamp((Ship.X - x) / t, -maxVx, maxVx) + (_rng.NextDouble() - 0.5) * 30;
        Bullets.Add(new Bullet { X = x, Y = y, Vx = vx, Vy = vy, Bomb = bomb });
        Play(Sfx.AlienFire);
    }

    /// <summary>The warp sends its ships out one after another, a few at a time.</summary>
    private void Spawn(double dt)
    {
        if (_toCome <= 0) return;
        _spawnIn -= dt;
        if (_spawnIn > 0 || Remaining >= Math.Min(6, 3 + Rank)) return;
        _spawnIn = (0.9 + 0.6 * _rng.NextDouble()) / Pace;
        _toCome--;
        var f = Add(FoeKind.Warper, 0, 0, 0);
        f.Orbit = _rng.NextDouble() * Math.PI * 2;
        f.Spin = _rng.Next(2) == 0 ? -1 : 1;
        f.FireIn = 1 + _rng.NextDouble();
        f.Shots = 99;
        (f.X, f.Y) = (WarpX, WarpY);
        Play(Sfx.Warp);
    }

    /// <summary>Round and out from the eye of the warp, quicker as it comes; one that gets away goes back in.</summary>
    private void Spiral(Foe f, double dt, bool active)
    {
        f.Radius += (45 + f.Radius * 0.4) * Pace * dt;
        f.Orbit += f.Spin * (2.6 - Math.Min(1.8, f.Radius / 180)) * dt;
        f.X = WarpX + Math.Cos(f.Orbit) * f.Radius * 1.1;
        f.Y = WarpY + Math.Sin(f.Orbit) * f.Radius * 0.8;
        f.Angle = f.Orbit * 2;
        bool gone = f.X < Field.Left - 30 || f.X > Field.Right + 30 || f.Y > Field.Bottom + 30 || f.Y < Field.Top - 30;
        if (gone)
        {
            f.Radius = 0;
            f.T = 0;
            (f.X, f.Y) = (WarpX, WarpY);
            return;
        }
        if (!active || Calm || f.Radius < 80) return;
        f.FireIn -= dt;
        if (f.FireIn <= 0 && Bullets.Count < MaxBullets && f.Y < Ship.Y - 100)
        {
            f.FireIn = (1.2 + _rng.NextDouble()) / Pace;
            AimedBullet(f.X, f.Y + 8, 160);
        }
    }

    /// <summary>The flagship comes down, then drifts from side to side, firing from both cannons and dropping bombs.</summary>
    private void MoveFlagship(double dt, bool active)
    {
        if (Flagship is not { CoreAlive: true } d) return;
        d.T += dt;
        if (d.Y < 150) { d.Y = Math.Min(150, d.Y + 140 * dt); return; }
        d.Y = 150 + Math.Sin(d.T * 0.8) * 12;
        if (!active || Calm) return;
        d.X += d.Dx * 55 * Pace * dt;
        double lo = Field.Left + Dreadnought.HalfWidth + 30, hi = Field.Right - Dreadnought.HalfWidth - 30;
        if (d.X < lo && d.Dx < 0 || d.X > hi && d.Dx > 0) d.Dx = -d.Dx;
        d.FireIn -= dt;
        if (d.FireIn <= 0 && Bullets.Count < MaxBullets)
        {
            d.FireIn = (0.9 + 0.5 * _rng.NextDouble()) / Pace;
            double side = _rng.Next(2) == 0 ? -1 : 1;
            AimedBullet(d.X + side * (Dreadnought.HalfWidth + 6), d.Y + 24, 140);
        }
        d.BombIn -= dt;
        if (d.BombIn <= 0 && Bullets.Count < MaxBullets)
        {
            d.BombIn = (2.6 + _rng.NextDouble()) / Pace;
            AimedBullet(d.X, d.Y + 70, 90, bomb: true);
        }
        // The band turns, carrying any hole away from the core, and slowly mends itself.
        d.TurnIn -= dt;
        if (d.TurnIn <= 0)
        {
            d.TurnIn = 1.4 / Pace;
            d.Turn();
        }
        // The band slowly mends itself.
        d.RegrowIn -= dt;
        if (d.RegrowIn <= 0)
        {
            d.RegrowIn = 3.5 / Pace;
            var holes = new List<(int R, int C)>();
            for (int r = 0; r < Dreadnought.BandRows; r++)
                for (int c = 0; c < Dreadnought.BandCols; c++)
                    if (!d.Band[r, c]) holes.Add((r, c));
            if (holes.Count > 0) { var (hr, hc) = holes[_rng.Next(holes.Count)]; d.Band[hr, hc] = true; }
        }
    }

    private void MoveBullets(double dt)
    {
        foreach (var b in Bullets)
        {
            b.X += b.Vx * dt;
            b.Y += b.Vy * dt;
            if (b.Y > Field.Bottom + 10 || b.X < Field.Left - 10 || b.X > Field.Right + 10) b.Dead = true;
            else if (Barrier is { } ff && Phase == Phase.Play) WearField(ff, b);
        }
        Bullets.RemoveAll(b => b.Dead);
    }

    /// <summary>A bullet meeting the force field takes a cell (a bomb, a bigger bite) and is stopped.</summary>
    private void WearField(ForceField ff, Bullet b)
    {
        int c = ForceField.ColAt(b.X);
        if (c < 0) return;
        for (int r = 0; r < ForceField.Rows; r++)
        {
            double top = ForceField.TopOf(c) + r * ForceField.CellH;
            if (!ff.Cells[r, c] || b.Y < top || b.Y > top + ForceField.CellH + 6) continue;
            int reach = b.Bomb ? 2 : 0;
            for (int dc = -reach; dc <= reach; dc++)
                for (int dr = 0; dr <= (b.Bomb ? 1 : 0); dr++)
                    if (c + dc is >= 0 and < ForceField.Cols && r + dr < ForceField.Rows) ff.Cells[r + dr, c + dc] = false;
            b.Dead = true;
            Play(Sfx.ShieldChip);
            return;
        }
    }

    private void Hits()
    {
        var s = Ship;
        if (!s.Alive) return;
        foreach (var b in Bullets)
        {
            double reach = b.Bomb ? 18 : 12;
            if (Math.Abs(b.X - s.X) >= reach || Math.Abs(b.Y - s.Y) >= 14) continue;
            b.Dead = true;
            Die("SHOT DOWN");
            return;
        }
        foreach (var b in Beams)
        {
            if (!b.Firing || Math.Abs(b.X - s.X) >= Beam.HalfWidth + 12 || s.Y < b.Y) continue;
            Die("LASERED");
            return;
        }
        foreach (var f in Foes)
        {
            if (f.Dead) continue;
            if (f.Kind == FoeKind.Invader)
            {
                // The Armada landing.
                if (f.Y > Field.ZoneTop - 10) { Die("OVERRUN"); return; }
                continue;
            }
            if (f.State == FoeState.Holding && f.Kind != FoeKind.Warper) continue;
            if (Math.Abs(f.X - s.X) >= f.Size + 12 || Math.Abs(f.Y - s.Y) >= f.Size + 8) continue;
            Kill(f);
            Die("RAMMED");
            return;
        }
    }

    private void Die(string cause)
    {
        Cause = cause;
        Ship.Alive = false;
        Lives--;
        Shot = null;
        Effects.Add(new Effect { Kind = EffectKind.Blast, X = Ship.X, Y = Ship.Y, Life = 1.4, Size = 1 });
        SetPhase(Phase.Dying);
        Music(false);
        Play(Sfx.Death);
        string[] jeers = ["HA! ANOTHER ONE FOR MY COLLECTION", $"IS THAT YOUR BEST, {RankName}?", "PATHETIC", $"RUN HOME, LITTLE {RankName}", "YOU AMUSE ME"];
        Speak(jeers[_rng.Next(jeers.Length)]);
    }

    /// <summary>The next fighter, with the divers called home, the Armada pulled back and the warp emptied.</summary>
    private void Respawn()
    {
        ResetShip();
        Bullets.Clear();
        Beams.Clear();
        Shot = null;
        foreach (var f in Foes.Where(f => !f.Dead && f.State == FoeState.Diving))
        {
            f.State = FoeState.Returning;
            f.T = 0;
        }
        if (Mission == Mission.Armada) _fleetY = Cause == "OVERRUN" ? 0 : Math.Max(0, _fleetY - 130);
        foreach (var f in Foes.Where(f => !f.Dead && f.Kind == FoeKind.Warper))
        {
            f.Radius = 0;
            (f.X, f.Y) = (WarpX, WarpY);
        }
        SetPhase(Phase.Start);
        _ready = true;
        _diveIn = 2;
        Say("READY", null, ReadySeconds);
    }

    private void Progress()
    {
        if (Calm) return;
        bool done = Mission switch
        {
            Mission.Dreadnought => Flagship is { CoreAlive: false },
            Mission.Warp => _toCome == 0 && Remaining == 0,
            _ => Remaining == 0,
        };
        if (!done) return;
        Foes.Clear();
        Beams.Clear();
        Bullets.Clear();
        Shot = null;
        SetPhase(Phase.Clear);
        Music(false);
        Say("MISSION COMPLETE", Mission == Mission.Dreadnought ? $"RANK UP: {RankNames[Math.Min(Rank + 1, RankNames.Length - 1)]}" : null, ClearSeconds);
        Play(Sfx.Clear);
    }

    private void AddScore(int points)
    {
        Score += points;
        while (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into the play of a mission.</summary>
    internal void Go(int wave = 1)
    {
        StartGame();
        BeginWave(wave);
        if (Flagship is { } d) d.Y = 150;
        SetPhase(Phase.Play);
        Message = SubMessage = null;
    }

    internal void ClearFoes() => Foes.Clear();

    internal Foe PutFoe(FoeKind kind, double x, double y, FoeState state = FoeState.Holding, int variant = 0)
    {
        var f = Add(kind, variant, 0, 0);
        (f.X, f.Y, f.State) = (x, y, state);
        if (state == FoeState.Diving) { f.T = LoopSeconds; f.TargetX = x; (f.Vx, f.Vy) = (0, 1); }
        return f;
    }

    internal void PutBullet(double x, double y, double vx = 0, double vy = BulletSpeed, bool bomb = false) =>
        Bullets.Add(new Bullet { X = x, Y = y, Vx = vx, Vy = vy, Bomb = bomb });

    internal void SendDive(Foe f) => StartDive(f);
    internal void SetShip(double x, double y = Field.ShipHome) => (Ship.X, Ship.Y) = (x, y);
    internal void SetLives(int n) => Lives = n;
    internal void SetScore(int n) => Score = n;
    internal void LowerFleet(double dy) => _fleetY += dy;
}
