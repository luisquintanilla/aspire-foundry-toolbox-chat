using AspireFoundryToolboxChat.Web.Services.Ingestion;
using Microsoft.Extensions.VectorData;

namespace AspireFoundryToolboxChat.Web.Services;

public class SemanticSearch(
    VectorStoreCollection<string, IngestedChunk> vectorCollection,
    [FromKeyedServices("ingestion_directory")] DirectoryInfo ingestionDirectory,
    DataIngestor dataIngestor)
{
    private readonly object _ingestionSync = new();
    private Task? _ingestionTask;

    public Task LoadDocumentsAsync()
    {
        lock (_ingestionSync)
        {
            return _ingestionTask ??= LoadDocumentsCoreAsync();
        }
    }

    private async Task LoadDocumentsCoreAsync()
    {
        try
        {
            await dataIngestor.IngestDataAsync(ingestionDirectory, searchPattern: "*.*");
        }
        catch
        {
            lock (_ingestionSync)
            {
                _ingestionTask = null;
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<IngestedChunk>> SearchAsync(string text, string? documentIdFilter, int maxResults)
    {
        // Ensure documents have been loaded before searching
        await LoadDocumentsAsync();

        var nearest = vectorCollection.SearchAsync(text, maxResults, new VectorSearchOptions<IngestedChunk>
        {
            Filter = documentIdFilter is { Length: > 0 } ? record => record.DocumentId == documentIdFilter : null,
        });

        return await nearest.Select(result => result.Record).ToListAsync();
    }
}
