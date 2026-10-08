using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace W3UltrawideFix;

static class Program
{
    static readonly (int W, int H, string Label)[] Presets =
    {
        (2560, 1080, "21:9"),
        (3440, 1440, "21:9"),
        (3840, 1600, "21:9"),
        (5120, 2160, "21:9"),
        (6880, 2880, "21:9"),
        (3840, 1200, "32:10"),
        (5120, 1440, "32:9"),
    };

    static bool _interactive = true;

    static int Main(string[] args)
    {
        Console.Title = "Witcher 3 Remastered Ultrawide Cutscene Fix";
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Error(ex is UnauthorizedAccessException
                ? "Access denied. Run the tool as administrator and make sure the game is closed."
                : ex.Message);
            return 1;
        }
        finally
        {
            if (_interactive)
            {
                Console.WriteLine();
                Console.Write("Press any key to exit...");
                Console.ReadKey(true);
            }
        }
    }

    static int Run(string[] args)
    {
        string? path = null, res = null;
        bool restore = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--restore": restore = true; break;
                case "--res" when i + 1 < args.Length: res = args[++i]; break;
                case "--path" when i + 1 < args.Length: path = args[++i]; break;
                case "-h" or "--help" or "/?": PrintHelp(); return 0;
                default: path ??= args[i]; break;
            }
        }
        _interactive = res == null && !restore;

        Console.WriteLine("=== The Witcher 3 Remastered - Ultrawide Cutscene Fix ===");
        Console.WriteLine();

        var root = GameLocator.FindGameRoot(path);
        while (root == null)
        {
            if (!_interactive) throw new InvalidOperationException("Game folder not found. Pass it with --path.");
            Console.WriteLine("Could not find The Witcher 3 automatically.");
            Console.Write("Paste the game folder path (or Q to quit): ");
            var input = Console.ReadLine();
            if (input == null || input.Trim().Equals("q", StringComparison.OrdinalIgnoreCase)) return 0;
            root = GameLocator.FindGameRoot(input);
        }

        Info($"Game folder: {root}");

        if (Process.GetProcessesByName("witcher3").Length > 0)
            throw new InvalidOperationException("The Witcher 3 is running. Close the game and try again.");

        var exes = GameLocator.FindExecutables(root);
        foreach (var exe in exes)
        {
            Patcher.Inspect(exe);
            var version = FileVersionInfo.GetVersionInfo(exe.Path).FileVersion?.Split('(')[0].Trim();
            var state = exe.State switch
            {
                ExeState.Original => $"original 16:9 ({exe.Occurrences} values found)",
                ExeState.Patched => $"patched, aspect ratio {exe.CurrentRatio:0.####}",
                _ => "unsupported or modified by another tool",
            };
            Info($"{exe.Label} exe, version {version}: {state}");
        }
        Console.WriteLine();

        var usable = exes.Where(e => e.State != ExeState.Unknown).ToList();
        if (usable.Count == 0)
            throw new InvalidOperationException("No patchable witcher3.exe found. If you used another ultrawide patcher before, run \"Verify integrity of game files\" in Steam/GOG first.");

        if (restore)
            return DoRestore(usable);

        if (res != null)
        {
            var (w, h) = ParseResolution(res) ?? throw new InvalidOperationException($"Invalid resolution \"{res}\". Use WIDTHxHEIGHT, e.g. 3440x1440.");
            return DoPatch(usable, w, h);
        }

        return Menu(usable);
    }

    static int Menu(System.Collections.Generic.List<ExeInfo> exes)
    {
        bool anyPatched = exes.Any(e => e.State == ExeState.Patched);

        Console.WriteLine("Select your monitor resolution:");
        for (int i = 0; i < Presets.Length; i++)
            Console.WriteLine($"  {i + 1}) {Presets[i].W}x{Presets[i].H}  ({Presets[i].Label})");
        Console.WriteLine("  C) Custom resolution");
        if (anyPatched) Console.WriteLine("  R) Restore original (remove the fix)");
        Console.WriteLine("  Q) Quit");
        Console.WriteLine();

        while (true)
        {
            Console.Write("Your choice: ");
            var choice = (Console.ReadLine() ?? "q").Trim().ToLowerInvariant();

            if (choice == "q") return 0;
            if (choice == "r" && anyPatched) return DoRestore(exes);

            if (choice == "c")
            {
                while (true)
                {
                    Console.Write("Enter resolution as WIDTHxHEIGHT (e.g. 3440x1440): ");
                    var parsed = ParseResolution(Console.ReadLine() ?? "");
                    if (parsed is { } r) return DoPatch(exes, r.W, r.H);
                    Error("Invalid resolution.");
                }
            }

            if (int.TryParse(choice, out var n) && n >= 1 && n <= Presets.Length)
                return DoPatch(exes, Presets[n - 1].W, Presets[n - 1].H);

            Error("Invalid choice.");
        }
    }

    static int DoPatch(System.Collections.Generic.List<ExeInfo> exes, int w, int h)
    {
        float ratio = (float)w / h;
        if (ratio <= 16f / 9f + 0.001f)
            throw new InvalidOperationException($"{w}x{h} is not wider than 16:9, nothing to fix.");

        Info($"Patching for {w}x{h} (aspect ratio {ratio.ToString("0.####", CultureInfo.InvariantCulture)})...");
        foreach (var exe in exes)
        {
            int count = Patcher.Patch(exe, ratio);
            Success($"{exe.Label}: replaced {count} values. Backup: {System.IO.Path.GetFileName(exe.BackupPath)}");
        }
        Console.WriteLine();
        Success("Done! Cutscenes will now fill the whole screen.");
        Info("Note: game updates and \"Verify integrity of game files\" undo the fix. Just run this tool again.");
        return 0;
    }

    static int DoRestore(System.Collections.Generic.List<ExeInfo> exes)
    {
        foreach (var exe in exes.Where(e => e.State == ExeState.Patched))
        {
            Patcher.Restore(exe);
            Success($"{exe.Label}: original exe restored.");
        }
        if (!exes.Any(e => e.State == ExeState.Patched)) Info("Nothing to restore, the game is not patched.");
        return 0;
    }

    static (int W, int H)? ParseResolution(string s)
    {
        var parts = s.Trim().ToLowerInvariant().Split('x', '*', ':', ' ');
        parts = parts.Where(p => p.Length > 0).ToArray();
        if (parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h) && w > 0 && h > 0)
            return (w, h);
        return null;
    }

    static void PrintHelp()
    {
        Console.WriteLine("Usage: W3UltrawideFix.exe [--path <game folder>] [--res <WIDTHxHEIGHT>] [--restore]");
        Console.WriteLine("  No arguments: interactive menu.");
        Console.WriteLine("  --res 3440x1440   patch without prompting");
        Console.WriteLine("  --restore         restore the original exe from backup");
    }

    static void Info(string msg) => Write(ConsoleColor.Gray, msg);
    static void Success(string msg) => Write(ConsoleColor.Green, msg);
    static void Error(string msg) => Write(ConsoleColor.Red, "Error: " + msg);

    static void Write(ConsoleColor color, string msg)
    {
        var old = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(msg);
        Console.ForegroundColor = old;
    }
}
