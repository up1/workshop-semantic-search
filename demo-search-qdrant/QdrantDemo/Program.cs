using Npgsql;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Ollama;

// ──────────────────────────────────────────────
//  Configuration – adjust these values
// ──────────────────────────────────────────────
const string pgConnectionString = "Host=localhost;Port=5432;Database=mydb;Username=user;Password=password";

const string qdrantHost = "localhost";
const int qdrantPort = 6334;  // gRPC port
const string collectionName = "my_documents";

const string ollamaBaseUrl = "http://localhost:11434";
const string ollamaModel = "bge-m3";             // BGE-M3 multilingual embedding model
const int embeddingDimension = 1024;             // must match the model's output dimension

const int batchSize = 50;  // how many rows to process per batch

// ──────────────────────────────────────────────
//  Parse command-line arguments
// ──────────────────────────────────────────────
string? process = null;
string? query = null;
int topK = 5;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--process" when i + 1 < args.Length:
            process = args[++i];
            break;
        case "--query" when i + 1 < args.Length:
            query = args[++i];
            break;
        case "--top" when i + 1 < args.Length:
            topK = int.Parse(args[++i]);
            break;
    }
}

switch (process)
{
    case "migrate":
        await RunMigrate();
        break;
     case "migrate-with-rest-api":
        await RunMigrateWithRestApi();
        break;
    case "search":
        if (string.IsNullOrWhiteSpace(query))
        {
            Console.WriteLine("Error: --query is required for search process.");
            Console.WriteLine("Usage: dotnet run -- --process search --query \"your search text\" [--top 5]");
            return;
        }
        await RunSearch(query, topK);
        break;
    case "search-with-rest-api":
        if (string.IsNullOrWhiteSpace(query))
        {
            Console.WriteLine("Error: --query is required for search process.");
            Console.WriteLine("Usage: dotnet run -- --process search-with-rest-api --query \"your search text\" [--top 5]");
            return;
        }
        await RunSearchWithRestApi(query, topK);
        break;
    case "hybrid-search-with-rest-api":
        if (string.IsNullOrWhiteSpace(query))
        {
            Console.WriteLine("Error: --query is required for search process.");
            Console.WriteLine("Usage: dotnet run -- --process hybrid-search-with-rest-api --query \"your search text\" [--top 5]");
            return;
        }
        await RunHybridSearchWithRestApi(query, topK);
        break;
    default:
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run -- --process migrate");
        Console.WriteLine("  dotnet run -- --process migrate-with-rest-api");
        Console.WriteLine("  dotnet run -- --process search --query \"your search text\" [--top 5]");
        Console.WriteLine("  dotnet run -- --process search-with-rest-api --query \"your search text\" [--top 5]");
        Console.WriteLine("  dotnet run -- --process hybrid-search-with-rest-api --query \"your search text\" [--top 5]");
        return;
}

async Task RunHybridSearchWithRestApi(string query, int topK)
{
    throw new NotImplementedException();
}

async Task RunSearchWithRestApi(string searchQuery, int top)
{
    const string qdrantRestUrl = $"http://{qdrantHost}:6333";
    const string apiKey = "demo";

    Console.WriteLine($"=== Searching (REST) for: \"{searchQuery}\" (top {top}) ===\n");

    using var httpClient = new HttpClient
    {
        BaseAddress = new Uri(qdrantRestUrl),
        Timeout = TimeSpan.FromMinutes(5)
    };
    httpClient.DefaultRequestHeaders.Add("api-key", apiKey);

    // Step 1 – Generate embedding for the search query via Ollama
    using var ollamaClient = new OllamaApiClient(
        new HttpClient { BaseAddress = new Uri(ollamaBaseUrl + "/api"), Timeout = TimeSpan.FromMinutes(10) });

    var embeddingResponse = await ollamaClient.Embeddings.GenerateEmbeddingAsync(
        model: ollamaModel,
        prompt: searchQuery);

    if (embeddingResponse.Embedding == null || embeddingResponse.Embedding.Count == 0)
    {
        Console.WriteLine("Error: Failed to generate embedding for the query.");
        return;
    }

    var queryVector = embeddingResponse.Embedding.Select(d => (float)d).ToArray();

    // Step 2 – Search Qdrant via REST API
    var searchBody = System.Text.Json.JsonSerializer.Serialize(new
    {
        vector = queryVector,
        limit = top,
        with_payload = true
    });

    var searchResponse = await httpClient.PostAsync(
        $"/collections/{collectionName}/points/search",
        new StringContent(searchBody, System.Text.Encoding.UTF8, "application/json"));
    searchResponse.EnsureSuccessStatusCode();

    var searchJson = await searchResponse.Content.ReadAsStringAsync();
    var searchDoc = System.Text.Json.JsonDocument.Parse(searchJson);
    var results = searchDoc.RootElement.GetProperty("result").EnumerateArray().ToList();

    if (results.Count == 0)
    {
        Console.WriteLine("No results found.");
        return;
    }

    Console.WriteLine($"Found {results.Count} results:\n");
    foreach (var result in results)
    {
        var score = result.GetProperty("score").GetDouble();
        var id = result.GetProperty("id").GetUInt64();
        var payload = result.GetProperty("payload");

        var docName = payload.TryGetProperty("doc_name", out var name) ? name.GetString() ?? "N/A" : "N/A";
        var docDesc = payload.TryGetProperty("doc_desc", out var desc) ? desc.GetString() ?? "N/A" : "N/A";

        Console.WriteLine($"  Score: {score:F4} | ID: {id}");
        Console.WriteLine($"  Name:  {docName}");
        Console.WriteLine($"  Desc:  {docDesc}");
        Console.WriteLine();
    }
}

