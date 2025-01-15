using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.AI.Projects;

public class AgentService
{
    private readonly AgentsClient _client;

    public AgentService()
    {
        var connectionString = Environment.GetEnvironmentVariable("PROJECT_CONNECTION_STRING");
        _client = new AgentsClient(connectionString, new DefaultAzureCredential());
    }

    public async Task<Agent> CreateAgentAsync()
    {
        // Create the agent
        Azure.Response<Agent> agentResponse = await _client.CreateAgentAsync(
            model: "gpt-4o-mini",
            name: "Math Tutor",
            instructions: "You are a personal math tutor. Write and run code to answer math questions.",
            tools: new List<ToolDefinition> { new CodeInterpreterToolDefinition() });
        
        Agent agent = agentResponse.Value;

        // Create a thread
        Azure.Response<AgentThread> threadResponse = await _client.CreateThreadAsync();
        AgentThread thread = threadResponse.Value;

        // Create a message
        Azure.Response<ThreadMessage> messageResponse = await _client.CreateMessageAsync(
            thread.Id,
            MessageRole.User,
            "I need to solve the equation `3x + 11 = 14`. Can you help me?");
        
        ThreadMessage message = messageResponse.Value;

        // Execute a run against the agent
        Azure.Response<ThreadRun> runResponse = await _client.CreateRunAsync(
            thread.Id,
            agent.Id,
            additionalInstructions: "Please address the user as Jane Doe. The user has a premium account.");
        
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

        // Note: messages iterate from newest to oldest, with the messages[0] being the most recent
        foreach (ThreadMessage threadMessage in messages)
        {
            Console.Write($"{threadMessage.CreatedAt:yyyy-MM-dd HH:mm:ss} - {threadMessage.Role,10}: ");
            foreach (MessageContent contentItem in threadMessage.ContentItems)
            {
                if (contentItem is MessageTextContent textItem)
                {
                    Console.Write(textItem.Text);
                }
                else if (contentItem is MessageImageFileContent imageFileItem)
                {
                    Console.Write($"<image from ID: {imageFileItem.FileId}>");
                }
                Console.WriteLine();
            }
        }

        return agent;
    }
}