using Microsoft.EntityFrameworkCore;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.Storage;

/// <summary>
/// EF Core-backed implementation of <see cref="IAgentProfileStore"/>.
/// Seeds a "default" profile on first access if none exists.
/// </summary>
public sealed class AgentProfileStore : IAgentProfileStore
{
    private readonly IDbContextFactory<OpenClawDbContext> _dbFactory;

    public AgentProfileStore(IDbContextFactory<OpenClawDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<AgentProfile?> GetAsync(string name, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await db.AgentProfiles.FindAsync([name], ct);
        return entity is null ? null : ToModel(entity);
    }

    public async Task<AgentProfile> GetDefaultAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        // Only return enabled Standard profiles as default. System/ToolTester profiles
        // are not selectable as the default chat agent.
        var entity = await db.AgentProfiles.FirstOrDefaultAsync(
            e => e.IsDefault && e.IsEnabled && e.Kind == ProfileKind.Standard, ct);

        if (entity is not null)
            return ToModel(entity);

        // Seed a default profile if none exists
        var defaultProfile = new AgentProfile
        {
            Name = "openclawnet-agent",
            DisplayName = "OpenClawNet Agent",
            IsDefault = true,
            IsEnabled = true,
            Provider = "ollama-default",
            Instructions = "You are OpenClawNet, a helpful AI assistant built with .NET. You help users with tasks using the tools available to you. Be concise, accurate, and proactive in using tools when they can help answer questions.",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await SaveAsync(defaultProfile, ct);
        return defaultProfile;
    }

    public async Task<IReadOnlyList<AgentProfile>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entities = await db.AgentProfiles.OrderBy(e => e.Name).ToListAsync(ct);
        return entities.Select(ToModel).ToList();
    }

    public async Task SaveAsync(AgentProfile profile, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Before saving, ensure only one default profile exists
        if (profile.IsDefault)
        {
            var currentDefaults = await db.AgentProfiles
                .Where(p => p.IsDefault && p.Name != profile.Name)
                .ToListAsync(ct);
            foreach (var d in currentDefaults)
                d.IsDefault = false;
        }

        var existing = await db.AgentProfiles.FindAsync([profile.Name], ct);

        if (existing is not null)
        {
            existing.DisplayName = profile.DisplayName;
            existing.Provider = profile.Provider;
            existing.Model = profile.Model;
            existing.Endpoint = profile.Endpoint;
            existing.ApiKey = profile.ApiKey;
            existing.DeploymentName = profile.DeploymentName;
            existing.AuthMode = profile.AuthMode;
            existing.Instructions = profile.Instructions;
            existing.EnabledTools = profile.EnabledTools;
            existing.Temperature = profile.Temperature;
            existing.MaxTokens = profile.MaxTokens;
            existing.IsDefault = profile.IsDefault;
            existing.RetrievalLevel = profile.RetrievalLevel;
            existing.Kind = profile.Kind;
            existing.RequireToolApproval = profile.RequireToolApproval;
            existing.IsEnabled = profile.IsEnabled;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            db.AgentProfiles.Add(new AgentProfileEntity
            {
                Name = profile.Name,
                DisplayName = profile.DisplayName,
                Provider = profile.Provider,
                Model = profile.Model,
                Endpoint = profile.Endpoint,
                ApiKey = profile.ApiKey,
                DeploymentName = profile.DeploymentName,
                AuthMode = profile.AuthMode,
                Instructions = profile.Instructions,
                EnabledTools = profile.EnabledTools,
                Temperature = profile.Temperature,
                MaxTokens = profile.MaxTokens,
                IsDefault = profile.IsDefault,
                RetrievalLevel = profile.RetrievalLevel,
                Kind = profile.Kind,
                RequireToolApproval = profile.RequireToolApproval,
                IsEnabled = profile.IsEnabled,
                CreatedAt = profile.CreatedAt,
                UpdatedAt = profile.UpdatedAt
            });
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string name, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await db.AgentProfiles.FindAsync([name], ct);
        if (entity is not null)
        {
            db.AgentProfiles.Remove(entity);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<AgentProfileEntity?> GetEntityAsync(string name, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.AgentProfiles.FindAsync([name], ct);
    }

    public async Task SaveEntityAsync(AgentProfileEntity entity, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var existing = await db.AgentProfiles.FindAsync([entity.Name], ct);
        if (existing is not null)
        {
            existing.LastTestedAt = entity.LastTestedAt;
            existing.LastTestSucceeded = entity.LastTestSucceeded;
            existing.LastTestError = entity.LastTestError;
            existing.UpdatedAt = entity.UpdatedAt;
            await db.SaveChangesAsync(ct);
        }
    }

    private static AgentProfile ToModel(AgentProfileEntity entity) => new()
    {
        Name = entity.Name,
        DisplayName = entity.DisplayName,
        Provider = entity.Provider,
        Model = entity.Model,
        Endpoint = entity.Endpoint,
        ApiKey = entity.ApiKey,
        DeploymentName = entity.DeploymentName,
        AuthMode = entity.AuthMode,
        Instructions = entity.Instructions,
        EnabledTools = entity.EnabledTools,
        Temperature = entity.Temperature,
        MaxTokens = entity.MaxTokens,
        IsDefault = entity.IsDefault,
        RetrievalLevel = entity.RetrievalLevel,
        Kind = entity.Kind,
        RequireToolApproval = entity.RequireToolApproval,
        IsEnabled = entity.IsEnabled,
        LastTestedAt = entity.LastTestedAt,
        LastTestSucceeded = entity.LastTestSucceeded,
        LastTestError = entity.LastTestError,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt
    };
}
