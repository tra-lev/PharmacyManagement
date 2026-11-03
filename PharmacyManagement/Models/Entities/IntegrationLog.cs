using System;
using System.Collections.Generic;

namespace PharmacyManagement.Models.Entities;

public partial class IntegrationLog
{
    public long IntegrationLogId { get; set; }

    public string IntegrationType { get; set; } = null!;

    public string ReferenceType { get; set; } = null!;

    public long ReferenceId { get; set; }

    public string? RequestPayload { get; set; }

    public string? ResponsePayload { get; set; }

    public byte Status { get; set; }

    public int RetryCount { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? LastAttemptAt { get; set; }

    public string? ErrorMessage { get; set; }
}