async Task RunMigrateWithRestApi()
{
    const string qdrantRestUrl = $"http://{qdrantHost}:6333"; // REST API port
    const string apiKey = "demo";

    using var httpClient = new HttpClient
    {
        BaseAddress = new Uri(qdrantRestUrl),
        Timeout = TimeSpan.FromMinutes(5)
    };
    httpClient.DefaultRequestHeaders.Add("api-key", apiKey);

    // Step 1 – Create / recreate Qdrant collection via REST
    Console.WriteLine("=== Step 1: Connect to Qdrant (REST) & ensure collection exists ===");

    var listResponse = await httpClient.GetAsync("/collections");
    listResponse.EnsureSuccessStatusCode();
    var listJson = await listResponse.Content.ReadAsStringAsync();
    var listDoc = System.Text.Json.JsonDocument.Parse(listJson);
    var existingCollections = listDoc.RootElement
        .GetProperty("result")
        .GetProperty("collections")
        .EnumerateArray()
        .Select(c => c.GetProperty("name").GetString())
        .ToList();

    if (existingCollections.Contains(collectionName))
    {
        Console.WriteLine($"Collection '{collectionName}' already exists. Deleting...");
        var deleteResponse = await httpClient.DeleteAsync($"/collections/{collectionName}");
        deleteResponse.EnsureSuccessStatusCode();
    }

    var createBody = System.Text.Json.JsonSerializer.Serialize(new
    {
        vectors = new
        {
            size = embeddingDimension,
            distance = "Cosine"
        }
    });
    var createResponse = await httpClient.PutAsync(
        $"/collections/{collectionName}",
        new StringContent(createBody, System.Text.Encoding.UTF8, "application/json"));
    createResponse.EnsureSuccessStatusCode();
    Console.WriteLine($"Collection '{collectionName}' created (dim={embeddingDimension}, cosine).");

    // Step 2 – Read documents from PostgreSQL
    Console.WriteLine("\n=== Step 2: Read documents from PostgreSQL ===");

    var documents = new List<Document>();

    await using var conn = new NpgsqlConnection(pgConnectionString);
    await conn.OpenAsync();

    await using var cmd = new NpgsqlCommand("SELECT id, doc_name, doc_desc, search_text FROM documents", conn);
    await using var reader = await cmd.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        documents.Add(new Document
        {
            Id = reader.GetInt64(0),
            DocName = reader.IsDBNull(1) ? "" : reader.GetString(1),
            DocDesc = reader.IsDBNull(2) ? "" : reader.GetString(2),
            SearchText = reader.IsDBNull(3) ? "" : reader.GetString(3)
        });
    }

    Console.WriteLine($"Loaded {documents.Count} documents from PostgreSQL.");

    // Step 3 – Generate embeddings via Ollama & upsert to Qdrant via REST
    Console.WriteLine("\n=== Step 3: Generate embeddings (Ollama) & upsert to Qdrant (REST) ===");

    using var ollamaClient = new OllamaApiClient(
        new HttpClient { BaseAddress = new Uri(ollamaBaseUrl + "/api"), Timeout = TimeSpan.FromMinutes(10) });

    var totalBatches = (int)Math.Ceiling((double)documents.Count / batchSize);
    var processedCount = 0;

    for (var batchIndex = 0; batchIndex < totalBatches; batchIndex++)
    {
        var batch = documents.Skip(batchIndex * batchSize).Take(batchSize).ToList();

        var points = new List<object>();

        foreach (var doc in batch)
        {
            var textToEmbed = string.IsNullOrWhiteSpace(doc.SearchText) ? doc.DocName : doc.SearchText;

            var embeddingResponse = await ollamaClient.Embeddings.GenerateEmbeddingAsync(
                model: ollamaModel,
                prompt: textToEmbed);

            if (embeddingResponse.Embedding == null || embeddingResponse.Embedding.Count == 0)
            {
                Console.WriteLine($"  [WARN] Empty embedding for doc id={doc.Id}, skipping.");
                continue;
            }

            var embedding = embeddingResponse.Embedding.Select(d => (float)d).ToArray();

            points.Add(new
            {
                id = doc.Id,
                vector = embedding,
                payload = new Dictionary<string, string>
                {
                    ["doc_name"] = doc.DocName,
                    ["doc_desc"] = doc.DocDesc,
                    ["search_text"] = doc.SearchText
                }
            });
            processedCount++;
        }

        if (points.Count > 0)
        {
            var upsertBody = System.Text.Json.JsonSerializer.Serialize(new { points });
            var upsertResponse = await httpClient.PutAsync(
                $"/collections/{collectionName}/points",
                new StringContent(upsertBody, System.Text.Encoding.UTF8, "application/json"));
            upsertResponse.EnsureSuccessStatusCode();
        }

        Console.WriteLine($"  Batch {batchIndex + 1}/{totalBatches} – upserted {points.Count} points (total: {processedCount})");
    }

    // Step 4 – Verify
    Console.WriteLine("\n=== Step 4: Verify collection ===");
    var infoResponse = await httpClient.GetAsync($"/collections/{collectionName}");
    infoResponse.EnsureSuccessStatusCode();
    var infoJson = await infoResponse.Content.ReadAsStringAsync();
    var infoDoc = System.Text.Json.JsonDocument.Parse(infoJson);
    var pointsCount = infoDoc.RootElement
        .GetProperty("result")
        .GetProperty("points_count")
        .GetUInt64();
    Console.WriteLine($"Collection '{collectionName}' now has {pointsCount} points.");
    Console.WriteLine("\nMigration (REST API) complete!");
}

