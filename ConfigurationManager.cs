using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ManageDns
{
/// <summary>
	/// Gestiona la carga y resolución de la configuración del sistema combinando JSON base, JSON local, variables de entorno y User Secrets.
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
		/// Carga la configuración final aplicando la precedencia: Local JSON > Base JSON > Env Var > User Secrets.
		/// </summary>
		/// <returns>Una instancia de <see cref="AppSettings"/> con los valores resueltos.</returns>
		public static AppSettings LoadConfiguration()
		{
			string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
			
			// 1. Cargar appsettings.json (configuración base)
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

			// 2. Cargar appsettings.local.json (configuración local de pruebas)
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

			// 3. Cargar secretos de usuario (User Secrets)
			Dictionary<string, string> userSecrets = LoadUserSecrets();

			// Resolver valores aplicando la precedencia: Local JSON > Base JSON > Env Var > User Secrets
			var finalConfig = new AppSettings();

			// CfApiToken
			finalConfig.CfApiToken = ResolveStringSetting(
				localConfig?.CfApiToken,
				baseConfig?.CfApiToken,
				"CF_API_TOKEN",
				userSecrets.GetValueOrDefault("CfApiToken"),
				TokenPlaceholder
			);


			// StatusUrl
			finalConfig.StatusUrl = ResolveStringSetting(
				localConfig?.StatusUrl,
				baseConfig?.StatusUrl,
				"STATUS_URL",
				userSecrets.GetValueOrDefault("StatusUrl"),
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
				userSecrets.GetValueOrDefault("IntervalSeconds"),
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

			return finalConfig;
		}

		/// <summary>
		/// Resuelve un parámetro de configuración de texto buscando secuencialmente en el valor local, base, variables de entorno y User Secrets.
		/// </summary>
		/// <param name="localVal">Valor definido en el archivo de configuración local.</param>
		/// <param name="baseVal">Valor definido en el archivo de configuración base.</param>
		/// <param name="envVarName">Nombre de la variable de entorno correspondiente.</param>
		/// <param name="secretVal">Valor almacenado en los User Secrets del usuario.</param>
		/// <param name="placeholder">Texto temporal por defecto que debe ser ignorado.</param>
		/// <returns>El valor resuelto de mayor precedencia, o null si no se define ninguno válido.</returns>
		private static string ResolveStringSetting(
			string localVal,
			string baseVal,
			string envVarName,
			string secretVal,
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

			// 4. Secretos de usuario (User Secrets)
			if (!string.IsNullOrEmpty(secretVal))
			{
				return secretVal;
			}

			return null;
		}

		/// <summary>
		/// Carga los User Secrets del proyecto localizando su identificador (UserSecretsId) en el archivo .csproj.
		/// </summary>
		/// <returns>Un diccionario con los secretos cargados, con claves insensibles a mayúsculas/minúsculas.</returns>
		private static Dictionary<string, string> LoadUserSecrets()
		{
			var secrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			try
			{
				string csprojPath = FindCsprojPath();
				if (csprojPath != null)
				{
					string csprojContent = File.ReadAllText(csprojPath);
					var match = Regex.Match(csprojContent, @"<UserSecretsId>(.*?)</UserSecretsId>");
					if (match.Success)
					{
						string userSecretsId = match.Groups[1].Value.Trim();
						string secretsPath = GetSecretsFilePath(userSecretsId);
						if (secretsPath != null && File.Exists(secretsPath))
						{
							string secretsJson = File.ReadAllText(secretsPath);
							using (JsonDocument doc = JsonDocument.Parse(secretsJson))
							{
								foreach (var prop in doc.RootElement.EnumerateObject())
								{
									secrets[prop.Name] = prop.Value.ToString();
								}
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[Config] Advertencia al leer User Secrets: {ex.Message}");
			}
			return secrets;
		}

		/// <summary>
		/// Busca de forma ascendente en la jerarquía de directorios el primer archivo con extensión .csproj.
		/// </summary>
		/// <returns>La ruta completa del archivo .csproj, o null si no se encuentra.</returns>
		private static string FindCsprojPath()
		{
			string currentDir = AppDomain.CurrentDomain.BaseDirectory;
			var dirInfo = new DirectoryInfo(currentDir);
			while (dirInfo != null)
			{
				var files = dirInfo.GetFiles("*.csproj");
				if (files.Length > 0)
				{
					return files[0].FullName;
				}
				dirInfo = dirInfo.Parent;
			}
			return null;
		}

		/// <summary>
		/// Obtiene la ruta física del archivo secrets.json de Microsoft según la plataforma (Windows o Unix/macOS).
		/// </summary>
		/// <param name="secretsId">Identificador único de los secretos de usuario del proyecto.</param>
		/// <returns>Ruta absoluta al archivo secrets.json, o null si el identificador está vacío.</returns>
		private static string GetSecretsFilePath(string secretsId)
		{
			if (string.IsNullOrEmpty(secretsId)) return null;

			if (OperatingSystem.IsWindows())
			{
				string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
				return Path.Combine(appData, "Microsoft", "UserSecrets", secretsId, "secrets.json");
			}
			else
			{
				string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
				return Path.Combine(userProfile, ".microsoft", "usersecrets", secretsId, "secrets.json");
			}
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
