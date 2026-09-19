using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YtpdWeb.Api.Models;
using YtpdWeb.Api.Services;

namespace YtpdWeb.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/resolve")]
public class ResolveController(YoutubeResolverService resolver, ILogger<ResolveController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ResolveResponseDto>> Resolve(ResolveRequest request, CancellationToken ct)
    {
        try
        {
            var result = await resolver.ResolveAsync(request.Url, ct);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to resolve {Url}", request.Url);
            return BadRequest(new { message = "Could not resolve that URL. It may be private, age-restricted, or unavailable." });
        }
    }
}
