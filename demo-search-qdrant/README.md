# Demo with Vector Databse with [Qdrant](https://qdrant.tech/)


## 1. Install with Docker
```
$docker compose up -d qdrant
$docker compose logs -f qdrant
$docker compose ps
```

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
```
$dotnet run
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