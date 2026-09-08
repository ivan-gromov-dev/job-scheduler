using System.Text.Json;

namespace JobScheduler.Core.Serialization;

public sealed class JsonJobPayloadSerializer(JsonSerializerOptions? options = null) : IJobPayloadSerializer
{
    private readonly JsonSerializerOptions options = options ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public string Serialize<TJob>(TJob job) => JsonSerializer.Serialize(job, options);

    public TJob Deserialize<TJob>(string payload) =>
        JsonSerializer.Deserialize<TJob>(payload, options)
        ?? throw new JsonException($"Payload for '{typeof(TJob).FullName}' deserialized to null.");
}
