using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NoteManagement.Api.Extensions;
using NoteManagement.Application.DTOs.Notes;
using NoteManagement.Application.Interfaces;

namespace NoteManagement.Api.Controllers;

[ApiController]
[Route("api/notes")]
[Authorize]
public sealed class NotesController : ControllerBase
{
    private readonly INoteService _noteService;

    public NotesController(INoteService noteService)
    {
        _noteService = noteService;
    }

    /// <summary>FRS-NOTE-001.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(NoteResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<NoteResponseDto>> Create(CreateNoteRequestDto request, CancellationToken cancellationToken)
    {
        var note = await _noteService.CreateAsync(User.GetUserId(), request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, note);
    }

    /// <summary>FRS-NOTE-002/006/007 — fixed default view only (see NoteService.ListAsync remarks).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(NoteListResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<NoteListResponseDto>> List(CancellationToken cancellationToken)
    {
        var result = await _noteService.ListAsync(User.GetUserId(), cancellationToken);
        return Ok(result);
    }

    /// <summary>FRS-NOTE-002.</summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(NoteResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NoteResponseDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var note = await _noteService.GetByIdAsync(User.GetUserId(), id, cancellationToken);
        return Ok(note);
    }

    /// <summary>FRS-NOTE-003.</summary>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(NoteResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NoteResponseDto>> Update(Guid id, UpdateNoteRequestDto request, CancellationToken cancellationToken)
    {
        var note = await _noteService.UpdateAsync(User.GetUserId(), id, request, cancellationToken);
        return Ok(note);
    }

    /// <summary>FRS-NOTE-004.</summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _noteService.DeleteAsync(User.GetUserId(), id, cancellationToken);
        return NoContent();
    }

    /// <summary>FRS-NOTE-005.</summary>
    [HttpPost("{id}/restore")]
    [ProducesResponseType(typeof(NoteResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<NoteResponseDto>> Restore(Guid id, CancellationToken cancellationToken)
    {
        var note = await _noteService.RestoreAsync(User.GetUserId(), id, cancellationToken);
        return Ok(note);
    }
}
