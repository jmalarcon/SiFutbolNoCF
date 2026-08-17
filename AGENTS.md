# AGENTS.md

Guía de referencia rápida, contexto de negocio y reglas de actuación obligatorias para agentes de IA y desarrolladores que trabajen en este repositorio.

---

## 1. Propósito del Proyecto

**SiFutbolNoCF** es una herramienta diseñada para mitigar de forma automatizada los **bloqueos dinámicos** que los proveedores de servicios de Internet (ISP) aplican en España a las direcciones IP de Cloudflare durante la emisión de eventos deportivos de pago.

### Mecanismo de Funcionamiento
1. Consulta periódicamente el *endpoint* oficial de estado (`https://hayahora.futbol/estado/data.json`) para obtener el conjunto de direcciones IP bloqueadas activamente por los operadores en España (evaluando el último cambio de estado registrado en `stateChanges`).
2. **Resolución DNS y Detección**: Resuelve las IPs públicas del dominio monitoreado (registros A, AAAA o CNAMEs) mediante `System.Net.Dns.GetHostAddressesAsync`.
3. **Persistencia de IPs de Cloudflare**: Almacena las IPs conocidas de Cloudflare en `.sifutbolnocf.cache.json` para conservarlas incluso cuando el proxy esté desactivado o tras reinicios del proceso.
4. **Si hay bloqueo**: Si alguna de las IPs de Cloudflare del dominio está bloqueada, desactiva de inmediato el *proxy* de Cloudflare (cambia la "nube naranja" a "nube gris"), exponiendo temporalmente la IP original del servidor.
5. **Cuando termina el bloqueo**: Si las IPs de Cloudflare ya no aparecen bloqueadas en el *endpoint*, reactiva el *proxy* de Cloudflare (vuelve la "nube naranja") para restaurar la protección CDN y certificados.

---

## 2. Stack Tecnológico y Arquitectura

- **Plataforma**: .NET 10.0 (`net10.0`), C#.
- **Filosofía**: **Zero-Dependencies**. Siempre que sea posible y no complique innecesariamente el código utiliza exclusivamente la librería estándar de .NET (BCL: `System.Text.Json`, `System.Net.Http`, etc.) sin añadir paquetes NuGet de terceros. Si hay opciones de paquetes mejores, explícalas antes de utilizarlas y que decida el usuario. Si se utiliza alguna, justificarlo en `AGENTS.md`.
- **Compilación Multiplataforma**: Generación de binarios autónomos de un solo archivo (*single-file self-contained*) para Windows, Linux y macOS (arquitecturas `x64` y `arm64`).

### Estructura del Código
- [`Program.cs`](Program.cs): Punto de entrada, procesamiento de argumentos CLI, interfaz en consola y orquestación de servicios.
- **`Models/`**: Modelos de datos del sistema:
  - [`AppSettings.cs`](Models/AppSettings.cs): Modelos de configuración (`AppSettings`, `DomainConfig`).
  - [`CloudflareModels.cs`](Models/CloudflareModels.cs): Modelos de respuesta de la API de Cloudflare (`CloudflareResponse`, `DnsRecord`, etc.).
- **`Services/`**: Servicios especializados con responsabilidad única:
  - [`ConfigurationManager.cs`](Services/ConfigurationManager.cs): Gestión y resolución de la configuración combinando archivos JSON y variables de entorno.
  - [`CloudflareService.cs`](Services/CloudflareService.cs): Operaciones con la API v4 de Cloudflare (búsqueda de zonas, consulta y actualización de registros DNS).
  - [`FootballStatusService.cs`](Services/FootballStatusService.cs): Descarga del endpoint `data.json` y extracción de IPs bloqueadas con `JsonDocument`.
  - [`DnsResolverService.cs`](Services/DnsResolverService.cs): Resolución DNS recursiva de hostnames a IPs.
  - [`IpCacheService.cs`](Services/IpCacheService.cs): Persistencia y gestión de la caché local de IPs en `.sifutbolnocf.cache.json`.
