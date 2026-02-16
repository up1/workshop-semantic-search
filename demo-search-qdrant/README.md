# Demo with Vector Databse with [Qdrant](https://qdrant.tech/)


## 1. Install with Docker
```
$docker compose up -d qdrant
$docker compose logs -f qdrant
$docker compose ps
```

Ports of Qdrant:
* REST API: 6333
* GRPC API: 6334

Check status of Qdrant with
```
$curl --location 'http://localhost:6333/healthz' \
--header 'api-key: demo' \
--header 'Cookie: MANTIS_PROJECT_COOKIE=0'

$curl --location 'http://localhost:6333/collections' \
--header 'api-key: demo' \
--header 'Cookie: MANTIS_PROJECT_COOKIE=0'
```

## 2. Create .Net project and install Nuget package
* [Qdrant client](https://github.com/qdrant/qdrant-dotnet)
```
$dotnet new console -n QdrantDemo
$cd QdrantDemo
$dotnet add package Qdrant.Client
```

## 3. Migrate data from PostgreSQL to Qdrant
* From table=`documents` in PostgreSQL to collection=`my_documents` in Qdrant
* Use batch size = 50 for migration
* Use embedding model=bge-m3
* Use API key=demo for authentication
* Use GRPC protocol for communication with Qdrant
```
$dotnet run -- --process migrate
```

## 4. Check data in Qdrant
```
$curl --location 'http://localhost:6333/collections/my_documents/points/query' \
--header 'api-key: demo' \
--header 'Content-Type: application/json' \
--header 'Cookie: MANTIS_PROJECT_COOKIE=0' \
--data '{
    "limit": 5,
    "with_payload": true, 
    "with_vectors": true
}'
```

## 5. Search in Qdrant
```
$dotnet run -- --process search --query "ภาษา"

$dotnet run -- --process search --query "ภาษา" --top 3
```

## 6. Delete collection in Qdrant
```$curl --location 'http://localhost:6333/collections/my_documents' \
--header 'api-key: demo' \
--header 'Cookie: MANTIS_PROJECT_COOKIE=0' \
--request DELETE
```

## 7. Migate data from PostgreSQL to Qdrant with REST API
```
$dotnet run -- --process migrate-with-rest-api
```

## 8. Search in Qdrant with REST API
```
$dotnet run -- --process search-with-rest-api --query "learning" --top 5
```

## 9. Hybrid search in Qdrant with REST API
* Sparse search with BM25 algorithm + Dense search with vector similarity search
* Dense search with vector similarity search only

```
$dotnet run -- --process hybrid-search-with-rest-api --query "learning" --top 5
```