var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure and services are added in later milestones (see docs/build-guide.md).
builder.AddProject<Projects.LankaMart_Gateway>("gateway");

builder.Build().Run();
