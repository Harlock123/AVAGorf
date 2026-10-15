using AVAGorf.Engine;

namespace AVAGorf.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avagorf-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A mission in play with its enemy in place (or none, if a test wants its own), and nothing attacking on its own.</summary>
    private static GameEngine Game(int wave = 1, bool keepFoes = false)
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.Go(wave);
        if (!keepFoes) g.ClearFoes();
        return g;
    }

    /// <summary>A real game in play, with plenty of fighters.</summary>
    private static GameEngine Real(int wave = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(wave);
        g.SetLives(99);
        return g;
    }

    private static readonly Controls FireButton = new(Fire: true);

    /// <summary>Fires and runs until the shot is spent.</summary>
    private static void Shoot(GameEngine g)
    {
        g.Tick(default);
        g.Tick(FireButton);
        for (int i = 0; i < 120 && g.Shot != null; i++) g.Tick(default);
    }

    // ───────────────────────────── the missions ─────────────────────────────

    [Fact]
    public void The_five_missions_come_in_order_then_round_again_a_rank_higher_and_faster()
    {
        var order = Enumerable.Range(1, 6).Select(w => Game(w).Mission).ToList();
        Assert.Equal([Mission.Armada, Mission.Lancers, Mission.Swarm, Mission.Warp, Mission.Dreadnought, Mission.Armada], order);
        Assert.Equal("CADET", Game(5).RankName);
        Assert.Equal("LIEUTENANT", Game(6).RankName);
        Assert.Equal("MARSHAL", Game(40).RankName);
        Assert.True(Game(6).Pace > Game(5).Pace);
    }

    [Fact]
    public void Each_mission_sets_out_its_own_enemy()
    {
        var a = Game(1, keepFoes: true);
        Assert.Equal(40, a.Foes.Count(f => f.Kind == FoeKind.Invader));
        Assert.NotNull(a.Barrier);
        var l = Game(2, keepFoes: true);
        Assert.Equal(2, l.Foes.Count(f => f.Kind == FoeKind.Lancer));
        Assert.Equal(12, l.Foes.Count(f => f.Kind == FoeKind.Fighter));
        var s = Game(3, keepFoes: true);
        Assert.Equal(2, s.Foes.Count(f => f.Kind == FoeKind.Leader));
        Assert.Equal(22, s.Foes.Count(f => f.Kind == FoeKind.Fighter));
        var w = Game(4, keepFoes: true);
        Assert.Empty(w.Foes);
        Assert.Equal(16, w.WarpToCome);
        var d = Game(5, keepFoes: true);
        Assert.NotNull(d.Flagship);
        Assert.Null(d.Barrier);
    }

    [Fact]
    public void Each_mission_opens_with_its_name_and_a_word_from_the_Overlord()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.StartGame();
        Assert.Equal("MISSION 1", g.Message);
        Assert.Equal("THE ARMADA", g.SubMessage);
        Assert.Contains("ARMADA", g.Taunt);
    }

    // ───────────────────────────── the fighter ─────────────────────────────

    [Fact]
    public void The_fighter_flies_about_the_bottom_of_the_screen_and_no_further()
    {
        var g = Game();
        g.Run(200, new Controls(Dx: -1, Dy: -1));
        Assert.Equal(Field.Left + 20, g.Ship.X, 1);
        Assert.Equal(Field.ZoneTop + 14, g.Ship.Y, 1);
        g.Run(300, new Controls(Dx: 1, Dy: 1));
        Assert.Equal(Field.Right - 20, g.Ship.X, 1);
        Assert.Equal(Field.ZoneBottom, g.Ship.Y, 1);
    }

    [Fact]
    public void Firing_again_cancels_the_shot_in_flight()
    {
        var g = Game();
        g.Tick(FireButton);
        var first = g.Shot;
        Assert.NotNull(first);
        g.Idle(10);
        double high = first!.Y;
        g.Tick(FireButton);
        Assert.NotSame(first, g.Shot);
        Assert.True(g.Shot!.Y > high, "the new shot starts again from the fighter");
        // Holding the button doesn't fire again.
        var second = g.Shot;
        g.Run(5, FireButton);
        Assert.Same(second, g.Shot);
    }

    // ───────────────────────────── the Armada ─────────────────────────────

    [Fact]
    public void The_Armada_marches_and_steps_down_at_the_edges()
    {
        var g = Real();
        var f = g.Foes[0];
        double y0 = f.Y;
        g.Calm = false;
        var xs = new List<double>();
        for (int i = 0; i < 60 * 20 && f.Y == y0; i++) { g.Tick(default); xs.Add(f.X); }
        Assert.True(f.Y > y0, "it stepped down");
        Assert.True(xs.Max() - xs.Min() > 20, "it marched sideways first");
    }

    [Fact]
    public void Invaders_are_worth_30_20_and_10_by_row()
    {
        var g = Game(1, keepFoes: true);
        var top = g.Foes.First(f => f.Row == 0);
        var mid = g.Foes.First(f => f.Row == 2);
        var low = g.Foes.First(f => f.Row == 4);
        Assert.Equal([30, 20, 10], new[] { top, mid, low }.Select(GameEngine.Points));
        // The bottom row of a column first: the beam stops at the first it meets.
        g.SetShip(low.X);
        Shoot(g);
        Assert.True(low.Dead);
        Assert.Equal(10, g.Score);
    }

    [Fact]
    public void The_beam_passes_through_the_force_field_but_bullets_and_bombs_wear_it_away()
    {
        var g = Game(1);
        var ff = g.Barrier!;
        int full = ff.Count;
        Shoot(g);
        Assert.Equal(full, ff.Count);
        g.PutBullet(Field.CentreX, 420);
        g.Idle(30);
        Assert.Equal(full - 1, ff.Count);
        Assert.Empty(g.Bullets);
        g.PutBullet(Field.CentreX + 100, 420, bomb: true);
        g.Idle(40);
        Assert.True(full - 1 - ff.Count >= 5, "a bomb takes a bigger bite");
    }

    [Fact]
    public void The_Armada_landing_costs_a_fighter_and_sends_it_back_up()
    {
        var g = Game(1, keepFoes: true);
        g.LowerFleet(Field.ZoneTop - 280);
        int lives = g.Lives;
        g.Idle(2);
        Assert.Equal("OVERRUN", g.Cause);
        Assert.Equal(lives - 1, g.Lives);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.True(g.Foes.Max(f => f.Y) < 300);
    }

    // ───────────────────────────── the Lancers ─────────────────────────────

    [Fact]
    public void A_Lancers_beam_warns_first_then_burns_straight_down()
    {
        var g = Real(2);
        var lancer = g.Foes.First(f => f.Kind == FoeKind.Lancer);
        for (int i = 0; i < 60 * 8 && g.Beams.Count == 0; i++) g.Tick(default);
        var b = Assert.Single(g.Beams.Take(1));
        Assert.False(b.Firing);
        // A fighter in its path, once it fires.
        g.SetShip(b.X);
        for (int i = 0; i < 60 && !b.Firing && g.Ship.Alive; i++) g.Tick(default);
        g.SetShip(b.X);
        g.Idle(2);
        Assert.False(g.Ship.Alive);
        Assert.Equal("LASERED", g.Cause);
        Assert.NotNull(lancer);
    }

    [Fact]
    public void The_fighters_circle_their_Lancers_and_a_Lancer_is_worth_300()
    {
        var g = Game(2, keepFoes: true);
        var l = g.Foes.First(f => f.Kind == FoeKind.Lancer);
        var ring = g.Foes.Where(f => f.Kind == FoeKind.Fighter && f.Group == l.Group).ToList();
        Assert.Equal(6, ring.Count);
        g.Idle(60);
        Assert.All(ring, f => Assert.InRange(Math.Sqrt((f.X - l.X) * (f.X - l.X) + (f.Y - l.Y) * (f.Y - l.Y)), 45, 70));
        Assert.Equal(300, GameEngine.Points(l));
    }

    [Fact]
    public void Shooting_a_Lancer_stops_its_beam()
    {
        var g = Real(2);
        for (int i = 0; i < 60 * 8 && g.Beams.Count == 0; i++) g.Tick(default);
        var b = g.Beams[0];
        b.Owner.Dead = true;
        g.Tick(default);
        Assert.DoesNotContain(b, g.Beams);
    }

    // ───────────────────────────── the Swarm ─────────────────────────────

    [Fact]
    public void Swarm_divers_loop_out_come_down_and_back_round_to_their_places()
    {
        var g = Game(3, keepFoes: true);
        var f = g.Foes.First(x => x.Kind == FoeKind.Fighter && x.Row == 3);
        g.SendDive(f);
        // Out of its way.
        g.SetShip(Field.Right - 20, Field.ZoneTop + 20);
        bool low = false;
        for (int i = 0; i < 60 * 10 && !(low && f.State == FoeState.Holding); i++)
        {
            g.Tick(default);
            if (f.Y > Field.ZoneTop) low = true;
        }
        Assert.True(low);
        Assert.Equal(FoeState.Holding, f.State);
        Assert.Equal(GameEngine.Points(new Foe { Kind = FoeKind.Fighter }) * 2, GameEngine.Points(new Foe { Kind = FoeKind.Fighter, State = FoeState.Diving }));
    }

    [Fact]
    public void A_diver_that_rams_the_fighter_goes_down_with_it()
    {
        var g = Game(3);
        var f = g.PutFoe(FoeKind.Fighter, g.Ship.X, g.Ship.Y - 10, FoeState.Diving);
        g.Tick(default);
        Assert.True(f.Dead);
        Assert.Equal("RAMMED", g.Cause);
    }

    // ───────────────────────────── the Warp ─────────────────────────────

    [Fact]
    public void Warp_ships_spiral_out_of_the_eye_and_are_worth_most_near_it()
    {
        var g = Real(4);
        for (int i = 0; i < 60 * 3 && g.Foes.Count == 0; i++) g.Tick(default);
        var f = g.Foes[0];
        double r0 = f.Radius;
        g.Idle(30);
        Assert.True(f.Dead || f.Radius > r0);
        Assert.Equal(300, GameEngine.Points(new Foe { Kind = FoeKind.Warper, Radius = 50 }));
        Assert.Equal(150, GameEngine.Points(new Foe { Kind = FoeKind.Warper, Radius = 150 }));
        Assert.Equal(80, GameEngine.Points(new Foe { Kind = FoeKind.Warper, Radius = 260 }));
    }

    [Fact]
    public void The_warp_is_won_when_every_ship_it_sends_is_shot()
    {
        var g = Real(4);
        g.SetLives(99);
        for (int i = 0; i < 60 * 240 && g.Phase != Phase.Clear; i++) g.Tick(g.Pilot.Decide(g));
        Assert.Equal(Phase.Clear, g.Phase);
        Assert.Equal(0, g.WarpToCome);
    }

    // ───────────────────────────── the Dreadnought ─────────────────────────────

    [Fact]
    public void The_band_must_be_shot_through_before_the_core_can_be_hit()
    {
        var g = Game(5);
        var d = g.Flagship!;
        g.SetShip(d.X);
        for (int r = 0; r < Dreadnought.BandRows; r++)
        {
            Shoot(g);
            Assert.True(d.CoreAlive);
        }
        int col = d.ColAt(d.X);
        for (int r = 0; r < Dreadnought.BandRows; r++) Assert.False(d.Band[r, col]);
        int before = g.Score;
        Shoot(g);
        Assert.False(d.CoreAlive);
        Assert.Equal(g.CorePoints, g.Score - before);
    }

    [Fact]
    public void The_hull_stops_shots_beside_the_core()
    {
        var g = Game(5);
        var d = g.Flagship!;
        for (int r = 0; r < Dreadnought.BandRows; r++) d.Band[r, 1] = false;
        g.SetShip(d.X - Dreadnought.HalfWidth + 1.5 * Dreadnought.CellW);
        Shoot(g);
        Assert.True(d.CoreAlive);
    }

    [Fact]
    public void The_band_turns_and_mends()
    {
        var g = Real(5);
        var d = g.Flagship!;
        for (int r = 0; r < Dreadnought.BandRows; r++) d.Band[r, 5] = false;
        g.Idle(60 * 2);
        Assert.True(d.Band[0, 5] || d.Band[1, 5] || d.Band[2, 5], "the hole moved along (or mended)");
    }

    [Fact]
    public void Destroying_the_Dreadnought_wins_the_round_and_a_promotion()
    {
        var g = Game(5);
        var d = g.Flagship!;
        for (int r = 0; r < Dreadnought.BandRows; r++)
            for (int c = 0; c < Dreadnought.BandCols; c++)
                d.Band[r, c] = false;
        g.SetShip(d.X);
        Shoot(g);
        Assert.False(d.CoreAlive);
        g.Calm = false;
        g.Tick(default);
        Assert.Equal(Phase.Clear, g.Phase);
        g.Idle((int)(GameEngine.DreadnoughtDownSeconds * 60) + 2);
        Assert.Equal(6, g.Wave);
        Assert.Equal("LIEUTENANT", g.RankName);
        Assert.Contains("LIEUTENANT", g.Taunt);
    }

    // ───────────────────────────── losing fighters ─────────────────────────────

    [Fact]
    public void A_bullet_brings_the_fighter_down_the_Overlord_jeers_and_the_next_one_comes()
    {
        var g = Game(2);
        int lives = g.Lives;
        g.PutBullet(g.Ship.X, g.Ship.Y - 40);
        g.Idle(20);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("SHOT DOWN", g.Cause);
        Assert.Equal(lives - 1, g.Lives);
        Assert.NotNull(g.Taunt);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.Equal(Phase.Start, g.Phase);
        Assert.Equal("READY", g.Message);
        Assert.True(g.Ship.Alive);
    }

    [Fact]
    public void The_game_ends_with_the_last_fighter_and_a_good_score_gets_initials()
    {
        var g = Game(2);
        g.SetLives(1);
        g.SetScore(500000);
        g.PutBullet(g.Ship.X, g.Ship.Y - 20);
        g.Idle(5);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.Equal(GameState.GameOver, g.State);
        g.Idle(4 * 60);
        Assert.Equal(GameState.EnterInitials, g.State);
    }

    [Fact]
    public void An_extra_fighter_at_15000()
    {
        var g = Game(5);
        int lives = g.Lives;
        g.SetScore(14000);
        var d = g.Flagship!;
        for (int r = 0; r < Dreadnought.BandRows; r++) d.Band[r, d.ColAt(d.X)] = false;
        g.SetShip(d.X);
        Shoot(g);
        Assert.Equal(lives + 1, g.Lives);
    }

    // ───────────────────────────── the machine ─────────────────────────────

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle((int)(GameEngine.AttractSeconds * 60) + 2);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(GameEngine.HighScoreSeconds * 60) + 2);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_computer_pilot_wins_the_first_round_of_missions()
    {
        var g = Real();
        for (int i = 0; i < 60 * 300 && g.Wave <= 5; i++) g.Tick(g.Pilot.Decide(g));
        Assert.True(g.Wave > 5, $"reached mission {g.Wave}");
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play()
        {
            var g = Real(2, seed: 3);
            for (int i = 0; i < 60 * 40; i++) g.Tick(g.Pilot.Decide(g));
            return $"{g.Score} {g.Wave} {g.Lives} {g.Ship.X:0.00} {g.Remaining}";
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void Pausing_stops_everything()
    {
        var g = Real(2);
        g.Idle(120);
        var before = string.Join(",", g.Foes.Select(f => $"{f.X:0.0}"));
        g.Pause();
        g.Idle(120);
        Assert.Equal(before, string.Join(",", g.Foes.Select(f => $"{f.X:0.0}")));
        g.Resume();
        Assert.Equal(GameState.Playing, g.State);
    }
}
