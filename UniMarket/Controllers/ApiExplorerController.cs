using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;

[ApiController]
[Route("api-list")]
public class ApiExplorerController : ControllerBase
{
    private readonly IApiDescriptionGroupCollectionProvider _apiExplorer;

    public ApiExplorerController(IApiDescriptionGroupCollectionProvider apiExplorer)
    {
        _apiExplorer = apiExplorer;
    }

    [HttpGet]
    public IActionResult GetAllApis()
    {
        var apiDescriptions = _apiExplorer.ApiDescriptionGroups.Items
            .SelectMany(group => group.Items)
            .Select(api => new
            {
                Method = api.HttpMethod,
                Url = "/" + api.RelativePath,
                Controller = api.ActionDescriptor.RouteValues["controller"],
                Action = api.ActionDescriptor.RouteValues["action"],
                Parameters = api.ParameterDescriptions.Select(p => new
                {
                    Name = p.Name,
                    Source = p.Source.ToString(),
                    Type = p.Type?.Name ?? "unknown"
                })
            });

        return Ok(apiDescriptions);
    }
}
