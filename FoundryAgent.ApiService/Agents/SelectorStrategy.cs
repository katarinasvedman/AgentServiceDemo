using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Chat;

namespace FoundryAgent.ApiService.Strategies
{
    /// <summary>
    /// A termination strategy that ends the chat when either:
    /// 1. The maximum number of iterations is reached
    /// 2. An approval message is received from any agent
    /// </summary>
    public class SelectorStrategy : SelectionStrategy
    {
        private readonly KernelFunction _selectionFunction;
        private readonly int _maximumIterations = 10;
        const string WriterName = "WriterAssitant";
        const string EditorName = "Editor";
        const string VerifierName = "Verifier";



        public SelectorStrategy(int maximumIterations = 10)
        {
            _maximumIterations = maximumIterations;
            _selectionFunction = AgentGroupChat.CreatePromptFunctionForStrategy(
                $$$"""
                Examine the provided RESPONSE and choose the next participant.
                State only the name of the chosen participant without explanation.
                Never choose the participant named in the RESPONSE.

                You ensure that the output contains an actual well-written article, not just bullet points on what or how to write the article. 
                If the article isn't to that level yet, ask the {{{WriterName}}} for a rewrite. 
                If the team has written a strong article with a clear point that meets the requirements, and has been reviewed by the {{{EditorName}}}, and has been fact-checked and approved by the {{{VerifierName}}} agent then reply 'approved'. 
                Otherwise choose next participant.

                Choose only from these participants:
                - {{{WriterName}}}
                - {{{EditorName}}}
                - {{{VerifierName}}}

                Always follow these rules when choosing the next participant:
                - If RESPONSE is user input, it is {{{WriterName}}}'s turn.                
                - If RESPONSE is by {{{EditorName}}}, it is {{{WriterName}}}'s turn.

                RESPONSE:
                {{$lastmessage}}
                """,
                safeParameterNames: "lastmessage");
        }

        protected override Task<Agent> SelectAgentAsync(IReadOnlyList<Agent> agents, IReadOnlyList<ChatMessageContent> history, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}