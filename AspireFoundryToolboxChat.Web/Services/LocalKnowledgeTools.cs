using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace AspireFoundryToolboxChat.Web.Services;

public sealed class LocalKnowledgeTools(SemanticSearch search)
{
    public IReadOnlyList<AIFunction> CreateFunctions() =>
    [
        AIFunctionFactory.Create(LoadDocumentsAsync),
        AIFunctionFactory.Create(SearchAsync),
    ];

    [Description("Loads the local documents before searching them. Call this once before the first local document search.")]
    public async Task<string> LoadDocumentsAsync()
    {
        await search.LoadDocumentsAsync();
        return "Local documents are ready to search.";
    }

    [Description("Searches locally indexed documents for grounded information. Returned excerpts become chat-model context.")]
    public async Task<IReadOnlyList<string>> SearchAsync(
        [Description("The phrase or keywords to search for.")] string searchPhrase,
        [Description("An optional exact filename to limit the search.")] string? filenameFilter = null)
    {
        var results = await search.SearchAsync(searchPhrase, filenameFilter, maxResults: 5);
        return results
            .Select(result => $"<result filename=\"{result.DocumentId}\">{result.Text}</result>")
            .ToArray();
    }
}
