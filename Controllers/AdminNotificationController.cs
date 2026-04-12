using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBD.Infrastructure.Data;
using SBD.Messaging.Events;

namespace Gateway.Controllers;

/// <summary>
/// Admin notification management — compose and broadcast notifications.
///
/// Behavior:
///  - Target "user":   one per-user event → reaches SignalR + WebPush + FCM
///  - Target "role/school/area/all": fans out to per-user events for every member
///    of the group (so WebPush + FCM can reach offline users). SignalR still
///    delivers in real time because each user is in their personal `user:{id}` group.
/// </summary>
[ApiController]
[Route("api/v1/admin/notifications")]
[Authorize(Roles = "super_admin,SuperAdmin")]
public class AdminNotificationController(SbdDbContext db, IPublishEndpoint publish) : ControllerBase
{
    [HttpPost("send")]
    public async Task<IActionResult> Send([FromBody] AdminSendRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Title) || string.IsNullOrWhiteSpace(req.Message))
            return BadRequest(new { message = "title และ message ต้องไม่ว่าง" });

        var userIds = await ResolveUserIdsAsync(req, ct);

        if (userIds.Count == 0)
            return BadRequest(new { message = "ไม่พบผู้รับที่ตรงกับเงื่อนไข" });

        var correlationId = Guid.NewGuid().ToString("N")[..12];

        // Fan-out: one event per user — consumer sends via SignalR user:{id} group
        // + WebPush + FCM for offline users.
        foreach (var uid in userIds)
        {
            await publish.Publish(new PushNotificationEvent(
                UserId:    uid,
                GroupName: null,
                Title:     req.Title,
                Message:   req.Message,
                Type:      req.Type ?? "info",
                ActionUrl: req.ActionUrl
            ), ct);
        }

        return Ok(new { message = "ส่งการแจ้งเตือนสำเร็จ", recipients = userIds.Count, correlationId });
    }

    [HttpPost("test-send")]
    public async Task<IActionResult> TestSend([FromBody] AdminTestSendRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Role))
            return BadRequest(new { message = "ระบุ role ไม่ถูกต้อง" });

        var userIds = await GetUsersByRoleAsync(req.Role, ct);

        if (userIds.Count == 0)
            return BadRequest(new { message = $"ไม่พบผู้ใช้ในบทบาท {req.Role}" });

        var title = req.Title ?? "🔔 ทดสอบระบบแจ้งเตือน";
        var msg   = req.Message ?? $"ข้อความทดสอบสำหรับกลุ่ม {req.Role}";

        foreach (var uid in userIds)
        {
            await publish.Publish(new PushNotificationEvent(
                UserId:    uid,
                GroupName: null,
                Title:     title,
                Message:   msg,
                Type:      "info",
                ActionUrl: null
            ), ct);
        }

        return Ok(new { message = $"ส่งทดสอบไปยัง {userIds.Count} คนในบทบาท {req.Role} สำเร็จ" });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<List<int>> ResolveUserIdsAsync(AdminSendRequest req, CancellationToken ct)
    {
        return req.TargetType switch
        {
            "user"   => req.TargetId is int uid
                        ? new List<int> { uid }
                        : new List<int>(),

            "all"    => await db.Users
                            .Where(u => u.IsActive)
                            .Select(u => u.Id)
                            .ToListAsync(ct),

            "role"   => !string.IsNullOrEmpty(req.TargetValue)
                        ? await GetUsersByRoleAsync(req.TargetValue, ct)
                        : new List<int>(),

            "school" => req.TargetId is int sid
                        ? await db.UserRoles
                            .Where(ur => ur.ScopeType == "school" && ur.ScopeId == sid)
                            .Select(ur => ur.UserId)
                            .Distinct()
                            .ToListAsync(ct)
                        : new List<int>(),

            "area"   => req.TargetId is int aid
                        ? await db.UserRoles
                            .Where(ur => ur.ScopeType == "area" && ur.ScopeId == aid)
                            .Select(ur => ur.UserId)
                            .Distinct()
                            .ToListAsync(ct)
                        : new List<int>(),

            _ => new List<int>()
        };
    }

    private Task<List<int>> GetUsersByRoleAsync(string roleCode, CancellationToken ct)
    {
        return db.Users
            .Where(u => u.IsActive && u.UserRoles.Any(ur => ur.Role.Code == roleCode))
            .Select(u => u.Id)
            .ToListAsync(ct);
    }
}

public class AdminSendRequest
{
    public required string Title       { get; set; }
    public required string Message     { get; set; }
    public string?         Type        { get; set; }
    public string?         ActionUrl   { get; set; }
    public required string TargetType  { get; set; }
    public int?            TargetId    { get; set; }
    public string?         TargetValue { get; set; }
}

public class AdminTestSendRequest
{
    public required string Role    { get; set; }
    public string?         Title   { get; set; }
    public string?         Message { get; set; }
}
