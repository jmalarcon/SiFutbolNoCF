using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using SiFutbolNoCF.Models;
using SiFutbolNoCF.Services;
using SiFutbolNoCF.Services.Notifications;

namespace SiFutbolNoCF
{
	/// <summary>
	/// Clase principal y punto de entrada de la aplicación SiFutbolNoCF.
	/// </summary>
	/// <remarks>
	/// Responsable de procesar los argumentos de línea de comandos, controlar el ciclo de ejecución
	/// y gobernar la presentación visual, mensajes y emojis en la consola.
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

			// Comprobar si se solicita ejecutar un único ciclo con -1 o --once
			if (args.Length == 1 && (args[0] == "-1" || args[0] == "--once"))
			{
				await RunDaemon(runOnce: true);
			}
			// Comprobar si se proporcionan exactamente 6 argumentos para el modo directo (one-off)
			else if (args.Length == 6)
			{
				await RunOneOff(args);
			}
			// Por defecto, iniciar en modo demonio continuo con bucle periódico
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

			Console.WriteLine($"===== Ayuda: SiFutbolNoCF v{version} =====");
			Console.WriteLine();
			Console.WriteLine("Funcionalidad:");
			Console.WriteLine("  Este programa ayuda a mitigar los bloqueos de ISP (por culpa de La Liga cuando hay fútbol)");
			Console.WriteLine("  activando o desactivando automáticamente el proxy de Cloudflare (nube naranja) para los registros DNS.");
			Console.WriteLine("  Además envia notificaciones cuando cambia el estado de cualquier dominio en CloudFlare.");
			Console.WriteLine();
			Console.WriteLine(" Consulta todos los detalles y características en https://github.com/jmalarcon/SiFutbolNoCF/.");
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
			string domain = args[0];
			string record = args[1];
			string type = args[2];
			bool activateCfProxy = ParseBoolean(args[3]);
			string apiToken = args[4];
			string zoneId = args[5];

			string fullname = (string.IsNullOrEmpty(record) || record == "@") ? domain : $"{record}.{domain}";
			Console.WriteLine($"   ├─ 👀 {fullname} (tipo: {type})");

			// Inicializar el servicio de notificaciones opcional si existe configuración en el entorno
			NotificationService notificationService = null;
			try
			{
				var config = ConfigurationManager.LoadConfiguration();
				notificationService = new NotificationService(config?.Notifications);
			}
			catch
			{
				// Ignorar fallos de configuración de alertas en ejecución manual
			}

			// Delegar la ejecución manual en ProxySyncService
			var result = await ProxySyncService.ExecuteOneOffAsync(domain, record, type, activateCfProxy, apiToken, zoneId, notificationService);

			if (!result.Success)
			{
				Console.WriteLine($"   ├─── ❌ Error: {result.ErrorMessage}");
				Environment.Exit(1);
			}

			string proxyEmoji = activateCfProxy ? "🔒 ON" : "🔓 OFF";
			string prevProxyEmoji = result.PreviousProxied ? "🔒 ON" : "🔓 OFF";

			if (result.Updated)
			{
				Console.WriteLine($"   ├─── ✅ Actualizado │ {prevProxyEmoji} → {proxyEmoji} (IP origen: {result.OriginIp})");

				// Mostrar estado de las alertas enviadas
				foreach (var notif in result.NotificationResults)
				{
					if (notif.Success)
					{
						Console.WriteLine($"   ├─── 📱 Alerta enviada por {notif.ProviderName}");
					}
					else
					{
						Console.WriteLine($"   ├─── ⚠️ Error al enviar alerta por {notif.ProviderName}: {notif.ErrorMessage}");
					}
				}
			}
			else
			{
				Console.WriteLine($"   ├─── ℹ️ Sin cambios │ Ya está {proxyEmoji} (IP origen: {result.OriginIp})");
			}

