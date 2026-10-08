using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TreeEditor.Domain.Entities;

namespace TreeEditor.Infrastructure.Persistence.Configurations;

public sealed class TreeRevisionConfiguration : IEntityTypeConfiguration<TreeRevision>
{
    public void Configure(EntityTypeBuilder<TreeRevision> builder)
    {
        builder.ToTable("tree_revision");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(r => r.Revision).HasColumnName("revision");
    }
}
