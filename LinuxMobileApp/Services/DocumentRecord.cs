namespace LinuxMobileApp.Services;

public sealed class DocumentRecord
{
    public Guid Id { get; set; }
    public required string FileName { get; set; }
    public required string Extension { get; set; }
    public required string ContentType { get; set; }
    public required string ExtractedText { get; set; }
    public required byte[] FileBytes { get; set; }
    public required string AccessTokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
