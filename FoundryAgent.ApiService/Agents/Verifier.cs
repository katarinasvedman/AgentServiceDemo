using System.ComponentModel;
using Azure.Identity;
using Azure.AI.Projects;
using Microsoft.SemanticKernel;
using Azure.Core;
using Microsoft.Extensions.Configuration;

public class Verifier
{
    private readonly AgentsClient _client;
    private readonly AIProjectClient _projectClient;
    private readonly Agent _agent;

    public Verifier(IConfiguration configuration)
    {
        var connectionString = configuration["ConnectionStrings:ProjectConnectionString"] ?? Environment.GetEnvironmentVariable("PROJECT_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Project connection string must be provided via appsettings or environment variables.");
        }
        var clientOptions = new AIProjectClientOptions();

        // Adding the custom headers policy
        clientOptions.AddPolicy(new CustomHeadersPolicy(), HttpPipelinePosition.PerCall);
        _client = new AgentsClient(connectionString, new DefaultAzureCredential());
        _projectClient = new AIProjectClient(connectionString, new DefaultAzureCredential(), clientOptions);        
     
        try{      
            var agent = _client.GetAgent("asst_wqcjB99DZ00S7JF7C0oT7YCy").Value;
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
        // Get Bing connection
        ConnectionResponse bingConnection = await _projectClient.GetConnectionsClient().GetConnectionAsync("bingsearch");
        var bingConnectionId = bingConnection.Id;

        // Get File Search connection
        ConnectionResponse fileSearchConnection = await _projectClient.GetConnectionsClient().GetConnectionAsync("ai-basic-hub-yew5-connection-AIServices");
        var fileSearchConnectionId = fileSearchConnection.Id;

        // Create tool connection lists
        ToolConnectionList bingConnectionList = new ToolConnectionList
        {
            ConnectionList = { new ToolConnection(bingConnectionId) }
        };
        ToolConnectionList fileSearchConnectionList = new ToolConnectionList
        {
            ConnectionList = { new ToolConnection(fileSearchConnectionId) }
        };
        BingGroundingToolDefinition bingGroundingTool = new BingGroundingToolDefinition(bingConnectionList);
        FileSearchToolDefinition fileSearchTool = new FileSearchToolDefinition();

        /*string instructions = """
                You carefully read and ensure an article's accuracy. 
                You should use the Bing tool to search the internet to verify any relevant facts, and explicitly approve or reject the article based on accuracy.
                Never directly perform the correction or provide example. 
                Respond with your fact-check reasoning only. 
                If the article is rejected ask for rewrite, otherwise respond with approve.
                """;*/
        string instructions2 = """
                You carefully read and ensure an product description accuracy. 
                You must verify that the text comply with regulations by using the File search tool, and explicitly approve or reject the description based on accuracy.                
                Never directly perform the correction or provide example. 
                Respond with your fact-check reasoning only. 
                If the description is rejected ask for rewrite, otherwise respond with 'approve'.
                """;

        // Create the agent
        var agent = await _client.CreateAgentAsync(
                model: "gpt-4o",
                name: "Verifier",
                instructions: instructions2
                //,tools: new List<ToolDefinition> { bingGroundingTool, fileSearchTool }
                );
        return agent;
    }
    public Agent GetAgent()
    {
        return _agent;
    }


    //Semantic Kernel attribute to define the function that will be called by the agent
    [KernelFunction("verify_article")]
    [Description("A responsible agent who will verify the facts and ensure that the article is accurate and well-written")]
    [return: Description("Article verification output")]
    public async Task<string> VerifyArticle(string article)
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


