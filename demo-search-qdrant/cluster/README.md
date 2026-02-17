# Demo with cluster of Vector Databases with [Qdrant](https://qdrant.tech/)
* [Distributed Deployment Guide](https://qdrant.tech/documentation/guides/distributed_deployment/)

## 1. Start first node of Qdrant cluster
```
$docker compose build qdrant01
$docker compose up -d qdrant01
$docker compose ps
$docker compose logs -f qdrant01
```

Access to Qdrant Dashboard
* http://localhost:6333/dashboard

Check cluster status
```
$curl http://localhost:6333/cluster/status
```

## 2. Start second node of Qdrant cluster
```
$docker compose build qdrant02
$docker compose up -d qdrant02
$docker compose ps
$docker compose logs -f qdrant02
```

Access to Qdrant Dashboard
* http://localhost:6333/dashboard

Check cluster status
```
$curl http://localhost:6333/cluster/status
```