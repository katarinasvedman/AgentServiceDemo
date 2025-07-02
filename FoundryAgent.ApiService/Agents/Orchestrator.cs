using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.AI.Projects;
using Microsoft.SemanticKernel;
using Microsoft.Extensions.Configuration;

public class Orchestrator
{
    private readonly AIProjectClient _projectClient;
    private readonly AgentsClient _client;
    private readonly Azure.AI.Projects.Agent? _agent;

    public Orchestrator(IConfiguration configuration)
    {
        var connectionString = configuration["ConnectionStrings:ProjectConnectionString"] ?? Environment.GetEnvironmentVariable("PROJECT_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Project connection string must be provided via appsettings or environment variables.");
        }
        _client = new AgentsClient(connectionString, new DefaultAzureCredential());
        var clientOptions = new AIProjectClientOptions();
        _projectClient = new AIProjectClient(connectionString, new DefaultAzureCredential(), clientOptions);
        try{
            var agent = _client.GetAgent("asst_ZJuQoROkJBOQXIouEPEB9eXg").Value;
            if (agent != null)
            {
                _agent = agent;
            }
        }catch (Exception ex)
        {
            _agent = CreateAgentAsync().GetAwaiter().GetResult();
            Console.WriteLine($"Error retrieving agent: {ex.Message}");
        }
    }

    private async Task<Agent> CreateAgentAsync()
    {
        // Create the agent
        Azure.Response<Agent> agentResponse = await _client.CreateAgentAsync(
            model: "gpt-4o",
            name: "Orchestrator",
            instructions: "You are leading a journalism team that conducts research to craft high-quality articles. Your team is a writer assistand, an edior and a verifier. You ensure that the output contains an actual well-written article, not just bullet points on what or how to write the article. If the article isn't to that level yet, ask the writer for a rewrite. If the team has written a strong article with a clear point that meets the requirements, and has been reviewed by the editor, and has been fact-checked and approved by the verifier agent then reply 'approved'. Otherwise state what condition has not yet been met. Make sure that the final article is written for the reader and does not include the internal team notes.");
        
        return agentResponse.Value;
    }

    public Agent GetAgent()
    {
        if (_agent == null)
            throw new InvalidOperationException("Orchestrator agent is not initialized.");
        return _agent;
    }

    //Semantic Kernel attribute to define the function that will be called by the agent
    [KernelFunction("create_article")]
    [Description("Entry point for creating articles, start here! Team leader for the article creation who verifies when the article is complete and meets all requirements.")]
    [return: Description("The finished article")]
    public async Task<string> CreateArticle(string article)
    {
        // Create a thread
        Azure.Response<AgentThread> threadResponse = await _client.CreateThreadAsync();
        AgentThread thread = threadResponse.Value;

        // Create a message
        Azure.Response<ThreadMessage> messageResponse = await _client.CreateMessageAsync(
            thread.Id,
            MessageRole.User,
            article);
        
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