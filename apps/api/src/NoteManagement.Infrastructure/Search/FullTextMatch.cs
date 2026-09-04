namespace NoteManagement.Infrastructure.Search;

/// <summary>
/// Maps CONTAINSTABLE's result shape ([KEY], [RANK]). Infrastructure-only — not a Domain entity,
/// not backed by any real table/view (see FullTextMatchConfiguration). Queried only via
/// SearchRepository's FromSqlInterpolated call.
/// </summary>
internal sealed class FullTextMatch
{
    public Guid Key { get; init; }

    public int Rank { get; init; }
}
