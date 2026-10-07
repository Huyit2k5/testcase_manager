using System.Collections.Concurrent;
using Volo.Abp.BlobStoring;
using Volo.Abp.DependencyInjection;

namespace Acme.TestCaseManagement;

/// <summary>
/// Keeps blobs in memory for the tests of the module, which then need no folder. What ABP's file system provider does with a
/// real folder is covered by the HTTP tests, which run the sample host.
/// </summary>
public class InMemoryBlobProvider : BlobProviderBase, ITransientDependency
{
    private static readonly ConcurrentDictionary<string, byte[]> Blobs = new();

    /// <summary>Whether a blob of that name is stored; the tests use it to see that nothing is left behind.</summary>
    public static bool Contains(string blobName) => Blobs.Keys.Any(key => key.EndsWith("/" + blobName, StringComparison.Ordinal));

    public static int Count => Blobs.Count;

    /// <summary>Makes a stored blob unreadable, as a lost file would be.</summary>
    public static void Remove(string blobName)
    {
        foreach (var key in Blobs.Keys.Where(k => k.EndsWith("/" + blobName, StringComparison.Ordinal)).ToList())
        {
            Blobs.TryRemove(key, out _);
        }
    }

    private static string Key(BlobProviderArgs args) => $"{args.ContainerName}/{args.BlobName}";

    public override async Task SaveAsync(BlobProviderSaveArgs args)
    {
        if (!args.OverrideExisting && Blobs.ContainsKey(Key(args)))
        {
            throw new BlobAlreadyExistsException($"A blob named {args.BlobName} already exists.");
        }

        using var copy = new MemoryStream();
        await args.BlobStream.CopyToAsync(copy, args.CancellationToken);
        Blobs[Key(args)] = copy.ToArray();
    }

    public override Task<bool> DeleteAsync(BlobProviderDeleteArgs args) => Task.FromResult(Blobs.TryRemove(Key(args), out _));

    public override Task<bool> ExistsAsync(BlobProviderExistsArgs args) => Task.FromResult(Blobs.ContainsKey(Key(args)));

    public override Task<Stream?> GetOrNullAsync(BlobProviderGetArgs args) =>
        Task.FromResult<Stream?>(Blobs.TryGetValue(Key(args), out var bytes) ? new MemoryStream(bytes) : null);
}
