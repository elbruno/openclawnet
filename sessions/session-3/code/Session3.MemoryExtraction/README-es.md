# Demostración Session 3: Extracción Básica de Memoria

## ¿Qué Demuestra Este Código?

Esta demostración muestra cómo extraer hechos clave y recuerdos del texto de conversación. Los conceptos principales incluyen:

- **Parsing de contexto conversacional** para identificar información importante
- **Categorización de recuerdos** por tipo (preferencias, hechos, configuraciones, herramientas, etc.)
- **Puntuación de confianza** para indicar la confiabilidad de la extracción
- **Integración con Ollama LLM** (llama3.2) para comprensión semántica

## Requisitos

- **.NET 10.0+** (o CLI `dotnet`)
- **Ollama** (para modelo llama3.2)
- **Linux/macOS/Windows** (multiplataforma)

## Configuración

### 1. Instalar Ollama

```bash
# Descargar desde https://ollama.ai
# macOS/Windows: Ejecutar instalador
# Linux: curl -fsSL https://ollama.ai/install.sh | sh
```

### 2. Descargar Modelo llama3.2

```bash
ollama pull llama3.2
```

### 3. Iniciar Ollama Server

```bash
ollama serve
# Se ejecuta en http://localhost:11434
```

## Cómo Ejecutar

### Desde la Línea de Comandos

```bash
cd Session3.MemoryExtraction
dotnet run
```

### Solo Compilar

```bash
dotnet build
```

### Ejecutar con URL Ollama Personalizada

```bash
export OLLAMA_URL=http://localhost:11434  # Unix/macOS
set OLLAMA_URL=http://localhost:11434     # Windows
dotnet run
```

## Salida Esperada

```
=== Session 3: Basic Memory Extraction Demo ===

📝 Conversation:
[texto de conversación...]

⏳ Extracting memories...

✅ Extracted Memories:

  📌 preference: Prefers PostgreSQL for microservices relational data
     Confidence: 95%

  📌 preference: Redis for caching with sub-millisecond latency
     Confidence: 90%

  📌 config: Prometheus scrapes metrics every 15 seconds
     Confidence: 85%

  📌 tool: ELK stack for logging infrastructure
     Confidence: 88%
```

## Arquitectura

```
Program.cs
├── MemoryExtractor (cliente HTTP a Ollama)
│   ├── ExtractMemoriesAsync()
│   └── Integración API Ollama
└── Memory (tipo record)
    ├── Type: string
    ├── Content: string
    └── Confidence: int
```

## Conceptos Clave

- **Análisis Semántico**: Usa LLM para entender significado, no solo regex
- **Puntuación de Confianza**: Muestra qué tan confiable es cada extracción
- **Categorización por Tipo**: Agrupa recuerdos por dominio (preferencia, hecho, configuración, herramienta, etc.)
- **Formato JSON**: Datos estructurados para procesamiento posterior

## Próximos Pasos

- Extender a conversaciones multi-turno
- Agregar persistencia de memoria (almacenamiento en base de datos)
- Implementar deduplicación de memoria
- Construir umbrales de confianza para filtrado

## Referencias

- [Documentación de Ollama](https://github.com/ollama/ollama)
- [Tarjeta del Modelo llama3.2](https://www.llama.com/docs/model-cards-and-prompt-formats/llama2)
