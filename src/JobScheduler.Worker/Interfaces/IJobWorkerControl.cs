namespace JobScheduler.Worker;

public interface IJobWorkerControl
{
    DrainProgress GetProgress();
    DrainProgress BeginDrain();
}
