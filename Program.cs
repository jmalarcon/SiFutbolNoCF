using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ManageDns.Models;
using ManageDns.Services;

namespace ManageDns
{
	/// <summary>
	/// Clase principal y punto de entrada de la aplicación SiFutbolNoCF.
	/// </summary>
	/// <remarks>
	/// Orquesta los servicios estáticos especializados para la conmutación inteligente de proxies en Cloudflare.
	/// Es el único componente responsable de gobernar la presentación visual, mensajes y emojis por consola.
	/// </remarks>
	class Program
	{
		/// <summary>
		/// Punto de entrada del programa. Determina el modo de ejecución (One-off, Ayuda o Demonio).
		/// </summary>
		/// <param name="args">Argumentos recibidos por línea de comandos.</param>
		static async Task Main(string[] args)
		{
			// Forzar codificación UTF-8 en la consola para garantizar la correcta visualización de emojis en cualquier terminal
			Console.OutputEncoding = Encoding.UTF8;

			// Comprobar si el usuario solicita ayuda mediante -? o --help
			if (args.Length == 1 && (args[0] == "-?" || args[0] == "--help"))
			{
				ShowHelp();
				return;
			}

			// Comprobar si se solicita ejecutar un único ciclo con -1 o --once (ideal para cron jobs o tareas programadas)
			if (args.Length == 1 && (args[0] == "-1" || args[0] == "--once"))
			{
				await RunDaemon(runOnce: true);
			}
			// Comprobar si se proporcionan exactamente 6 argumentos para el modo directo de ejecución única (one-off)
			else if (args.Length == 6)
			{
				await RunOneOff(args);
			}
			// Por defecto, iniciar en modo demonio continuo con bucle infinito y comprobaciones periódicas
			else
			{
				await RunDaemon(runOnce: false);
			}
		}

		/// <summary>
		/// Muestra la guía de ayuda y uso del programa por consola detallando los modos y parámetros admitidos.
		/// </summary>
		static void ShowHelp()
		{
			// Obtener la versión dinámica desde los metadatos del ensamblado
			var assembly = typeof(Program).Assembly;
			var version = assembly.GetName().Version?.ToString() ?? "1.0.0.0";

			// Imprimir cabecera de ayuda
			Console.WriteLine($"===== Ayuda: CF Football Bypass INTELIGENTE v{version} =====");
			Console.WriteLine();
			Console.WriteLine("Funcionalidad:");
			Console.WriteLine("  Este programa ayuda a mitigar los bloqueos de ISP (por culpa de La Liga cuando hay fútbol)");
			Console.WriteLine("  activando o desactivando automáticamente el proxy de Cloudflare (nube naranja) para los registros DNS.");
			Console.WriteLine();
			Console.WriteLine("Modos de Funcionamiento:");
			Console.WriteLine("  1. Modo Demonio (Bucle Continuo o Único):");
			Console.WriteLine("     - Por defecto (sin parámetros): Lee la configuración de 'appsettings.local.json' > 'appsettings.json' >");
			Console.WriteLine("       variables de entorno, y comprueba periódicamente el estado de las IPs bloqueadas.");
			Console.WriteLine("     - Con parámetro '-1' o '--once': Realiza el ciclo de comprobación una única vez y finaliza.");
			Console.WriteLine();
			Console.WriteLine("  2. Modo Ejecución Única (One-off):");
			Console.WriteLine("     - Se activa al pasar exactamente 6 parámetros en la línea de comandos.");
			Console.WriteLine("       Actualiza de forma inmediata el estado del proxy de un registro de Cloudflare.");
			Console.WriteLine();
			Console.WriteLine("Parámetros del modo Ejecución Única (One-off):");
			Console.WriteLine("  Uso: SiFutbolNoCF <dominio> <registro> <tipo> <activarProxyCloudflare> <apiToken> <zoneId>");
			Console.WriteLine();
			Console.WriteLine("  Significado de los parámetros:");
			Console.WriteLine("    1. dominio:          El dominio principal o raíz (ej. 'ejemplo.com').");
			Console.WriteLine("    2. registro:         El subdominio o registro a actualizar (ej. 'www' o '@' para la raíz).");
			Console.WriteLine("    3. tipo:             Tipo de registro DNS (ej. 'A' o 'CNAME').");
			Console.WriteLine("    4. activateCfProxy:  Establece el estado para el proxy de Cloudflare. Valores permitidos:");
			Console.WriteLine("                         'true', '1', 'on', 'yes' (activar) / 'false', '0', 'off', 'no' (desactivar).");
			Console.WriteLine("    5. apiToken:         Token de la API de Cloudflare con permisos de edición DNS.");
			Console.WriteLine("    6. zoneId:           ID de zona de Cloudflare correspondiente al dominio.");
			Console.WriteLine();
			Console.WriteLine("Ejemplo de uso único (one-off):");
			Console.WriteLine("  SiFutbolNoCF miweb.com @ A false mi-token-secreto-123 xyz789zoneid");
			Console.WriteLine("===============================================================");
		}

