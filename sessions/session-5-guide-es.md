# Sesión 5: Canales, Navegador y Eventos

**Duración:** 50 minutos | **Nivel:** .NET Intermedio

---

## Descripción General

OpenClawNet está listo para producción. Ahora es el momento de **llevarlo a todas partes**. En esta sesión, extendemos la plataforma en tres direcciones:

1. **Canales de Bot** — Conectar el agente a Microsoft Teams usando Bot Framework, para que los usuarios interactúen desde las aplicaciones de chat que ya usan.
2. **Control del Navegador** — Automatización web con Playwright: navegar páginas, extraer contenido, tomar capturas de pantalla, rellenar formularios.
3. **Disparadores por Eventos** — Ir más allá del polling programado a ejecución por webhooks: pushes de GitHub, alertas de monitoreo y eventos personalizados disparan ejecuciones del agente al instante.

Al final de esta sesión, tu agente recibirá mensajes de Teams, navegará la web como un humano y responderá a eventos del mundo real — completando el viaje de chatbot local a plataforma de IA completamente conectada.

---

## Antes de la Sesión

### Requisitos Previos

- Sesión 4 completa y funcionando
- .NET 10 SDK, VS Code o Visual Studio
- LLM local en ejecución (Ollama con `llama3.2` o Foundry Local)
- **Teams:** Cuenta de desarrollador de Microsoft 365 para pruebas (o usar Bot Framework Emulator)
- **Herramienta browser:** Ejecutar `playwright install chromium` después de `dotnet build`

### Punto de Partida

- El código `session-4-complete`
- Proveedores de nube, programación, health checks y pruebas funcionando
- 24 pruebas pasando

### Checkpoint Git

**Tag de inicio:** `session-5-start` (alias: `session-4-complete`)  
**Tag de fin:** `session-5-complete`

---

## Etapa 1: Canales de Bot — Integración con Teams (15 min)

### Conceptos

**El problema de los canales.**
Hasta ahora, los usuarios solo pueden hablar con OpenClawNet a través de la UI web o la API REST. Pero la mayoría de los usuarios empresariales viven en Teams. Llevar el agente a Teams elimina la fricción: sin nueva pestaña, sin nueva cuenta, solo chat.

**Bot Framework + IBotAdapter.**
Microsoft Bot Framework proporciona el protocolo de comunicación para Teams, Slack y otros canales. Lo envolvemos en nuestra propia abstracción `IBotAdapter` para que el runtime del agente sea agnóstico al canal:

```csharp
public interface IBotAdapter
{
    string Platform { get; }  // "teams", "slack", ...
    Task HandleRequestAsync(HttpContext httpContext, CancellationToken ct = default);
}
```

**Cómo funcionan los bots de Teams.**
1. El usuario envía un mensaje en Teams.
2. Teams llama a nuestro webhook `/api/messages` (HTTP POST).
3. Bot Framework valida el token JWT y parsea la `Activity`.
4. Nuestro `OpenClawNetBot : TeamsActivityHandler` recibe el turno.
5. Llamamos a `IAgentOrchestrator.ProcessAsync` — el mismo runtime que usa la UI web.
6. La respuesta vuelve a Teams vía `turnContext.SendActivityAsync`.

**Continuidad de sesión.**
Cada conversación de Teams se mapea a un session ID de OpenClawNet usando un `ConcurrentDictionary<string, Guid>` en memoria. El historial de mensajes y la memoria persisten entre mensajes del mismo hilo de Teams.

### Código

```csharp
// Registro DI
if (builder.Configuration.GetValue<bool>("Teams:Enabled"))
{
    builder.Services.AddTeamsAdapter();
}

// appsettings.json
"Teams": {
  "Enabled": true,
  "MicrosoftAppId": "<tu-bot-app-id>",
  "MicrosoftAppPassword": "<tu-client-secret>"
}
```

### Demo en Vivo

1. Conectar Bot Framework Emulator a `http://localhost:5000/api/messages`
2. Chatear con el agente a través del emulador
3. Mostrar las herramientas funcionando (p.ej., "lista los archivos del directorio actual")

