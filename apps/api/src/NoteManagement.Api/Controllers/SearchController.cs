using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NoteManagement.Api.Extensions;
using NoteManagement.Application.DTOs.Search;
using NoteManagement.Application.Interfaces;

namespace NoteManagement.Api.Controllers;

[ApiController]
[Route("api/search")]
[Authorize]
public sealed class SearchController : ControllerBase
{
    private readonly ISearchService _searchService;

    public SearchController(ISearchService searchService)
    {
        _searchService = searchService;
    }

    /// <summary>FRS-SEARCH-001..004.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(SearchResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SearchResponseDto>> Search([FromQuery] SearchQueryDto query, CancellationToken cancellationToken)
    {
        var result = await _searchService.SearchAsync(User.GetUserId(), query, cancellationToken);
        return Ok(result);
    }
}
