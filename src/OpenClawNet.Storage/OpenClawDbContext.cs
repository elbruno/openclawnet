using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.Storage;

public class OpenClawDbContext : DbContext
{
    public OpenClawDbContext(DbContextOptions<OpenClawDbContext> options) : base(options) { }
    
    public DbSet<ChatSession> Sessions => Set<ChatSession>();
    public DbSet<ChatMessageEntity> Messages => Set<ChatMessageEntity>();
    public DbSet<SessionSummary> Summaries => Set<SessionSummary>();
    public DbSet<ToolCallRecord> ToolCalls => Set<ToolCallRecord>();
    public DbSet<ScheduledJob> Jobs => Set<ScheduledJob>();
    public DbSet<JobRun> JobRuns => Set<JobRun>();
    public DbSet<JobRunEvent> JobRunEvents => Set<JobRunEvent>();
    public DbSet<ProviderSetting> ProviderSettings => Set<ProviderSetting>();
    public DbSet<AgentProfileEntity> AgentProfiles => Set<AgentProfileEntity>();
    public DbSet<ModelProviderDefinition> ModelProviders => Set<ModelProviderDefinition>();
    public DbSet<McpServerDefinitionEntity> McpServerDefinitions => Set<McpServerDefinitionEntity>();
    public DbSet<McpToolOverrideEntity> McpToolOverrides => Set<McpToolOverrideEntity>();
    public DbSet<SchemaVersionEntity> SchemaVersions => Set<SchemaVersionEntity>();
    public DbSet<ToolTestRecord> ToolTestRecords => Set<ToolTestRecord>();
    public DbSet<SecretEntity> Secrets => Set<SecretEntity>();
    public DbSet<SecretVersionEntity> SecretVersions => Set<SecretVersionEntity>();
    public DbSet<SecretAccessAuditEntity> SecretAccessAudit => Set<SecretAccessAuditEntity>();
    public DbSet<JobRunArtifact> JobRunArtifacts => Set<JobRunArtifact>();
    public DbSet<JobDefinitionStateChange> JobStateChanges => Set<JobDefinitionStateChange>();
    public DbSet<ToolApprovalLog> ToolApprovalLogs => Set<ToolApprovalLog>();
    public DbSet<AgentInvocationLog> AgentInvocationLogs => Set<AgentInvocationLog>();
    public DbSet<ChatSessionArtifact> ChatSessionArtifacts => Set<ChatSessionArtifact>();
    public DbSet<JobChannelConfiguration> JobChannelConfigurations => Set<JobChannelConfiguration>();
    public DbSet<AdapterDeliveryLog> AdapterDeliveryLogs => Set<AdapterDeliveryLog>();
    public DbSet<SkillVector> SkillVectors => Set<SkillVector>();
    public DbSet<OAuthTokenEntity> OAuthTokens => Set<OAuthTokenEntity>();
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ChatSession>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasMany(s => s.Messages).WithOne(m => m.Session).HasForeignKey(m => m.SessionId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(s => s.Summaries).WithOne(s => s.Session).HasForeignKey(s => s.SessionId).OnDelete(DeleteBehavior.Cascade);
        });
        
        modelBuilder.Entity<ChatMessageEntity>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasIndex(m => new { m.SessionId, m.OrderIndex });
        });
        
        modelBuilder.Entity<SessionSummary>(e =>
        {
            e.HasKey(s => s.Id);
        });
        
        modelBuilder.Entity<ToolCallRecord>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasIndex(t => t.SessionId);
        });
        
        modelBuilder.Entity<ScheduledJob>(e =>
        {
            e.ToTable("Jobs");
            e.HasKey(j => j.Id);
            e.Property(j => j.Status)
                .HasConversion(
                    v => v.ToString().ToLowerInvariant(),
                    v => Enum.Parse<JobStatus>(v, ignoreCase: true))
                .HasDefaultValue(JobStatus.Draft);
            e.Property(j => j.TriggerType)
                .HasConversion(
                    v => v.ToString().ToLowerInvariant(),
                    v => Enum.Parse<TriggerType>(v, ignoreCase: true))
                .HasDefaultValue(TriggerType.Manual);
            e.HasMany(j => j.Runs).WithOne(r => r.Job).HasForeignKey(r => r.JobId).OnDelete(DeleteBehavior.Cascade);
        });
        
        modelBuilder.Entity<JobRun>(e =>
        {
            e.ToTable("JobRuns");
            e.HasKey(r => r.Id);
        });

        modelBuilder.Entity<JobRunEvent>(e =>
        {
            e.ToTable("JobRunEvents");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Run)
                .WithMany()
                .HasForeignKey(x => x.JobRunId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.JobRunId, x.Sequence });
        });
        
        modelBuilder.Entity<ProviderSetting>(e =>
        {
            e.HasKey(s => s.Key);
        });

        modelBuilder.Entity<AgentProfileEntity>(e =>
        {
            e.ToTable("AgentProfiles");
            e.HasKey(x => x.Name);
            e.Property(x => x.Kind)
                .HasConversion(
                    v => v.ToString(),
                    v => Enum.Parse<OpenClawNet.Models.Abstractions.ProfileKind>(v, ignoreCase: true))
                .HasDefaultValue(OpenClawNet.Models.Abstractions.ProfileKind.Standard);
        });

        modelBuilder.Entity<ModelProviderDefinition>(e =>
        {
            e.ToTable("ModelProviders");
            e.HasKey(x => x.Name);
        });

        modelBuilder.Entity<McpServerDefinitionEntity>(e =>
        {
            e.ToTable("McpServerDefinitions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<McpToolOverrideEntity>(e =>
        {
            e.ToTable("McpToolOverrides");
            e.HasKey(x => new { x.ServerId, x.ToolName });
        });

        modelBuilder.Entity<SchemaVersionEntity>(e =>
        {
            e.ToTable("SchemaVersions");
            e.HasKey(x => x.Key);
        });

        modelBuilder.Entity<ToolTestRecord>(e =>
        {
            e.ToTable("ToolTestRecords");
            e.HasKey(x => x.Name);
        });

        modelBuilder.Entity<SecretEntity>(e =>
        {
            e.ToTable("Secrets");
            e.HasKey(x => x.Name);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasMany(x => x.Versions)
                .WithOne(x => x.Secret)
                .HasForeignKey(x => x.SecretName)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SecretVersionEntity>(e =>
        {
            e.ToTable("SecretVersions");
            e.HasKey(x => x.Id);
            e.Property(x => x.SecretName).IsRequired();
            e.Property(x => x.EncryptedValue).IsRequired();
            e.HasIndex(x => new { x.SecretName, x.Version }).IsUnique();
            e.HasIndex(x => new { x.SecretName, x.IsCurrent });
            e.HasIndex(x => x.SecretName)
                .IsUnique()
                .HasFilter("IsCurrent = 1");
        });

        modelBuilder.Entity<SecretAccessAuditEntity>(e =>
        {
            e.ToTable("SecretAccessAudit");
            e.HasKey(x => x.Id);
            e.Property(x => x.Sequence).ValueGeneratedOnAdd();
            e.Property(x => x.CallerType).IsRequired().HasMaxLength(32);
            e.Property(x => x.CallerId).IsRequired();
            e.Property(x => x.SecretName).IsRequired();
            e.Property(x => x.PreviousRowHash).HasMaxLength(64);
            e.Property(x => x.RowHash).HasMaxLength(64);
            e.HasIndex(x => x.Sequence).IsUnique();
            e.HasIndex(x => new { x.SecretName, x.AccessedAt });
            e.HasIndex(x => x.RowHash);
        });

        modelBuilder.Entity<JobRunArtifact>(e =>
        {
            e.ToTable("JobRunArtifacts");
            e.HasKey(a => a.Id);
            e.Property(a => a.ArtifactType)
                .HasConversion(
                    v => v.ToString().ToLowerInvariant(),
                    v => Enum.Parse<JobRunArtifactKind>(v, ignoreCase: true))
                .HasDefaultValueSql("'text'");
            e.HasOne(a => a.Run)
                .WithMany()
                .HasForeignKey(a => a.JobRunId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => new { a.JobId, a.CreatedAt }).IsDescending(false, true);
            e.HasIndex(a => new { a.JobRunId, a.Sequence });
        });

        modelBuilder.Entity<JobDefinitionStateChange>(e =>
        {
            e.ToTable("JobStateChanges");
            e.HasKey(x => x.Id);
            e.Property(x => x.FromStatus)
                .HasConversion(
                    v => v.ToString().ToLowerInvariant(),
                    v => Enum.Parse<JobStatus>(v, ignoreCase: true));
            e.Property(x => x.ToStatus)
                .HasConversion(
                    v => v.ToString().ToLowerInvariant(),
                    v => Enum.Parse<JobStatus>(v, ignoreCase: true));
            e.HasOne(x => x.Job)
                .WithMany()
                .HasForeignKey(x => x.JobId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.JobId, x.ChangedAt }).IsDescending(false, true);
        });

        modelBuilder.Entity<ToolApprovalLog>(e =>
        {
            e.ToTable("ToolApprovalLogs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Source)
                .HasConversion(
                    v => v.ToString().ToLowerInvariant(),
                    v => Enum.Parse<ApprovalDecisionSource>(v, ignoreCase: true));
            e.HasIndex(x => x.SessionId);
            e.HasIndex(x => x.RequestId);
            e.HasIndex(x => x.DecidedAt);
        });

        modelBuilder.Entity<AgentInvocationLog>(e =>
        {
            e.ToTable("AgentInvocationLogs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind)
                .HasConversion(
                    v => v.ToString().ToLowerInvariant(),
                    v => Enum.Parse<AgentInvocationKind>(v, ignoreCase: true));
            e.HasIndex(x => new { x.Kind, x.SourceId });
            e.HasIndex(x => x.StartedAt);
        });

        modelBuilder.Entity<ChatSessionArtifact>(e =>
        {
            e.ToTable("ChatSessionArtifacts");
            e.HasKey(a => a.Id);
            e.Property(a => a.ArtifactType)
                .HasConversion(
                    v => v.ToString().ToLowerInvariant(),
                    v => Enum.Parse<JobRunArtifactKind>(v, ignoreCase: true))
                .HasDefaultValueSql("'text'");
            e.HasOne(a => a.Session)
                .WithMany()
                .HasForeignKey(a => a.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => new { a.SessionId, a.Sequence });
            e.HasIndex(a => a.CreatedAt);
        });

        modelBuilder.Entity<JobChannelConfiguration>(e =>
        {
            e.ToTable("JobChannelConfigurations");
            e.HasKey(c => c.Id);
            e.Property(c => c.JobId)
                .IsRequired();
            e.Property(c => c.ChannelType)
                .IsRequired()
                .HasMaxLength(64);
            e.Property(c => c.ChannelConfig)
                .IsRequired();
            e.HasOne(c => c.Job)
                .WithMany()
                .HasForeignKey(c => c.JobId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(c => c.JobId);
            e.HasIndex(c => new { c.JobId, c.ChannelType }).IsUnique();
        });

        modelBuilder.Entity<AdapterDeliveryLog>(e =>
        {
            e.ToTable("AdapterDeliveryLogs");
            e.HasKey(l => l.Id);
            e.Property(l => l.JobId)
                .IsRequired();
            e.Property(l => l.ChannelType)
                .IsRequired()
                .HasMaxLength(64);
            e.Property(l => l.ChannelConfig)
                .IsRequired();
            e.Property(l => l.Status)
                .HasConversion(
                    v => v.ToString().ToLowerInvariant(),
                    v => Enum.Parse<DeliveryStatus>(v, ignoreCase: true))
                .HasDefaultValue(DeliveryStatus.Pending);
            e.HasOne(l => l.Job)
                .WithMany()
                .HasForeignKey(l => l.JobId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(l => l.JobId);
            e.HasIndex(l => l.CreatedAt);
        });

        modelBuilder.Entity<SkillVector>(e =>
        {
            e.ToTable("SkillVectors");
            e.HasKey(v => v.Id);
            e.Property(v => v.SkillName)
                .IsRequired()
                .HasMaxLength(256);
            e.Property(v => v.Embedding)
                .IsRequired();
            e.Property(v => v.CreatedAt)
                .IsRequired()
                .HasDefaultValueSql("datetime('now', 'utc')");
            e.HasIndex(v => v.SkillName).IsUnique();
        });

        modelBuilder.Entity<OAuthTokenEntity>(e =>
        {
            e.ToTable("OAuthTokens");
            e.HasKey(t => t.Id);
            e.Property(t => t.Provider)
                .IsRequired()
                .HasMaxLength(64);
            e.Property(t => t.UserId)
                .IsRequired()
                .HasMaxLength(256);
            e.Property(t => t.AccessTokenCiphertext)
                .IsRequired();
            e.Property(t => t.RefreshTokenCiphertext)
                .IsRequired();
            e.Property(t => t.ExpiresAtUtc)
                .IsRequired();
            e.Property(t => t.Scopes)
                .IsRequired();
            e.HasIndex(t => new { t.Provider, t.UserId }).IsUnique();
        });
        
        modelBuilder.Entity<AdapterDeliveryLog>(e =>
        {
            e.ToTable("AdapterDeliveryLogs");
            e.HasKey(l => l.Id);
            e.Property(l => l.JobId)
                .IsRequired();
            e.Property(l => l.ChannelType)
                .IsRequired()
                .HasMaxLength(64);
            e.Property(l => l.ChannelConfig)
                .IsRequired();
            e.Property(l => l.Status)
                .HasConversion(
                    v => v.ToString().ToLowerInvariant(),
                    v => Enum.Parse<DeliveryStatus>(v, ignoreCase: true))
                .HasDefaultValue(DeliveryStatus.Pending);
            e.HasOne(l => l.Job)
                .WithMany()
                .HasForeignKey(l => l.JobId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
