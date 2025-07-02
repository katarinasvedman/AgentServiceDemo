using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Agents.AzureAI;
using Microsoft.SemanticKernel.Agents.Chat;
using Azure.AI.Projects;
using Azure.Identity;
using System.Text;
using Markdig;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;


var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire components.
builder.AddServiceDefaults();

// Add the AzureOpenAI chat completion service as a singleton
var openAiEndpoint = builder.Configuration["AzureOpenAI:Endpoint"] ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
var openAiApiKey = builder.Configuration["AzureOpenAI:ApiKey"] ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
var openAiModel = builder.Configuration["AzureOpenAI:Model"] ?? "gpt-4o-mini";
if (string.IsNullOrWhiteSpace(openAiEndpoint) || string.IsNullOrWhiteSpace(openAiApiKey))
{
    throw new InvalidOperationException("Azure OpenAI endpoint and API key must be provided via appsettings or environment variables.");
}
builder.Services.AddAzureOpenAIChatCompletion(openAiModel, openAiEndpoint, openAiApiKey);

// Add services to the container.
builder.Services.AddProblemDetails();

// Create singletons of your plugins
builder.Services.AddSingleton<AgentServicePlugin>(sp => new AgentServicePlugin(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton<AgentService>(sp => new AgentService(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton<Verifier>(sp => new Verifier(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton<Editor>(sp => new Editor(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton<WriterAssistant>(sp => new WriterAssistant(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton<Orchestrator>(sp => new Orchestrator(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton<GitHubAgent>(sp => new GitHubAgent(sp.GetRequiredService<IConfiguration>()));
// Create the plugin collection (using the KernelPluginFactory to create plugins from objects)
builder.Services.AddSingleton<KernelPluginCollection>((serviceProvider) => 
    [
        KernelPluginFactory.CreateFromObject(serviceProvider.GetRequiredService<AgentServicePlugin>()),
        KernelPluginFactory.CreateFromObject(serviceProvider.GetRequiredService<AgentService>())
    ]
);

// Finally, create the Kernel service with the service provider and plugin collection
builder.Services.AddTransient((serviceProvider)=> {
    KernelPluginCollection pluginCollection = serviceProvider.GetRequiredService<KernelPluginCollection>();

    return new Kernel(serviceProvider, pluginCollection);
});

/*

// Configure Semantic Kernel
builder.Services.AddTransient(sp =>
{
    Console.WriteLine("Creating kernel...");
    var kernelBuilder = Kernel.CreateBuilder();
    kernelBuilder.AddAzureOpenAIChatCompletion("gpt-4o-mini", "https://agent-ai-servicesyew5.cognitiveservices.azure.com/", "8yvRzm6r83VOcnbgPUTusEUPeeAnlIjCsZyhU0dAMX4PinJZu5dgJQQJ99BAACYeBjFXJ3w3AAAAACOG1OhY");
    kernelBuilder.Services.AddLogging(c => c.AddDebug().SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace));
    // Initialize plugin and add to the kernel    
    /*var agentServicePlugin = new AgentServicePlugin();
    var agentService = new AgentService();    
    kernelBuilder.Plugins.AddFromObject(agentServicePlugin, "AgentServicePlugin");
    kernelBuilder.Plugins.AddFromObject(agentService, "AgentService");*/
    /*
    var editor = new Editor();
    var writerAssistant = new WriterAssistant();
    var verifier = new Verifier();    
    kernelBuilder.Plugins.AddFromObject(editor, "Editor");
    kernelBuilder.Plugins.AddFromObject(writerAssistant, "WriterAssistant");
    kernelBuilder.Plugins.AddFromObject(verifier, "Verifier");

    var kernel = kernelBuilder.Build();

    return kernel;
});*/

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

// Add a WeatherForecast endpoint
app.MapGet("/weatherforecast", () =>
{
    var summaries = new[]
    {
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    };
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
});

// Add a new endpoint to orchestrate SolveEquation and ExplainSolution
//https://localhost:7586/solveandexplain?equation=3x+5=20
app.MapGet("/solveandexplain", async context =>
{
    var kernel = context.RequestServices.GetRequiredService<Kernel>();
    var equation = context.Request.Query["equation"].ToString();
    var arguments = new KernelArguments
    {
        { "equation", equation }
    };

    // Solve the equation
    var solveResult = await kernel.InvokeAsync("AgentServicePlugin", "solve_equation", arguments);
    var solution = solveResult.GetValue<string>();
    arguments.Clear();
    arguments.Add("solution", solution);

    // Explain the solution
    var explainResult = await kernel.InvokeAsync("AgentService", "explain_solution", arguments);
    var explanation = explainResult.GetValue<string>();

     // Pretty log output
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("=== Solve and Explain Result ===");
    Console.ResetColor();
    Console.WriteLine($"Equation: {equation}");
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"Solution: {solution}");
    Console.ResetColor();
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("Explanation:");
    Console.ResetColor();
    Console.WriteLine(explanation);
    Console.WriteLine(new string('-', 60));

    await context.Response.WriteAsJsonAsync(new { Solution = solution, Explanation = explanation });
})
.WithName("SolveAndExplain");

// Add a new endpoint to orchestrate SolveEquation and ExplainSolution
//Make sure the plugins are registered in the kernel (the code is in the beginning of the file)
//https://localhost:7586/semantickernel?request=3x+5=14
app.MapGet("/SemanticKernel", async context =>
{
    var kernel = context.RequestServices.GetRequiredService<Kernel>();
    var request = context.Request.Query["request"].ToString();
    var arguments = new KernelArguments
    {
        { "request", request }
    };

    // Plugin already initialized...     
    ChatHistory chatHistory = [];
    chatHistory.AddUserMessage(request);

    IChatCompletionService chatCompletion = kernel.GetRequiredService<IChatCompletionService>();

    OpenAIPromptExecutionSettings openAIPromptExecutionSettings = new()
    {
        FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
    };

    var response = await chatCompletion.GetChatMessageContentAsync(
        chatHistory,
        executionSettings: openAIPromptExecutionSettings,
        kernel: kernel);
    
    // Pretty log output
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("=== SemanticKernel Result ===");
    Console.ResetColor();
    Console.ForegroundColor = ConsoleColor.Magenta;
    Console.WriteLine("[Assistant]:");
    Console.ResetColor();
    var plainText = Markdown.ToPlainText(response.Content ?? string.Empty);
    Console.WriteLine(plainText);
    Console.WriteLine(new string('-', 60));

    await context.Response.WriteAsJsonAsync(new { Response = response.Content });
})
.WithName("SemanticKernel");

// Add a new endpoint to write article
//https://localhost:7586/DeepResearch?input=write a product description about a jeans skirt
app.MapGet("/DeepResearch", async context =>
{
    var kernel = context.RequestServices.GetRequiredService<Kernel>();
    Console.WriteLine("DeepResearch endpoint called with request: " + context.Request.Query["input"].ToString());
    try{
        // Get and validate input
        var input = context.Request.Query["input"].ToString();
        if (string.IsNullOrEmpty(input))
        {
            await context.Response.WriteAsJsonAsync(new { Error = "Input query parameter is required" });
            return;
        }

        // Create Azure.AI.Projects client and agents
        var connectionString = builder.Configuration["ConnectionStrings:ProjectConnectionString"] ?? Environment.GetEnvironmentVariable("PROJECT_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Project connection string must be provided via appsettings or environment variables.");
        }
        AgentsClient _client = new AgentsClient(connectionString, new DefaultAzureCredential());
        // Resolve agents from DI for secure secret management
        var writerAssistant = context.RequestServices.GetRequiredService<WriterAssistant>();
        var editor = context.RequestServices.GetRequiredService<Editor>();
        var verifier = context.RequestServices.GetRequiredService<Verifier>();

        // Create agents with error checking       
        AzureAIAgent? writerAgent = null;
        AzureAIAgent? editorAgent = null;
        AzureAIAgent? verifierAgent = null;
        try
        {            
            editorAgent = new(editor.GetAgent(), _client);
            verifierAgent = new(verifier.GetAgent(), _client);
            writerAgent = new(writerAssistant.GetAgent(), _client);
        }
        catch (Exception ex)
        {
            await context.Response.WriteAsJsonAsync(new { Error = $"Failed to initialize agents: {ex.Message}" });
            return;
        }
        const string WriterName = "WriterAssistant";
        const string EditorName = "Editor";
        const string VerifierName = "Verifier";
        // Define the selection strategy
        KernelFunction selectionFunction =
        AgentGroupChat.CreatePromptFunctionForStrategy(
            $$$"""
                Examine the provided RESPONSE and choose the next participant.
                State only the name of the participant to take the next turn.
                Never choose the participant named in the RESPONSE.
                The product description written by the {{{WriterName}}} needs to be reviewed by the {{{EditorName}}} agent, 
                and lastly verified to meet company regulations and approved by the {{{VerifierName}}} agent. 
                If the {{{EditorName}}} provides suggestions, it should be sent back to the {{{WriterName}}} for a rewrite.
                If the {{{VerifierName}}} does not approve the article, it should be sent back to the {{{WriterName}}} for a rewrite.
                Choose only from these participants:
                - {{{WriterName}}}
                - {{{EditorName}}}
                - {{{VerifierName}}}
                RESPONSE:
                {{$lastmessage}}
                """,
                safeParameterNames: "lastmessage");
        // Define the termination strategy
        const string TerminationToken = "approve";
        KernelFunction terminationFunction =
            AgentGroupChat.CreatePromptFunctionForStrategy(
                $$$"""
                Examine the RESPONSE and determine whether the content has been deemed satisfactory.
                If content is satisfactory, respond with a single word without explanation: {{{TerminationToken}}}.
                If specific suggestions are being provided by {{{EditorName}}}, it is not satisfactory.
                If specific suggestions are being provided by {{{VerifierName}}}, it is not satisfactory.
                If no correction is suggested, it is satisfactory.

                RESPONSE:
                {{$lastmessage}}
                """,
                safeParameterNames: "lastmessage");
        ChatHistoryTruncationReducer historyReducer = new(3);
        KernelFunctionSelectionStrategy selectionStrategy =
          new(selectionFunction, kernel)
          {
              InitialAgent = writerAgent,
              ResultParser = (result) => result.GetValue<string>() ?? WriterName,
              HistoryVariableName = "lastmessage",
              HistoryReducer = historyReducer,
          };
        KernelFunctionTerminationStrategy terminationStrategy =
            new(terminationFunction, kernel)
            {
                Agents = [verifierAgent],
                HistoryReducer = historyReducer,
                HistoryVariableName = "lastmessage",
                MaximumIterations = 10,
                ResultParser = (result) => result.GetValue<string>()?.Contains(TerminationToken, StringComparison.OrdinalIgnoreCase) ?? false
            };
        // Create a chat for agent interaction with error checking
        AgentGroupChat chat = new(writerAgent, editorAgent, verifierAgent)
        {
            ExecutionSettings = new AgentGroupChatSettings
            {
                SelectionStrategy = selectionStrategy,
                TerminationStrategy = terminationStrategy
            }
        };

        try
        {
            // Invoke chat and display messages
            ChatMessageContent chatInput = new(AuthorRole.User, input);
            chat.AddChatMessage(chatInput);

            var messages = new List<string>();
            try
            {
                await foreach (ChatMessageContent response in chat.InvokeAsync())
                {

                    var messageBuilder = new StringBuilder();

                    // Add agent name with markdown formatting
                    messageBuilder.AppendLine($"### {response.AuthorName}");
                    messageBuilder.AppendLine();

                    // Add message content
                    messageBuilder.AppendLine(response.Content);

                    // Add separator
                    messageBuilder.AppendLine();
                    messageBuilder.AppendLine("---");
                    messageBuilder.AppendLine();

                    var formattedMessage = messageBuilder.ToString();
                    messages.Add(formattedMessage);

                    // Print to console with some basic formatting
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.Write($"[{response.AuthorName}]");
                    Console.ResetColor();
                    Console.Write(": ");

                    // Convert markdown to plain text for console
                    var plainText = Markdown.ToPlainText(response.Content ?? string.Empty);
                    Console.WriteLine(plainText);
                    Console.WriteLine(new string('-', 80)); // Separator line
                }

                await context.Response.WriteAsJsonAsync(new
                {
                    Messages = messages,
                    IsComplete = chat.IsComplete
                });
            }
            catch (KernelException kex)
            {
                var innerException = kex.InnerException;
                var errorDetails = new
                {
                    Error = "Agent execution failed",
                    Message = kex.Message,
                    InnerException = innerException?.Message,
                    InnerExceptionType = innerException?.GetType().FullName,
                    LastMessages = messages,
                    Source = kex.Source,
                    TargetSite = kex.TargetSite?.Name,
                    ThreadId = kex.HResult // This might help identify the specific thread that failed
                };

                // Log the full exception for debugging
                Console.WriteLine($"Agent execution failed: {kex}");
                Console.WriteLine($"Inner exception: {innerException}");

                await context.Response.WriteAsJsonAsync(errorDetails);
            }
        }
        finally
        {
            if (chat != null)
            {
                await chat.ResetAsync();
            }
        }
    }
    catch (Exception ex)
    {
        await context.Response.WriteAsJsonAsync(new
        {
            Error = "An unexpected error occurred",
            Details = ex.Message,
            StackTrace = ex.StackTrace
        });
    }
})
.WithName("DeepResearch");

// Add a new endpoint to interact with GitHub via MCP tools
// Example: https://localhost:7586/GitHub?tool=search_repositories&input=Show me the open issues in microsoft/semantic-kernel
app.MapGet("/GitHub", async context =>
{
    var kernel = context.RequestServices.GetRequiredService<Kernel>();

    // Create an MCPClient for the GitHub server
    await using var mcpClient = await McpClientFactory.CreateAsync(new StdioClientTransport(new()
    {
        Name = "MCPServer",
        Command = "npx",
        Arguments = ["-y", "@modelcontextprotocol/server-github"],
    }));
    /*
        //Create an MCPClient for the remote Function MCP server
        // Create the logging handler and wrap it around the default HttpClientHandler
        //var loggingHandler = new LoggingHandler(new HttpClientHandler() {  CheckCertificateRevocationList = true });

        // Create HttpClient with our logging handler
        //HttpClient httpClient = new HttpClient(loggingHandler);

        SseClientTransportOptions sseClientTransportOptions = new SseClientTransportOptions
        {
            Endpoint = new Uri("https://apim-27yukyxutncd2.azure-api.net/mcp/sse"),
            AdditionalHeaders = new Dictionary<string, string>()
            {
                { "Ocp-Apim-Trace", "true" }
            },
            Name = "MCPFunction"
        };

        var clientTransport = new SseClientTransport(
            sseClientTransportOptions, httpClient, ownsHttpClient: true);

        var client = await McpClientFactory.CreateAsync(clientTransport);
    */

    // Retrieve the list of tools available on the GitHub server
    var tools = await mcpClient.ListToolsAsync().ConfigureAwait(false);
    if (!tools.Any())
    {
        await context.Response.WriteAsJsonAsync(new { Error = "No GitHub MCP tools available." });
        return;
    }

    // Log all available tools
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("Available GitHub MCP tools:");
    foreach (var tool in tools)
    {
        Console.WriteLine($"- {tool.Name}: {tool.Description}");
    }
    Console.ResetColor();
    // Register the GitHub plugin if not already present
    if (!kernel.Plugins.Contains("GitHub"))
    {
        kernel.Plugins.AddFromFunctions("GitHub", tools.Select(aiFunction => aiFunction.AsKernelFunction()));
    }

    /*try
    {
        // Enable automatic function calling
        OpenAIPromptExecutionSettings executionSettings = new()
        {
            Temperature = 0,
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(options: new() { RetainArgumentTypes = true })
        };

        // Test using GitHub tools
        var prompt = "Summarize the last four commits to the microsoft/semantic-kernel repository?";
        var result = await kernel.InvokePromptAsync(prompt, new(executionSettings)).ConfigureAwait(false);
        Console.WriteLine($"\n\n{prompt}\n{result}");


        //var result = await kernel.InvokeAsync("GitHub", selectedTool.Name, arguments);
        var response = result.GetValue<string>() ?? "No response from GitHub MCP tools.";

        // Pretty log output
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== GitHub MCP Result ===");
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("[GitHub MCP]:");
        Console.ResetColor();
        var plainText = Markdown.ToPlainText(response);
        Console.WriteLine(plainText);
        Console.WriteLine(new string('-', 60));

        await context.Response.WriteAsJsonAsync(new { Response = response });
    }
    catch (Exception ex)
    {
        await context.Response.WriteAsJsonAsync(new {
            Error = "Failed to invoke GitHub MCP tool.",
            Details = ex.Message
        });
    }*/
})
.WithName("GitHubMCP");

// Register agent classes with DI for secure secret management
/*builder.Services.AddSingleton<Editor>(sp => new Editor(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton<WriterAssistant>(sp => new WriterAssistant(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton<Orchestrator>(sp => new Orchestrator(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton<GitHubAgent>(sp => new GitHubAgent(sp.GetRequiredService<IConfiguration>()));*/

app.Run();

public record WeatherForecast(DateOnly Date, int TemperatureC, string Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

// NOTE: All agent classes now use IConfiguration for secret retrieval via DI.
//       No secrets are hardcoded. For production, use Azure Key Vault or environment variables for secret storage.
//       See README for secret management guidance.
