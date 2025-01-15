var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.FoundryAgent_ApiService>("apiservice");
//var agentService = builder.AddProject<Projects.AgentAPI>("agentservice");

builder.AddProject<Projects.FoundryAgent_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService);
    //.WithReference(agentService);

builder.Build().Run();
