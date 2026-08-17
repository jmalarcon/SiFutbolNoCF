using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ManageDns.Models;

namespace ManageDns.Services
{
	/// <summary>
	/// Gestiona la carga y resolución jerárquica de la configuración del sistema.
	/// </summary>
	/// <remarks>
	/// Aplica un orden de precedencia estricto: appsettings.local.json > appsettings.json > Variables de Entorno.
	/// Está completamente desacoplado de la interfaz de usuario / consola.
	/// </remarks>
	public static class ConfigurationManager
	{
		// Opciones JSON para deserializar la configuración ignorando mayúsculas y minúsculas
		private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true
		};

		// Textos comodín por defecto en plantillas que deben ser descartados si no se han editado
		private const string TokenPlaceholder = "TU_CLOUDFLARE_API_TOKEN";
		private const string ZonePlaceholder = "TU_CLOUDFLARE_ZONE_ID";

		/// <summary>
		/// Carga la configuración combinada aplicando la precedencia establecida y limpiando placeholders temporales.
		/// </summary>
		/// <returns>Instancia resuelta de <see cref="AppSettings"/> con todos los parámetros validados.</returns>
		public static AppSettings LoadConfiguration()
		{
			// Obtener el directorio base donde reside el ejecutable de la aplicación
			string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

			// 1. Intentar cargar appsettings.local.json (fichero local de desarrollo para no exponer secretos)
			string localConfigPath = Path.Combine(baseDirectory, "appsettings.local.json");
			AppSettings localConfig = null;
			if (File.Exists(localConfigPath))
			{
				try
				{
					// Leer el texto del JSON local y deserializarlo a modelo
					string json = File.ReadAllText(localConfigPath);
					localConfig = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
				}
				catch
				{
					// Ignorar fallo de deserialización local para continuar con el archivo base
				}
			}

			// 2. Intentar cargar appsettings.json (fichero de configuración base distribuible)
			string baseConfigPath = Path.Combine(baseDirectory, "appsettings.json");
			AppSettings baseConfig = null;
			if (File.Exists(baseConfigPath))
			{
				try
				{
					// Leer el texto del JSON base y deserializarlo a modelo
					string json = File.ReadAllText(baseConfigPath);
					baseConfig = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
				}
				catch
				{
					// Ignorar fallo de deserialización base para continuar con variables de entorno
				}
			}

			// 3. Crear el objeto final consolidado y resolver cada parámetro según orden de precedencia
			var finalConfig = new AppSettings();

			// Resolver el Token de la API de Cloudflare (Local > Base > CF_API_TOKEN)
			finalConfig.CfApiToken = ResolveStringSetting(
				localConfig?.CfApiToken,
				baseConfig?.CfApiToken,
				"CF_API_TOKEN",
				TokenPlaceholder
			);

			// Resolver la URL de estado de fútbol (Local > Base > STATUS_URL)
			finalConfig.StatusUrl = ResolveStringSetting(
				localConfig?.StatusUrl,
				baseConfig?.StatusUrl,
				"STATUS_URL",
				""
			);

			// Si no se proporcionó ninguna URL, establecer la ruta oficial por defecto
			if (string.IsNullOrEmpty(finalConfig.StatusUrl))
			{
				finalConfig.StatusUrl = "https://hayahora.futbol/estado/data.json";
			}

			// Resolver el intervalo en segundos entre comprobaciones (Local > Base > INTERVAL_SECONDS)
			string intervalLocal = localConfig?.IntervalSeconds > 0 ? localConfig.IntervalSeconds.ToString() : null;
			string intervalBase = baseConfig?.IntervalSeconds > 0 ? baseConfig.IntervalSeconds.ToString() : null;
			string intervalStr = ResolveStringSetting(
				intervalLocal,
				intervalBase,
				"INTERVAL_SECONDS",
				""
			);

			// Validar que el intervalo resuelto sea un número entero positivo mayor que cero
			if (int.TryParse(intervalStr, out int parsedInterval) && parsedInterval > 0)
			{
				finalConfig.IntervalSeconds = parsedInterval;
			}
			else
			{
				// Establecer 300 segundos (5 minutos) como valor por defecto estándar
				finalConfig.IntervalSeconds = 300;
			}

			// Resolver el modo de intervalo adaptativo (Local > Base > ADAPTIVE_INTERVAL)
			string adaptiveLocal = localConfig?.AdaptiveInterval.HasValue == true ? localConfig.AdaptiveInterval.Value.ToString() : null;
			string adaptiveBase = baseConfig?.AdaptiveInterval.HasValue == true ? baseConfig.AdaptiveInterval.Value.ToString() : null;
			string adaptiveStr = ResolveStringSetting(
				adaptiveLocal,
				adaptiveBase,
				"ADAPTIVE_INTERVAL",
				""
			);

			// El valor por defecto es true si no se especifica explícitamente lo contrario (false, 0, no, off)
			if (string.IsNullOrEmpty(adaptiveStr))
			{
				finalConfig.AdaptiveInterval = true;
			}
			else
			{
				// Evaluar si el usuario desactivó explícitamente los intervalos adaptativos
				finalConfig.AdaptiveInterval = !adaptiveStr.Trim().Equals("false", StringComparison.OrdinalIgnoreCase)
				                               && !adaptiveStr.Trim().Equals("0", StringComparison.OrdinalIgnoreCase)
				                               && !adaptiveStr.Trim().Equals("no", StringComparison.OrdinalIgnoreCase)
				                               && !adaptiveStr.Trim().Equals("off", StringComparison.OrdinalIgnoreCase);
			}

			// Resolver el nivel de verbosidad de logs (Local > Base > VERBOSITY)
			string verbosityStr = ResolveStringSetting(
				localConfig?.Verbosity,
				baseConfig?.Verbosity,
				"VERBOSITY",
				""
			);

			// Establecer 'Full' si se solicita explícitamente; en cualquier otro caso usar 'ChangesOnly' por defecto
			if (!string.IsNullOrEmpty(verbosityStr) && verbosityStr.Trim().Equals("full", StringComparison.OrdinalIgnoreCase))
			{
				finalConfig.Verbosity = "Full";
			}
			else
			{
				finalConfig.Verbosity = "ChangesOnly";
			}

			// Resolver la lista de dominios: dar preferencia a los dominios definidos en el archivo local si existen
			if (localConfig?.Domains != null && localConfig.Domains.Count > 0)
			{
				finalConfig.Domains = localConfig.Domains;
			}
			else if (baseConfig?.Domains != null && baseConfig.Domains.Count > 0)
			{
				finalConfig.Domains = baseConfig.Domains;
			}
			else
			{
				// Si no hay dominios configurados en ninguna fuente, inicializar lista vacía
				finalConfig.Domains = new List<DomainConfig>();
			}

			// Limpiar los valores de placeholder para permitir que el programa auto-descubra los Zone IDs
			foreach (var dom in finalConfig.Domains)
			{
				if (!string.IsNullOrEmpty(dom.CfZoneId) && dom.CfZoneId.StartsWith(ZonePlaceholder, StringComparison.OrdinalIgnoreCase))
				{
					// Poner a null para que Program.cs active la búsqueda automática mediante la API
					dom.CfZoneId = null;
				}
			}

			// Devolver la configuración completa validada
			return finalConfig;
		}

