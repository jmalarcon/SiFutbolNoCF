using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ManageDns
{
	/// <summary>
	/// Clase principal que contiene el punto de entrada y el flujo principal del programa (monitoreo y actualización de DNS).
	/// </summary>
	class Program
	{
		#region Campos y Propiedades
		// Opciones globales de serialización JSON case-insensitive para .NET 10
		private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true
		};

		// Cliente HttpClient único y estático, reutilizado de forma segura entre llamadas
		private static readonly HttpClient _httpClient = new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(10)
		};
		#endregion

		/// <summary>
		/// Punto de entrada del programa. Determina el modo de ejecución (One-off o Demonio) según los parámetros de entrada.
		/// </summary>
		/// <param name="args">Argumentos recibidos por línea de comandos.</param>
		static async Task Main(string[] args)
		{
			// Forzar codificación UTF-8 para visualización de emojis en Windows
			Console.OutputEncoding = Encoding.UTF8;

			//Muestra la ayuda con -? o --help
			if (args.Length == 1 && (args[0] == "-?" || args[0] == "--help"))
			{
				ShowHelp();
				return;
			}
			//Si se le pasa -1 o --once, se ejecuta una sola vez y luego termina
			if (args.Length == 1 && (args[0] == "-1" || args[0] == "--one"))
			{
				await RunDaemon(runOnce: true);
			}
			// Si se le pasan exactamente los 6 argumentos que necesita, funciona en modo de ejecución única (one-off)
			// Esto es útil para pruebas rápidas o para ejecutar el programa manualmente sin necesidad de un bucle infinito.
			else if (args.Length == 6)
			{
				await RunOneOff(args);
			}
			// Si va sin parámetros o con un número icorrecto de ellos, se ejecuta en modo "demonio" (bucle continuo inteligente)
			// leyendo appsettings.json, appsettings.local.json, variables de entorno o secretos de .NET para configurarse.
			else
			{
				await RunDaemon(runOnce: false);
			}
		}

		/// <summary>
		/// Muestra la ayuda del programa por consola detallando la funcionalidad, los dos modos de funcionamiento y los parámetros requeridos.
		/// </summary>
		static void ShowHelp()
		{
			var assembly = typeof(Program).Assembly;
			var version = assembly.GetName().Version?.ToString() ?? "1.0.0.0";

			Console.WriteLine($"===== Ayuda: CF Football Bypass INTELIGENTE v{version} =====");
			Console.WriteLine();
			Console.WriteLine("Funcionalidad:");
			Console.WriteLine("  Este programa ayuda a mitigar los bloqueos de ISP (por culpa de La Liga cuando hay o va a haber fútbol)");
			Console.WriteLine("  activando o desactivando automáticamente el proxy de Cloudflare (nube naranja) para los registros DNS.");
			Console.WriteLine();
			Console.WriteLine("Modos de Funcionamiento:");
			Console.WriteLine("  1. Modo Demonio (Bucle Continuo o Único):");
			Console.WriteLine("     - Por defecto (sin parámetros): Lee la configuración de 'appsettings.local.json' > 'appsettings.json' >");
			Console.WriteLine("       variables de entorno > secretos de .NET,  y comprueba periódicamente el estado de los dominios,");
			Console.WriteLine("       actualizando Cloudflare de forma continua.");
			Console.WriteLine("     - Con parámetro '-1' o '--one': Realiza el ciclo de comprobación y actualización");
			Console.WriteLine("       de 'appsettings.json' una única vez y finaliza.");
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
		/// Ejecuta una única operación de conmutación de DNS basándose estrictamente en los argumentos proporcionados por el usuario.
		/// </summary>
		/// <param name="args">Colección de parámetros recibidos (dominio, registro, tipo, activateCfProxy, apiToken, zoneId).</param>
		static async Task RunOneOff(string[] args)
		{
			string domain = args[0];
			string record = args[1];
			string type = args[2];
			bool activateCfProxy = ParseBoolean(args[3]);
			string apiToken = args[4];
			string zoneId = args[5];

			try
			{
				await UpdateDnsRecord(domain, record, type, activateCfProxy, apiToken, zoneId);
			}
			catch (Exception ex)
			{
				Console.Write("   ├─ ");
				WriteColored("❌ Error: ", ConsoleColor.Red);
				Console.WriteLine(ex.Message);
				Environment.Exit(1);
			}
			Environment.Exit(0);
		}

		/// <summary>
		/// Ejecuta el proceso de forma programada o única, comprobando el estado de bloqueo de los dominios y actualizándolos.
		/// </summary>
		/// <param name="runOnce">Si es verdadero, ejecuta el ciclo de comprobación de dominios una única vez y finaliza.</param>
		static async Task RunDaemon(bool runOnce = false)
		{
			// Leer metadatos del ensamblado (desde AssemblyInfo.cs)
			var assembly = typeof(Program).Assembly;
			var version = assembly.GetName().Version?.ToString() ?? "1.0.0.0";

			Console.WriteLine($"===== CF Football Bypass INTELIGENTE v{version} =====");
			Console.WriteLine("===============================================================");

			// Leer variables mediante ConfigurationManager
			AppSettings config = null;
			try
			{
				config = ConfigurationManager.LoadConfiguration();
			}
			catch (Exception ex)
			{
				LogMessage("❌", "ERROR", ConsoleColor.Red, $"Error al cargar la configuración: {ex.Message}");
				Environment.Exit(1);
			}

			if (config == null)
			{
				LogMessage("❌", "ERROR", ConsoleColor.Red, "No se pudo cargar la configuración.");
				Environment.Exit(1);
			}

			string cfApiToken = config.CfApiToken;
			string statusUrl = config.StatusUrl ?? "https://hayahora.futbol/status.json"; //Valor por defecto, pero se puede obtener de settings por si cambia
			int intervalSeconds = config.IntervalSeconds;
			var domains = config.Domains;

			if (string.IsNullOrEmpty(cfApiToken))
			{
				LogMessage("❌", "ERROR", ConsoleColor.Red, "CfApiToken debe estar configurado (vía appsettings.local.json, appsettings.json, variables de entorno o user-secrets).");
				Environment.Exit(1);
			}

			if (domains == null || domains.Count == 0)
			{
				LogMessage("❌", "ERROR", ConsoleColor.Red, "No se encontraron dominios válidos configurados.");
				Environment.Exit(1);
			}

			// Validar y auto-detectar IDs de zona si faltan en la configuración
			foreach (var dom in domains)
			{
				if (string.IsNullOrEmpty(dom.CfZoneId))
				{
					string record = string.IsNullOrEmpty(dom.record) ? "@" : dom.record;
					string fullname = (record == "@") ? dom.name : $"{record}.{dom.name}";

					LogMessage("🔍", "CONFIG", ConsoleColor.Cyan, $"Auto-detectando ID de zona para {dom.name}...");
					try
					{
						string resolvedZoneId = await FetchZoneId(dom.name, cfApiToken);
						dom.CfZoneId = resolvedZoneId;
						LogMessage("✅", "CONFIG", ConsoleColor.Green, $"ID de zona detectado para {dom.name}: {resolvedZoneId}");
					}
					catch (Exception ex)
					{
						LogMessage("❌", "ERROR", ConsoleColor.Red, $"El dominio {fullname} no tiene un ID de zona (CfZoneId) y falló la auto-detección: {ex.Message}");
						Environment.Exit(1);
					}
				}
			}

			while (true)
			{
				Console.WriteLine();
				LogMessage("🔍", "Chequeando el estado de los dominios...", ConsoleColor.Blue, "");

				foreach (var dom in domains)
				{
					string record = string.IsNullOrEmpty(dom.record) ? "@" : dom.record;
					string type = string.IsNullOrEmpty(dom.type) ? "A" : dom.type;
					string fullname = (record == "@") ? dom.name : $"{record}.{dom.name}";

					// Consultar el estado del dominio
					string queryUrl = $"{statusUrl}?domain={Uri.EscapeDataString(fullname)}";
					Console.Write("   ├─ 🔍 Consultando estado para ");
					WriteColored(fullname, ConsoleColor.White);
					Console.WriteLine("...");

					string jsonResponse = await FetchStatus(queryUrl);
					if (string.IsNullOrEmpty(jsonResponse) || jsonResponse.Trim() == "null")
					{
						Console.Write("   │  ");
						WriteColored("⚠️", ConsoleColor.Yellow);
						Console.WriteLine($" Error al obtener el estado para {fullname}, se omitirá en este ciclo.");
						continue;
					}

					StatusResponse status = null;
					try
					{
						status = JsonSerializer.Deserialize<StatusResponse>(jsonResponse, _jsonOptions);
					}
					catch (Exception ex)
					{
						Console.Write("   │  ");
						WriteColored("⚠️", ConsoleColor.Yellow);
						Console.WriteLine($" Error al parsear el estado para {fullname}: {ex.Message}, se omitirá.");
						continue;
					}

					if (status == null)
					{
						Console.Write("   │  ");
						WriteColored("⚠️", ConsoleColor.Yellow);
						Console.WriteLine($" Respuesta nula para {fullname}, se omitirá.");
						continue;
					}

					// ok == true significa que NO está bloqueado -> activateCfProxy activado (desiredProxy = true)
					// ok == false significa que SÍ está bloqueado -> activateCfProxy desactivado (desiredProxy = false)
					bool desiredProxy = status.ok;

					if (desiredProxy)
					{
						Console.Write("   │  ");
						WriteColored("✅", ConsoleColor.Green);
						Console.Write($" {fullname} no está bloqueado. Estado activateCfProxy deseado: ");
						WriteColored("ACTIVAR", ConsoleColor.Green);
						Console.WriteLine(".");
					}
					else
					{
						Console.Write("   │  ");
						WriteColored("🔴", ConsoleColor.Red);
						Console.Write($" {fullname} detectado como BLOQUEADO. Estado activateCfProxy deseado: ");
						WriteColored("DESACTIVAR", ConsoleColor.Red);
						Console.WriteLine(".");
					}

					// Actualizar en Cloudflare
					try
					{
						await UpdateDnsRecord(dom.name, record, type, desiredProxy, cfApiToken, dom.CfZoneId);
					}
					catch (Exception ex)
					{
						Console.Write("   │  ");
						WriteColored("❌ Error al actualizar Cloudflare para ", ConsoleColor.Red);
						Console.Write(fullname);
						Console.WriteLine($": {ex.Message}");
					}
				}

				LogMessage("✅", "Ciclo completado", ConsoleColor.Green, "");

				if (runOnce)
				{
					break;
				}

				LogMessage("⏳", $"Esperando {intervalSeconds} segundos antes de volver a comprobar...", ConsoleColor.DarkGray, "");
				await Task.Delay(intervalSeconds * 1000);
			}
		}

		/// <summary>
		/// Realiza una solicitud HTTP GET al servicio de estado especificado y devuelve la respuesta JSON.
		/// </summary>
		/// <param name="url">URL completa del servicio REST.</param>
		/// <returns>La respuesta del servidor en formato string, o null si la petición falla.</returns>
		static async Task<string> FetchStatus(string url)
		{
			try
			{
				var response = await _httpClient.GetAsync(url);
				if (response.StatusCode == HttpStatusCode.OK)
				{
					return await response.Content.ReadAsStringAsync();
				}
			}
			catch
			{
				// Ignorar
			}
			return null;
		}

		/// <summary>
		/// Busca el ID de zona en Cloudflare para un dominio específico utilizando la API de zonas.
		/// </summary>
		/// <param name="domain">Nombre del dominio raíz (ej. ejemplo.com).</param>
		/// <param name="apiToken">Token de autorización de Cloudflare.</param>
		/// <returns>El ID de la zona correspondiente de Cloudflare.</returns>
		static async Task<string> FetchZoneId(string domain, string apiToken)
		{
			string queryUrl = $"https://api.cloudflare.com/client/v4/zones?name={Uri.EscapeDataString(domain)}";
			using (var request = new HttpRequestMessage(HttpMethod.Get, queryUrl))
			{
				request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
				using (var response = await _httpClient.SendAsync(request))
				{
					int code = (int)response.StatusCode;
					if (code != 200)
					{
						throw new Exception($"HTTP {code} al consultar zonas de Cloudflare");
					}

					string responseBody = await response.Content.ReadAsStringAsync();
					var zonesResponse = JsonSerializer.Deserialize<CloudflareZonesResponse>(responseBody, _jsonOptions);

					if (zonesResponse == null || !zonesResponse.success || zonesResponse.result == null || zonesResponse.result.Count == 0)
					{
						throw new Exception("Zona no encontrada en la cuenta asociada.");
					}

					return zonesResponse.result[0].id;
				}
			}
		}

		/// <summary>
		/// Busca el registro DNS en Cloudflare y lo actualiza con el estado deseado del activateCfProxy y TTL si difiere del actual.
		/// </summary>
		/// <param name="domain">Dominio principal (ej. midominio.com).</param>
		/// <param name="record">Subdominio/Registro a actualizar (ej. www o @).</param>
		/// <param name="type">Tipo de registro DNS (ej. A o CNAME).</param>
		/// <param name="activateCfProxy">Define si el tráfico debe pasar (true) o no (false) por el proxy de Cloudflare.</param>
		/// <param name="apiToken">Token de autenticación de Cloudflare.</param>
		/// <param name="zoneId">Identificador único de la zona en Cloudflare.</param>
		static async Task UpdateDnsRecord(string domain, string record, string type, bool activateCfProxy, string apiToken, string zoneId)
		{
			// Construir nombre completo
			string fullname = (string.IsNullOrEmpty(record) || record == "@") ? domain : $"{record}.{domain}";
			string endpoint = $"https://api.cloudflare.com/client/v4/zones/{zoneId}/dns_records";

			WriteColored("   ├─", ConsoleColor.DarkGray);
			Console.Write(" ");
			WriteColored("🔍 Buscando", ConsoleColor.Blue);
			Console.Write(" ");
			WriteColored(fullname, ConsoleColor.White);
			WriteColored($" (tipo: {type})", ConsoleColor.DarkGray);
			Console.WriteLine();

			// Consultar el registro existente
			string queryUrl = $"{endpoint}?name={Uri.EscapeDataString(fullname)}&type={type}";

			string responseBody = null;
			int httpCode = 0;

			try
			{
				using (var request = new HttpRequestMessage(HttpMethod.Get, queryUrl))
				{
					request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
					using (var response = await _httpClient.SendAsync(request))
					{
						httpCode = (int)response.StatusCode;
						responseBody = await response.Content.ReadAsStringAsync();
					}
				}
			}
			catch (Exception ex)
			{
				var innerMessage = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
				throw new Exception($"Error de conexión: {innerMessage}");
			}

			if (httpCode != 200)
			{
				throw new Exception($"HTTP {httpCode} al consultar Cloudflare");
			}

			// Deserializar la respuesta
			CloudflareResponse cfResponse = null;

			try
			{
				cfResponse = JsonSerializer.Deserialize<CloudflareResponse>(responseBody, _jsonOptions);
			}
			catch
			{
				// Ignorar y lanzar error de respuesta inválida abajo
			}

			if (cfResponse == null || !cfResponse.success || cfResponse.result == null)
			{
				throw new Exception("Respuesta inválida de Cloudflare API");
			}

			if (cfResponse.result.Count == 0)
			{
				throw new Exception("Registro no encontrado. Verifica nombre correcto y tipo de registro");
			}

			// Procesar el registro encontrado (se toma el primero)
			var recordData = cfResponse.result[0];
			string recordId = recordData.id;
			string content = recordData.content;
			bool currentProxied = recordData.proxied;

			// Determinar emoji para el estado actual del proxy (ativo o no)
			string proxyEmoji = activateCfProxy ? "🔒 ON" : "🔓 OFF";
			string currentProxyEmoji = currentProxied ? "🔒 ON" : "🔓 OFF";

			// Verificar si ya está en el estado deseado
			if (currentProxied == activateCfProxy)
			{
				ConsoleColor statusColor = activateCfProxy ? ConsoleColor.Green : ConsoleColor.Yellow;
				
				WriteColored("   ├─", ConsoleColor.DarkGray);
				Console.Write(" ");
				WriteColored("ℹ️   Sin cambios", statusColor);
				Console.Write(" │ ");
				WriteColored(fullname, ConsoleColor.White);
				
				WriteColored(" ya está ", ConsoleColor.DarkGray);
				Console.Write(proxyEmoji);
				WriteColored(" (IP: ", ConsoleColor.DarkGray);
				WriteColored(content, ConsoleColor.Cyan);
				WriteColored(")", ConsoleColor.DarkGray);
				Console.WriteLine();
				return;
			}

			// Preparar payload de actualización
			var payload = new
			{
				type = type,
				name = fullname,
				content = content,
				proxied = activateCfProxy,
				ttl = activateCfProxy ? 1 : 300 // TTL auto (1) si está proxied, 5min (300) si no
			};

			string jsonPayload = JsonSerializer.Serialize(payload, _jsonOptions);

			// Actualizar registro con PUT
			string updateUrl = $"{endpoint}/{recordId}";
			using (var request = new HttpRequestMessage(HttpMethod.Put, updateUrl))
			{
				request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
				request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

				int updateCode = 0;
				string respBody = null;

				try
				{
					using (var response = await _httpClient.SendAsync(request))
					{
						updateCode = (int)response.StatusCode;
						respBody = await response.Content.ReadAsStringAsync();
					}
				}
				catch (Exception ex)
				{
					var innerMessage = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
					throw new Exception($"Error de conexión al actualizar: {innerMessage}");
				}

				if (updateCode == 200)
				{
					string change = $"{currentProxyEmoji} → {proxyEmoji}";
					
					WriteColored("   ├─", ConsoleColor.DarkGray);
					Console.Write(" ");
					WriteColored("✅ Actualizado", ConsoleColor.Green);
					Console.Write(" │ ");
					WriteColored(fullname, ConsoleColor.White);
					
					WriteColored($" {change}", ConsoleColor.DarkGray);
					WriteColored(" (IP: ", ConsoleColor.DarkGray);
					WriteColored(content, ConsoleColor.Cyan);
					WriteColored(")", ConsoleColor.DarkGray);
					Console.WriteLine();
				}
				else
				{
					var updateResult = JsonSerializer.Deserialize<CloudflareResponse>(respBody, _jsonOptions);
					string errorMsg = "Error desconocido";
					if (updateResult != null && updateResult.errors != null && updateResult.errors.Count > 0)
					{
						errorMsg = updateResult.errors[0].message;
					}
					throw new Exception($"HTTP {updateCode} │ {errorMsg}");
				}
			}
		}

		#region Utilidades de consola
		/// <summary>
		/// Registra un mensaje formateado en la consola con marca de tiempo, emoji, nivel de log y color.
		/// </summary>
		/// <param name="emoji">Emoji descriptivo del estado o acción.</param>
		/// <param name="level">Nivel de registro o etiqueta del mensaje.</param>
		/// <param name="color">Color asociado al nivel de log.</param>
		/// <param name="message">Texto opcional con el detalle del mensaje.</param>
		static void LogMessage(string emoji, string level, ConsoleColor color, string message)
		{
			string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

			WriteColored($"[{timestamp}] ", ConsoleColor.DarkGray);
			WriteColored($"{emoji} {level}", color);

			if (string.IsNullOrEmpty(message))
			{
				Console.WriteLine();
			}
			else
			{
				Console.WriteLine($" │ {message}");
			}
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

		/// <summary>
		/// Escribe en la consola un texto con un color específico y luego restaura el color original.
		/// </summary>
		/// <param name="text">Texto a escribir en la consola.</param>
		/// <param name="color">Color de consola deseado.</param>
		static void WriteColored(string text, ConsoleColor color)
		{
			var prev = Console.ForegroundColor;
			Console.ForegroundColor = color;
			Console.Write(text);
			Console.ForegroundColor = prev;
		}
		#endregion
	}

	#region Clases auxiliares
	/// <summary>
	/// Representa la respuesta de estado devuelta por el endpoint de verificación de bloqueo.
	/// </summary>
	public class StatusResponse
	{
		/// <summary>Determina si el dominio está funcionando correctamente (true) o si está bloqueado por el ISP (false).</summary>
		public bool ok { get; set; }
	}

	/// <summary>
	/// Representa la envoltura de respuesta estándar devuelta por la API de Cloudflare.
	/// </summary>
	public class CloudflareResponse
	{
		/// <summary>Colección de registros DNS devueltos por la consulta.</summary>
		public List<DnsRecord> result { get; set; }

		/// <summary>Indica si la petición a Cloudflare fue exitosa.</summary>
		public bool success { get; set; }

		/// <summary>Lista de posibles errores devueltos por la API de Cloudflare.</summary>
		public List<CloudflareError> errors { get; set; }
	}

	/// <summary>
	/// Estructura detallada de un registro DNS individual devuelto por Cloudflare.
	/// </summary>
	public class DnsRecord
	{
		/// <summary>Identificador único del registro DNS en Cloudflare.</summary>
		public string id { get; set; }

		/// <summary>Nombre completo del registro (ej. sub.dominio.com).</summary>
		public string name { get; set; }

		/// <summary>Tipo de registro DNS (A, CNAME, etc.).</summary>
		public string type { get; set; }

		/// <summary>Contenido del registro DNS (dirección IP o nombre de destino).</summary>
		public string content { get; set; }

		/// <summary>Indica si el activateCfProxy ("nube naranja") está activado en Cloudflare.</summary>
		public bool proxied { get; set; }

		/// <summary>Tiempo de vida (TTL) del registro DNS en segundos.</summary>
		public int ttl { get; set; }
	}

	/// <summary>
	/// Representa un error detallado retornado por la API de Cloudflare.
	/// </summary>
	public class CloudflareError
	{
		/// <summary>Código numérico del error.</summary>
		public int code { get; set; }

		/// <summary>Mensaje explicativo del error.</summary>
		public string message { get; set; }
	}
	/// <summary>
	/// Representa la respuesta de la API de Cloudflare al buscar zonas.
	/// </summary>
	public class CloudflareZonesResponse
	{
		/// <summary>Colección de zonas devueltas por la consulta.</summary>
		public List<CloudflareZone> result { get; set; }

		/// <summary>Indica si la petición fue exitosa.</summary>
		public bool success { get; set; }
	}

	/// <summary>
	/// Representa la información básica de una zona de Cloudflare.
	/// </summary>
	public class CloudflareZone
	{
		/// <summary>Identificador único de la zona en Cloudflare.</summary>
		public string id { get; set; }

		/// <summary>Nombre del dominio de la zona.</summary>
		public string name { get; set; }
	}
	#endregion
}
