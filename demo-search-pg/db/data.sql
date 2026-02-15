-- Insert 10,000 rows of data into the "documents" table
-- randomly generated document names, descriptions, and links for testing purposes
INSERT INTO documents (doc_name, doc_desc, doc_link)
SELECT
    random()::text || ' Document ' || s.i AS doc_name,
    random()::text || ' This is a description for document ' || s.i AS doc_desc,
    'http://example.com/document/' || s.i AS doc_link
FROM generate_series(1, 10000) AS s(i); 