		/// <summary>
		/// Resuelve un parámetro de texto buscando secuencialmente en el valor local, base y variables de entorno.
		/// </summary>
		/// <param name="localVal">Valor del archivo de configuración local.</param>
		/// <param name="baseVal">Valor del archivo de configuración base.</param>
		/// <param name="envVarName">Nombre de la variable de entorno a consultar.</param>
		/// <param name="placeholder">Texto por defecto que debe considerarse como no configurado.</param>
		/// <returns>El valor de mayor precedencia encontrado, o null si ninguno es válido.</returns>
		private static string ResolveStringSetting(
			string localVal,
			string baseVal,
			string envVarName,
			string placeholder)
		{
			// 1. Prioridad máxima: appsettings.local.json si contiene un valor distinto del placeholder
			if (!string.IsNullOrEmpty(localVal) && localVal != placeholder)
			{
				return localVal;
			}

			// 2. Segunda prioridad: appsettings.json si contiene un valor distinto del placeholder
			if (!string.IsNullOrEmpty(baseVal) && baseVal != placeholder)
			{
				return baseVal;
			}

			// 3. Tercera prioridad: Variables de entorno del sistema (buscando nombre estándar, sin guiones y en minúsculas)
			string envVal = Environment.GetEnvironmentVariable(envVarName)
							?? Environment.GetEnvironmentVariable(envVarName.Replace("_", ""))
							?? Environment.GetEnvironmentVariable(envVarName.ToLowerInvariant());

			// Si la variable de entorno existe y no está vacía, devolver su valor
			if (!string.IsNullOrEmpty(envVal))
			{
				return envVal;
			}

			// No se encontró ningún valor válido
			return null;
		}
	}
}
