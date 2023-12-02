using Microsoft.AspNetCore.Mvc;
using Dapr;
using KnowledgeBase.Shared;
using Dapr.Client;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Memory.AzureSearch;
using Microsoft.SemanticKernel.Orchestration;
using Azure.Identity;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDaprClient();

var app = builder.Build();

app.UseRouting();

app.MapGet("/probe", () => new OkResult());

app.MapPost("/api/searchknowldge", async (DaprClient daprClient, SearchKnowledge searchKnowledge) => 
{
    Console.WriteLine($"Searching for prompt {searchKnowledge.Prompt}");
    var azureOpenAIGptDeployment = (await daprClient.GetSecretAsync("skcodemotion2023akv", "AzureOpenAiChatGptDeployment")).Values.FirstOrDefault();
    var azureOpenAIserviceEndpoint = (await daprClient.GetSecretAsync("skcodemotion2023akv", "AzureOpenAiServiceEndpoint")).Values.FirstOrDefault();
    // var azureOpenAIServiceKey = (await daprClient.GetSecretAsync("skcodemotion2023akv", "AzureOpenAiServiceKey")).Values.FirstOrDefault();
    var azureSearchEndpoint = (await daprClient.GetSecretAsync("skcodemotion2023akv", "AzureSearchServiceEndpoint")).Values.FirstOrDefault();
    // var azureSearchKey = (await daprClient.GetSecretAsync("skcodemotion2023akv", "AzureSearchServiceKey")).Values.FirstOrDefault();
    var azureSearchKeyIndex = (await daprClient.GetSecretAsync("skcodemotion2023akv", "AzureSearchIndex")).Values.FirstOrDefault();

    var semanticKernel = Kernel.Builder
        .WithAzureChatCompletionService(
            azureOpenAIGptDeployment,
            azureOpenAIserviceEndpoint,
            new DefaultAzureCredential())
        .WithAzureTextEmbeddingGenerationService(
            "text-embedding-ada-002",
            azureOpenAIserviceEndpoint,
            new DefaultAzureCredential())
        .WithMemoryStorage(new AzureSearchMemoryStore(
            azureSearchEndpoint,
            new DefaultAzureCredential()))
        .Build();

        var memories = semanticKernel.Memory.SearchAsync(azureSearchKeyIndex, searchKnowledge.Prompt, limit: 5);

        var relatedMemory = (await memories.FirstOrDefaultAsync())?.Metadata.Text ?? "I know nothing.";

        var queryFunction = semanticKernel.CreateSemanticFunction(@"
        Consider only the following memories:
        {{$input}}
        Q:{{$prompt}}.
        If you don't know the answer just say I don'tknow. Answer briefly. A:.", maxTokens: 800, temperature: 0.7, topP: 0.95);

        var contextVariables = new ContextVariables();
        contextVariables.Set("input", relatedMemory);
        contextVariables.Set("prompt", searchKnowledge.Prompt);
        var myContext = new SKContext(contextVariables);

        var result = await queryFunction.InvokeAsync(myContext);

        var answer = new SearchKnowledgeAnswer { Answer = result.Result ?? "I didn't find an answer." };

        return answer;
});

app.Run();
