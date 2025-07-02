using System.ComponentModel;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Chat;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Azure.Identity;
using Azure.AI.Projects;
using Azure.Core;
using Azure.Core.Pipeline;

public class SKGroupChatAgents
{
    private readonly Kernel kernel;
    public SKGroupChatAgents()
    { 
        var deployment = "gpt-4o-mini";
        var endpoint = "Your AOAI endpoint";
        var key = "Your AOAI key";    
        //var connectionString = "francecentral.api.azureml.ms;4af326ce-fa13-4b32-8c36-6fb8963741c0;rg-aiagent-foundry;ai-basic-project-yew5";//Environment.GetEnvironmentVariable("PROJECT_CONNECTION_STRING");

        kernel = Kernel.CreateBuilder()
        .AddAzureOpenAIChatCompletion(deployment, endpoint, key)
        .Build();
    }

    public async Task<string> InvokeSKAgents()
    {
        
        const string SearchHostName = "Search";
        const string SearchHostInstructions = "You are a search expert, help me use tools to find relevant knowledge";
        const string SaveHostName = "SaveBlog";
        const string SavehHostInstructions = "Save blog content. Respond with 'Saved' to when your blog are saved.";
        const string WriteBlogName = "WriteBlog";
        const string WriteBlogInstructions = "You are a blog writer, please help me write a blog based on bing search content.";
        
        #pragma warning disable SKEXP0110

        ChatCompletionAgent search_agent =
                    new()
                    {
                        Name = SearchHostName,
                        Instructions = SearchHostInstructions,
                        Kernel = kernel,
                        Arguments = new KernelArguments(new AzureOpenAIPromptExecutionSettings() { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto() }),
                    };

            ChatCompletionAgent save_blog_agent =
            new()
            {
                Name = SaveHostName,
                Instructions = SavehHostInstructions,
                Kernel = kernel,
                Arguments = new KernelArguments(new AzureOpenAIPromptExecutionSettings() { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto() }),
            };

            ChatCompletionAgent write_blog_agent =
            new()
            {
                Name = WriteBlogName,
                Instructions = WriteBlogInstructions,
                Kernel = kernel
            };

            KernelPlugin search_plugin = KernelPluginFactory.CreateFromType<SearchPlugin>();
            search_agent.Kernel.Plugins.Add(search_plugin);
            /*KernelPlugin write_blog_plugin = KernelPluginFactory.CreateFromType<SavePlugin>();
            search_agent.Kernel.Plugins.Add(write_plugin);*/
            KernelPlugin save_blog_plugin = KernelPluginFactory.CreateFromType<SavePlugin>();
            search_agent.Kernel.Plugins.Add(save_blog_plugin);

            AgentGroupChat chat =
            new(search_agent, write_blog_agent,save_blog_agent)
            {
                ExecutionSettings =
                    new()
                    {
                        TerminationStrategy =
                            new ApprovalTerminationStrategy()
                            {
                                // Only the art-director may approve.
                                Agents = [save_blog_agent],
                                // Limit total number of turns
                                MaximumIterations = 10,
                            }
                    }
            };

            ChatMessageContent input = new(AuthorRole.User, """
                    I am writing a blog about GraphRAG. Search for the following 2 questions and write a Chinese blog based on the search results ,save it           
                        1. What is Microsoft GraphRAG?
                        2. Vector-based RAG vs GraphRAG
                    """);
            chat.AddChatMessage(input);

            #pragma warning disable SKEXP0110   
            #pragma warning disable SKEXP0001               


            await foreach (ChatMessageContent content in chat.InvokeAsync())
            {
                Console.WriteLine($"# {content.Role} - {content.AuthorName ?? "*"}: '{content.Content}'");
            }            

            return "response";
    }
    private sealed class ApprovalTerminationStrategy : TerminationStrategy
    {
        // Terminate when the final message contains the term "approve"
        protected override Task<bool> ShouldAgentTerminateAsync(Microsoft.SemanticKernel.Agents.Agent agent, IReadOnlyList<ChatMessageContent> history, CancellationToken cancellationToken)
            => Task.FromResult(history[history.Count - 1].Content?.Contains("approve", StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
#pragma warning disable SKEXP0110

internal class CustomHeadersPolicy : HttpPipelineSynchronousPolicy
{
    public override void OnSendingRequest(HttpMessage message)
    {
        message.Request.Headers.Add("x-ms-enable-preview", "true");
    }
}

public sealed class SearchPlugin
{
        [KernelFunction, Description("Search by Bing")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1024:Use properties where appropriate", Justification = "Too smart")]
        public async Task<String> Search([Description("search Item")]
            string searchItem)
        {
            var connectionString = "Your Azure AI Agent Service Connection String";
            var clientOptions = new AIProjectClientOptions();

            // Adding the custom headers policy
            clientOptions.AddPolicy(new CustomHeadersPolicy(), HttpPipelinePosition.PerCall);
            var projectClient = new AIProjectClient(connectionString, new DefaultAzureCredential(), clientOptions);

            ConnectionResponse bingConnection = await projectClient.GetConnectionsClient().GetConnectionAsync("kinfey-bing-search");
            var connectionId = bingConnection.Id;

            AgentsClient agentClient = projectClient.GetAgentsClient();

            ToolConnectionList connectionList = new ToolConnectionList
            {
                ConnectionList = { new ToolConnection(connectionId) }
            };
            BingGroundingToolDefinition bingGroundingTool = new BingGroundingToolDefinition(connectionList);

            Azure.Response<Azure.AI.Projects.Agent> agentResponse = await agentClient.CreateAgentAsync(
            model: "gpt-4-1106-preview",
            name: "my-assistant",
            instructions: "You are a helpful assistant.",
            tools: new List<ToolDefinition> { bingGroundingTool });
            Azure.AI.Projects.Agent agent = agentResponse.Value;

            // Create thread for communication           
            Azure.Response<Azure.AI.Projects.AgentThread> threadResponse = await agentClient.CreateThreadAsync();
            Azure.AI.Projects.AgentThread thread = threadResponse.Value;

            // Create a message
            Azure.Response<Azure.AI.Projects.ThreadMessage> messageResponse = await agentClient.CreateMessageAsync(
            thread.Id,
            MessageRole.User,
            "How does wikipedia explain Euler's Identity?");
        
            Azure.AI.Projects.ThreadMessage message = messageResponse.Value;

            // Run the agent
            Azure.Response<Azure.AI.Projects.ThreadRun> runResponse = await agentClient.CreateRunAsync(thread, agent);
            Azure.AI.Projects.ThreadRun run = runResponse.Value;

            // Poll the run status until it is completed
        do
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            runResponse = await agentClient.GetRunAsync(thread.Id, run.Id);
        }
        while (runResponse.Value.Status == RunStatus.Queued || runResponse.Value.Status == RunStatus.InProgress);

        // Retrieve messages after the run
        Azure.Response<PageableList<Azure.AI.Projects.ThreadMessage>> afterRunMessagesResponse = await agentClient.GetMessagesAsync(thread.Id);
        IReadOnlyList<Azure.AI.Projects.ThreadMessage> messages = afterRunMessagesResponse.Value.Data;

            string searchResult = "";

            // Note: messages iterate from newest to oldest, with the messages[0] being the most recent
            foreach (ThreadMessage threadMessage in messages)
            {
                Console.Write($"{threadMessage.CreatedAt:yyyy-MM-dd HH:mm:ss} - {threadMessage.Role,10}: ");

                if(threadMessage.Role.ToString().ToLower()=="assistant")
                {
                    foreach (MessageContent contentItem in threadMessage.ContentItems)
                    {
                        if (contentItem is MessageTextContent textItem)
                        {
                            Console.Write(textItem.Text);
                            searchResult = textItem.Text;
                        }
                        break;
                        // Console.WriteLine();
                    }
                }
            }

            return searchResult;
        }
}

public sealed class SavePlugin
{
        [KernelFunction, Description("Save blog")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1024:Use properties where appropriate", Justification = "Too smart")]
        public async Task<String> Save([Description("save blog content")]
            string content)
        {
            Console.Write("###"+content);
            var connectionString = "Your Azure AI Agent Service Connection String";
            var clientOptions = new AIProjectClientOptions();

            // Adding the custom headers policy
            clientOptions.AddPolicy(new CustomHeadersPolicy(), HttpPipelinePosition.PerCall);
            var projectClient = new AIProjectClient(connectionString, new DefaultAzureCredential(), clientOptions);

            ConnectionResponse bingConnection = await projectClient.GetConnectionsClient().GetConnectionAsync("kinfey-bing-search");
            var connectionId = bingConnection.Id;

            AgentsClient agentClient = projectClient.GetAgentsClient();

            ToolConnectionList connectionList = new ToolConnectionList
            {
                ConnectionList = { new ToolConnection(connectionId) }
            };
            BingGroundingToolDefinition bingGroundingTool = new BingGroundingToolDefinition(connectionList);

            Azure.Response<Azure.AI.Projects.Agent> agentResponse = await agentClient.CreateAgentAsync(
            model: "gpt-4-1106-preview",
            name: "my-assistant",
            instructions: "You are a helpful assistant.",
            tools: new List<ToolDefinition> { bingGroundingTool });
            Azure.AI.Projects.Agent agent = agentResponse.Value;

            // Create thread for communication           
            Azure.Response<Azure.AI.Projects.AgentThread> threadResponse = await agentClient.CreateThreadAsync();
            Azure.AI.Projects.AgentThread thread = threadResponse.Value;

            // Create a message
            Azure.Response<Azure.AI.Projects.ThreadMessage> messageResponse = await agentClient.CreateMessageAsync(
            thread.Id,
            MessageRole.User,
            "How does wikipedia explain Euler's Identity?");
        
            Azure.AI.Projects.ThreadMessage message = messageResponse.Value;

            // Run the agent
            Azure.Response<Azure.AI.Projects.ThreadRun> runResponse = await agentClient.CreateRunAsync(thread, agent);
            Azure.AI.Projects.ThreadRun run = runResponse.Value;

            // Poll the run status until it is completed
        do
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            runResponse = await agentClient.GetRunAsync(thread.Id, run.Id);
        }
        while (runResponse.Value.Status == RunStatus.Queued || runResponse.Value.Status == RunStatus.InProgress);

        // Retrieve messages after the run
        Azure.Response<PageableList<Azure.AI.Projects.ThreadMessage>> afterRunMessagesResponse = await agentClient.GetMessagesAsync(thread.Id);
        IReadOnlyList<Azure.AI.Projects.ThreadMessage> messages = afterRunMessagesResponse.Value.Data;

            string searchResult = "";

            // Note: messages iterate from newest to oldest, with the messages[0] being the most recent
            foreach (Azure.AI.Projects.ThreadMessage threadMessage in messages)
            {
                Console.Write($"{threadMessage.CreatedAt:yyyy-MM-dd HH:mm:ss} - {threadMessage.Role,10}: ");

                if(threadMessage.Role.ToString().ToLower()=="assistant")
                {
                    foreach (MessageContent contentItem in threadMessage.ContentItems)
                    {
                        if (contentItem is MessageTextContent textItem)
                        {
                            Console.Write(textItem.Text);
                            searchResult = textItem.Text;
                        }
                        break;
                        // Console.WriteLine();
                    }
                }
            }

            return searchResult;
        }
}
   


