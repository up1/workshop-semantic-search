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
```
$dotnet run -- --process migrate
````


## Check data in PostgreSQL database
```
SELECT * FROM pg_extension WHERE extname = 'vector'

SELECT * FROM documents;
```

