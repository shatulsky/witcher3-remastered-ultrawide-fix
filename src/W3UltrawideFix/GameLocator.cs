using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace W3UltrawideFix;

static class GameLocator
{
    static readonly (string Rel, string Label)[] Executables =
    {
        (@"bin\x64_dx12\witcher3.exe", "DirectX 12"),
        (@"bin\x64\witcher3.exe", "DirectX 11"),
    };

    public static List<ExeInfo> FindExecutables(string root) =>
        Executables
            .Select(e => new ExeInfo(Path.Combine(root, e.Rel), e.Label))
            .Where(e => File.Exists(e.Path))
            .ToList();

    public static string? FindGameRoot(string? userPath)
    {
        foreach (var candidate in Candidates(userPath))
        {
            var root = NormalizeToRoot(candidate);
            if (root != null) return root;
        }
        return null;
    }

    static string? NormalizeToRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            path = Path.GetFullPath(path!.Trim().Trim('"'));
            if (File.Exists(path)) path = Path.GetDirectoryName(path)!;

            // Accept the game root itself, or any folder up to two levels inside it (e.g. bin\x64_dx12).
            var dir = new DirectoryInfo(path);
            for (int i = 0; i < 3 && dir != null; i++, dir = dir.Parent)
                if (FindExecutables(dir.FullName).Count > 0) return dir.FullName;
        }
        catch (Exception) { }
        return null;
    }

    static IEnumerable<string?> Candidates(string? userPath)
    {
        yield return userPath;
        yield return AppDomain.CurrentDomain.BaseDirectory;
        yield return Environment.CurrentDirectory;

        foreach (var lib in SteamLibraries())
            yield return Path.Combine(lib, "steamapps", "common", "The Witcher 3");

        foreach (var id in new[] { "1495134320", "1207664663" })
        {
            yield return ReadRegistry(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\GOG.com\Games\" + id, "path");
            yield return ReadRegistry(RegistryHive.LocalMachine, @"SOFTWARE\GOG.com\Games\" + id, "path");
        }

        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        yield return Path.Combine(pf86, @"GOG Galaxy\Games\The Witcher 3 Wild Hunt GOTY");
        yield return Path.Combine(pf86, @"GOG Galaxy\Games\The Witcher 3 Wild Hunt");
    }

    static IEnumerable<string> SteamLibraries()
    {
        var steam = ReadRegistry(RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath")
                    ?? ReadRegistry(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
        if (steam == null) yield break;

        steam = steam.Replace('/', '\\');
        yield return steam;

        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;

        string text;
        try { text = File.ReadAllText(vdf); } catch (Exception) { yield break; }

        foreach (Match m in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\""))
            yield return m.Groups[1].Value.Replace(@"\\", @"\");
    }

    static string? ReadRegistry(RegistryHive hive, string key, string value)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var sub = baseKey.OpenSubKey(key);
            return sub?.GetValue(value) as string;
        }
        catch (Exception) { return null; }
    }
}
