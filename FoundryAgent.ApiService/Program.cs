using Azure.Identity;
using Microsoft.SemanticKernel;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire components.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

// Configure Semantic Kernel
builder.Services.AddSingleton(sp =>
{
    Console.WriteLine("Creating kernel...");
    var kernelBuilder = Kernel.CreateBuilder();
    // Initialize plugin and add to the kernel    
    var agentServicePlugin = new AgentServicePlugin();
    kernelBuilder.Plugins.AddFromObject(agentServicePlugin, "AgentServicePlugin");

    var kernel = kernelBuilder.Build();

    return kernel;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
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
app.MapGet("/solveandexplain", async context =>
{
    var kernel = context.RequestServices.GetRequiredService<Kernel>();
    var equation = context.Request.Query["equation"].ToString();
    var arguments = new KernelArguments
    {
        { "equation", equation }
    };

    //Register plugins? https://github.com/kinfey/MultiAIAgent/blob/main/09.AzureAIAgentWithSK02.ipynb

    // Solve the equation
    var solveResult = await kernel.InvokeAsync("AgentServicePlugin", "solve_equation", arguments);
    var solution = solveResult.GetValue<string>();
    arguments.Clear();
    arguments.Add("solution", solution);

    // Explain the solution
    var explainResult = await kernel.InvokeAsync("AgentServicePlugin", "explain_solution", arguments);
    var explanation = explainResult.GetValue<string>();


    await context.Response.WriteAsJsonAsync(new { Solution = solution, Explanation = explanation });
})
.WithName("SolveAndExplain");

app.Run();

public record WeatherForecast(DateOnly Date, int TemperatureC, string Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}