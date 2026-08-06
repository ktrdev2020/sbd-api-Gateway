using Gateway.Data;
using Gateway.Services.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

/// <summary>
/// Plan #47 — exports the school's administrative-structure chart as DOCX
/// (editable Word). Reuses the OrgStructureController.Get composite read
/// (same SbdDbContext, same shape) — this controller just adapts to the
/// generator. SchoolAdmin scope enforced.
/// </summary>
[ApiController]
[Route("api/v1/school/{schoolCode}/org-structure")]
[Authorize]
public class OrgStructureReportController : ControllerBase
{
    private readonly OrgStructureController _composite;
    private readonly OrgStructureDocxGenerator _docx;

    public OrgStructureReportController(
        SbdDbContext db,
        OrgStructureDocxGenerator docx,
        Gateway.Services.ICapabilityService capabilities)
    {
        _composite = new OrgStructureController(db, capabilities);
        _docx = docx;
        // The base controller needs HttpContext to evaluate scope (User claim).
        _composite.ControllerContext = ControllerContext;
    }

    [HttpGet("report.docx")]
    public async Task<IActionResult> DownloadDocx(string schoolCode, [FromQuery] int? year)
    {
        // Reuse composite controller — bind its ControllerContext so it sees the same User.
        _composite.ControllerContext = ControllerContext;
        var result = await _composite.Get(schoolCode, year);
        if (result.Result is ForbidResult) return Forbid();
        if (result.Result is NotFoundObjectResult nf) return nf;
        if (result.Result is BadRequestObjectResult br) return br;

        // Successful Ok carries the DTO either via .Value or ObjectResult.Value
        var data = result.Value ?? (result.Result as OkObjectResult)?.Value as OrgStructureDto;
        if (data == null) return StatusCode(500, new { message = "Could not load org-structure data" });

        var ms = _docx.Generate(data);
        var safeName = $"org-structure-{schoolCode}-{data.FiscalYear}.docx";
        // Same defect BudgetApi hit in feedback id=41 — OpenXml 3.2.0 writes an
        // absolute root-relationship target that only Word tolerates. Confirmed
        // present in this endpoint's output on production before this fix.
        return File(Gateway.Services.Reporting.DocxPackageNormalizer.Normalize(ms),
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document", safeName);
    }
}
