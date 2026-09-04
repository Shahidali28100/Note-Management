using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoteManagement.Infrastructure.Search;

namespace NoteManagement.Infrastructure.Configurations;

/// <summary>
/// A keyless projection type for CONTAINSTABLE's result shape — ToView(null) excludes it from
/// migrations (no real table/view backs it; see plan.md architecture decisions). Internal, like
/// FullTextMatch itself — this configuration only needs to be visible to
/// ApplyConfigurationsFromAssembly's reflection-based scan within this assembly, never outside it.
/// </summary>
internal sealed class FullTextMatchConfiguration : IEntityTypeConfiguration<FullTextMatch>
{
    public void Configure(EntityTypeBuilder<FullTextMatch> builder)
    {
        builder.HasNoKey();
        builder.ToView(null);
        builder.Property(m => m.Key).HasColumnName("KEY");
        builder.Property(m => m.Rank).HasColumnName("RANK");
    }
}
