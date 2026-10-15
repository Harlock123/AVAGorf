using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAGorf.Engine;
using AVAGorf.Views;

namespace AVAGorf.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set GORF_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avagorf-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("GORF_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAGorf-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }
    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("ACE", 48250, 11);
        g.HighScores.Insert("AVA", 31200, 7);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void The_armada()
    {
        var (w, c, g) = Open();
        g.StartGame();
        g.SetLives(4);
        g.Idle(40);
        c.Advance(0.5);
        Shot(w, c, "mission-start");
        Assert.True(PlayUntil(g, c, g => g.WaveTime > 8 && g.Ship.Alive && g.Shot is { Y: < 500 } && g.Bullets.Count >= 2 && g.Barrier!.Count < 185));
        Shot(w, c, "armada");
    }

    [AvaloniaFact]
    public void The_lancers()
    {
        var (w, c, g) = Open(3);
        g.Go(2);
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, g => g.WaveTime > 3 && g.Ship.Alive && g.Beams.Any(b => b.Firing && b.T > Beam.WarnSeconds + 0.2)
                                       && g.Foes.Count(f => f.State == FoeState.Diving) >= 1));
        Shot(w, c, "lancers");
    }

    [AvaloniaFact]
    public void The_swarm()
    {
        var (w, c, g) = Open(4);
        g.Go(3);
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, g => g.WaveTime > 5 && g.Ship.Alive && g.Foes.Count(f => f.State == FoeState.Diving && f.Y > 300) >= 2 && g.Bullets.Count >= 1));
        Shot(w, c, "swarm");
    }

    [AvaloniaFact]
    public void The_warp()
    {
        var (w, c, g) = Open(5);
        g.Go(4);
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, g => g.WaveTime > 6 && g.Ship.Alive && g.Remaining >= 3 && g.Foes.Count(f => f.Radius > 120) >= 2));
        Shot(w, c, "warp");
    }

    [AvaloniaFact]
    public void The_dreadnought()
    {
        var (w, c, g) = Open(6);
        g.Go(5);
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, g => g.Flagship is { } d && d.Band.Cast<bool>().Count(b => !b) >= 6 && g.Ship.Alive && g.Shot != null && g.Bullets.Count >= 2, 120));
        Shot(w, c, "dreadnought");
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Clear && g.PhaseTime > 0.6, 180));
        Shot(w, c, "core-hit");
    }

    [AvaloniaFact]
    public void A_fighter_lost_and_the_Overlord_jeers()
    {
        var (w, c, g) = Open();
        g.Go(2);
        g.SetLives(3);
        Assert.True(PlayUntil(g, c, g => g.WaveTime > 5 && g.Ship.Alive));
        g.PutBullet(g.Ship.X, g.Ship.Y - 20);
        g.Idle(50);
        c.Advance(0.4);
        Shot(w, c, "lost");
    }
}
