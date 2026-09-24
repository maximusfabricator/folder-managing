using System.Text.Json;
using FolderManaging.Core.Models;

namespace FolderManaging.Core.Storage;

public sealed class JsonFolderStateStore : IFolderStateStore
{
    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Loads and merges menu presets (for shell / companion sync paths).</summary>
    public static AppStateDocument LoadSynchronously()
    {
        var path = AppPaths.StateFilePath;
        if (!File.Exists(path))
        {
            var empty = new AppStateDocument();
            MenuPresetNormalizer.Merge(empty);
            return empty;
        }

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 4096,
            FileOptions.SequentialScan);

        var doc = JsonSerializer.Deserialize<AppStateDocument>(stream, SerializerOptions) ?? new AppStateDocument();
        MenuPresetNormalizer.Merge(doc);
        return doc;
    }

    /// <summary>Atomic write of full state (used by Companion immediate-save).</summary>
    public static void SaveSynchronously(AppStateDocument state)
    {
        MenuPresetNormalizer.Merge(state);
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var path = AppPaths.StateFilePath;
        var tempPath = path + ".tmp";

        using (var stream = new FileStream(
                   tempPath,
                   FileMode.Create,
                   FileAccess.Write,
                   FileShare.None,
                   bufferSize: 4096,
                   FileOptions.SequentialScan))
        {
            JsonSerializer.Serialize(stream, state, SerializerOptions);
        }

        File.Copy(tempPath, path, overwrite: true);
        File.Delete(tempPath);
    }

    public async Task<AppStateDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        var path = AppPaths.StateFilePath;
        if (!File.Exists(path))
        {
            var empty = new AppStateDocument();
            MenuPresetNormalizer.Merge(empty);
            return empty;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var doc = await JsonSerializer.DeserializeAsync<AppStateDocument>(stream, SerializerOptions, cancellationToken)
                  .ConfigureAwait(false);

        doc ??= new AppStateDocument();
        MenuPresetNormalizer.Merge(doc);
        return doc;
    }

    public async Task SaveAsync(AppStateDocument state, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var path = AppPaths.StateFilePath;
        var tempPath = path + ".tmp";

        await using (var stream = new FileStream(
                         tempPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 4096,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await JsonSerializer.SerializeAsync(stream, state, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
        }

        File.Copy(tempPath, path, overwrite: true);
        File.Delete(tempPath);
    }
}
