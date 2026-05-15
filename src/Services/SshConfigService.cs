using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using ClipboardToSsh.Models;

namespace ClipboardToSsh.Services;

public partial class SshConfigService
{
    private static readonly Regex HostBlockRegex = HostBlockRegexGenerated();
    private static readonly Regex PropertyRegex = PropertyRegexGenerated();

    public List<SshHost> ParseHosts(string configPath)
    {
        if (!File.Exists(configPath))
            return [];

        var content = File.ReadAllText(configPath);
        return ParseConfig(content);
    }

    private List<SshHost> ParseConfig(string config)
    {
        var hosts = new List<SshHost>();
        var currentHostName = "";
        var currentHostValue = "";
        var currentPort = 22;
        var currentUser = "";

        foreach (var line in config.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            var match = PropertyRegex.Match(trimmed);
            if (match.Success)
            {
                var key = match.Groups[1].Value.ToLowerInvariant();
                var value = match.Groups[2].Value.Trim();

                switch (key)
                {
                    case "host":
                        if (!string.IsNullOrEmpty(currentHostName))
                        {
                            var user = string.IsNullOrEmpty(currentUser)
                                ? Environment.UserName
                                : currentUser;
                            hosts.Add(new SshHost(currentHostName, currentHostValue, currentPort, user));
                        }
                        currentHostName = value;
                        currentHostValue = "";
                        currentPort = 22;
                        currentUser = "";
                        break;
                    case "hostname":
                        currentHostValue = value;
                        break;
                    case "port":
                        currentPort = int.TryParse(value, out var port) ? port : 22;
                        break;
                    case "user":
                        currentUser = value;
                        break;
                }
            }
        }

        if (!string.IsNullOrEmpty(currentHostName))
        {
            var user = string.IsNullOrEmpty(currentUser)
                ? Environment.UserName
                : currentUser;
            hosts.Add(new SshHost(currentHostName, currentHostValue, currentPort, user));
        }

        return hosts;
    }

    [GeneratedRegex(@"^(\w+)\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex HostBlockRegexGenerated();

    [GeneratedRegex(@"^(\w+)\s+(.+)$")]
    private static partial Regex PropertyRegexGenerated();
}
