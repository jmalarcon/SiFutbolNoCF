using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ManageDns
{
	/// <summary>
	/// Gestiona la carga y resolución de la configuración del sistema combinando JSON base, JSON local y variables de entorno.
	/// </summary>
	public class ConfigurationManager
	{
		private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true
		};

		private const string TokenPlaceholder = "TU_CLOUDFLARE_API_TOKEN";
		private const string ZonePlaceholder = "TU_CLOUDFLARE_ZONE_ID";

		/// <summary>
		/// Carga la configuración final aplicando la precedencia: Local JSON > Base JSON > Env Var.
		/// </summary>
		/// <returns>Una instancia de <see cref="AppSettings"/> con los valores resueltos.</returns>
		public static AppSettings LoadConfiguration()
		{
			string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

			// 1. Cargar appsettings.local.json (si existe en BaseDirectory, ej. copiado únicamente en compilación Debug)
			string localConfigPath = Path.Combine(baseDirectory, "appsettings.local.json");
			AppSettings localConfig = null;
			if (File.Exists(localConfigPath))
			{
				try
				{
					string json = File.ReadAllText(localConfigPath);
					localConfig = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[Config] Advertencia al leer appsettings.local.json: {ex.Message}");
				}
			}

			// 2. Cargar appsettings.json (configuración base)
			string baseConfigPath = Path.Combine(baseDirectory, "appsettings.json");
			AppSettings baseConfig = null;
			if (File.Exists(baseConfigPath))
			{
				try
				{
					string json = File.ReadAllText(baseConfigPath);
					baseConfig = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[Config] Advertencia al leer appsettings.json: {ex.Message}");
				}
			}

			// Resolver valores aplicando la precedencia: Local JSON > Base JSON > Env Var
			var finalConfig = new AppSettings();

			// CfApiToken
			finalConfig.CfApiToken = ResolveStringSetting(
				localConfig?.CfApiToken,
				baseConfig?.CfApiToken,
				"CF_API_TOKEN",
				TokenPlaceholder
			);


			// StatusUrl
			finalConfig.StatusUrl = ResolveStringSetting(
				localConfig?.StatusUrl,
				baseConfig?.StatusUrl,
				"STATUS_URL",
				""
			);
			if (string.IsNullOrEmpty(finalConfig.StatusUrl))
			{
				finalConfig.StatusUrl = "https://hayahora.futbol/status.json";
			}

			// IntervalSeconds
			string intervalLocal = localConfig?.IntervalSeconds > 0 ? localConfig.IntervalSeconds.ToString() : null;
			string intervalBase = baseConfig?.IntervalSeconds > 0 ? baseConfig.IntervalSeconds.ToString() : null;
			string intervalStr = ResolveStringSetting(
				intervalLocal,
				intervalBase,
				"INTERVAL_SECONDS",
				""
			);
			if (int.TryParse(intervalStr, out int parsedInterval) && parsedInterval > 0)
			{
				finalConfig.IntervalSeconds = parsedInterval;
			}
			else
			{
				finalConfig.IntervalSeconds = 300; // Predeterminado
			}

			// Dominios (como es una lista, toma local si existe y no está vacía, de lo contrario base)
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
				finalConfig.Domains = new List<DomainConfig>();
			}

			// Limpiar placeholders en dominios (ej. TU_CLOUDFLARE_ZONE_ID) para permitir auto-detección
			foreach (var dom in finalConfig.Domains)
			{
				if (!string.IsNullOrEmpty(dom.CfZoneId) && dom.CfZoneId.StartsWith(ZonePlaceholder, StringComparison.OrdinalIgnoreCase))
				{
					dom.CfZoneId = null;
				}
			}

			return finalConfig;
		}

		/// <summary>
		/// Resuelve un parámetro de configuración de texto buscando secuencialmente en el valor local, base y variables de entorno.
		/// </summary>
		/// <param name="localVal">Valor definido en el archivo de configuración local.</param>
		/// <param name="baseVal">Valor definido en el archivo de configuración base.</param>
		/// <param name="envVarName">Nombre de la variable de entorno correspondiente.</param>
		/// <param name="placeholder">Texto temporal por defecto que debe ser ignorado.</param>
		/// <returns>El valor resuelto de mayor precedencia, o null si no se define ninguno válido.</returns>
		private static string ResolveStringSetting(
			string localVal,
			string baseVal,
			string envVarName,
			string placeholder)
		{
			// 1. appsettings.local.json
			if (!string.IsNullOrEmpty(localVal) && localVal != placeholder)
			{
				return localVal;
			}

			// 2. appsettings.json
			if (!string.IsNullOrEmpty(baseVal) && baseVal != placeholder)
			{
				return baseVal;
			}

			// 3. Variables de entorno (busca nombre estándar, mayúsculas, y eliminando guiones bajos)
			string envVal = Environment.GetEnvironmentVariable(envVarName)
							?? Environment.GetEnvironmentVariable(envVarName.Replace("_", ""))
							?? Environment.GetEnvironmentVariable(envVarName.ToLowerInvariant());
			if (!string.IsNullOrEmpty(envVal))
			{
				return envVal;
			}

			return null;
		}
	}

	/// <summary>
	/// Representa las opciones de configuración global cargadas para la aplicación.
	/// </summary>
	public class AppSettings
	{
		/// <summary>Token de autorización de la API de Cloudflare.</summary>
		public string CfApiToken { get; set; }


		/// <summary>Segundos de espera en el modo continuo antes de realizar el siguiente escaneo.</summary>
		public int IntervalSeconds { get; set; }

		/// <summary>URL del servicio REST que devuelve el estado de bloqueo del dominio.</summary>
		public string StatusUrl { get; set; }

		/// <summary>Lista de dominios y subdominios configurados para monitoreo y conmutación de proxy.</summary>
		public List<DomainConfig> Domains { get; set; }
	}

	/// <summary>
	/// Representa la configuración específica de un subdominio o registro DNS que se va a monitorizar.
	/// </summary>
	public class DomainConfig
	{
		/// <summary>Nombre del dominio raíz (ej. dominio.com).</summary>
		public string name { get; set; }

		/// <summary>Nombre del registro o subdominio (ej. www, api o @ para el raíz).</summary>
		public string record { get; set; }

		/// <summary>Tipo de registro DNS (ej. A, AAAA, CNAME).</summary>
		public string type { get; set; }

		/// <summary>Identificador de zona de Cloudflare asociado a este dominio específico.</summary>
		public string CfZoneId { get; set; }
	}
}
