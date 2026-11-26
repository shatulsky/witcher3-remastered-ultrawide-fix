using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace W3UltrawideFix;

enum ExeState { Original, Patched, Unknown }

sealed class ExeInfo(string path, string label)
{
    public string Path { get; } = path;
    public string Label { get; } = label;
    public string? Version { get; } = FileVersionInfo.GetVersionInfo(path).FileVersion?.Split('(')[0].Trim();
    public bool IsPatch502 => Version == "5.0.0.1145604";
    public string LegacyBackupPath => Path + ".uwfix-backup";
    public string BackupPath => IsPatch502 ? Path + ".uwfix-1145604-backup" : LegacyBackupPath;
    public ExeState State { get; set; }
    public int Occurrences { get; set; }
    public float? CurrentRatio { get; set; }
}

static class Patcher
{
    // 16:9 (1.7777778f) little-endian: the aspect ratio the game uses to letterbox cutscenes.
    public static readonly byte[] OriginalRatio = BitConverter.GetBytes(16f / 9f);

    // Steam patch 5.02, DirectX 12. These three locations match the previous build's cutscene constants.
    const string Steam502Sha256 = "6d0d60127b4fdd9b06d7f4027350bcb2386c1d4b3369424bb41b5510ed5fa534";
    static readonly int[] Steam502Offsets = { 0x24bb691, 0x2b5b8b0, 0x3b2f8c0 };

    static string Hash(Stream data)
    {
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
    }

    static string Hash(byte[] data)
    {
        using var stream = new MemoryStream(data);
        return Hash(stream);
    }

    static IReadOnlyList<int> FindOriginalOffsets(ExeInfo exe, byte[] data)
    {
        if (!exe.IsPatch502) return FindAll(data, OriginalRatio);
        return Hash(data) == Steam502Sha256 ? Steam502Offsets : Array.Empty<int>();
    }

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
        var hits = FindOriginalOffsets(exe, data);
        if (hits.Count > 0)
        {
            exe.State = ExeState.Original;
            exe.Occurrences = hits.Count;
            return;
        }

        var backup = LoadMatchingBackup(exe, data);
        if (backup != null)
        {
            var offsets = FindOriginalOffsets(exe, backup);
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
        var path = File.Exists(exe.BackupPath) ? exe.BackupPath : exe.LegacyBackupPath;
        if (!File.Exists(path)) return null;
        var backup = File.ReadAllBytes(path);
        if (backup.Length != current.Length) return null;

        var offsets = FindOriginalOffsets(exe, backup);
        if (offsets.Count == 0) return null;

        // A 5.02 patch replaces all three values with the same finite ultrawide ratio.
        var ratio = BitConverter.ToSingle(current, offsets[0]);
        if (exe.IsPatch502 && (float.IsNaN(ratio) || float.IsInfinity(ratio) || ratio <= 16f / 9f))
            return null;

        int position = 0;
        foreach (int offset in offsets)
        {
            if (exe.IsPatch502 && BitConverter.ToSingle(current, offset) != ratio) return null;
            for (; position < offset; position++)
                if (current[position] != backup[position]) return null;
            position = offset + sizeof(float);
        }
        for (; position < current.Length; position++)
            if (current[position] != backup[position]) return null;

        return backup;
    }

    static void SaveBackup(ExeInfo exe, byte[] source)
    {
        if (!exe.IsPatch502)
        {
            File.WriteAllBytes(exe.BackupPath, source);
            return;
        }

        if (!File.Exists(exe.BackupPath))
        {
            using var backup = new FileStream(exe.BackupPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            backup.Write(source, 0, source.Length);
            backup.Flush(true);
        }

        using var saved = File.OpenRead(exe.BackupPath);
        if (Hash(saved) != Steam502Sha256)
            throw new IOException("The existing 5.02 backup does not match the original executable. Move that backup aside before retrying.");
    }

    static void ReplaceExecutable(string path, byte[] current, byte[] output)
    {
        var currentHash = Hash(current);
        var outputHash = Hash(output);
        if (currentHash == outputHash) return;

        var staged = path + ".uwfix-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(output, 0, output.Length);
                stream.Flush(true);
            }

            using (var stagedFile = File.OpenRead(staged))
                if (Hash(stagedFile) != outputHash)
                    throw new IOException("The staged executable failed verification.");

            using (var installed = File.OpenRead(path))
                if (Hash(installed) != currentHash)
                    throw new IOException("The game executable changed during patching. Try again after the update finishes.");

            File.Replace(staged, path, null);
        }
        finally
        {
            if (File.Exists(staged)) File.Delete(staged);
        }
    }

    public static int Patch(ExeInfo exe, float ratio)
    {
        var current = File.ReadAllBytes(exe.Path);
        byte[] source;

        if (FindOriginalOffsets(exe, current).Count > 0)
        {
            source = current;
            SaveBackup(exe, source);
        }
        else
        {
            source = LoadMatchingBackup(exe, current)
                ?? throw new InvalidOperationException("The exe is not in its original state and no matching backup was found. Verify game files in Steam/GOG and try again.");
            if (exe.IsPatch502) SaveBackup(exe, source);
        }

        var output = (byte[])source.Clone();
        var replacement = BitConverter.GetBytes(ratio);
        var offsets = FindOriginalOffsets(exe, source);
        foreach (var o in offsets)
            Buffer.BlockCopy(replacement, 0, output, o, 4);

        ReplaceExecutable(exe.Path, current, output);
        return offsets.Count;
    }

    public static void Restore(ExeInfo exe)
    {
        var current = File.ReadAllBytes(exe.Path);
        var backup = LoadMatchingBackup(exe, current)
            ?? throw new InvalidOperationException("No matching backup found. Use \"Verify integrity of game files\" in Steam/GOG instead.");
        ReplaceExecutable(exe.Path, current, backup);
    }
}
