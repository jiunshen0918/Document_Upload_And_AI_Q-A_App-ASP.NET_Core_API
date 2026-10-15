using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LinuxMobileApp.Services;
using LinuxMobileApp.Utilities;

namespace LinuxMobileApp.Controllers;

[ApiController]
[Route("api/uploads")]
public sealed class UploadsController(DocumentsDbContext db) : ControllerBase
{
    private const long MaxFileSize = 25 * 1024 * 1024;

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxFileSize + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxFileSize + 1024 * 1024)]
    public async Task<IActionResult> Upload([FromForm] IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { success = false, message = "Choose a non-empty file to upload." });
        if (file.Length > MaxFileSize)
            return StatusCode(StatusCodes.Status413PayloadTooLarge,
                new { success = false, message = "The file exceeds the 25 MB limit." });

        var fileName = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(fileName) || !AllowedFileTypes.IsAllowed(fileName))
            return BadRequest(new { success = false, message = "Unsupported file type. Upload TXT, MD, PDF, DOCX, CSV, XLSX, JSON, XML, JPG, or PNG." });

        byte[] bytes;
        string extractedText;
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        try
        {
            await using var input = new MemoryStream();
            await file.CopyToAsync(input, cancellationToken);
            bytes = input.ToArray();
            extractedText = await AllowedFileTypes.ExtractTextAsync(extension, bytes, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var detail = exception is InvalidDataException or FormatException
                ? $" {exception.Message}"
                : " Check that the file is valid and uncorrupted.";
            return BadRequest(new { success = false, message = $"The file is corrupted or unreadable.{detail}" });
        }

        var token = DocumentQaService.CreateAccessToken();
        var record = new DocumentRecord
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            Extension = extension,
            ContentType = ContentTypeFor(extension),
            ExtractedText = extractedText,
            FileBytes = bytes,
            AccessTokenHash = DocumentQaService.HashAccessToken(token),
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Documents.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new
        {
            success = true,
            message = "File uploaded and saved successfully.",
            documentId = record.Id,
            fileName,
            accessToken = token
        });
    }

    private static string ContentTypeFor(string extension) => extension switch
    {
        ".pdf" => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".csv" => "text/csv",
        ".json" => "application/json",
        ".xml" => "application/xml",
        ".jpg" => "image/jpeg",
        ".png" => "image/png",
        ".md" => "text/markdown",
        _ => "text/plain"
    };
}
