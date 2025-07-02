using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents.Chat;

namespace FoundryAgent.ApiService.Strategies
{
    /// <summary>
    /// A termination strategy that ends the chat when either:
    /// 1. The maximum number of iterations is reached
    /// 2. An approval message is received from any agent
    /// </summary>
    public sealed class ApprovalTerminationStrategy : TerminationStrategy
    {
        private readonly int _maximumIterations;

        public ApprovalTerminationStrategy(int maximumIterations = 10)
        {
            _maximumIterations = maximumIterations;
        }

        // Terminate when the final message contains the term "approve" or max iterations reached
        protected override Task<bool> ShouldAgentTerminateAsync(Microsoft.SemanticKernel.Agents.Agent agent, IReadOnlyList<ChatMessageContent> history, CancellationToken cancellationToken)
            => Task.FromResult(
                history.Count >= _maximumIterations || 
                (history[history.Count - 1].Content?.Contains("TERMINATE", StringComparison.OrdinalIgnoreCase) ?? false));
    }
}