using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ClipboardToSsh.Models;
using Renci.SshNet;

namespace ClipboardToSsh.Services;

public class SftpService
{
    private static readonly string[] WordList = new[]
    {
        "apple", "banana", "cherry", "dragon", "eagle", "forest",
        "grape", "harbor", "island", "jungle", "kiwi", "lemon",
        "mango", "nectar", "orange", "pepper", "quartz", "river",
        "solar", "tiger", "urban", "violet", "walnut", "xenon",
        "yellow", "zebra", "amber", "blaze", "coral", "delta",
        "ember", "flame", "glacier", "horizon", "ivory", "jade",
        "karma", "lunar", "marble", "nebula", "ocean", "prism",
        "quest", "radar", "silver", "thunder", "unity", "vegas",
        "winter", "xray", "yield", "zephyr", "azure", "birch",
        "cedar", "dusk", "echo", "falcon", "glyph", "haven"
    };

    public (string filename, byte[] data) GenerateFilenameAndData(ClipboardContent content)
    {
        var extension = content.Type == ClipboardContentType.Image ? "png" : "txt";
        var word1 = WordList[RandomNumberGenerator.GetInt32(WordList.Length)];
        var word2 = WordList[RandomNumberGenerator.GetInt32(WordList.Length)];
        var hex = RandomNumberGenerator.GetInt32(256).ToString("x2");
        var filename = $"{word1}-{word2}-{hex}.{extension}";

        byte[] data;
        if (content.Type == ClipboardContentType.Image && content.ImageData != null)
        {
            data = content.ImageData;
        }
        else if (content.Text != null)
        {
            data = Encoding.UTF8.GetBytes(content.Text);
        }
        else
        {
            throw new InvalidOperationException("No content to upload");
        }

        return (filename, data);
    }

    public async Task<string> UploadAsync(SshHost host, string filename, byte[] data, string? password = null)
    {
        SftpClient sftpClient;

        if (password != null)
        {
            sftpClient = new SftpClient(host.HostName, host.Port, host.User, password);
        }
        else
        {
            var privateKeyFile = TryLoadPrivateKey();
            if (privateKeyFile != null)
            {
                sftpClient = new SftpClient(host.HostName, host.Port, host.User, privateKeyFile);
            }
            else
            {
                throw new InvalidOperationException("No authentication method available. Provide a password or configure SSH keys.");
            }
        }

        await Task.Run(() => sftpClient.Connect());

        if (!sftpClient.IsConnected)
            throw new InvalidOperationException("Failed to connect to SFTP server");

        try
        {
            var remotePath = $"/tmp/{filename}";
            await Task.Run(() => sftpClient.UploadFile(new MemoryStream(data), remotePath, true));
            return remotePath;
        }
        finally
        {
            sftpClient.Disconnect();
        }
    }

    private PrivateKeyFile? TryLoadPrivateKey()
    {
        var sshKeyPaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "id_rsa"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "id_ed25519"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "id_ecdsa"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "id_ecdsa_sk")
        };

        foreach (var keyPath in sshKeyPaths)
        {
            if (File.Exists(keyPath))
            {
                try
                {
                    return new PrivateKeyFile(keyPath);
                }
                catch
                {
                }
            }
        }

        return null;
    }
}