// ──────────────────────────────────────────────
//  Process: migrate
// ──────────────────────────────────────────────
async Task RunMigrate()
{
    // Step 1 – Create / recreate Qdrant collection
    Console.WriteLine("=== Step 1: Connect to Qdrant & ensure collection exists ===");

    var qdrantClient = new QdrantClient(
        qdrantHost,
        qdrantPort,
        apiKey: "demo"
        );

    var collections = await qdrantClient.ListCollectionsAsync();
    if (collections.Any(c => c == collectionName))
    {
        Console.WriteLine($"Collection '{collectionName}' already exists. Deleting...");
        await qdrantClient.DeleteCollectionAsync(collectionName);
    }

    await qdrantClient.CreateCollectionAsync(collectionName, new VectorParams
    {
        Size = (ulong)embeddingDimension,
        Distance = Distance.Cosine
    });
    Console.WriteLine($"Collection '{collectionName}' created (dim={embeddingDimension}, cosine).");

    // Step 2 – Read documents from PostgreSQL
    Console.WriteLine("\n=== Step 2: Read documents from PostgreSQL ===");

    var documents = new List<Document>();

    await using var conn = new NpgsqlConnection(pgConnectionString);
    await conn.OpenAsync();

    await using var cmd = new NpgsqlCommand("SELECT id, doc_name, doc_desc, search_text FROM documents", conn);
    await using var reader = await cmd.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        documents.Add(new Document
        {
            Id = reader.GetInt64(0),
            DocName = reader.IsDBNull(1) ? "" : reader.GetString(1),
            DocDesc = reader.IsDBNull(2) ? "" : reader.GetString(2),
            SearchText = reader.IsDBNull(3) ? "" : reader.GetString(3)
        });
    }

    Console.WriteLine($"Loaded {documents.Count} documents from PostgreSQL.");

    // Step 3 – Generate embeddings via Ollama & upsert to Qdrant
    Console.WriteLine("\n=== Step 3: Generate embeddings (Ollama) & upsert to Qdrant ===");

    using var ollamaClient = new OllamaApiClient(
        new HttpClient { BaseAddress = new Uri(ollamaBaseUrl + "/api"), Timeout = TimeSpan.FromMinutes(10) });

    var totalBatches = (int)Math.Ceiling((double)documents.Count / batchSize);
    var processedCount = 0;

    for (var batchIndex = 0; batchIndex < totalBatches; batchIndex++)
    {
        var batch = documents.Skip(batchIndex * batchSize).Take(batchSize).ToList();

        var points = new List<PointStruct>();

        foreach (var doc in batch)
        {
            var textToEmbed = string.IsNullOrWhiteSpace(doc.SearchText) ? doc.DocName : doc.SearchText;

            var embeddingResponse = await ollamaClient.Embeddings.GenerateEmbeddingAsync(
                model: ollamaModel,
                prompt: textToEmbed);

            if (embeddingResponse.Embedding == null || embeddingResponse.Embedding.Count == 0)
            {
                Console.WriteLine($"  [WARN] Empty embedding for doc id={doc.Id}, skipping.");
                continue;
            }

            var embedding = embeddingResponse.Embedding.Select(d => (float)d).ToArray();

            var point = new PointStruct
            {
                Id = new PointId { Num = (ulong)doc.Id },
                Vectors = embedding
            };

            point.Payload.Add("doc_name", doc.DocName);
            point.Payload.Add("doc_desc", doc.DocDesc);
            point.Payload.Add("search_text", doc.SearchText);

            points.Add(point);
            processedCount++;
        }

        if (points.Count > 0)
        {
            await qdrantClient.UpsertAsync(collectionName, points);
        }

        Console.WriteLine($"  Batch {batchIndex + 1}/{totalBatches} – upserted {points.Count} points (total: {processedCount})");
    }

    // Step 4 – Verify
    Console.WriteLine("\n=== Step 4: Verify collection ===");
    var collectionInfo = await qdrantClient.GetCollectionInfoAsync(collectionName);
    Console.WriteLine($"Collection '{collectionName}' now has {collectionInfo.PointsCount} points.");
    Console.WriteLine("\nMigration complete!");
}