			Environment.Exit(0);
		}

		/// <summary>
		/// Bucle principal de monitorización periódica de dominios y conmutación inteligente de proxies.
		/// </summary>
		/// <param name="runOnce">Indica si debe ejecutarse un único ciclo (true) o entrar en bucle continuo (false).</param>
		static async Task RunDaemon(bool runOnce = false)
		{
			var assembly = typeof(Program).Assembly;
			var version = assembly.GetName().Version?.ToString() ?? "1.0.0.0";

			Console.WriteLine($"===== SiFutbolNoCF v{version} =====");
			Console.WriteLine("===============================================================");

			// 1. Cargar y validar la configuración
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

			if (config == null)
			{
				LogMessage("❌", "ERROR", "No se pudo cargar la configuración.");
				Environment.Exit(1);
			}

			if (string.IsNullOrEmpty(config.CfApiToken))
			{
				LogMessage("❌", "ERROR", "CfApiToken debe estar configurado.");
				Environment.Exit(1);
			}

			if (config.Domains == null || config.Domains.Count == 0)
			{
				LogMessage("❌", "ERROR", "No se encontraron dominios válidos configurados.");
				Environment.Exit(1);
			}

			// 2. Auto-detectar los Zone IDs de Cloudflare si no están especificados
			var zoneDetections = await ProxySyncService.ResolveZoneIdsAsync(config.Domains, config.CfApiToken);
			foreach (var detection in zoneDetections)
			{
				LogMessage("🔍", "CONFIG", $"Auto-detectando ID de zona para {detection.DomainName}...");
				if (detection.Success)
				{
					LogMessage("✅", "CONFIG", $"ID de zona detectado para {detection.DomainName}: {detection.ZoneId}");
				}
				else
				{
					LogMessage("❌", "ERROR", $"El dominio {detection.Fullname} no tiene un ID de zona (CfZoneId) y falló la auto-detección: {detection.ErrorMessage}");
					Environment.Exit(1);
				}
			}

			// 3. Inicializar el servicio de notificaciones
			var notificationService = new NotificationService(config.Notifications);

			bool isAdaptive = config.AdaptiveInterval ?? true;
			bool isFullVerbosity = string.Equals(config.Verbosity, "Full", StringComparison.OrdinalIgnoreCase);
			bool isFirstRun = true;
			DateTime? blockStartTime = null;

			// 4. Iniciar el bucle de comprobación
			while (true)
			{
				bool showCycleDetails = isFirstRun || isFullVerbosity;

				if (showCycleDetails)
				{
					Console.WriteLine();
					LogTimestamp("Descargando estado de IPs bloqueadas...");
				}
				else
				{
					LogTimestamp("Comprobando estado de IPs bloqueadas...");
				}

				// Ejecutar el ciclo completo de sincronización mediante ProxySyncService
				var cycleResult = await ProxySyncService.ExecuteCycleAsync(config, notificationService);

				// Mostrar errores o estado de descarga de IPs
				if (!string.IsNullOrEmpty(cycleResult.BlockedIpsError))
				{
					LogMessage("⚠️", "ESTADO", $"Error al consultar IPs bloqueadas: {cycleResult.BlockedIpsError}");
				}
				else if (showCycleDetails)
				{
					LogMessage("ℹ️", "ESTADO", $"Total de IPs bloqueadas activamente: {cycleResult.BlockedIps.Count}");
				}

				// Renderizar resultados individuales de cada dominio
				foreach (var domResult in cycleResult.DomainResults)
				{
					RenderDomainResult(domResult, showCycleDetails);
				}

				// Renderizar resultados del envío de notificaciones
				if (cycleResult.Changes.Count > 0 && cycleResult.NotificationResults.Count > 0)
				{
					foreach (var notif in cycleResult.NotificationResults)
					{
						if (notif.Success)
						{
							string pluralSuffix = cycleResult.Changes.Count == 1 ? "dominio" : "dominios";
							Console.WriteLine($"   ├─── 📱 Alerta enviada por {notif.ProviderName} ({cycleResult.Changes.Count} {pluralSuffix})");
						}
						else
						{
							Console.WriteLine($"   ├─── ⚠️ Error al enviar alerta por {notif.ProviderName}: {notif.ErrorMessage}");
						}
					}
				}

				// Actualizar el registro temporal del inicio de bloqueo
				if (cycleResult.AnyDomainBlocked)
				{
					blockStartTime ??= DateTime.Now;
				}
				else
				{
					blockStartTime = null;
				}

				// Notificar fin de ciclo si corresponde
				if (showCycleDetails || cycleResult.Changes.Count > 0)
				{
					LogMessage("✅", "Ciclo completado");
				}

				if (runOnce)
				{
					break;
				}

				// Calcular tiempo de espera del siguiente ciclo
				var delay = ProxySyncService.CalculateNextDelay(isAdaptive, config.IntervalSeconds, cycleResult.AnyDomainBlocked, blockStartTime);

				if (showCycleDetails || delay.DelaySeconds > config.IntervalSeconds)
				{
					TimeSpan waitSpan = TimeSpan.FromSeconds(delay.DelaySeconds);
					string formattedTime = $"{(int)waitSpan.TotalHours:D2}:{waitSpan.Minutes:D2}:{waitSpan.Seconds:D2}";
					LogMessage("⏳", $"Esperando {formattedTime} ({delay.DelaySeconds}s) antes de volver a comprobar │ {delay.Reason}");
				}

				isFirstRun = false;
				await Task.Delay(delay.DelaySeconds * 1000);
			}
		}

		/// <summary>
		/// Renderiza en consola el resultado del procesamiento de un dominio respetando el formato jerárquico y emojis.
		/// </summary>
		/// <param name="domResult">Resultado del dominio a presentar.</param>
		/// <param name="showFullDetails">Indica si deben mostrarse todos los detalles o solo los cambios/errores.</param>
		static void RenderDomainResult(DomainSyncResult domResult, bool showFullDetails)
		{
			bool shouldPrint = showFullDetails || domResult.Status == DomainSyncStatus.Updated || domResult.Status == DomainSyncStatus.Error || domResult.Status == DomainSyncStatus.DnsRecordNotFound;

			if (!shouldPrint)
			{
				return;
			}

			Console.WriteLine();
			Console.WriteLine($"   ├─ 👀 {domResult.Fullname} (tipo: {domResult.RecordType})");

			if (domResult.Status == DomainSyncStatus.DnsRecordNotFound)
			{
				Console.WriteLine($"   ├─── ⚠️ {domResult.ErrorMessage}, se omitirá.");
				return;
			}

			if (domResult.Status == DomainSyncStatus.Error)
			{
				Console.WriteLine($"   ├─── ❌ {domResult.ErrorMessage}");
				return;
			}

			if (!string.IsNullOrEmpty(domResult.StatusLine))
			{
				Console.WriteLine($"   ├─── {domResult.StatusLine}");
			}

			string proxyEmoji = domResult.DesiredProxied ? "🔒 ON" : "🔓 OFF";
			string prevProxyEmoji = domResult.PreviousProxied ? "🔒 ON" : "🔓 OFF";

			if (domResult.Status == DomainSyncStatus.Updated)
			{
				Console.WriteLine($"   ├─── ✅ Actualizado │ {prevProxyEmoji} → {proxyEmoji} (IP origen: {domResult.OriginIp})");
			}
			else if (domResult.Status == DomainSyncStatus.NoChange && showFullDetails)
			{
				Console.WriteLine($"   ├─── ℹ️ Sin cambios │ Ya está {proxyEmoji} (IP origen: {domResult.OriginIp})");
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
			string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
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
			if (string.IsNullOrEmpty(value)) return false;
			value = value.Trim().ToLowerInvariant();
			return value == "true" || value == "1" || value == "on" || value == "yes";
		}
		#endregion
	}
}
