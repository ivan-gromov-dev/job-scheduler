using JobScheduler.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddJobWorker();
builder.Services.AddScheduleMaterializer();

var host = builder.Build();
host.Run();
