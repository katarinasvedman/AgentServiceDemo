using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.AI.Projects;
using Microsoft.SemanticKernel;
using Microsoft.Extensions.Configuration;

public class Editor
{
    private readonly AIProjectClient _projectClient;
    private readonly AgentsClient _client;
    private readonly Azure.AI.Projects.Agent? _agent;

    public Editor(IConfiguration configuration)
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
            var agent = _client.GetAgent("asst_BwcNBE5VdN9GkNf47jXNEWoZ").Value;
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
         /*string instructions = """
                 You are an expert editor. 
                You carefully read an article and make suggestions for improvements and suggest additional topics that should be researched to improve the article quality. 
                Never directly perform the correction or provide example. Once the content has been updated in a subsequent response, 
                you will review the content again until satisfactory. When it is satisfactory don't add antoher suggestions. 
                RULES: 
                - Only identify suggestions that are specific and actionable. 
                - Verify previous suggestions have been addressed. 
                - Never repeat previous suggestions"
                """;*/
        string instructions2 = """
                You are a clothing brand marketing assistant editor. You need to improve the given text.
                Never directly perform the correction or provide example. Once the content has been updated in a subsequent response, 
                you will review the content again until satisfactory. When it is satisfactory don't add other suggestions.
                RULES: 
                - Only identify suggestions that are specific and actionable. 
                - Verify previous suggestions have been addressed. 
                - Never repeat previous suggestions"
                """;
        // Create the agent
        Azure.Response<Agent> agentResponse = await _client.CreateAgentAsync(
            model: "gpt-4o",
            name: "Editor",
            instructions: instructions2);
        
        return agentResponse.Value;
    }
    public Agent GetAgent()
    {
        if (_agent == null)
            throw new InvalidOperationException("Editor agent is not initialized.");
        return _agent;
    }


    //Semantic Kernel attribute to define the function that will be called by the agent
    [KernelFunction("edit_product_description")]
    [Description("An expert editor of product descriptions who can read a description and make suggestions for improvements and additional topics that should be researched")]
    //[Description("An expert editor of written articles who can read an article and make suggestions for improvements and additional topics that should be researched")]
    [return: Description("Editor suggestions")]
    public async Task<string> EditArticle(string article)
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