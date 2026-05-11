namespace OpenClawNet.Web.Models.Secrets;

public sealed record SecretSummaryDto(string Name, string? Description, DateTime UpdatedAt);

public sealed record SecretWriteRequest(string Value, string? Description);

public sealed record SecretRotateRequest(string NewValue);

public sealed record AuditVerifyResponse(bool Valid);
