using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SBD.Messaging.Events;

namespace Gateway.Controllers;

/// <summary>
/// Admin notification management — compose and broadcast notifications.
/// Angular calls these endpoints; Gateway publishes PushNotificationEvent to MassTransit.
/// RealtimeService.PushNotificationConsumer handles delivery via SignalR + WebPush + FCM.
/// </summary>
[ApiController]
[Route("api/v1/admin/notifications")]
[Authorize(Roles = "super_admin,SuperAdmin")]
public class AdminNotificationController(IPublishEndpoint publish) : ControllerBase
{
    /// <summary>
    /// POST /api/v1/admin/notifications/send
    /// Compose and send a notification to a specific target.
    /// </summary>
    [HttpPost("send")]
    public async Task<IActionResult> Send([FromBody] AdminSendRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title) || string.IsNullOrWhiteSpace(req.Message))
            return BadRequest(new { message = "title และ message ต้องไม่ว่าง" });

        var (userId, groupName) = ResolveTarget(req);

        if (userId == null && groupName == null)
            return BadRequest(new { message = "ระบุ targetType ไม่ถูกต้อง" });

        var correlationId = Guid.NewGuid().ToString("N")[..12];

        await publish.Publish(new PushNotificationEvent(
            UserId:    userId,
            GroupName: groupName,
            Title:     req.Title,
            Message:   req.Message,
            Type:      req.Type ?? "info",
            ActionUrl: req.ActionUrl
        ));

        return Ok(new { message = "ส่งการแจ้งเตือนสำเร็จ", correlationId });
    }

    /// <summary>
    /// POST /api/v1/admin/notifications/test-send
    /// Send a test notification to a specific role group for verification.
    /// </summary>
    [HttpPost("test-send")]
    public async Task<IActionResult> TestSend([FromBody] AdminTestSendRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Role))
            return BadRequest(new { message = "ระบุ role ไม่ถูกต้อง" });

        await publish.Publish(new PushNotificationEvent(
            UserId:    null,
            GroupName: $"role:{req.Role}",
            Title:     req.Title ?? "🔔 ทดสอบระบบแจ้งเตือน",
            Message:   req.Message ?? $"นี่คือข้อความทดสอบสำหรับกลุ่ม {req.Role}",
            Type:      "info",
            ActionUrl: null
        ));

        return Ok(new { message = $"ส่งการทดสอบไปยัง role:{req.Role} สำเร็จ" });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (int? userId, string? groupName) ResolveTarget(AdminSendRequest req)
    {
        return req.TargetType switch
        {
            "all"    => (null, "all"),
            "user"   => (req.TargetId, null),
            "role"   => (null, !string.IsNullOrEmpty(req.TargetValue) ? $"role:{req.TargetValue}" : null),
            "school" => (null, req.TargetId.HasValue ? $"school:{req.TargetId}" : null),
            "area"   => (null, req.TargetId.HasValue ? $"area:{req.TargetId}" : null),
            _        => (null, null)
        };
    }
}

public class AdminSendRequest
{
    public required string Title       { get; set; }
    public required string Message     { get; set; }
    public string?         Type        { get; set; }  // info|success|warning|error
    public string?         ActionUrl   { get; set; }
    public required string TargetType  { get; set; }  // all|user|role|school|area
    public int?            TargetId    { get; set; }  // userId / schoolId / areaId
    public string?         TargetValue { get; set; }  // role name (e.g. "Teacher")
}

public class AdminTestSendRequest
{
    public required string Role    { get; set; }
    public string?         Title   { get; set; }
    public string?         Message { get; set; }
}
