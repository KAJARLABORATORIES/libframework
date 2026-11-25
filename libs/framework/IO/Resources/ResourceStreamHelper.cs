namespace kajarlabs.Framework.IO.Resources;

/// <summary>
/// Reads binary payloads from resource streams efficiently, pre-allocating exact buffers for seekable streams.
/// </summary>
internal static class ResourceStreamHelper
{
    public static byte[] ReadAllBytes(Stream stream)
    {
        if (stream.CanSeek)
        {
            var remaining = stream.Length - stream.Position;

            if (remaining <= 0)
                return [];

            if (remaining > Array.MaxLength)
                throw new IOException($"Resource stream length {remaining} exceeds maximum byte array capacity.");

            var buffer = new byte[(int)remaining];
            stream.ReadExactly(buffer);
            return buffer;
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (stream.CanSeek)
        {
            var remaining = stream.Length - stream.Position;

            if (remaining <= 0)
                return [];

            if (remaining > Array.MaxLength)
                throw new IOException($"Resource stream length {remaining} exceeds maximum byte array capacity.");

            var buffer = new byte[(int)remaining];
            await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
            return buffer;
        }

        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        return memory.ToArray();
    }
}
