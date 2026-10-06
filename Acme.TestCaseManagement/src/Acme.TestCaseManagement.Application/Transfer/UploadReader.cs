using Volo.Abp.Content;

namespace Acme.TestCaseManagement.Transfer;

internal static class UploadReader
{
    /// <summary>
    /// Reads an upload, stopping one byte past <paramref name="maxBytes"/>, so that the caller can tell the file is too
    /// large without an oversized upload being read to the end.
    /// </summary>
    public static async Task<byte[]> ReadAsync(IRemoteStreamContent file, long maxBytes)
    {
        await using var stream = file.GetStream();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        var limit = maxBytes + 1;

        while (buffer.Length < limit)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, limit - buffer.Length)));
            if (read == 0)
            {
                break;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
