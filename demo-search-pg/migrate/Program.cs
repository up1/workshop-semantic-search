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
else if (process == "keyword_search")
{
    var input = "";
    Console.WriteLine("Enter search query:");
    input = Console.ReadLine() ?? "";
    await RunSearchWithFullTextSearch(input);
}
else if (process == "semantic_search")
{
    var input = "";
    Console.WriteLine("Enter search query:");
    input = Console.ReadLine() ?? "";
    await RunSearchWithSemantic(input);
}
else if (process == "hybrid_search")
{
    var input = "";
    Console.WriteLine("Enter search query:");
    input = Console.ReadLine() ?? "";
    await RunSearchWithHybrid(input);
}
else
{
    Console.WriteLine("Usage: dotnet run -- --process migrate");
    return;
}

async Task RunSearchWithHybrid(string input)
{
    var connectionString = "Host=localhost;Port=5432;Database=mydb;Username=user;Password=password";
    var ollamaModel = "bge-m3";
    var topK = 10;
    var ftsWeight = 0.3;      // Weight for full-text search score
    var semanticWeight = 0.7; // Weight for semantic search score

    // Step 1: Generate embedding for the input query using Ollama
    Console.WriteLine($"Hybrid searching for: \"{input}\"");
    using var ollama = new OllamaApiClient();
    var response = await ollama.Embeddings.GenerateEmbeddingAsync(
        model: ollamaModel,
        prompt: input);

    var embedding = response.Embedding;
    var vectorStr = "[" + string.Join(",",
        embedding!.Select(v => v.ToString(CultureInfo.InvariantCulture))) + "]";

    // Step 2: Hybrid search combining full-text search (RRF) + semantic search (RRF)
    // Uses Reciprocal Rank Fusion (RRF) to combine both ranking signals
    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();

    var sql = @"
        WITH semantic AS (
            SELECT id, doc_name, doc_desc,
                   ROW_NUMBER() OVER (ORDER BY embedding <=> @embedding::vector ASC) AS rank_pos,
                   embedding <=> @embedding::vector AS distance
            FROM documents
            ORDER BY distance ASC
            LIMIT 50
        ),
        fulltext AS (
            SELECT id, doc_name, doc_desc,
                   ROW_NUMBER() OVER (ORDER BY ts_rank(search_vector, 
                       plainto_tsquery('english', @input) || plainto_tsquery('chamkho', @input)
                   ) DESC) AS rank_pos,
                   ts_rank(search_vector, 
                       plainto_tsquery('english', @input) || plainto_tsquery('chamkho', @input)
                   ) AS fts_rank
            FROM documents
            WHERE search_vector @@ (plainto_tsquery('english', @input) || plainto_tsquery('chamkho', @input))
            ORDER BY fts_rank DESC
            LIMIT 50
        )
        SELECT 
            COALESCE(s.id, f.id) AS id,
            COALESCE(s.doc_name, f.doc_name) AS doc_name,
            COALESCE(s.doc_desc, f.doc_desc) AS doc_desc,
            COALESCE(s.distance, 1.0) AS distance,
            COALESCE(f.fts_rank, 0.0) AS fts_rank,
            -- RRF score: 1/(k+rank), k=60 is a common constant
            @semanticWeight * COALESCE(1.0 / (60 + s.rank_pos), 0) 
            + @ftsWeight * COALESCE(1.0 / (60 + f.rank_pos), 0) AS hybrid_score
        FROM semantic s
        FULL OUTER JOIN fulltext f ON s.id = f.id
        ORDER BY hybrid_score DESC
        LIMIT @topK";

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("embedding", vectorStr);
    cmd.Parameters.AddWithValue("input", input);
    cmd.Parameters.AddWithValue("topK", topK);
    cmd.Parameters.AddWithValue("semanticWeight", ftsWeight);     // note: positional param names
    cmd.Parameters.AddWithValue("ftsWeight", semanticWeight);

    // Fix: Npgsql matches parameters by name, so correct the naming
    cmd.Parameters.Clear();
    cmd.Parameters.AddWithValue("embedding", vectorStr);
    cmd.Parameters.AddWithValue("input", input);
    cmd.Parameters.AddWithValue("topK", topK);
    cmd.Parameters.AddWithValue("semanticWeight", semanticWeight);
    cmd.Parameters.AddWithValue("ftsWeight", ftsWeight);

    await using var reader = await cmd.ExecuteReaderAsync();

    // Step 3: Display results in table format
    var results = new List<(int Id, string DocName, string DocDesc, double Distance, double FtsRank, double HybridScore)>();
    while (await reader.ReadAsync())
    {
        var id = reader.GetInt32(0);
        var docName = reader.GetString(1);
        var docDesc = reader.IsDBNull(2) ? "" : reader.GetString(2);
        var distance = reader.GetDouble(3);
        var ftsRank = reader.GetFloat(4);
        var hybridScore = reader.GetDouble(5);
        results.Add((id, docName, docDesc, distance, ftsRank, hybridScore));
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
    var colDist = 10;
    var colFts = 10;
    var colHybrid = 12;

    Console.WriteLine();
    Console.WriteLine($"{"ID".PadRight(colId)} | {"Doc Name".PadRight(colName)} | {"Description".PadRight(colDesc)} | {"Cos Dist".PadRight(colDist)} | {"FTS Rank".PadRight(colFts)} | {"Hybrid".PadRight(colHybrid)}");
    Console.WriteLine(new string('-', colId + colName + colDesc + colDist + colFts + colHybrid + 15));

    foreach (var (id, docName, docDesc, distance, ftsRank, hybridScore) in results)
    {
        var desc = docDesc.Length > 50 ? docDesc[..47] + "..." : docDesc;
        Console.WriteLine($"{id.ToString().PadRight(colId)} | {docName.PadRight(colName)} | {desc.PadRight(colDesc)} | {distance:F6} | {ftsRank:F6} | {hybridScore:F6}");
    }

    Console.WriteLine();
    Console.WriteLine($"Found {results.Count} results (weights: semantic={semanticWeight}, fts={ftsWeight}).");
}

async Task RunSearchWithFullTextSearch(string input)
{
    var connectionString = "Host=localhost;Port=5432;Database=mydb;Username=user;Password=password";
    var topK = 10;

    Console.WriteLine($"Full-text searching for: \"{input}\"");

    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();

    // Build tsquery from input using both english and chamkho configurations
    // plainto_tsquery converts plain text into a tsquery with & (AND) between words
    var sql = @"
        SELECT id, doc_name, doc_desc,
               ts_rank(search_vector, 
                   plainto_tsquery('english', @input) || plainto_tsquery('chamkho', @input)
               ) AS rank
        FROM documents
        WHERE search_vector @@ (plainto_tsquery('english', @input) || plainto_tsquery('chamkho', @input))
        ORDER BY rank DESC
        LIMIT @topK";

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("input", input);
    cmd.Parameters.AddWithValue("topK", topK);

    await using var reader = await cmd.ExecuteReaderAsync();

    var results = new List<(int Id, string DocName, string DocDesc, double Rank)>();
    while (await reader.ReadAsync())
    {
        var id = reader.GetInt32(0);
        var docName = reader.GetString(1);
        var docDesc = reader.IsDBNull(2) ? "" : reader.GetString(2);
        var rank = reader.GetFloat(3);
        results.Add((id, docName, docDesc, rank));
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
    var colRank = 10;

    Console.WriteLine();
    Console.WriteLine($"{"ID".PadRight(colId)} | {"Doc Name".PadRight(colName)} | {"Description".PadRight(colDesc)} | {"Rank".PadRight(colRank)}");
    Console.WriteLine(new string('-', colId + colName + colDesc + colRank + 9));

    foreach (var (id, docName, docDesc, rank) in results)
    {
        var desc = docDesc.Length > 50 ? docDesc[..47] + "..." : docDesc;
        Console.WriteLine($"{id.ToString().PadRight(colId)} | {docName.PadRight(colName)} | {desc.PadRight(colDesc)} | {rank:F6}");
    }

    Console.WriteLine();
    Console.WriteLine($"Found {results.Count} results.");
}

async Task RunSearchWithSemantic(string input)
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
