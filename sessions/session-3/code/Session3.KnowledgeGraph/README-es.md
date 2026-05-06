# Demostración Session 3: Construcción de Grafo de Conocimiento

## ¿Qué Demuestra Este Código?

Esta demostración muestra cómo construir relaciones estructuradas entre recuerdos extraídos para formar un grafo de conocimiento. Los conceptos clave incluyen:

- **Extracción de Relaciones**: Identificar relaciones semánticas entre hechos (supports, complements, integrates_with, etc.)
- **Nodos y Aristas del Grafo**: Estructurar recuerdos como nodos de grafo con aristas dirigidas que representan relaciones
- **Puntuación de Confianza**: Asignar puntuaciones de confiabilidad a las relaciones
- **Clustering**: Agrupar recuerdos relacionados en clusters conceptuales (temas)
- **Análisis Semántico**: Usar Ollama LLM (llama3.2) para entender cómo se relacionan los recuerdos

## Requisitos

- **.NET 10.0+** (o CLI `dotnet`)
- **Ollama** (para modelo llama3.2)
- **Demostración Session 3 Demo 1**: Extracción Básica de Memoria (para objetos de memoria ascendentes)
- **Linux/macOS/Windows** (multiplataforma)

## Configuración

### 1. Instalar y Ejecutar Ollama

```bash
# Instalar Ollama (https://ollama.ai)
ollama pull llama3.2
ollama serve  # Se ejecuta en http://localhost:11434
```

### 2. Preparar Recuerdos

Esta demostración toma recuerdos extraídos de Session 3 Demo 1 como entrada. Asegúrate de que tu pipeline de extracción de memoria funcione primero.

## Cómo Ejecutar

### Desde la Línea de Comandos

```bash
cd Session3.KnowledgeGraph
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
=== Session 3: Knowledge Graph Construction Demo ===

📚 Input Memories:

  • [preference] Prefers PostgreSQL for microservices relational data
  • [preference] Redis for caching with sub-millisecond latency
  • [config] Prometheus scrapes metrics every 15 seconds
  • [tool] ELK stack for logging infrastructure
  • [preference] ACID transactions are important for data integrity
  • [tool] Grafana for metrics visualization

⏳ Building knowledge graph...

✅ Knowledge Graph Constructed:

📊 Statistics:
  • Nodes (unique memories): 6
  • Edges (relationships): 4

🔗 Relationships:

  Prefers PostgreSQL for microservices relational data
    ├─ [supports] ──→ ACID transactions are important for data integrity
    └─ (confidence: 92%)

  Redis for caching with sub-millisecond latency
    ├─ [complements] ──→ Prometheus scrapes metrics every 15 seconds
    └─ (confidence: 85%)

  ...

🎯 Memory Clusters:

  Cluster: Monitoring Infrastructure
    - Prometheus scrapes metrics every 15 seconds
    - Grafana for metrics visualization
    - ELK stack for logging infrastructure

  Cluster: Data Management
    - Prefers PostgreSQL for microservices relational data
    - ACID transactions are important for data integrity
    - Redis for caching with sub-millisecond latency
```

## Arquitectura

```
Program.cs
├── KnowledgeGraphBuilder (extracción semántica de relaciones)
│   ├── BuildGraph()
│   └── Integración API Ollama
├── KnowledgeGraph (estructura de datos)
│   ├── Nodes: List<string>
│   ├── Edges: List<Edge>
│   └── GetClusters(): List<MemoryCluster>
├── Edge (relación)
│   ├── Source: string
│   ├── Target: string
│   ├── Relationship: string (ej: "supports", "integrates_with")
│   └── Confidence: int
└── MemoryCluster
    ├── Topic: string
    └── Nodes: List<string>
```

## Conceptos Clave

- **Teoría de Grafos**: Los nodos representan recuerdos; las aristas representan relaciones
- **Tipos de Relaciones**: supports, complements, integrates_with, feeds, conflicts, etc.
- **Clustering**: Agrupa recuerdos semánticamente relacionados
- **Comprensión Semántica**: LLM identifica relaciones más allá de coincidencia de palabras clave
- **Puntuaciones de Confianza**: Muestra confiabilidad de relaciones extraídas

## Tipos de Relaciones

| Relación | Significado | Ejemplo |
|----------|-------------|---------|
| `supports` | Un hecho respalda otro | PostgreSQL + transacciones ACID |
| `complements` | Funcionan bien juntos | Redis + Prometheus |
| `integrates_with` | Pueden combinarse | ELK + Prometheus |
| `feeds` | Proporciona entrada a | Prometheus → Grafana |
| `conflicts` | Contradice u se opone | Monolito vs microservicios |
| `related_to` | Asociación general | Cualquier recuerdo relacionado |

## Flujo de Trabajo

1. **Entrada**: Recuerdos extraídos de Demo 1
2. **Detección de Relaciones**: LLM identifica conexiones semánticas
3. **Construcción de Grafo**: Construir nodos y aristas
4. **Clustering**: Agrupar recuerdos relacionados por tema
5. **Salida**: Grafo de conocimiento estructurado

## Próximos Pasos

- Persistir grafo a Neo4j o base de datos de grafos similar
- Implementar traversal de grafo (búsqueda de caminos, análisis de influencia)
- Agregar relaciones temporales (antes, después, causa)
- Construir interfaz de consulta para búsqueda semántica
- Fusionar múltiples grafos de diferentes conversaciones

## Referencias

- [Conceptos de Base de Datos de Grafos](https://neo4j.com/developer/graph-database/)
- [Redes Semánticas](https://es.wikipedia.org/wiki/Red_sem%C3%A1ntica)
- [Grafos de Conocimiento en IA](https://es.wikipedia.org/wiki/Grafo_de_conocimiento)
- [Documentación de Ollama](https://github.com/ollama/ollama)
