using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DallasPermitFunc;

public sealed class ChatFunction(AIProjectClient projectClient, IConfiguration config, ILogger<ChatFunction> logger)
{
    private readonly string _agentName = config["FOUNDRY_AGENT_NAME"]
        ?? throw new InvalidOperationException("FOUNDRY_AGENT_NAME is not configured.");

    // 3. SECURED CHAT ENDPOINT (JwtAuthenticationMiddleware rejects unauthenticated calls).
    [Function("chat")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "chat")] HttpRequest req)
    {
        var body = await req.ReadFromJsonAsync<ChatRequest>();
        if (body is null || string.IsNullOrWhiteSpace(body.Message))
        {
            return new BadRequestObjectResult(new { error = "'message' is required." });
        }

        logger.LogInformation("Chat request from oid {Oid}", req.HttpContext.User.FindFirst("oid")?.Value);

        var convClient = projectClient.ProjectOpenAIClient.GetProjectConversationsClient();
        var conv = string.IsNullOrEmpty(body.ConversationId)
            ? (await convClient.CreateProjectConversationAsync()).Value
            : (await convClient.GetProjectConversationAsync(body.ConversationId)).Value;

        var respClient = projectClient.ProjectOpenAIClient.GetProjectResponsesClientForAgent(_agentName, conv);
        var resp = await respClient.CreateResponseAsync(body.Message);

        return new OkObjectResult(new { conversationId = conv.Id, reply = resp.Value.GetOutputText() });
    }
}

public sealed record ChatRequest(string Message, string? ConversationId);
