namespace ClipboardToSsh.Models;

public enum ClipboardContentType { Text, Image }

public record ClipboardContent(ClipboardContentType Type, string? Text, byte[]? ImageData);