		/// <summary>
		/// Ejecuta una conmutación directa e inmediata de proxy en Cloudflare a partir de 6 argumentos CLI.
		/// </summary>
		/// <param name="args">Argumentos: [0]dominio, [1]registro, [2]tipo, [3]activateCfProxy, [4]apiToken, [5]zoneId.</param>
		static async Task RunOneOff(string[] args)
		{
			// Mapear los argumentos posicionales recibidos por línea de comandos
			string domain = args[0];
			string record = args[1];
			string type = args[2];
			bool activateCfProxy = ParseBoolean(args[3]);
			string apiToken = args[4];
			string zoneId = args[5];

			// Construir el nombre calificado del host a conmutar
			string fullname = (string.IsNullOrEmpty(record) || record == "@") ? domain : $"{record}.{domain}";
			Console.WriteLine($"   ├─ 👀 {fullname} (tipo: {type})");

			try
			{
				// 1. Consultar el estado actual del registro en Cloudflare
				var currentRecord = await CloudflareService.FetchDnsRecordAsync(domain, record, type, apiToken, zoneId);
				if (currentRecord == null)
				{
					throw new Exception("Registro no encontrado en Cloudflare.");
				}

				// 2. Determinar emojis e indicadores de texto para el estado del proxy
				string proxyEmoji = activateCfProxy ? "🔒 ON" : "🔓 OFF";
				string currentProxyEmoji = currentRecord.proxied ? "🔒 ON" : "🔓 OFF";

				// 3. Aplicar la actualización en Cloudflare mediante el servicio
				bool updated = await CloudflareService.ApplyDnsRecordUpdateAsync(domain, record, type, currentRecord, activateCfProxy, apiToken, zoneId);

				// 4. Mostrar el resultado por consola
				if (updated)
				{
					Console.WriteLine($"   ├─── ✅ Actualizado │ {currentProxyEmoji} → {proxyEmoji} (IP origen: {currentRecord.content})");
				}
				else
				{
					Console.WriteLine($"   ├─── ℹ️ Sin cambios │ Ya está {proxyEmoji} (IP origen: {currentRecord.content})");
				}
			}
			catch (Exception ex)
			{
				// Mostrar el error en formato de sub-rama y finalizar con código de error
				Console.WriteLine($"   ├─── ❌ Error: {ex.Message}");
				Environment.Exit(1);
			}

			// Finalizar satisfactoriamente
			Environment.Exit(0);
		}

