using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class AuditLog
{
    public long AuditLogId { get; set; }

    public string? UserId { get; set; }

    public string ActionType { get; set; } = null!;

    public string EntityName { get; set; } = null!;

    public string? EntityId { get; set; }

    public string? OldValues { get; set; }

    public string? NewValues { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public DateTime CreatedAt { get; set; }
}
