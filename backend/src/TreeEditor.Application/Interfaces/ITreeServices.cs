using TreeEditor.Application.Dtos;

namespace TreeEditor.Application.Interfaces;

public interface ITreeQueryService
{
    Task<IReadOnlyList<ElementNodeDto>> GetRootsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ElementNodeDto>> GetChildrenAsync(int parentId, CancellationToken cancellationToken);

    Task<ElementNodeDto?> GetElementAsync(int id, CancellationToken cancellationToken);
}

public interface IApplyService
{
    Task<ApplyResult> ApplyAsync(ApplyRequest request, CancellationToken cancellationToken);
}

public interface IResetService
{
    Task<long> ResetAsync(CancellationToken cancellationToken);
}
