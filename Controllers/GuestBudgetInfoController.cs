using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;

namespace Gateway.Controllers;

/// <summary>
/// Plan #107 — Public guest endpoints for the /budget-info portal page.
/// Serves REAL finance-adjacent data only:
///   • SchoolIncomeReports  — เงินรายได้สถานศึกษา + เงินนอกงบประมาณอื่น (FY2567/2568,
///     from the district finance workbook, per school)
///   • SchoolShortageStats  — นักเรียนขาดแคลน 7 หมวด (DMC 10 มิ.ย. 2569, per school)
/// Anything without a real source stays disabled on the frontend.
/// Reporting tables are not EF entities — queried via Database.SqlQuery&lt;T&gt;.
/// </summary>
[ApiController]
[Route("api/v1/guest/budget-info")]
[AllowAnonymous]
public class GuestBudgetInfoController : ControllerBase
{
    private const int CacheSeconds = 3600;
    private readonly SbdDbContext _context;

    public GuestBudgetInfoController(SbdDbContext context)
    {
        _context = context;
    }

    /// <summary>District totals: income per fiscal year/type + shortage headline.</summary>
    [HttpGet("summary")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<GuestBudgetSummaryDto>> GetSummary(CancellationToken ct)
    {
        var income = await _context.Database.SqlQuery<IncomeAggRow>($"""
            SELECT "FiscalYear" AS "FiscalYear", "IncomeType" AS "IncomeType",
                   COUNT(DISTINCT "SmisCode")::int AS "SchoolCount",
                   ROUND(SUM("Amount"), 2) AS "Total"
            FROM "SchoolIncomeReports" GROUP BY 1, 2 ORDER BY 1 DESC, 2
            """).ToListAsync(ct);

        var shortage = await _context.Database.SqlQuery<ShortageAggRow>($"""
            SELECT "Category" AS "Category",
                   MAX("Year")::int AS "Year",
                   SUM("Male")::int AS "Male", SUM("Female")::int AS "Female", SUM("Total")::int AS "Total"
            FROM "SchoolShortageStats"
            WHERE "Year" = (SELECT MAX("Year") FROM "SchoolShortageStats")
            GROUP BY 1 ORDER BY 5 DESC
            """).ToListAsync(ct);

        return Ok(new GuestBudgetSummaryDto(
            income.Select(i => new GuestIncomeAggDto(i.FiscalYear, i.IncomeType, i.SchoolCount, i.Total)).ToList(),
            shortage.Select(s => new GuestShortageAggDto(s.Year, s.Category, s.Male, s.Female, s.Total)).ToList()));
    }

    /// <summary>Per-school income pivot across fiscal years, ranked by latest-year total.</summary>
    [HttpGet("school-income")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<IEnumerable<GuestSchoolIncomeRow>>> GetSchoolIncome(CancellationToken ct)
    {
        var rows = await _context.Database.SqlQuery<GuestSchoolIncomeRow>($"""
            SELECT r."SmisCode" AS "SmisCode", s."NameTh" AS "SchoolName",
                   ROUND(SUM("Amount") FILTER (WHERE "FiscalYear"=2568 AND "IncomeType"='school_income'), 2) AS "Income2568",
                   ROUND(SUM("Amount") FILTER (WHERE "FiscalYear"=2568 AND "IncomeType"='other_off_budget'), 2) AS "Other2568",
                   ROUND(SUM("Amount") FILTER (WHERE "FiscalYear"=2567 AND "IncomeType"='school_income'), 2) AS "Income2567",
                   ROUND(SUM("Amount") FILTER (WHERE "FiscalYear"=2567 AND "IncomeType"='other_off_budget'), 2) AS "Other2567"
            FROM "SchoolIncomeReports" r
            JOIN "Schools" s ON s."SmisCode" = r."SmisCode" AND s."DeletedAt" IS NULL
            GROUP BY 1, 2
            ORDER BY COALESCE(SUM("Amount") FILTER (WHERE "FiscalYear"=2568), 0) DESC
            """).ToListAsync(ct);
        return Ok(rows);
    }

    /// <summary>Per-school นักเรียนขาดแคลน (latest year), ranked by total.</summary>
    [HttpGet("shortage-schools")]
    [ResponseCache(Duration = CacheSeconds, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<IEnumerable<GuestShortageSchoolRow>>> GetShortageSchools(CancellationToken ct)
    {
        var rows = await _context.Database.SqlQuery<GuestShortageSchoolRow>($"""
            SELECT r."SmisCode" AS "SmisCode", s."NameTh" AS "SchoolName",
                   SUM("Total") FILTER (WHERE "Category"='นักเรียนขาดแคลนทั้งหมด')::int AS "Total",
                   SUM("Total") FILTER (WHERE "Category"='เครื่องแบบนักเรียน')::int AS "Uniform",
                   SUM("Total") FILTER (WHERE "Category"='เครื่องเขียน')::int AS "Stationery",
                   SUM("Total") FILTER (WHERE "Category"='แบบเรียน(หนังสือยืมเรียน)')::int AS "Textbook",
                   SUM("Total") FILTER (WHERE "Category"='อาหารกลางวัน')::int AS "Lunch",
                   SUM("Total") FILTER (WHERE "Category"='ขาดแคลน 3 รายการหรือมากกว่า')::int AS "ThreePlus"
            FROM "SchoolShortageStats" r
            JOIN "Schools" s ON s."SmisCode" = r."SmisCode" AND s."DeletedAt" IS NULL
            WHERE r."Year" = (SELECT MAX("Year") FROM "SchoolShortageStats")
            GROUP BY 1, 2
            ORDER BY 3 DESC NULLS LAST
            """).ToListAsync(ct);
        return Ok(rows);
    }
}

internal sealed class IncomeAggRow
{
    public int FiscalYear { get; set; }
    public string IncomeType { get; set; } = "";
    public int SchoolCount { get; set; }
    public decimal? Total { get; set; }
}

internal sealed class ShortageAggRow
{
    public int Year { get; set; }
    public string Category { get; set; } = "";
    public int? Male { get; set; }
    public int? Female { get; set; }
    public int? Total { get; set; }
}

public record GuestIncomeAggDto(int FiscalYear, string IncomeType, int SchoolCount, decimal? Total);
public record GuestShortageAggDto(int Year, string Category, int? Male, int? Female, int? Total);
public record GuestBudgetSummaryDto(List<GuestIncomeAggDto> Income, List<GuestShortageAggDto> Shortage);

public class GuestSchoolIncomeRow
{
    public string SmisCode { get; set; } = "";
    public string SchoolName { get; set; } = "";
    public decimal? Income2568 { get; set; }
    public decimal? Other2568 { get; set; }
    public decimal? Income2567 { get; set; }
    public decimal? Other2567 { get; set; }
}

public class GuestShortageSchoolRow
{
    public string SmisCode { get; set; } = "";
    public string SchoolName { get; set; } = "";
    public int? Total { get; set; }
    public int? Uniform { get; set; }
    public int? Stationery { get; set; }
    public int? Textbook { get; set; }
    public int? Lunch { get; set; }
    public int? ThreePlus { get; set; }
}