		/// <summary>
		/// Bucle principal de monitorización periódica de dominios y conmutación inteligente de proxies.
		/// </summary>
		/// <param name="runOnce">Indica si debe ejecutarse un único ciclo (true) o entrar en bucle continuo (false).</param>
		static async Task RunDaemon(bool runOnce = false)
		{
			// Obtener la versión de la aplicación para la cabecera
			var assembly = typeof(Program).Assembly;
			var version = assembly.GetName().Version?.ToString() ?? "1.0.0.0";

			Console.WriteLine($"===== CF Football Bypass INTELIGENTE v{version} =====");
			Console.WriteLine("===============================================================");

			// 1. Cargar la configuración resuelta desde JSON o variables de entorno
			AppSettings config = null;
			try
			{
				config = ConfigurationManager.LoadConfiguration();
			}
			catch (Exception ex)
			{
				LogMessage("❌", "ERROR", $"Error al cargar la configuración: {ex.Message}");
				Environment.Exit(1);
			}

			// Validar que la configuración no sea nula
			if (config == null)
			{
				LogMessage("❌", "ERROR", "No se pudo cargar la configuración.");
				Environment.Exit(1);
			}

			// Extraer los parámetros de configuración necesarios
			string cfApiToken = config.CfApiToken;
			string statusUrl = config.StatusUrl;
			int intervalSeconds = config.IntervalSeconds;
			var domains = config.Domains;

			// Validar la presencia obligatoria del token de API de Cloudflare
			if (string.IsNullOrEmpty(cfApiToken))
			{
				LogMessage("❌", "ERROR", "CfApiToken debe estar configurado.");
				Environment.Exit(1);
			}

			// Validar que exista al menos un dominio configurado para monitorizar
			if (domains == null || domains.Count == 0)
			{
				LogMessage("❌", "ERROR", "No se encontraron dominios válidos configurados.");
				Environment.Exit(1);
			}

			// 2. Auto-detectar los Zone IDs de Cloudflare si el usuario los dejó vacíos
			foreach (var dom in domains)
			{
				if (string.IsNullOrEmpty(dom.CfZoneId))
				{
					string record = string.IsNullOrEmpty(dom.record) ? "@" : dom.record;
					string fullname = (record == "@") ? dom.name : $"{record}.{dom.name}";

					LogMessage("🔍", "CONFIG", $"Auto-detectando ID de zona para {dom.name}...");
					try
					{
						// Consultar la API de Cloudflare para resolver el ID de la zona
						string resolvedZoneId = await CloudflareService.FetchZoneIdAsync(dom.name, cfApiToken);
						dom.CfZoneId = resolvedZoneId;
						LogMessage("✅", "CONFIG", $"ID de zona detectado para {dom.name}: {resolvedZoneId}");
					}
					catch (Exception ex)
					{
						// Si falla la auto-detección, notificar y finalizar con error
						LogMessage("❌", "ERROR", $"El dominio {fullname} no tiene un ID de zona (CfZoneId) y falló la auto-detección: {ex.Message}");
						Environment.Exit(1);
					}
				}
			}

			// Determinar si la verbosidad configurada es completa (Full) o filtrada por cambios (ChangesOnly)
			bool isFullVerbosity = string.Equals(config.Verbosity, "Full", StringComparison.OrdinalIgnoreCase);

			// Bandera para identificar el primer ciclo de comprobación
			bool isFirstRun = true;

			// 3. Iniciar el bucle de comprobación y sincronización
			while (true)
			{
				// Indicar si se deben mostrar todos los detalles del ciclo (primer ciclo o modo Full)
				bool showCycleDetails = isFirstRun || isFullVerbosity;

				// Contador de cambios aplicados en este ciclo
				int cycleChangesCount = 0;

				if (showCycleDetails)
				{
					Console.WriteLine();
					LogTimestamp("Descargando estado de IPs bloqueadas...");
				}
				else
				{
					LogTimestamp("Comprobando estado de IPs bloqueadas...");
				}

				// Descargar el archivo data.json una única vez por iteración para optimizar ancho de banda
				HashSet<string> blockedIps;
				try
				{
					blockedIps = await FootballStatusService.FetchBlockedIpsAsync(statusUrl);
					if (showCycleDetails)
					{
						LogMessage("ℹ️", "ESTADO", $"Total de IPs bloqueadas activamente: {blockedIps.Count}");
					}
				}
				catch (Exception ex)
				{
					LogMessage("⚠️", "ESTADO", $"Error al consultar IPs bloqueadas: {ex.Message}");
					blockedIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				}

				// Iterar sobre cada uno de los dominios configurados
				foreach (var dom in domains)
				{
					// Normalizar nombre de registro y tipo de registro
					string record = string.IsNullOrEmpty(dom.record) ? "@" : dom.record;
					string type = string.IsNullOrEmpty(dom.type) ? "A" : dom.type;
					string fullname = (record == "@") ? dom.name : $"{record}.{dom.name}";

					// Controlar si ya se imprimió la cabecera del dominio en este ciclo
					bool headerPrinted = false;

					// Función local para imprimir la cabecera del dominio si aún no se ha mostrado
					void EnsureDomainHeader()
					{
						if (!headerPrinted)
						{
							Console.WriteLine();
							Console.WriteLine($"   ├─ 👀 {fullname} (tipo: {type})");
							headerPrinted = true;
						}
					}

					// Si corresponde mostrar detalles completos, imprimir cabecera directamente
					if (showCycleDetails)
					{
						EnsureDomainHeader();
					}

					// Consultar el registro actual en Cloudflare para conocer su estado
					DnsRecord currentRecord = null;
					try
					{
						currentRecord = await CloudflareService.FetchDnsRecordAsync(dom.name, record, type, cfApiToken, dom.CfZoneId);
					}
					catch (Exception ex)
					{
						EnsureDomainHeader();
						Console.WriteLine($"   ├─── ❌ Error al consultar Cloudflare para {fullname}: {ex.Message}");
						continue;
					}

					// Si el registro no existe en la zona, notificar y saltar al siguiente dominio
					if (currentRecord == null)
					{
						EnsureDomainHeader();
						Console.WriteLine($"   ├─── ⚠️ Registro {fullname} no encontrado en Cloudflare, se omitirá.");
						continue;
					}

					// Obtener el estado actual del proxy (nube naranja o gris)
					bool currentProxied = currentRecord.proxied;
					bool desiredProxy = true;
					string statusLine = string.Empty;

					if (currentProxied)
					{
						// ESCENARIO 1: El proxy está activo en Cloudflare.
						// El DNS público resuelve a las direcciones IP de Cloudflare.
						var resolvedIps = await DnsResolverService.ResolveHostIpsAsync(fullname);
						if (resolvedIps.Count > 0)
						{
							// Guardar las IPs de Cloudflare detectadas en la caché persistente para recordarlas si se desactiva el proxy
							IpCacheService.SetIps(fullname, resolvedIps);

							// Comprobar si alguna de las IPs de Cloudflare resueltas está en la lista de bloqueadas
							bool isBlocked = resolvedIps.Any(ip => blockedIps.Contains(ip));
							if (isBlocked)
							{
								// Hay bloqueo activo: se debe desactivar el proxy para exponer la IP de origen
								desiredProxy = false;
								statusLine = $"🔴 Estado: BLOQUEADO (IPs CF: {string.Join(", ", resolvedIps)}). Estado proxy deseado: DESACTIVAR.";
							}
							else
							{
								// No hay bloqueo: mantener el proxy activado
								desiredProxy = true;
								statusLine = $"✅ Estado: no bloqueado (IPs CF: {string.Join(", ", resolvedIps)}). Estado proxy deseado: ACTIVAR.";
							}
						}
						else
						{
							// Si falló la resolución DNS, conservar el estado actual para evitar cambios involuntarios
							statusLine = $"⚠️ No se pudieron resolver IPs por DNS para {fullname}, se mantendrá el estado actual.";
							desiredProxy = currentProxied;
						}
					}
					else
					{
						// ESCENARIO 2: El proxy está desactivado (nube gris).
						// El DNS público resolvería a la IP de origen, por lo que consultamos las IPs de Cloudflare recordadas en la caché.
						var cachedIps = IpCacheService.GetIps(fullname);
						if (cachedIps != null && cachedIps.Count > 0)
						{
							// Comprobar si las IPs de Cloudflare que le corresponden a este dominio siguen bloqueadas
							bool isBlocked = cachedIps.Any(ip => blockedIps.Contains(ip));
							if (isBlocked)
							{
								// El partido sigue y las IPs continúan bloqueadas: mantener proxy desactivado
								desiredProxy = false;
								statusLine = $"🔴 Estado: BLOQUEO ACTIVO en Cloudflare (IPs CF: {string.Join(", ", cachedIps)}). Estado proxy deseado: DESACTIVAR.";
							}
							else
							{
								// El partido finalizó y las IPs están libres: reactivar el proxy de Cloudflare
								desiredProxy = true;
								statusLine = $"✅ Estado: Cloudflare libre de bloqueos (IPs CF: {string.Join(", ", cachedIps)}). Estado proxy deseado: ACTIVAR.";
							}
						}
						else
						{
							// Si es un arranque en frío sin historial en caché, resolver el dominio por DNS
							var resolvedIps = await DnsResolverService.ResolveHostIpsAsync(fullname);
							bool isBlocked = resolvedIps.Count > 0 && resolvedIps.Any(ip => blockedIps.Contains(ip));
							if (isBlocked)
							{
								desiredProxy = false;
								statusLine = $"🔴 Estado: BLOQUEADO (IPs: {string.Join(", ", resolvedIps)}). Estado proxy deseado: DESACTIVAR.";
							}
							else
							{
								desiredProxy = true;
								statusLine = $"✅ Estado: no bloqueado. Estado proxy deseado: ACTIVAR.";
							}
						}
					}

					// Si corresponde mostrar detalles completos, imprimir la línea de estado calculada
					if (showCycleDetails && !string.IsNullOrEmpty(statusLine))
					{
						Console.WriteLine($"   ├─── {statusLine}");
					}

					// Aplicar la actualización en Cloudflare si el estado deseado difiere del actual
					try
					{
						string proxyEmoji = desiredProxy ? "🔒 ON" : "🔓 OFF";
						string currentProxyEmoji = currentRecord.proxied ? "🔒 ON" : "🔓 OFF";

						bool updated = await CloudflareService.ApplyDnsRecordUpdateAsync(dom.name, record, type, currentRecord, desiredProxy, cfApiToken, dom.CfZoneId);

						if (updated)
						{
							// Incrementar el contador de cambios realizados en este ciclo
							cycleChangesCount++;

							// Asegurar que la cabecera y el motivo se muestren en modo filtrado
							EnsureDomainHeader();
							if (!showCycleDetails && !string.IsNullOrEmpty(statusLine))
							{
								Console.WriteLine($"   ├─── {statusLine}");
							}

							Console.WriteLine($"   ├─── ✅ Actualizado │ {currentProxyEmoji} → {proxyEmoji} (IP origen: {currentRecord.content})");
						}
						else
						{
							if (showCycleDetails)
							{
								Console.WriteLine($"   ├─── ℹ️ Sin cambios │ Ya está {proxyEmoji} (IP origen: {currentRecord.content})");
							}
						}
					}
					catch (Exception ex)
					{
						EnsureDomainHeader();
						Console.WriteLine($"   ├─── ❌ Error al actualizar Cloudflare para {fullname}: {ex.Message}");
					}
				}

				// Notificar finalización del ciclo si se muestran detalles o si hubo cambios aplicados
				if (showCycleDetails || cycleChangesCount > 0)
				{
					LogMessage("✅", "Ciclo completado");
				}

				// Si se ejecutó en modo de ciclo único (-1 o --once), salir del bucle
				if (runOnce)
				{
					break;
				}

				// Esperar el intervalo configurado antes de comenzar la siguiente iteración
				if (showCycleDetails)
				{
					LogMessage("⏳", $"Esperando {intervalSeconds} segundos antes de volver a comprobar...");
				}

				// Marcar que el primer ciclo ha concluido
				isFirstRun = false;

				await Task.Delay(intervalSeconds * 1000);
			}
		}

