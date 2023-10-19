using Microsoft.AspNetCore.Mvc;
using Dapr;
using KnowledgeBase.Shared;
using Dapr.Client;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Memory.AzureSearch;
using System.Reflection.Metadata;
using KnowledgeBase.DocumentProcessing.Services;


var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDaprClient();
builder.Services.AddSingleton<IFormRecognizerManager, FormRecognizerManager>();

var app = builder.Build();

app.UseCloudEvents();
app.UseRouting();
app.UseEndpoints(endpoints => endpoints.MapSubscribeHandler());

app.MapGet("/probe", () => new OkResult());

app.MapPost("/documentprocess", [Topic("skcodemotion2023queue", "knowledgeprocess")] async (IFormRecognizerManager formRecognizerManager, DaprClient daprClient, DocumentProcessing documentProcessing) =>
{
    Console.WriteLine($"Processing document {documentProcessing.BlobName}");
    var forms = await formRecognizerManager.GetFormsFromPdfAsync(documentProcessing.BlobUri);

    foreach(var form in forms)
    {
        var paragraph = form.Lines.Select(y => y.Text).Aggregate((x, y) => $"{x} {y}");
        daprClient.PublishEventAsync("skcodemotion2023queue", "knowledgeprocess", new KnowledgeProcessing { BlobName = documentProcessing.BlobName, DocumentFrame = paragraph, PageNumber = form.PageNumber }).Wait();
    }
});

app.Run();