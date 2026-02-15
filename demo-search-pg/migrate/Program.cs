using System.Globalization;
using Npgsql;
using Ollama;

// Parse command-line arguments
var process = "";
for (int a = 0; a < args.Length - 1; a++)
{
    if (args[a] == "--process")
    {
        process = args[a + 1];
        break;
    }
}

if (process == "migrate")
{
    await RunMigrate();
}
else if (process == "search")
{
    var input = "";
    Console.WriteLine("Enter search query:");
    input = Console.ReadLine() ?? "";
    await RunSearch(input);
}
else
{
    Console.WriteLine("Usage: dotnet run -- --process migrate");
    return;
}

async Task RunSearch(string input)
{
    var connectionString = "Host=localhost;Port=5432;Database=mydb;Username=user;Password=password";
    var ollamaModel = "bge-m3";
    var topK = 5;

    // Step 1: Generate embedding for the input query using Ollama
    Console.WriteLine($"Generating embedding for: \"{input}\"");
    using var ollama = new OllamaApiClient();
    var response = await ollama.Embeddings.GenerateEmbeddingAsync(
        model: ollamaModel,
        prompt: input);

    var embedding = response.Embedding;
    var vectorStr = "[" + string.Join(",",
        embedding!.Select(v => v.ToString(CultureInfo.InvariantCulture))) + "]";

    // Step 2: Search documents table using cosine distance (<=>)
    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();

    // Use cosine distance operator <=> to find nearest neighbors based on embedding
    var sql = @"
        SELECT id, doc_name, doc_desc, embedding <=> @embedding::vector AS distance
        FROM documents
        ORDER BY distance ASC
        LIMIT @topK";

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("embedding", vectorStr);
    cmd.Parameters.AddWithValue("topK", topK);

    await using var reader = await cmd.ExecuteReaderAsync();

    // Step 3: Display results in table format
    var results = new List<(int Id, string DocName, string DocDesc, double Distance)>();
    while (await reader.ReadAsync())
    {
        var id = reader.GetInt32(0);
        var docName = reader.GetString(1);
        var docDesc = reader.IsDBNull(2) ? "" : reader.GetString(2);
        var distance = reader.GetDouble(3);
        results.Add((id, docName, docDesc, distance));
    }
    await conn.CloseAsync();

    if (results.Count == 0)
    {
        Console.WriteLine("No results found.");
        return;
    }

    // Print table header
    var colId = 4;
    var colName = Math.Max(10, results.Max(r => r.DocName.Length)) + 2;
    var colDesc = Math.Max(15, results.Min(r => Math.Min(r.DocDesc.Length, 50))) + 2;
    var colDist = 12;

    Console.WriteLine();
    Console.WriteLine($"{"ID".PadRight(colId)} | {"Doc Name".PadRight(colName)} | {"Description".PadRight(colDesc)} | {"Distance".PadRight(colDist)}");
    Console.WriteLine(new string('-', colId + colName + colDesc + colDist + 9));

    foreach (var (id, docName, docDesc, distance) in results)
    {
        var desc = docDesc.Length > 50 ? docDesc[..47] + "..." : docDesc;
        Console.WriteLine($"{id.ToString().PadRight(colId)} | {docName.PadRight(colName)} | {desc.PadRight(colDesc)} | {distance:F6}");
    }

    Console.WriteLine();
    Console.WriteLine($"Found {results.Count} results.");
}

async Task RunMigrate()
{
    // Configuration
    var connectionString = "Host=localhost;Port=5432;Database=mydb;Username=user;Password=password";
    var ollamaModel = "bge-m3";

    // Step 1: Read documents from database
    var documents = new List<(int Id, string DocName, string DocDesc)>();

    await using var readConn = new NpgsqlConnection(connectionString);
    await readConn.OpenAsync();

    await using var readCmd = new NpgsqlCommand(
        "SELECT id, doc_name, doc_desc FROM documents ORDER BY id", readConn);
    await using var reader = await readCmd.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        var id = reader.GetInt32(0);
        var docName = reader.GetString(1);
        var docDesc = reader.IsDBNull(2) ? "" : reader.GetString(2);
        documents.Add((id, docName, docDesc));
    }
    await readConn.CloseAsync();

    Console.WriteLine($"Read {documents.Count} documents from database");

    // Step 2 & 3: Generate embeddings via Ollama and update database
    using var ollama = new OllamaApiClient();

    await using var updateConn = new NpgsqlConnection(connectionString);
    await updateConn.OpenAsync();

    for (int i = 0; i < documents.Count; i++)
    {
        var (id, docName, docDesc) = documents[i];
        var text = $"{docName} {docDesc}";

        try
        {
            // Call Ollama API to generate embedding with bge-m3
            var response = await ollama.Embeddings.GenerateEmbeddingAsync(
                model: ollamaModel,
                prompt: text);

            var embedding = response.Embedding;

            // Format embedding as PostgreSQL vector string: [0.1,0.2,...]
            var vectorStr = "[" + string.Join(",",
                embedding!.Select(v => v.ToString(CultureInfo.InvariantCulture))) + "]";

            // Update embedding column in documents table
            await using var updateCmd = new NpgsqlCommand(
                "UPDATE documents SET embedding = @embedding::vector WHERE id = @id", updateConn);
            updateCmd.Parameters.AddWithValue("embedding", vectorStr);
            updateCmd.Parameters.AddWithValue("id", id);
            await updateCmd.ExecuteNonQueryAsync();

            Console.WriteLine($"[{i + 1}/{documents.Count}] Updated document {id}: {docName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{i + 1}/{documents.Count}] Error for document {id}: {ex.Message}");
        }
    }

    await updateConn.CloseAsync();
    Console.WriteLine("Done! All embeddings updated.");
}
