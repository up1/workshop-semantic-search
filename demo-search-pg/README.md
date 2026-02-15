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

## 3. Check data in PostgreSQL database 
```
SELECT * FROM pg_extension;
SELECT * FROM pg_extension WHERE extname = 'vector'

SELECT * FROM documents;



SET enable_seqscan = OFF
SET enable_seqscan = ON;
```

Search with full-text search
```
SELECT * 
FROM documents 
WHERE to_tsvector('chamkho', search_text) @@ to_tsquery('chamkho', 'เอก');


-- search term: "chamkho"
SELECT id, doc_name, doc_desc FROM documents
WHERE search_vector @@ plainto_tsquery('chamkho', 'search term')
ORDER BY ts_rank(search_vector, plainto_tsquery('chamkho', 'search term')) DESC
LIMIT 5;

-- search term with "chamkho" and "english"
SELECT id, doc_name, doc_desc FROM documents
WHERE search_vector @@ plainto_tsquery('chamkho', 'เอกสาร') OR
      search_vector @@ plainto_tsquery('english', 'เอกสาร')
ORDER BY ts_rank(search_vector, plainto_tsquery('chamkho', 'เอกสาร')) DESC,
         ts_rank(search_vector, plainto_tsquery('english', 'เอกสาร')) DESC
LIMIT 5;
```

Search with trigram search
```
SELECT id, doc_name, doc_desc, similarity(doc_name, 'เอกสาร')
FROM documents
ORDER BY similarity(doc_name, 'เอกสาร') DESC
```