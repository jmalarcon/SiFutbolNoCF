# AGENTS.md

Guía de referencia rápida, contexto de negocio y reglas de actuación obligatorias para agentes de IA y desarrolladores que trabajen en este repositorio.

---

## 1. Propósito del Proyecto

**SiFutbolNoCF** es una herramienta diseñada para mitigar de forma automatizada los **bloqueos dinámicos** que los proveedores de servicios de Internet (ISP) aplican en España a las direcciones IP de Cloudflare durante la emisión de eventos deportivos de pago.

### Mecanismo de Funcionamiento
1. Consulta periódicamente el *endpoint* de estado (`https://hayahora.futbol/status.json`) para verificar si los dominios monitoreados están bloqueados.
2. **Si hay bloqueo (`ok: false`)**: Desactiva de inmediato el *proxy* de Cloudflare (cambia la "nube naranja" a "nube gris"), exponiendo temporalmente la IP original del servidor para que el tráfico no pase por Cloudflare ni sea bloqueado.
3. **Cuando termina el bloqueo (`ok: true`)**: Reactiva el *proxy* de Cloudflare (vuelve la "nube naranja") para restaurar la protección, CDN y certificados de Cloudflare.

---

## 2. Stack Tecnológico y Arquitectura

- **Plataforma**: .NET 10.0 (`net10.0`), C#.
- **Filosofía**: **Zero-Dependencies**. Siempre que sea posible y no complique innecesariamente el código utiliza exclusivamente la librería estándar de .NET (BCL: `System.Text.Json`, `System.Net.Http`, etc.) sin añadir paquetes NuGet de terceros. Si hay opciones de paquetes mejores, explícalas antes de utilizarlas y que decida el usuario. Si se utiliza alguna, justificarlo en `AGENTS.md`.
- **Compilación Multiplataforma**: Generación de binarios autónomos de un solo archivo (*single-file self-contained*) para Windows, Linux y macOS (arquitecturas `x64` y `arm64`).

### Estructura del Código
- [`Program.cs`](Program.cs): Punto de entrada, enrutamiento de argumentos CLI, lógica del modo demonio/one-off, llamadas HTTP hacia la API de Cloudflare y la API de estado, e interfaz visual en consola.
- [`ConfigurationManager.cs`](ConfigurationManager.cs): Gestión y resolución de la configuración combinando archivos JSON y variables de entorno.
- [`SiFutbolNoCF.csproj`](SiFutbolNoCF.csproj): Definición del proyecto, propiedades de compilación y recursos embebidos.
- [`build.bat`](build.bat): Script para compilación y empaquetado desatendido en todas las plataformas soportadas hacia `./build/<plataforma>`.
- [`appsettings.json`](appsettings.json): Plantilla base de configuración para distribución.

---

## 3. Modos de Ejecución y Configuración

### Modos de Ejecución
1. **Modo Demonio Continuo** (sin argumentos): Bucle infinito que comprueba y sincroniza el estado de los dominios cada `IntervalSeconds` segundos.
2. **Modo de Ejecución Única** (`-1` o `--once`): Ejecuta una sola iteración completa del ciclo y finaliza (ideal para cron jobs, Azure WebJobs o GitHub Actions).
3. **Modo Directo / One-Off** (6 argumentos posicionales): Actualización inmediata de un registro sin depender de archivos de configuración:
   ```text
   SiFutbolNoCF <dominio> <registro> <tipo> <activateCfProxy> <apiToken> <zoneId>
   ```
4. **Modo Ayuda** (`-?` o `--help`): Muestra la guía de uso en consola.

### Precedencia y Búsqueda de Configuración
La aplicación carga los archivos JSON directamente desde su directorio base de ejecución (`AppDomain.CurrentDomain.BaseDirectory`), resolviendo sus valores evaluando las fuentes en este orden estricto de prioridad:
1. `appsettings.local.json` (fichero local de desarrollo, excluido de Git y configurado en `.csproj` para copiarse al directorio de salida solo en compilaciones `Debug` y nunca al publicar).
2. `appsettings.json` (fichero de configuración base distribuible).
3. Variables de Entorno (`CF_API_TOKEN`, `STATUS_URL`, `INTERVAL_SECONDS`).

