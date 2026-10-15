using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LinuxMobileApp.Services;

namespace LinuxMobileApp.Controllers;
 
[ApiController]
[Route("api/documents")] 
public sealed class DocumentQaController(DocumentsDbContext db, DocumentQaService qaService) : ControllerBase
{
    [HttpPost("{documentId:guid}/questions")]
    public async Task<IActionResult> Ask(Guid documentId, [FromBody] QuestionRequest request,
        [FromHeader(Name = "X-Document-Token")] string? accessToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question) || request.Question.Length > 4_000)
            return BadRequest(new { message = "Enter a question of 1 to 4,000 characters." });
        if (string.IsNullOrWhiteSpace(accessToken))
            return Unauthorized(new { message = "A document access token is required." });

        var tokenHash = DocumentQaService.HashAccessToken(accessToken);
        var document = await db.Documents.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == documentId && item.AccessTokenHash == tokenHash, cancellationToken);
        if (document is null)
            return NotFound(new { message = "Document not found or access is not permitted." });

        try
        {
            var answer = await qaService.AskAsync(document, request.Question.Trim(), cancellationToken);
            return Ok(new { documentId, answer });
        }
        catch (InvalidOperationException exception)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: exception.Message);
        }
        catch (HttpRequestException)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "The Gemini service could not complete the request. Check the API key and try again.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Problem(statusCode: StatusCodes.Status504GatewayTimeout, title: "The Gemini request timed out.");
        }
    }
}

public sealed record QuestionRequest(string Question);
