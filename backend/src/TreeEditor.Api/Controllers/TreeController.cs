using Microsoft.AspNetCore.Mvc;
using TreeEditor.Application.Dtos;
using TreeEditor.Application.Interfaces;

namespace TreeEditor.Api.Controllers;

[ApiController]
[Route("api/tree")]
public sealed class TreeController(ITreeQueryService query, IApplyService apply, ISyncService sync) : ControllerBase
{
    [HttpGet("roots")]
    public async Task<ActionResult<IReadOnlyList<ElementNodeDto>>> GetRoots(CancellationToken cancellationToken) =>
        Ok(await query.GetRootsAsync(cancellationToken));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ElementNodeDto>> GetElement(int id, CancellationToken cancellationToken)
    {
        var element = await query.GetElementAsync(id, cancellationToken);
        return element is null ? NotFound() : Ok(element);
    }

    [HttpGet("{id:int}/children")]
    public async Task<ActionResult<IReadOnlyList<ElementNodeDto>>> GetChildren(
        int id,
        CancellationToken cancellationToken) =>
        Ok(await query.GetChildrenAsync(id, cancellationToken));

    [HttpPost("apply")]
    public async Task<ActionResult<ApplyResult>> Apply(
        ApplyRequest request,
        CancellationToken cancellationToken) =>
        Ok(await apply.ApplyAsync(request, cancellationToken));

    [HttpGet("revision")]
    public async Task<ActionResult<RevisionDto>> GetRevision(CancellationToken cancellationToken) =>
        Ok(new RevisionDto(await sync.GetRevisionAsync(cancellationToken)));

    [HttpPost("check")]
    public async Task<ActionResult<TreeCheckResult>> Check(
        TreeCheckRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sync.CheckAsync(request, cancellationToken));
}
