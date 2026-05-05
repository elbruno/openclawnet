using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace OpenClawNet.Agent;

/// <summary>
/// Default implementation of skill service that loads from .squad/SKILLS_INVENTORY.md
/// and returns keyword-ranked candidate skills. Semantic re-ranking is applied by
/// <see cref="DefaultPromptComposer"/> via <see cref="ISemanticSkillRanker"/>.
/// </summary>
public sealed class DefaultSkillService : ISkillService
{
    private readonly ILogger<DefaultSkillService> _logger;
    private readonly WorkspaceOptions _workspaceOptions;
    private List<SkillSummary>? _cachedSkills;
    private DateTime _lastLoadTime = DateTime.MinValue;
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(5);

    public DefaultSkillService(
        ILogger<DefaultSkillService> logger,
        IOptions<WorkspaceOptions> workspaceOptions)
    {
        _logger = logger;
        _workspaceOptions = workspaceOptions.Value;
    }

    public async Task<IReadOnlyList<SkillSummary>> FindRelevantSkillsAsync(
        string taskDescription,
        int topN = 3,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var skills = await LoadSkillsInventoryAsync(cancellationToken);
            
            if (skills.Count == 0)
            {
                _logger.LogWarning("No skills found in inventory");
                return Array.Empty<SkillSummary>();
            }

            // Extract keywords from task description (simple tokenization)
            var taskKeywords = ExtractKeywords(taskDescription);
            
            if (taskKeywords.Length == 0)
            {
                _logger.LogDebug("No keywords extracted from task description");
                return Array.Empty<SkillSummary>();
            }

            // Score each skill based on keyword matches + confidence weight
            foreach (var skill in skills)
            {
                skill.RelevanceScore = CalculateRelevanceScore(skill, taskKeywords);
            }

            // Sort by relevance and take top N
            var keywordRankedSkills = skills
                .Where(s => s.RelevanceScore > 0)
                .OrderByDescending(s => s.RelevanceScore)
                .Take(topN)
                .ToList();

            if (keywordRankedSkills.Count == 0)
            {
                _logger.LogDebug("No keywords matched any skills");
                return Array.Empty<SkillSummary>();
            }

            // Semantic re-ranking is applied by DefaultPromptComposer (Story 3 / #89).
            // DefaultSkillService returns keyword-ranked candidates only.
            _logger.LogInformation(
                "Skill keyword-ranking: '{TaskKeywords}' → {Final} candidates [{Skills}]",
                string.Join(", ", taskKeywords),
                keywordRankedSkills.Count,
                string.Join(", ", keywordRankedSkills.Select(s => s.Name)));

            return keywordRankedSkills;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to find relevant skills, continuing without skill injection");
            return Array.Empty<SkillSummary>();
        }
    }

    private async Task<List<SkillSummary>> LoadSkillsInventoryAsync(CancellationToken cancellationToken)
    {
        // Check cache
        if (_cachedSkills != null && DateTime.UtcNow - _lastLoadTime < CacheExpiration)
        {
            return _cachedSkills;
        }

        var inventoryPath = Path.Combine(_workspaceOptions.WorkspacePath, ".squad", "SKILLS_INVENTORY.md");
        
        if (!File.Exists(inventoryPath))
        {
            _logger.LogDebug("Skills inventory not found at {Path}, skill injection disabled", inventoryPath);
            return new List<SkillSummary>();
        }

        var content = await File.ReadAllTextAsync(inventoryPath, cancellationToken);
        var skills = ParseInventory(content);
        
        _cachedSkills = skills;
        _lastLoadTime = DateTime.UtcNow;
        
        _logger.LogDebug("Loaded {Count} skills from inventory", skills.Count);
        return skills;
    }

    private List<SkillSummary> ParseInventory(string content)
    {
        var skills = new List<SkillSummary>();
        
        // Parse the Quick Reference table
        // Format: | skill-name | date | agent | confidence | keywords |
        var tableRegex = new Regex(@"\|\s*([a-z0-9-]+)\s*\|\s*([0-9-]+)\s*\|\s*([a-z]+)\s*\|\s*\*\*([A-Z]+)\*\*\s*\|\s*(.+?)\s*\|", 
            RegexOptions.Multiline);
        
        var matches = tableRegex.Matches(content);
        
        foreach (Match match in matches)
        {
            if (match.Groups.Count < 6) continue;
            
            var name = match.Groups[1].Value.Trim();
            var date = match.Groups[2].Value.Trim();
            var agent = match.Groups[3].Value.Trim();
            var confidenceStr = match.Groups[4].Value.Trim();
            var keywordsStr = match.Groups[5].Value.Trim();
            
            var confidence = confidenceStr.ToUpper() switch
            {
                "HIGH" => ConfidenceLevel.High,
                "MEDIUM" => ConfidenceLevel.Medium,
                "LOW" => ConfidenceLevel.Low,
                _ => ConfidenceLevel.Low
            };
            
            var keywords = keywordsStr
                .Split(',')
                .Select(k => k.Trim())
                .Where(k => !string.IsNullOrEmpty(k))
                .ToArray();
            
            skills.Add(new SkillSummary
            {
                Name = name,
                Description = $"Skill: {name}",
                Keywords = keywords,
                Confidence = confidence,
                ExtractedDate = date,
                ValidatedBy = [agent]
            });
        }
        
        return skills;
    }

    private static string[] ExtractKeywords(string taskDescription)
    {
        if (string.IsNullOrWhiteSpace(taskDescription))
            return Array.Empty<string>();

        // Simple tokenization: lowercase, split on non-alphanumeric, filter short words
        var words = Regex.Split(taskDescription.ToLowerInvariant(), @"[^a-z0-9-]+")
            .Where(w => w.Length >= 3)
            .Distinct()
            .ToArray();

        return words;
    }

    private static int CalculateRelevanceScore(SkillSummary skill, string[] taskKeywords)
    {
        var score = 0;
        var skillKeywords = skill.Keywords.Select(k => k.ToLowerInvariant()).ToHashSet();

        // Count keyword matches
        foreach (var keyword in taskKeywords)
        {
            if (skillKeywords.Contains(keyword))
            {
                score += 10; // Base score for exact match
            }
            else if (skillKeywords.Any(sk => sk.Contains(keyword) || keyword.Contains(sk)))
            {
                score += 5; // Partial match
            }
        }

        // Weight by confidence level
        var confidenceBoost = skill.Confidence switch
        {
            ConfidenceLevel.High => 3,
            ConfidenceLevel.Medium => 2,
            ConfidenceLevel.Low => 1,
            _ => 1
        };

        return score * confidenceBoost;
    }
}
