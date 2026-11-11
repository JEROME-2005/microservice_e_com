var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.E_Com_App_Server>("e-com-app-server");

builder.Build().Run();
