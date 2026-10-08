using TreeEditor.Application.Dtos;
using TreeEditor.Application.Interfaces;

namespace TreeEditor.Application.Services;

/// <summary>
/// Version-based sync for partially loaded clients. A client sends the ids it holds; the server
/// returns the current version of each (cheap: one indexed row per id) plus the ids that no longer
/// exist. The client refetches only the nodes whose version changed.
/// </summary>
public sealed class SyncService : ISyncService
{
    private readonly IElementRepository _repository;

    public SyncService(IElementRepository repository)
    {
        _repository = repository;
    }

    public Task<long> GetRevisionAsync(CancellationToken cancellationToken) =>
        _repository.GetRevisionAsync(cancellationToken);

    public async Task<TreeCheckResult> CheckAsync(TreeCheckRequest request, CancellationToken cancellationToken)
    {
        var ids = request.Ids.Distinct().ToArray();
        var revision = await _repository.GetRevisionAsync(cancellationToken);
        var versions = await _repository.GetVersionsAsync(ids, cancellationToken);

        var present = versions.Select(v => v.Id).ToHashSet();
        var deleted = ids.Where(id => !present.Contains(id)).ToList();

        return new TreeCheckResult(revision, versions, deleted);
    }
}
