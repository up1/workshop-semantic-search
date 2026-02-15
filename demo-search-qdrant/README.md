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