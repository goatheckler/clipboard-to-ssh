using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using ClipboardToSsh.Models;

namespace ClipboardToSsh.Services;

public class LinuxClipboardService : ClipboardMonitor
{
    public LinuxClipboardService() : base(null)
    {
    }

    public async Task<ClipboardContent?> GetImageFromClipboardAsync()
    {
        try
        {
            var tempFile = Path.GetTempFileName();
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "xclip",
                    Arguments = "-selection clipboard -t image/png -o",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            using var ms = new MemoryStream();
            await process.StandardOutput.BaseStream.CopyToAsync(ms);
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && ms.Length > 0)
            {
                return new ClipboardContent(ClipboardContentType.Image, null, ms.ToArray());
            }
        }
        catch
        {
        }

        return null;
    }

    public async Task<ClipboardContent?> GetTextFromClipboardAsync()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "xclip",
                    Arguments = "-selection clipboard -o",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var text = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && !string.IsNullOrEmpty(text))
            {
                return new ClipboardContent(ClipboardContentType.Text, text.Trim(), null);
            }
        }
        catch
        {
        }

        return null;
    }
}
