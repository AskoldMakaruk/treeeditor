using Microsoft.EntityFrameworkCore;
using TreeEditor.Domain.Entities;

namespace TreeEditor.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Element> Elements => Set<Element>();

    public DbSet<TreeRevision> TreeRevisions => Set<TreeRevision>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
