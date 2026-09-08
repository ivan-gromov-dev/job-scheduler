using JobScheduler.Core.Handlers;
using JobScheduler.Core.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddJobHandler<Greet, GreetHandler>("example.greet");
builder.Services.AddJobWorker(options => options.MaxConcurrency = 2);

using var host = builder.Build();
await host.StartAsync();

var client = host.Services.GetRequiredService<IJobClient>();
var job = await client.EnqueueAsync(new Greet("world"));
Console.WriteLine($"Enqueued {job.Id}");

await host.StopAsync();

internal sealed record Greet(string Name);

internal sealed class GreetHandler : IJobHandler<Greet>
{
    public Task HandleAsync(Greet job, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Hello, {job.Name}!");
        return Task.CompletedTask;
    }
}
