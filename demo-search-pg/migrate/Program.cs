using System.Globalization;
using Npgsql;
using Ollama;

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
