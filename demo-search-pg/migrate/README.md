# Embedding process with Ollama


## Install [Ollama](https://ollama.com/)

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


## Install Ollama library in .NET project
```
$ dotnet new console
$dotnet add package Ollama
```


## Install PostgreSQL library in .NET project
```
$dotnet add package Npgsql
```

## Run the .NET project
```
$dotnet run

```

Run with parameters:
* migrate - to create table and insert data with embedding vector
* keyword_search - to run keyword search with full-text search and trigram search
* semantic_search - to run semantic search with vector similarity search
```
$dotnet run -- --process migrate
````


## Check data in PostgreSQL database
```
SELECT * FROM pg_extension WHERE extname = 'vector'

SELECT * FROM documents;
```

## Query with vector similarity search
* <-> - L2 distance
* <#> - (negative) inner product
* <=> - cosine distance
* <+> - L1 distance
* <~> - Hamming distance (binary vectors)
* <%> - Jaccard distance (binary vectors)


```SELECT id, content, embedding <=> '[0.1, 0.2, ...]' AS distance
FROM documents
ORDER BY distance ASC
LIMIT 5;
```

## Improve performance with index (with distance)
```
CREATE INDEX ON documents USING ivfflat (embedding vector_cosine_ops) WITH (lists = 100);

CREATE INDEX ON documents USING hnsw (embedding vector_l2_ops) WITH (m = 16, ef_construction = 64);
``` 
