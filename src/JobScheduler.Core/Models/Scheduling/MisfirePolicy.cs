namespace JobScheduler.Core.Scheduling;

public enum MisfirePolicy
{
    Skip,
    Coalesce,
    CatchUp,
}
