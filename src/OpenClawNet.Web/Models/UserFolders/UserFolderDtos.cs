namespace OpenClawNet.Web.Models.UserFolders;

/// <summary>
/// W-4 contract: a user-storage folder owned by the operator under {users}/.
/// Mirrors the gateway response from <c>GET /api/user-folders</c>.
/// </summary>
public sealed record UserFolderDto(
    string Name,
    long SizeBytes,
    DateTime LastWriteTimeUtc);

/// <summary>Body for <c>POST /api/user-folders</c>.</summary>
public sealed record CreateUserFolderRequest(string FolderName);

/// <summary>
/// 400-response shape used by every <c>/api/user-folders</c> endpoint.
/// Drummond W-4 binding AC#1 — <c>UnsafePathReason.InvalidUserFolderName</c>
/// surfaces here as <see cref="Reason"/> = "InvalidUserFolderName".
/// </summary>
public sealed record UserFolderProblem(string Reason, string? Detail = null);

/// <summary>Returned by upload to refresh row stats without a full re-list.</summary>
public sealed record UserFolderUploadResult(string Name, long SizeBytes, DateTime LastWriteTimeUtc);
