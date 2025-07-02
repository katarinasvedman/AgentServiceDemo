using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.AI.Projects;
using Microsoft.SemanticKernel;
using Microsoft.Extensions.Configuration;

public class AgentService
{
    private readonly AgentsClient _client;
    private readonly Azure.AI.Projects.Agent _agent;

    public AgentService(IConfiguration configuration)
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
        // Create the agent
        Azure.Response<Agent> agentResponse = await _client.CreateAgentAsync(
            model: "gpt-4o",
            name: "Equation description",
            instructions: "You are a personal math tutor for kids that is expert on describing solutions in a simple way.");
        
        return agentResponse.Value;
    }

    //Semantic Kernel attribute to define the function that will be called by the agent
    [KernelFunction("explain_solution")]
    [Description("Provides a detailed explanation of the solution.")]
    [return: Description("The detailed explanation of the solution.")]
    //[SKParameter("solution", "The equation to calculate")] string title,
    public async Task<string> ExplainSolutionAsync(string solution)
    {
        // Create a thread
        Azure.Response<AgentThread> threadResponse = await _client.CreateThreadAsync();
        AgentThread thread = threadResponse.Value;

        // Create a message
        Azure.Response<ThreadMessage> messageResponse = await _client.CreateMessageAsync(
            thread.Id,
            MessageRole.User,
            solution);
        
        ThreadMessage message = messageResponse.Value;

        // Execute a run against the agent
        Azure.Response<ThreadRun> runResponse = await _client.CreateRunAsync(
            thread.Id,
            _agent.Id);
        
        ThreadRun run = runResponse.Value;

        // Poll the run status until it is completed
        do
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            runResponse = await _client.GetRunAsync(thread.Id, run.Id);
        }
        while (runResponse.Value.Status == RunStatus.Queued || runResponse.Value.Status == RunStatus.InProgress);

        // Retrieve messages after the run
        Azure.Response<PageableList<ThreadMessage>> afterRunMessagesResponse = await _client.GetMessagesAsync(thread.Id);
        IReadOnlyList<ThreadMessage> messages = afterRunMessagesResponse.Value.Data;

        // Extract and return the response from the agent
        foreach (ThreadMessage threadMessage in messages)
        {
            foreach (MessageContent contentItem in threadMessage.ContentItems)
            {
                if (contentItem is MessageTextContent textItem)
                {
                    return textItem.Text;
                }
            }
        }

        return "No response from the agent.";
    }
}