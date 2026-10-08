using Microsoft.EntityFrameworkCore.Storage;
using TreeEditor.Application.Interfaces;

namespace TreeEditor.Infrastructure.Persistence;

internal sealed class EfTransaction(IDbContextTransaction transaction) : ITransaction
{
    public Task CommitAsync(CancellationToken cancellationToken) =>
        transaction.CommitAsync(cancellationToken);

    public ValueTask DisposeAsync() => transaction.DisposeAsync();
}
