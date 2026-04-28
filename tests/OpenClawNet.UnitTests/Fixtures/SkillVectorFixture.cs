using System;
using System.Collections.Generic;

namespace OpenClawNet.UnitTests.Fixtures;

/// <summary>
/// In-memory fixture for testing SkillVector operations (insert, query, upsert, delete).
/// This fixture simulates the skill_vectors table structure for unit tests.
/// </summary>
public sealed class SkillVectorFixture
{
    private readonly List<SkillVectorRecord> _vectors = [];

    public IReadOnlyList<SkillVectorRecord> All => _vectors.AsReadOnly();

    public void InsertVector(string skillId, string skillName, float[] embedding, string description = "")
    {
        if (embedding == null || embedding.Length == 0)
            throw new ArgumentException("Embedding cannot be null or empty", nameof(embedding));

        var existing = _vectors.Find(v => v.SkillId == skillId);
        if (existing != null)
            throw new InvalidOperationException($"Skill vector for {skillId} already exists. Use Upsert instead.");

        _vectors.Add(new SkillVectorRecord
        {
            SkillId = skillId,
            SkillName = skillName,
            Embedding = (float[])embedding.Clone(),
            Description = description,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
    }

    public void UpsertVector(string skillId, string skillName, float[] embedding, string description = "")
    {
        if (embedding == null || embedding.Length == 0)
            throw new ArgumentException("Embedding cannot be null or empty", nameof(embedding));

        var existing = _vectors.Find(v => v.SkillId == skillId);
        if (existing != null)
        {
            existing.SkillName = skillName;
            existing.Embedding = (float[])embedding.Clone();
            existing.Description = description;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _vectors.Add(new SkillVectorRecord
            {
                SkillId = skillId,
                SkillName = skillName,
                Embedding = (float[])embedding.Clone(),
                Description = description,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
    }

    public bool DeleteVector(string skillId)
    {
        var existing = _vectors.Find(v => v.SkillId == skillId);
        if (existing != null)
        {
            _vectors.Remove(existing);
            return true;
        }
        return false;
    }

    public SkillVectorRecord? GetVector(string skillId)
    {
        return _vectors.Find(v => v.SkillId == skillId);
    }

    public List<(SkillVectorRecord Vector, float Similarity)> QueryByEmbedding(float[] queryEmbedding, int topK = 5)
    {
        if (queryEmbedding == null || queryEmbedding.Length == 0)
            throw new ArgumentException("Query embedding cannot be null or empty", nameof(queryEmbedding));

        var results = new List<(SkillVectorRecord, float)>();

        foreach (var vector in _vectors)
        {
            var similarity = CosineSimilarity(queryEmbedding, vector.Embedding);
            results.Add((vector, similarity));
        }

        results.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        return results.Take(topK).ToList();
    }

    public void Clear()
    {
        _vectors.Clear();
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Vectors must have same dimension");

        float dotProduct = 0;
        float magnitudeA = 0;
        float magnitudeB = 0;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }

        magnitudeA = (float)Math.Sqrt(magnitudeA);
        magnitudeB = (float)Math.Sqrt(magnitudeB);

        if (magnitudeA == 0 || magnitudeB == 0)
            return 0;

        return dotProduct / (magnitudeA * magnitudeB);
    }
}

public sealed class SkillVectorRecord
{
    public required string SkillId { get; set; }
    public required string SkillName { get; set; }
    public required float[] Embedding { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
