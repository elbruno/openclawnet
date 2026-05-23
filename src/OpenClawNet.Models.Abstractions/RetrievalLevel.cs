using System.Text.Json.Serialization;

namespace OpenClawNet.Models.Abstractions;

[JsonConverter(typeof(JsonStringEnumConverter<RetrievalLevel>))]
public enum RetrievalLevel
{
    Off = 0,
    MemoryOnly = 1,
    VectorDb = 2,
    Hybrid = 3
}
