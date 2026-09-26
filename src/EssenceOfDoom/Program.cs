using System.Globalization;
using System.Numerics;
using Raylib_cs;

namespace EssenceOfDoom;

public static class Program
{
    public static int Main(string[] args)
    {
        string? wadArg = null;
        string map = "E1M1";
        int skill = 4;
        string? shotPath = null; // dev aid: save a screenshot after --shot-tic tics (default 2 s) and quit
        int shotTic = 70;
        bool overview = false, autoFire = false; // more dev aids for screenshots
        Vec2? startAt = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--wad" when i + 1 < args.Length: wadArg = args[++i]; break;
                case "--map" when i + 1 < args.Length: map = args[++i].ToUpperInvariant(); break;
                case "--skill" when i + 1 < args.Length: skill = Math.Clamp(int.Parse(args[++i]), 1, 5); break;
                case "--shot" when i + 1 < args.Length: shotPath = args[++i]; break;
                case "--shot-tic" when i + 1 < args.Length: shotTic = int.Parse(args[++i]); break;
                case "--overview": overview = true; break;
                case "--fire": autoFire = true; break;
                case "--at" when i + 1 < args.Length:
                    var xy = args[++i].Split(',');
                    startAt = new Vec2(double.Parse(xy[0], CultureInfo.InvariantCulture), double.Parse(xy[1], CultureInfo.InvariantCulture));
                    break;
            }
        }

        var wadPath = Wad.Locate(wadArg);
        if (wadPath == null)
        {
            Console.Error.WriteLine("Could not find DOOM1.WAD. Pass --wad <path>, set DOOMWAD, or put it next to the executable.");
            return 1;
        }
        Console.WriteLine($"Loading {map} from {wadPath}");
        var level = Level.Load(new Wad(wadPath), map);

        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.Msaa4xHint | ConfigFlags.VSyncHint);
        Raylib.InitWindow(1600, 900, $"Essence of DOOM — {map}");
        // Keep the window inside smaller or high-DPI-scaled screens.
        int monitor = Raylib.GetCurrentMonitor();
        int mw = Raylib.GetMonitorWidth(monitor), mh = Raylib.GetMonitorHeight(monitor);
        if (mw > 0 && mh > 0 && (mw < 1600 / 0.9 || mh < 900 / 0.85))
        {
            int w = Math.Min(1600, (int)(mw * 0.9)), h = Math.Min(900, (int)(mh * 0.85));
            Raylib.SetWindowSize(w, h);
            Raylib.SetWindowPosition((mw - w) / 2, (mh - h) / 2);
        }
        Raylib.SetTargetFPS(144);
        Raylib.HideCursor();

        var game = new Game(level, skill, Environment.TickCount);
        if (startAt is Vec2 at) game.Player.Mo.Pos = game.Player.Mo.PrevPos = at;
        var renderer = new Renderer(game) { Overview = overview };
        double accumulator = 0;
        const double dt = 1.0 / Game.TicRate;
        bool usePressed = false;
        int weaponPressed = 0;
        double targetZoom = renderer.Zoom;

        while (!Raylib.WindowShouldClose())
        {
            // Edge-triggered inputs are latched until the next simulation tic consumes them.
            if (Raylib.IsKeyPressed(KeyboardKey.E) || Raylib.IsKeyPressed(KeyboardKey.Space) ||
                Raylib.IsMouseButtonPressed(MouseButton.Right)) usePressed = true;
            for (int k = 1; k <= 4; k++)
                if (Raylib.IsKeyPressed(KeyboardKey.One + k - 1)) weaponPressed = k;
            if (Raylib.IsKeyPressed(KeyboardKey.Tab)) renderer.Overview = !renderer.Overview;
            if (Raylib.IsKeyPressed(KeyboardKey.H)) renderer.ShowHelp = !renderer.ShowHelp;
            if (Raylib.IsKeyPressed(KeyboardKey.R))
            {
                // Restart: reload the level from the WAD so doors, floors and monsters reset.
                renderer.Dispose();
                level = Level.Load(new Wad(wadPath), map);
                game = new Game(level, skill, Environment.TickCount);
                renderer = new Renderer(game) { Zoom = targetZoom };
                accumulator = 0;
            }
            float wheel = Raylib.GetMouseWheelMove();
            if (wheel != 0) targetZoom = Math.Clamp(targetZoom * Math.Pow(1.15, wheel), 0.12, 3);
            if (!renderer.Overview) renderer.Zoom += (targetZoom - renderer.Zoom) * 0.25;

            accumulator = Math.Min(accumulator + Raylib.GetFrameTime(), 0.25);
            var mouse = Raylib.GetMousePosition();
            while (accumulator >= dt)
            {
                renderer.UpdateCamera(1);
                var input = ReadInput(game, renderer, mouse, usePressed, weaponPressed);
                if (autoFire) input = input with { Fire = true };
                usePressed = false;
                weaponPressed = 0;
                game.Update(input);
                accumulator -= dt;
            }

            double alpha = accumulator / dt;
            renderer.UpdateCamera(alpha);
            Raylib.BeginDrawing();
            renderer.Draw(alpha, mouse);
            Raylib.EndDrawing();
            if (shotPath != null && game.Tick >= shotTic)
            {
                Raylib.TakeScreenshot(shotPath);
                break;
            }
        }

        renderer.Dispose();
        Raylib.CloseWindow();
        return 0;
    }

    private static PlayerInput ReadInput(Game game, Renderer renderer, Vector2 mouse, bool use, int weapon)
    {
        double mx = 0, my = 0;
        if (Raylib.IsKeyDown(KeyboardKey.W) || Raylib.IsKeyDown(KeyboardKey.Up)) my += 1;
        if (Raylib.IsKeyDown(KeyboardKey.S) || Raylib.IsKeyDown(KeyboardKey.Down)) my -= 1;
        if (Raylib.IsKeyDown(KeyboardKey.D) || Raylib.IsKeyDown(KeyboardKey.Right)) mx += 1;
        if (Raylib.IsKeyDown(KeyboardKey.A) || Raylib.IsKeyDown(KeyboardKey.Left)) mx -= 1;

        var aimAt = renderer.ScreenToWorld(mouse);
        double aim = Game.AngleTo(game.Player.Mo.Pos, aimAt);
        bool fire = Raylib.IsMouseButtonDown(MouseButton.Left) || Raylib.IsKeyDown(KeyboardKey.LeftControl);
        bool walk = Raylib.IsKeyDown(KeyboardKey.LeftShift);
        return new PlayerInput(new Vec2(mx, my), aim, fire, use, walk, weapon);
    }
}
