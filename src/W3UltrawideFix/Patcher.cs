using System;
using System.Collections.Generic;
using System.IO;

namespace W3UltrawideFix;

enum ExeState { Original, Patched, Unknown }

sealed class ExeInfo(string path, string label)
{
    public string Path { get; } = path;
    public string Label { get; } = label;
    public string BackupPath => Path + ".uwfix-backup";
    public ExeState State { get; set; }
    public int Occurrences { get; set; }
    public float? CurrentRatio { get; set; }
}

static class Patcher
{
    // 16:9 (1.7777778f) little-endian: the aspect ratio the game uses to letterbox cutscenes.
    public static readonly byte[] OriginalRatio = BitConverter.GetBytes(16f / 9f);

    public static List<int> FindAll(byte[] data, byte[] pattern)
    {
        var result = new List<int>();
        for (int i = 0; i <= data.Length - pattern.Length; i++)
        {
            int j = 0;
            while (j < pattern.Length && data[i + j] == pattern[j]) j++;
            if (j == pattern.Length) result.Add(i);
        }
        return result;
    }

    public static void Inspect(ExeInfo exe)
    {
        var data = File.ReadAllBytes(exe.Path);
        var hits = FindAll(data, OriginalRatio);
        if (hits.Count > 0)
        {
            exe.State = ExeState.Original;
            exe.Occurrences = hits.Count;
            return;
        }

        var backup = LoadMatchingBackup(exe, data);
        if (backup != null)
        {
            var offsets = FindAll(backup, OriginalRatio);
            exe.State = ExeState.Patched;
            exe.Occurrences = offsets.Count;
            exe.CurrentRatio = BitConverter.ToSingle(data, offsets[0]);
            return;
        }

        exe.State = ExeState.Unknown;
    }

    // A backup is only trusted if it is the same build as the current exe, differing solely at the patched offsets.
    // This guards against a stale backup left over from before a game update.
    static byte[]? LoadMatchingBackup(ExeInfo exe, byte[] current)
    {
        if (!File.Exists(exe.BackupPath)) return null;
        var backup = File.ReadAllBytes(exe.BackupPath);
        if (backup.Length != current.Length) return null;

        var offsets = FindAll(backup, OriginalRatio);
        if (offsets.Count == 0) return null;

        var patched = new bool[current.Length];
        foreach (var o in offsets)
            for (int k = 0; k < 4; k++) patched[o + k] = true;

        for (int i = 0; i < current.Length; i++)
            if (!patched[i] && current[i] != backup[i]) return null;

        return backup;
    }

    public static int Patch(ExeInfo exe, float ratio)
    {
        var current = File.ReadAllBytes(exe.Path);
        byte[] source;

        if (FindAll(current, OriginalRatio).Count > 0)
        {
            File.WriteAllBytes(exe.BackupPath, current);
            source = current;
        }
        else
        {
            source = LoadMatchingBackup(exe, current)
                ?? throw new InvalidOperationException("The exe is not in its original state and no matching backup was found. Verify game files in Steam/GOG and try again.");
        }

        var output = (byte[])source.Clone();
        var replacement = BitConverter.GetBytes(ratio);
        var offsets = FindAll(source, OriginalRatio);
        foreach (var o in offsets)
            Buffer.BlockCopy(replacement, 0, output, o, 4);

        File.WriteAllBytes(exe.Path, output);
        return offsets.Count;
    }

    public static void Restore(ExeInfo exe)
    {
        var current = File.ReadAllBytes(exe.Path);
        var backup = LoadMatchingBackup(exe, current)
            ?? throw new InvalidOperationException("No matching backup found. Use \"Verify integrity of game files\" in Steam/GOG instead.");
        File.WriteAllBytes(exe.Path, backup);
    }
}