- [`SiFutbolNoCF.csproj`](SiFutbolNoCF.csproj): Definición del proyecto, propiedades de compilación y recursos embebidos.
- [`build.bat`](build.bat): Script Batch (Windows) para compilación y empaquetado desatendido en todas las plataformas soportadas hacia `./build/<plataforma>`.
- [`build.sh`](build.sh): Script Bash (Linux/macOS) para compilación y empaquetado desatendido en todas las plataformas soportadas hacia `./build/<plataforma>`.
- [`appsettings.json`](appsettings.json): Plantilla base de configuración para distribución.

---

## 3. Modos de Ejecución y Configuración

### Modos de Ejecución
1. **Modo Demonio Continuo** (sin argumentos): Bucle infinito que comprueba y sincroniza el estado de los dominios periódicamente. Aplica intervalos adaptativos inteligentes si `AdaptiveInterval` está activo (por defecto `true`), o intervalos fijos de `IntervalSeconds` segundos si se desactiva.
2. **Modo de Ejecución Única** (`-1` o `--once`): Ejecuta una sola iteración completa del ciclo y finaliza (ideal para cron jobs, Azure WebJobs o GitHub Actions).
3. **Modo Directo / One-Off** (6 argumentos posicionales): Actualización inmediata de un registro sin depender de archivos de configuración:
   ```text
   SiFutbolNoCF <dominio> <registro> <tipo> <activateCfProxy> <apiToken> <zoneId>
   ```
4. **Modo Ayuda** (`-?` o `--help`): Muestra la guía de uso en consola.

### Lógica de Intervalos Adaptativos (`AdaptiveInterval`)
- **Franja Valle (01:00 a 13:00)**: Pausa larga de 30 minutos (1800 s) ajustada a las 13:00 para ahorrar peticiones cuando no hay partidos.
- **Franja Activa (13:00 a 01:00)**: Intervalo estándar corto (`IntervalSeconds`, por defecto 300 s) para detectar bloqueos con rapidez.
- **Bloqueo Activo (partido en curso)**: Pausa inicial de 90 minutos (5400 s) tras detectar el bloqueo (los partidos duran > 105 min). Superados los 90 min, vuelve a comprobación frecuente para reactivar el proxy de Cloudflare en cuanto finalice.

### Precedencia y Búsqueda de Configuración
La aplicación carga los archivos JSON directamente desde su directorio base de ejecución (`AppDomain.CurrentDomain.BaseDirectory`), resolviendo sus valores evaluando las fuentes en este orden estricto de prioridad:
1. `appsettings.local.json` (fichero local de desarrollo, excluido de Git y configurado en `.csproj` para copiarse al directorio de salida solo en compilaciones `Debug` y nunca al publicar).
2. `appsettings.json` (fichero de configuración base distribuible).
3. Variables de Entorno (`CF_API_TOKEN`, `STATUS_URL`, `INTERVAL_SECONDS`, `ADAPTIVE_INTERVAL`, `VERBOSITY`).

---

## 4. Reglas Generales de Actuación para Agentes IA

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
- Evitar *code smells* y prácticas de programación que puedan generar deuda técnica o dificultar la mantenibilidad futura.
- El código que generes debe tener comentadas todas las líneas no triviales explicando qué hacen y por qué. Los comentarios deben ser claros, concisos y en español.

### 4.3. Multiplataforma Estricto
- Queda prohibido el uso de llamadas a APIs nativas del sistema operativo (P/Invoke) o dependencias específicas de Windows que impidan la portabilidad.
- Utilizar siempre las abstracciones multiplataforma de .NET (`Path.Combine`, `OperatingSystem.IsWindows()`, etc.).

### 4.4. Filosofía Zero-Dependencies
- No añadir paquetes NuGet externos salvo que sea una necesidad crítica e ineludible explícitamente autorizada. Todo el procesamiento (JSON, HTTP, manipulación de cadenas, etc.) debe resolverse con la librería estándar de .NET.

