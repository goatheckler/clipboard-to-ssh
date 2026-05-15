using System;

namespace ClipboardToSsh.Models;

public class ClipboardEntry
{
    public ClipboardContent Content { get; }
    public string LocalFilename { get; }
    public string RemoteFilename { get; }
    public long Timestamp { get; }

    public ClipboardEntry(ClipboardContent content, string filename)
    {
        Content = content;
        RemoteFilename = $"/tmp/{filename}";
        LocalFilename = System.IO.Path.Combine(System.IO.Path.GetTempPath(), filename);
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    public static ClipboardEntry FromContent(ClipboardContent content, string filename)
    {
        return new ClipboardEntry(content, filename);
    }
}