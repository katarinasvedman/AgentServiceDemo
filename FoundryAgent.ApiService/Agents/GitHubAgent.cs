using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;

public class GitHubAgent
{
    private readonly AgentsClient _client;
    private readonly Agent _agent;

    public GitHubAgent(IConfiguration configuration)
    {
        var connectionString = configuration["ConnectionStrings:ProjectConnectionString"] ?? Environment.GetEnvironmentVariable("PROJECT_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Project connection string must be provided via appsettings or environment variables.");
        }
        _client = new AgentsClient(connectionString, new DefaultAzureCredential());
        _agent = CreateAgentAsync().GetAwaiter().GetResult();
    }

    private async Task<Agent> CreateAgentAsync()
    {
        string instructions = @"You are a helpful assistant that answers user questions about GitHub repositories, issues, pull requests, and related topics. Use the available GitHub tools to search, retrieve, and summarize information as needed. Always provide clear, concise, and accurate answers.";
        var agent = await _client.CreateAgentAsync(
            model: "gpt-4o",
            name: "GitHubAgent",
            instructions: instructions
        );
        return agent;
    }

    public Agent GetAgent()
    {
        return _agent;
    }
}
