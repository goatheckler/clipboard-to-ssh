using System;

namespace ClipboardToSsh.Models;

public enum HistoryContentType { Text, Image }

public class HistoryEntry
{
    public long Id { get; set; }
    public HistoryContentType ContentType { get; set; }
    public string? Text { get; set; }
    public byte[]? ImageData { get; set; }
    public string Filename { get; set; } = "";
    public long Timestamp { get; set; }

    public ClipboardContent ToClipboardContent()
    {
        return new ClipboardContent(
            ContentType == HistoryContentType.Image ? ClipboardContentType.Image : ClipboardContentType.Text,
            Text,
            ImageData
        );
    }

    public static HistoryEntry FromClipboardContent(ClipboardContent content, string filename)
    {
        return new HistoryEntry
        {
            ContentType = content.Type == ClipboardContentType.Image ? HistoryContentType.Image : HistoryContentType.Text,
            Text = content.Text,
            ImageData = content.ImageData,
            Filename = filename,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
    }
}
