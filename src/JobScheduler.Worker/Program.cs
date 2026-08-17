using JobScheduler.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddJobWorker();

var host = builder.Build();
host.Run();
