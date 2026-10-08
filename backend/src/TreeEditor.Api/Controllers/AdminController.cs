using Microsoft.AspNetCore.Mvc;
using TreeEditor.Application.Interfaces;

namespace TreeEditor.Api.Controllers;

[ApiController]
[Route("api/admin")]
public sealed class AdminController(IResetService reset) : ControllerBase
{
    [HttpPost("reset")]
    public async Task<ActionResult> Reset(CancellationToken cancellationToken)
    {
        var revision = await reset.ResetAsync(cancellationToken);
        return Ok(new { status = "reset", revision });
    }
}
