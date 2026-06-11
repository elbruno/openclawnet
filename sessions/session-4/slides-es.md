---
marp: true
title: "OpenClawNet — Sesión 4: Despliega, opera y escala"
description: "Novedades de OpenClaw .NET y luego despliegue y operación con Aspire"
theme: openclaw
paginate: true
size: 16:9
footer: "OpenClawNet · Sesión 4 · Despliega, opera y escala"
---

<!-- _class: lead -->

# OpenClawNet
## Sesión 4 — Despliega, opera y escala

**Serie Microsoft Reactor · ~60 min · .NET Intermedio**

> *De “qué hay de nuevo” a operación lista para producción.*

<br>

<div class="speakers">

**Bruno Capuano** — Principal Cloud Advocate, Microsoft  
[github.com/elbruno](https://github.com/elbruno) · [@elbruno](https://twitter.com/elbruno)

**Pablo Piovano** — Microsoft MVP  
[linkedin.com/in/ppiova](https://www.linkedin.com/in/ppiova/)

</div>

---

## Estructura de la sesión

1. **Qué hay de nuevo en OpenClaw .NET**
2. **Desplegar con Aspire**
3. **Observar**
4. **Automatizar**
5. **Asegurar**
6. **Extender (skills)**
7. **Operar a escala**

---

<!-- _class: lead -->

# Parte 1 — Qué hay de nuevo

---

## 1) Skills basados en archivos

- Los skills ahora son artefactos de primera clase en el repositorio
- Versionables, revisables y fáciles de promover entre ambientes
- Modelo claro de ownership por dominio/equipo
- Habilita despliegues más seguros: dev, luego stage, luego producción

```text
skills/
  finance/
    reconciliation.md
  support/
    triage.md
```

---

## 2) Secrets Vault

- Manejo centralizado de secretos en lugar de configuración dispersa
- Runtime consume secretos mediante una abstracción controlada
- Separación de responsabilidades:
  - desarrollo usa nombres de secretos
  - operaciones administra valores y rotación
- Mejor auditoría y menor riesgo de exponer credenciales

---

## 3) Programación de jobs

- Programación integrada para jobs recurrentes y one-shot
- Automatiza tareas de mantenimiento y flujos repetitivos
- Visibilidad operativa: estado, última ejecución, próxima ejecución, fallas
- Base para patrones de confiabilidad en segundo plano

---

## Por qué estas 3 novedades importan juntas

- **Skills** definen el comportamiento del agente
- **Vault** protege configuración sensible y credenciales
- **Jobs** ejecutan ese comportamiento sin disparo manual

> Este es el puente entre “app demo” y “plataforma operable”.

---

<!-- _class: lead -->

# Parte 2 — Desplegar -> Observar -> Automatizar -> Asegurar -> Extender -> Operar a escala

---

## Desplegar (opciones de despliegue de Aspire)

Referencia: [aspire.dev/deployment](https://aspire.dev/deployment/)

- Comienza validando localmente con orquestación completa
- Elige el destino según restricciones:
  - Container Apps / runtime administrado de contenedores
  - Kubernetes (AKS o cluster existente)
  - Escenarios VM / host de contenedores
- Mantén un modelo de aplicación y adapta el destino por ambiente

---

## Observar

- Primero una base: salud, readiness y liveness
- Trazabilidad distribuida para flujos request/tool/job
- Logs + métricas + trazas en una narrativa operativa única
- Alertas accionables (no ruido de dashboard)

---

## Automatizar

- Jobs programados para tareas recurrentes de plataforma
- Chequeos operativos automáticos (drift, recursos obsoletos, fallas)
- Pasos de release automatizados con guardrails
- La automatización debe ser idempotente y observable

---

## Asegurar

- Secretos desde vault, no desde archivos de código
- Mínimo privilegio para identidades de runtime y automatización
- Límites de aprobación para herramientas/acciones riesgosas
- Seguridad integrada al despliegue, no al final

---

## Extender (skills)

- Agrega skills de dominio como paquetes basados en archivos
- Revisión de skills como código (PRs, ownership, changelog)
- Validación de calidad de prompts y límites de uso de tools
- Promoción con versionado y capacidad de rollback

---

## Exportar como Hosted Agent

- Selecciona uno o más perfiles de agente desde la página de definiciones
- Completa el prefijo de despliegue, la región de Azure, la imagen del contenedor y el puerto
- Genera un bundle zip con `main.bicep`, parámetros, manifiesto y notas de despliegue
- Usa el paquete como punto de partida para hospedar el agente en Azure Container Apps
- Trátalo como código: revisa, ajusta y luego despliega

---

## Operar a escala

- Planificación de capacidad y concurrencia para chat y jobs
- Estrategia de fallas: reintentos, backoff, dead-letter
- Despliegues seguros: canary, habilitación por fases, rollback rápido
- Gobernanza de costo y performance basada en telemetría real

---

## Flujo sugerido para la demo en vivo

1. Mostrar un nuevo skill basado en archivo cargado en runtime
2. Leer un secreto con configuración respaldada por vault
3. Crear y ejecutar un job programado
4. Recorrer perfiles de despliegue desde docs de Aspire
5. Observar trazas/salud luego del despliegue

---

## Recursos de la sesión

- Docs de despliegue Aspire: <https://aspire.dev/deployment/>
- Repositorio: <https://github.com/elbruno/openclawnet>
- Materiales de la sesión: `sessions/session-4/`

---

<!-- _class: lead -->

# Preguntas y respuestas

**OpenClaw .NET — Sesión 4**