---

## Etapa 2: Control del Navegador con Playwright (15 min)

### Conceptos

**¿Por qué automatización del navegador?**
Muchas tareas requieren interacción web real: leer contenido que requiere JavaScript, rellenar formularios, extraer datos de páginas dinámicas. La herramienta `web_fetch` solo funciona para contenido estático. Playwright controla un navegador real.

**BrowserTool como ITool.**
La herramienta del navegador se conecta al registro de herramientas existente — sin cambios en el runtime del agente:

```csharp
public sealed class BrowserTool : ITool
{
    public string Name => "browser";
    // Acciones: navigate | extract-text | screenshot | click | fill
}
```

**Setup:**
```bash
dotnet build
playwright install chromium   # configuración única
```

### Código

```csharp
// Extracción de texto — superpoder de investigación del agente
private async Task<ToolResult> ExtractTextAsync(ToolInput input, ...)
{
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
    var page = await browser.NewPageAsync();
    await page.GotoAsync(url);
    var text = await page.InnerTextAsync("body");
    return ToolResult.Ok(Name, text, sw.Elapsed);
}
```

### Demo en Vivo

1. Pedir al agente: *"Extrae el contenido principal de https://devblogs.microsoft.com/dotnet/"*
2. Mostrar la herramienta `browser` siendo llamada en las trazas de Aspire Dashboard
3. Tomar una captura: *"Toma una captura de https://learn.microsoft.com/dotnet/"*

**Momento Copilot:**
> *"Agrega una acción `wait-for-selector` a BrowserTool que espere hasta 5 segundos a que aparezca un selector CSS antes de extraer texto. Usa `WaitForSelectorAsync` de Playwright."*

---

## Etapa 3: Webhooks por Eventos (10 min)

### Conceptos

**Programación vs. eventos.**
La sesión 4 introdujo la programación basada en cron (sondeo cada N segundos/minutos). Los eventos son más poderosos: el agente reacciona *al instante* cuando algo sucede — un push de GitHub, una alerta, un pago recibido.

**El patrón webhook.**
Cualquier sistema que soporte webhooks puede llamar a `POST /api/webhooks/{eventType}` en el Gateway. El agente corre inmediatamente con el payload como contexto.

```bash
curl -X POST http://localhost:5000/api/webhooks/github-push \
  -H "Content-Type: application/json" \
  -d '{"message": "PR #42 merged to main"}'
```

### Demo en Vivo

1. Disparar un webhook manualmente con curl
2. Mostrar el agente procesando el evento en Aspire Dashboard
3. Verificar la sesión creada en la UI web

---

## Cierre (10 min)

### Recapitulación de la Serie Completa

| Sesión | Tema | Qué Construimos |
|---------|-------|----------------|
| **1** | Fundamentos + Chat Local | Aspire, LLM local, streaming HTTP SSE, UI de chat |
| **2** | Herramientas + Flujos del Agente | Bucle de herramientas, registro, ejecutor |
| **3** | Habilidades + Memoria | Habilidades Markdown, resumen, búsqueda semántica |
| **4** | Automatización + Nube | Proveedores nube, programación, health checks, 24 pruebas |
| **5** | Canales + Navegador + Eventos | Adaptador Teams, herramienta browser Playwright, webhooks |

### ¿A Dónde Ir Desde Aquí?

- **Más canales:** Slack (`SlackNet`), Discord (`Discord.Net`), WhatsApp (Twilio)
- **Navegador avanzado:** Automatización de formularios de varios pasos, navegación autenticada
- **Seguridad webhooks:** Verificación de firma HMAC para GitHub/Stripe
- **Multi-agente:** Comunicación agente a agente, patrones orquestadores

### Gracias + Preguntas

- Repositorio: `github.com/elbruno/openclawnet`
- Serie: Microsoft Reactor — OpenClawNet
- Construido con: .NET 10, Aspire, GitHub Copilot, LLMs Locales
