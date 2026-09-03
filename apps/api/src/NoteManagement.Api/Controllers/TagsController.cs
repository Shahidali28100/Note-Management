using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NoteManagement.Api.Extensions;
using NoteManagement.Application.DTOs.Tags;
using NoteManagement.Application.Interfaces;

namespace NoteManagement.Api.Controllers;

[ApiController]
[Route("api/tags")]
[Authorize]
public sealed class TagsController : ControllerBase
{
    private readonly ITagService _tagService;

    public TagsController(ITagService tagService)
    {
        _tagService = tagService;
    }

    /// <summary>FRS-TAG-001.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(TagResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TagResponseDto>> Create(CreateTagRequestDto request, CancellationToken cancellationToken)
    {
        var tag = await _tagService.CreateAsync(User.GetUserId(), request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, tag);
    }

    /// <summary>FRS-TAG-004.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<TagResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<TagResponseDto>>> List(CancellationToken cancellationToken)
    {
        var tags = await _tagService.ListAsync(User.GetUserId(), cancellationToken);
        return Ok(tags);
    }

    /// <summary>FRS-TAG-002.</summary>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(TagResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TagResponseDto>> Update(Guid id, UpdateTagRequestDto request, CancellationToken cancellationToken)
    {
        var tag = await _tagService.UpdateAsync(User.GetUserId(), id, request, cancellationToken);
        return Ok(tag);
    }

    /// <summary>FRS-TAG-003.</summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _tagService.DeleteAsync(User.GetUserId(), id, cancellationToken);
        return NoContent();
    }
}
