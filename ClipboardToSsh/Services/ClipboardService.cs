using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using ClipboardToSsh.Models;

namespace ClipboardToSsh.Services;

public class ClipboardMonitor
{
    private readonly IClipboard? _clipboard;
    private ClipboardContent? _lastContent;
    private byte[]? _lastImageHash;
    private string? _lastText;
    private System.Timers.Timer? _pollingTimer;

    public event EventHandler<ClipboardContent>? ClipboardChanged;

    public ClipboardMonitor(IClipboard? clipboard = null)
    {
        _clipboard = clipboard;
    }

    public void StartMonitoring(int intervalMs = 250)
    {
        _pollingTimer?.Stop();
        _pollingTimer = new System.Timers.Timer(intervalMs);
        _pollingTimer.Elapsed += async (_, _) => await CheckClipboard();
        _pollingTimer.AutoReset = true;
        _pollingTimer.Start();
    }

    public void StopMonitoring()
    {
        _pollingTimer?.Stop();
        _pollingTimer?.Dispose();
        _pollingTimer = null;
    }

    private async Task CheckClipboard()
    {
        try
        {
            if (_clipboard == null)
                return;

            var text = await _clipboard.TryGetTextAsync();
            if (text != null && text != _lastText)
            {
                _lastText = text;
                var content = new ClipboardContent(ClipboardContentType.Text, text, null);
                _lastContent = content;
                ClipboardChanged?.Invoke(this, content);
                return;
            }

            var bitmap = await _clipboard.TryGetBitmapAsync();
            if (bitmap != null)
            {
                using var stream = new MemoryStream();
                bitmap.Save(stream);
                var imageData = stream.ToArray();
                var hash = ComputeHash(imageData);
                if (!CompareHash(hash, _lastImageHash))
                {
                    _lastImageHash = hash;
                    var content = new ClipboardContent(ClipboardContentType.Image, null, imageData);
                    _lastContent = content;
                    ClipboardChanged?.Invoke(this, content);
                }
            }
        }
        catch
        {
        }
    }

    private static byte[] ComputeHash(byte[] data)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return sha.ComputeHash(data);
    }

    private static bool CompareHash(byte[] a, byte[]? b)
    {
        if (a == null || b == null) return a == b;
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }
}
