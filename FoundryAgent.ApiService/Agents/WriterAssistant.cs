using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.AI.Projects;
using Microsoft.SemanticKernel;
using Microsoft.Extensions.Configuration;

public class WriterAssistant
{
    private readonly AIProjectClient _projectClient;
    private readonly AgentsClient _client;
    private readonly Azure.AI.Projects.Agent? _agent;

    public WriterAssistant(IConfiguration configuration)
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
            var agent = _client.GetAgent("asst_nETuc67cSXfOc9EhIJwdsgZ5").Value;
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
                You are a high-quality journalist agent who excels at writing a first draft of an article as well as revising the article based on feedback from the other agents. 
                Do not just write bullet points on how you would write the article, but actually write it.
                Your responsibility is to rewrite content according to review suggestions.
                - Always apply all review direction.
                - Always revise the content in its entirety without explanation.
                """;*/
        string instructions2 = """
                You are a clothing brand marketing assistant who excels at writing a first, VERY SHORT, draft of a product description as well as revising the description based on feedback from the other agents. 
                Your ideas should be innovative and tailored to young adults. Give me three unique ideas, each with a brief sales description.
                Add details like fabric, color, manufacturing and distribution if possible.
                - Always apply all review direction.
                - Always revise the content in its entirety without explanation.
                - Always write a very short description.
                """;
        // Create the agent
        Azure.Response<Agent> agentResponse = await _client.CreateAgentAsync(
            model: "gpt-4o",
            name: "WriterAssistant",
            instructions: instructions2
            );
        
        return agentResponse.Value;
    }

    public Agent GetAgent()
    {
        if (_agent == null)
            throw new InvalidOperationException("WriterAssistant agent is not initialized.");
        return _agent;
    }


    //Semantic Kernel attribute to define the function that will be called by the agent
    /*[KernelFunction("article_assistant")]
    [Description("A high-quality journalist agent who excels at writing a first draft of an article as well as revising the article based on feedback from the other agents")]*/
    [KernelFunction("product_marketing_writer_assistant")]
    [Description("A high-quality product-marketing agent who excels at writing a first draft of product description as well as revising the description based on feedback from the other agents")]
    [return: Description("The product description draft")]
    public async Task<string> WriteArticle(string article)
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