---

## 4. Reglas Generales de Actuación para Agentes

Cualquier cambio o adición de código debe adherirse rigurosamente a las siguientes directrices:

### 4.1. Uso Exclusivo de C# Moderno (.NET 10)
- Emplear siempre las características y modos de proceder más modernos del lenguaje:
  - Interpolación de cadenas moderna en lugar de concatenaciones manuales o `string.Format`.
  - Operadores de coalescencia y asignación nula (`??`, `??=`) y navegación segura (`?.`).
  - *Pattern matching* y *switch expressions*.
  - Inicializadores simplificados y declaraciones de tipo implícitas/modernas donde aporten claridad.
- Evitar estructuras arcaicas, patrones obsoletos de versiones previas de C# o sintaxis innecesariamente verbosa.

### 4.2. Código Cuidado, Limpio y Optimizado
- Mantener un código de alta calidad: evitar código descuidado, ineficiente o asignaciones superfluas.
- Reutilizar instancias costosas (por ejemplo, mantener la instancia estática única de `HttpClient` con tiempos de espera configurados).
- Gestión rigurosa y preventiva de excepciones y validaciones de datos para evitar caídas imprevistas del bucle de monitorización.

### 4.3. Multiplataforma Estricto
- Queda prohibido el uso de llamadas a APIs nativas del sistema operativo (P/Invoke) o dependencias específicas de Windows que impidan la portabilidad.
- Utilizar siempre las abstracciones multiplataforma de .NET (`Path.Combine`, `OperatingSystem.IsWindows()`, etc.).

### 4.4. Filosofía Zero-Dependencies
- No añadir paquetes NuGet externos salvo que sea una necesidad crítica e ineludible explícitamente autorizada. Todo el procesamiento (JSON, HTTP, manipulación de cadenas, etc.) debe resolverse con la librería estándar de .NET.

### 4.5. Seguridad y Gestión de Secretos
- Nunca incorporar claves de API, tokens o IDs de zona reales en el código fuente ni en `appsettings.json`.
- Respetar el aislamiento de `appsettings.local.json` asegurando que no se compile en el output (`CopyToOutputDirectory: Never`) ni se incluya en el control de versiones.

### 4.6. Idioma, Documentación y Salida por Consola
- Todo el código (nombres de variables cuando aplique, comentarios de código y documentación XML `<summary>`) debe redactarse en **español**.
- Conservar el formato visual estándar de la consola:
  - **Color neutro por defecto**: No forzar colores de consola (`ConsoleColor` / ANSI foreground) para garantizar un contraste perfecto y natural en cualquier terminal (fondo negro, blanco de macOS, azul de PowerShell, etc.).
  - **Jerarquía y sangrado en 2 niveles**: Uso de `   ├─ 👀 <dominio>` para la cabecera del dominio e `   ├─── ` para las operaciones subordinadas.
  - **Emojis descriptivos con soporte UTF-8 (`Console.OutputEncoding = Encoding.UTF8`)**: `🔍`, `✅`, `❌`, `⚠️`, `⏳`, `🔒`, `🔓`, `🔴`, `👀`, `ℹ️`.
  - **Mensajes concisos y directos**, evitando redundancias de nombres de dominio en las sub-ramas.

## 5. Intrucciones adicionales
- Siempre se debe ofrecer un plan de acción y aclarar todas las dudas importantes antes de tocar código, salvo que sea algo trivial o de pequeño impacto.
- Despues de cada actuación relevante sobre el proyecto actualiza los archivos `AGENTS.md` (info desarrolladores y agentes IA) y `README.md` (info pública) para garantizar que tiene al día toda la información importante del proyecto.
