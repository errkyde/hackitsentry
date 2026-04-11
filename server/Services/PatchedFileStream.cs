namespace HackITSentry.Server.Services;

/// <summary>
/// Read-only stream that wraps a FileStream and applies byte patches on-the-fly.
/// Patches are applied during Read without loading the entire file into memory.
/// </summary>
public sealed class PatchedFileStream : Stream
{
    private readonly FileStream _inner;
    private readonly InstallerService.Patch[] _patches;

    public PatchedFileStream(string path, InstallerService.Patch[] patches)
    {
        _inner = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1024 * 1024);
        _patches = patches;
    }

    public override bool CanRead  => true;
    public override bool CanSeek  => false;
    public override bool CanWrite => false;
    public override long Length   => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        long startPos = _inner.Position;
        int bytesRead = _inner.Read(buffer, offset, count);
        ApplyPatches(buffer, offset, bytesRead, startPos);
        return bytesRead;
    }

    public override async Task<int> ReadAsync(
        byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        long startPos = _inner.Position;
        int bytesRead = await _inner.ReadAsync(buffer, offset, count, cancellationToken);
        ApplyPatches(buffer, offset, bytesRead, startPos);
        return bytesRead;
    }

    private void ApplyPatches(byte[] buffer, int bufferOffset, int bytesRead, long filePos)
    {
        foreach (var patch in _patches)
        {
            long patchEnd = patch.Offset + patch.SlotBytes;
            long readEnd  = filePos + bytesRead;

            if (patch.Offset >= readEnd || patchEnd <= filePos) continue;

            long overlapStart = Math.Max(filePos, patch.Offset);
            long overlapEnd   = Math.Min(readEnd, patchEnd);

            for (long pos = overlapStart; pos < overlapEnd; pos++)
            {
                int bufIdx   = bufferOffset + (int)(pos - filePos);
                int patchIdx = (int)(pos - patch.Offset);
                buffer[bufIdx] = patchIdx < patch.Value.Length ? patch.Value[patchIdx] : (byte)0;
            }
        }
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value)                 => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}
