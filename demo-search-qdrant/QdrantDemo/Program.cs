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
//  Step 1 – Create / recreate Qdrant collection
// ──────────────────────────────────────────────
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

// ──────────────────────────────────────────────
//  Step 2 – Read documents from PostgreSQL
// ──────────────────────────────────────────────
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

// ──────────────────────────────────────────────
//  Step 3 – Generate embeddings via Ollama & upsert to Qdrant
// ──────────────────────────────────────────────
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

        // Call Ollama embedding API via client
        var embeddingResponse = await ollamaClient.Embeddings.GenerateEmbeddingAsync(
            model: ollamaModel,
            prompt: textToEmbed);

        if (embeddingResponse.Embedding == null || embeddingResponse.Embedding.Count == 0)
        {
            Console.WriteLine($"  [WARN] Empty embedding for doc id={doc.Id}, skipping.");
            continue;
        }

        // Convert double[] to float[] for Qdrant
        var embedding = embeddingResponse.Embedding.Select(d => (float)d).ToArray();

        // Build Qdrant point
        var point = new PointStruct
        {
            Id = new PointId { Num = (ulong)doc.Id },
            Vectors = embedding
        };

        // Store metadata as payload
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

// ──────────────────────────────────────────────
//  Step 4 – Verify
// ──────────────────────────────────────────────
Console.WriteLine("\n=== Step 4: Verify collection ===");
var collectionInfo = await qdrantClient.GetCollectionInfoAsync(collectionName);
Console.WriteLine($"Collection '{collectionName}' now has {collectionInfo.PointsCount} points.");
Console.WriteLine("\nMigration complete!");

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
