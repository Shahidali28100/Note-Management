namespace NoteManagement.Application.DTOs.Search;

/// <summary>Plain-text excerpts with matched terms delimited by SearchHighlighter's sentinel markers — never HTML (SDS §44/§60).</summary>
public sealed record NoteHighlightDto(string Title, string Content);
