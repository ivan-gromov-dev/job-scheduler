namespace JobScheduler.Core.Serialization;

public interface IJobPayloadSerializer
{
    string Serialize<TJob>(TJob job);

    TJob Deserialize<TJob>(string payload);
}