		#region Utilidades de consola
		/// <summary>
		/// Registra un mensaje formateado en la consola con marca de tiempo, emoji, nivel de log opcional y mensaje.
		/// </summary>
		/// <param name="emoji">Emoji descriptivo del estado o acción.</param>
		/// <param name="level">Nivel de registro o etiqueta del mensaje.</param>
		/// <param name="message">Texto opcional con el detalle del mensaje.</param>
		static void LogMessage(string emoji, string level, string message = "")
		{
			// Obtener la fecha y hora local actual formateada
			string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

			// Imprimir el mensaje estructurado
			if (string.IsNullOrEmpty(message))
			{
				Console.WriteLine($"[{timestamp}] {emoji} {level}");
			}
			else
			{
				Console.WriteLine($"[{timestamp}] {emoji} {level} │ {message}");
			}
		}

		/// <summary>
		/// Registra una línea con marca de tiempo y mensaje simple en la consola.
		/// </summary>
		/// <param name="message">Mensaje a mostrar.</param>
		static void LogTimestamp(string message)
		{
			// Obtener la fecha y hora local actual formateada
			string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
			Console.WriteLine($"[{timestamp}] {message}");
		}

		/// <summary>
		/// Evalúa un texto y determina si representa un valor booleano verdadero (true, 1, on, yes).
		/// </summary>
		/// <param name="value">Valor de texto a analizar.</param>
		/// <returns>Verdadero si el texto equivale a true; de lo contrario, falso.</returns>
		static bool ParseBoolean(string value)
		{
			// Si la cadena es nula o vacía, retornar falso
			if (string.IsNullOrEmpty(value)) return false;

			// Normalizar a minúsculas y sin espacios
			value = value.Trim().ToLowerInvariant();

			// Evaluar equivalencias comunes de valores afirmativos
			return value == "true" || value == "1" || value == "on" || value == "yes";
		}
		#endregion
	}
}
