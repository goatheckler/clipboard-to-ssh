namespace ClipboardToSsh.Models;

public record SshHost(string Name, string HostName, int Port, string User);
