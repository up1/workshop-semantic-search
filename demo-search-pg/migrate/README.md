# Embedding process with Ollama


## 1. Install [Ollama](https://ollama.com/)

Install embedding model with Ollama:
* https://huggingface.co/spaces/mteb/leaderboard
* bge-m3
  * https://huggingface.co/BAAI/bge-m3
  * https://ollama.com/library/bge-m3
  * dimension: 1024
  * context window: 8k tokens
```
$ollama pull bge-m3
$ollama list
```

Try to access to Ollama API:
* http://localhost:11434/


## 2. Install Ollama library in .NET project
```
$ dotnet new console
$dotnet add package Ollama
```


## 3. Install PostgreSQL library in .NET project
```
$dotnet add package Npgsql
```

## 4. Run the .NET project
```
$dotnet run
```

Run with parameters:
* migrate - to create table and insert data with embedding vector
* keyword_search - to run keyword search with full-text search and trigram search
* semantic_search - to run semantic search with vector similarity search
* hybrid_search - to run hybrid search with both keyword search and semantic search

```
$dotnet run -- --process migrate
$dotnet run -- --process keyword_search
$dotnet run -- --process semantic_search
$dotnet run -- --process hybrid_search
```

## 5. Check data in PostgreSQL database
```
SELECT * FROM pg_extension WHERE extname = 'vector'

SELECT * FROM documents;
```

## 6. Query with full-text search
```
SELECT id, doc_name, doc_desc,
	   ts_rank(search_vector, 
		   plainto_tsquery('english', @input) || plainto_tsquery('chamkho', @input)
	   ) AS rank
FROM documents
WHERE search_vector @@ (plainto_tsquery('english', @input) || plainto_tsquery('chamkho', @input))
ORDER BY rank DESC
LIMIT 5
```

## 7. Query with vector similarity search
* <-> - L2 distance
* <#> - (negative) inner product
* <=> - cosine distance
* <+> - L1 distance
* <~> - Hamming distance (binary vectors)
* <%> - Jaccard distance (binary vectors)

```
SELECT id, content, embedding <=> '[0.1, 0.2, ...]' AS distance
FROM documents
ORDER BY distance ASC
LIMIT 5;
```

## 8. Improve performance with index (with distance)
* Use IVFFlat when you need exact results and can tolerate slightly slower searches
* Use HNSW when you need fast searches and can accept slight inaccuracies

### IVFFlat (Inverted File Flat) index:
* Suitable for exact nearest neighbor searches
* Divides the vector space into clusters, speeding up searches by first identifying relevant clusters
* Good balance of search speed and accuracy

### HNSW (Hierarchical Navigable Small World) index:
* Designed for approximate nearest-neighbor searches
* Creates a graph structure for swift navigation between vectors
* Extremely fast, but may occasionally miss the absolute nearest neighbor

```
CREATE INDEX ON documents USING ivfflat (embedding vector_cosine_ops) WITH (lists = 100);

CREATE INDEX ON documents USING hnsw (embedding vector_l2_ops) WITH (m = 16, ef_construction = 64);
``` 

## 9. Hybrid search with both keyword search and semantic search
* The hybrid search combines full-text search and semantic (vector) search using Reciprocal Rank Fusion (RRF)
  * semantic — ranks documents by cosine distance (<=>)
  * fulltext — ranks documents by ts_rank on the search_vector column
* Weights default to 0.7 semantic / 0.3 full-text