### 4.5. Seguridad y Gestión de Secretos
- Nunca incorporar claves de API, tokens o IDs de zona reales en el código fuente ni en `appsettings.json`.
- Respetar el aislamiento de `appsettings.local.json` asegurando que no se compile en el output (`CopyToOutputDirectory: Never`) ni se incluya en el control de versiones.
- Si ejecutas el código con dominios reales y cambias el estado del proxy de Cloudflare, asegúrate de volver a cambiarlo expresamente al estado anterior para dejar la prueba limpia y no afectar la disponibilidad del dominio. Puedes usar la ejecución directa con `--once`.

### 4.6. Idioma, Documentación y Salida por Consola
- Todo el código (nombres de variables cuando aplique, comentarios de código y documentación XML `<summary>`) debe redactarse en **español**.
- Conservar el formato visual estándar de la consola:
  - **Color neutro por defecto**: No forzar colores de consola (`ConsoleColor` / ANSI foreground) para garantizar un contraste perfecto y natural en cualquier terminal (fondo negro, blanco de macOS, azul de PowerShell, etc.).
  - **Jerarquía y sangrado en 2 niveles**: Uso de `   ├─ 👀 <dominio>` para la cabecera del dominio e `   ├─── ` para las operaciones subordinadas.
  - **Emojis descriptivos con soporte UTF-8 (`Console.OutputEncoding = Encoding.UTF8`)**: `🔍`, `✅`, `❌`, `⚠️`, `⏳`, `🔒`, `🔓`, `🔴`, `👀`, `ℹ️`.
  - **Mensajes concisos y directos**, evitando redundancias de nombres de dominio en las sub-ramas.

### 4.7. Responsabilidad Única y Desacoplamiento de UI
- Las clases y servicios de negocio (`Services/`, `Models/`) deben estar **totalmente desacoplados de la interfaz de usuario / consola**.
- Ninguna clase de servicio debe contener llamadas directas a `Console.WriteLine`, `Console.Write` ni manipular la salida estándar.
- Los servicios deben limitarse a realizar sus operaciones (HTTP, DNS, disco, parseo) y devolver datos o lanzar excepciones explicativas.
- `Program.cs` es el **único responsable exclusivo** de gobernar la presentación visual, formatear mensajes, imprimir ramas jerárquicas y emitir emojis por consola.

### 4.8. ETS
Actúa como un redactor técnico experto en Español Técnico Simplificado. Para todas tus respuestas, aplica estrictamente las siguientes reglas de control de lenguaje:

1. Estructura: Usa solo oraciones directas con la estructura (Sujeto + Verbo + Objeto). Evita oraciones subordinadas.
2. Longitud: Cada oración debe tener un máximo de 15 palabras. Cada párrafo, un máximo de 3 oraciones.
3. Verbos de acción: Prohíbe las perífrasis verbales. No escribas "proceder a realizar la limpieza", escribe "limpiar". Usa el imperativo directo para instrucciones.
4. Elimina la ambigüedad: No uses el "se" impersonal (ej. No digas "se desconecta el cable", di "desconecte el cable").
5. Vocabulario unívoco: Usa una sola palabra para cada concepto. Evita sinónimos. Prefiere palabras cortas y comunes (ej. Usa "usar" en lugar de "emplear" o "utilizar").
6. Sin adornos: Elimina adverbios terminados en "-mente" y adjetivos calificativos innecesarios.

Sigue los cuatro principios de Zinsser para una escritura de calidad:

1. Sencillez
2. Brevedad
3. Claridad
4. Humanidad

Usa español de España con tuteo.

### 4.9. Instrucciones adicionales
- Siempre debes ofrecer un plan de acción y aclarar con el usuario todas las dudas importantes antes de tocar código, salvo que sea algo trivial o de pequeño impacto.
- Despues de cada actuación relevante sobre el proyecto actualiza los archivos `AGENTS.md` (info desarrolladores y agentes IA) y `README.md` (info pública) para garantizar que tiene al día toda la información importante del proyecto.
- Si se ha añadido una nueva funcionalidad deberás aumentar la versión "minor" en `Properties/AssemblyInfo.cs`, y la versión "mayor" si se introduce un cambio que rompe la compatibilidad hacia atrás (Semantic Versoning).