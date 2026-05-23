using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenClawNet.Storage;

namespace OpenClawNet.Gateway.Endpoints;

/// <summary>
/// Encrypted secrets CRUD. Plaintext values are accepted on PUT and never
/// returned on GET — the listing endpoint exposes only name/description/timestamp.
/// </summary>
public static class SecretsEndpoints
{
    public static void MapSecretsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/secrets").WithTags("Secrets");

        group.MapGet("/", async (ISecretsStore store, CancellationToken ct) =>
            Results.Ok(await store.ListAsync(ct)))
            .WithName("ListSecrets")
            .WithDescription("Returns secret names and descriptions. Plaintext values are never returned.");

        group.MapPut("/{name}", async (string name, [FromBody] SecretWriteRequest req, ISecretsStore store, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(name)) return Results.BadRequest(new { error = "name is required" });
            if (req?.Value is null) return Results.BadRequest(new { error = "value is required" });
            await store.SetAsync(name, req.Value, req.Description, ct);
            return Results.NoContent();
        })
        .WithName("SetSecret")
        .WithDescription("Insert or update a secret. The plaintext is encrypted before persistence.");

        group.MapDelete("/{name}", async (string name, ISecretsStore store, CancellationToken ct) =>
            await store.DeleteAsync(name, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("DeleteSecret");

        group.MapGet("/{name}/versions", async (string name, ISecretsStore store, CancellationToken ct) =>
            Results.Ok(await store.ListVersionsAsync(name, ct)))
            .WithName("ListSecretVersions")
            .WithDescription("Lists version numbers for a secret (metadata only, no values).");

        group.MapPost("/{name}/rotate", async (string name, [FromBody] SecretRotateRequest req, ISecretsStore store, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(name)) return Results.BadRequest(new { error = "name is required" });
            if (req?.NewValue is null) return Results.BadRequest(new { error = "newValue is required" });
            try
            {
                await store.RotateAsync(name, req.NewValue, ct);
                return Results.NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("RotateSecret")
        .WithDescription("Creates a new secret version and makes it current atomically.");

        group.MapPost("/{name}/recover", async (string name, ISecretsStore store, CancellationToken ct) =>
            await store.RecoverAsync(name, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("RecoverSecret")
            .WithDescription("Recovers a soft-deleted secret.");

        group.MapDelete("/{name}/purge", async (
            string name,
            [FromHeader(Name = "X-Confirm-Purge")] string? confirmation,
            ISecretsStore store,
            CancellationToken ct) =>
        {
            if (!string.Equals(confirmation, name, StringComparison.Ordinal))
            {
                return Results.BadRequest(new
                {
                    error = $"Purge requires X-Confirm-Purge header with the exact secret name: {name}"
                });
            }

            return await store.PurgeAsync(name, ct) ? Results.NoContent() : Results.NotFound();
        })
            .WithName("PurgeSecret")
            .WithDescription("Permanently removes a secret and all versions after X-Confirm-Purge confirms the exact secret name.");

        group.MapPost("/audit/verify", async (IDbContextFactory<OpenClawDbContext> dbFactory, CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var isValid = await SecretAccessAuditHashChain.VerifyAsync(db, ct);
            return Results.Ok(new { valid = isValid });
        })
        .WithName("VerifyAuditChain")
        .WithDescription("Verifies the audit hash-chain for tamper detection.");

        group.MapPost("/templates/apply", async (
            [FromBody] TemplateApplyRequest req,
            ISecretsStore store,
            ISecretAccessAuditor auditor,
            CancellationToken ct) =>
        {
            if (req?.TemplateName is null) return Results.BadRequest(new { error = "templateName is required" });
            if (req.Secrets is null || req.Secrets.Count == 0) return Results.BadRequest(new { error = "secrets dictionary is required" });

            try
            {
                // Validate required fields based on template
                var validationErrors = ValidateTemplate(req.TemplateName, req.Secrets);
                if (validationErrors.Count > 0)
                {
                    return Results.BadRequest(new { error = "Validation failed", fields = validationErrors });
                }

                // Apply the bundle atomically
                await store.SetBundleAsync(req.Secrets, ct);

                // Audit the template apply action (without secret values)
                var ctx = new VaultCallerContext(VaultCallerType.System, $"TemplateApply:{req.TemplateName}");
                foreach (var secretName in req.Secrets.Keys)
                {
                    await auditor.RecordAsync(secretName, ctx, success: true, ct);
                }

                return Results.NoContent();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("ApplySecretTemplate")
        .WithDescription("Atomically applies a secret template bundle (e.g., Azure OpenAI). All fields are validated before any writes occur.");
    }

    private static Dictionary<string, string> ValidateTemplate(string templateName, IReadOnlyDictionary<string, string> secrets)
    {
        var errors = new Dictionary<string, string>();

        if (templateName == "AzureOpenAI")
        {
            var requiredFields = new[] { "AzureOpenAI_Endpoint", "AzureOpenAI_ModelId", "AzureOpenAI_ApiKey" };
            foreach (var field in requiredFields)
            {
                if (!secrets.ContainsKey(field) || string.IsNullOrWhiteSpace(secrets[field]))
                {
                    errors[field] = $"{field} is required";
                }
            }
        }
        else
        {
            errors["templateName"] = $"Unknown template: {templateName}";
        }

        return errors;
    }

    public sealed record SecretWriteRequest(string Value, string? Description);
    public sealed record SecretRotateRequest(string NewValue);
    public sealed record TemplateApplyRequest(string TemplateName, IReadOnlyDictionary<string, string> Secrets);
}