// ──────────────────────────────────────────────
//  Process: search
// ──────────────────────────────────────────────
async Task RunSearch(string searchQuery, int top)
{
    Console.WriteLine($"=== Searching for: \"{searchQuery}\" (top {top}) ===\n");

    var qdrantClient = new QdrantClient(
        qdrantHost,
        qdrantPort,
        apiKey: "demo"
        );

    using var ollamaClient = new OllamaApiClient(
        new HttpClient { BaseAddress = new Uri(ollamaBaseUrl + "/api"), Timeout = TimeSpan.FromMinutes(10) });

    // Generate embedding for the search query
    var embeddingResponse = await ollamaClient.Embeddings.GenerateEmbeddingAsync(
        model: ollamaModel,
        prompt: searchQuery);

    if (embeddingResponse.Embedding == null || embeddingResponse.Embedding.Count == 0)
    {
        Console.WriteLine("Error: Failed to generate embedding for the query.");
        return;
    }

    var queryVector = embeddingResponse.Embedding.Select(d => (float)d).ToArray();

    // Search Qdrant
    var results = await qdrantClient.SearchAsync(
        collectionName,
        queryVector,
        limit: (ulong)top);

    if (results.Count == 0)
    {
        Console.WriteLine("No results found.");
        return;
    }

    Console.WriteLine($"Found {results.Count} results:\n");
    foreach (var result in results)
    {
        var docName = result.Payload.TryGetValue("doc_name", out var name) ? name.StringValue : "N/A";
        var docDesc = result.Payload.TryGetValue("doc_desc", out var desc) ? desc.StringValue : "N/A";
        Console.WriteLine($"  Score: {result.Score:F4} | ID: {result.Id.Num}");
        Console.WriteLine($"  Name:  {docName}");
        Console.WriteLine($"  Desc:  {docDesc}");
        Console.WriteLine();
    }
}

// ──────────────────────────────────────────────
//  Models
// ──────────────────────────────────────────────
class Document
{
    public long Id { get; set; }
    public string DocName { get; set; } = "";
    public string DocDesc { get; set; } = "";
    public string SearchText { get; set; } = "";
}
