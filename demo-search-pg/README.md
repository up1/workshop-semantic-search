# Hybrid Search with PostgreSQL
* [PGVector](https://github.com/pgvector/pgvector)
* Thai-search in PostgreSQL 15 and 16 only
   [chamkho-pg](https://github.com/veer66/chamkho-pg)

## 1. Create PostgreSQL database
```
$docker compose build db
$docker compose up -d db
$docker compose ps
```

## 2. Connect database with PGAdmin
```
$docker compose up -d pgadmin
$docker compose ps
```

Go to http://localhost:5050