using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SiFutbolNoCF.Models.Notifications;

namespace SiFutbolNoCF.Services.Notifications
{
	/// <summary>
	/// Proveedor de notificaciones para Telegram mediante la API oficial de bots (HTTP POST).
	/// </summary>
	public class TelegramNotificationProvider : INotificationProvider
	{
		// Cliente HTTP reutilizable y optimizado para llamadas al API de Telegram
		private static readonly HttpClient _httpClient = new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(15)
		};

		// Configuración interna del canal de Telegram
		private readonly TelegramSettings _settings;

		/// <summary>
		/// Nombre descriptivo del canal de notificación.
		/// </summary>
		public string Name => "Telegram";

		/// <summary>
		/// Indica si el canal de Telegram tiene credenciales completas y está activo.
		/// </summary>
		public bool IsEnabled =>
			_settings != null &&
			_settings.Enabled == true &&
			!string.IsNullOrWhiteSpace(_settings.BotToken) &&
			!string.IsNullOrWhiteSpace(_settings.ChatId);

		/// <summary>
		/// Inicializa el proveedor extrayendo su sección de configuración del diccionario global y variables de entorno.
		/// </summary>
		/// <param name="notificationsConfig">Diccionario con las secciones de configuración de todos los canales.</param>
		public TelegramNotificationProvider(Dictionary<string, JsonElement> notificationsConfig)
		{
			JsonElement? section = null;
			if (notificationsConfig != null && notificationsConfig.TryGetValue(Name, out var elem))
			{
				section = elem;
			}

			// Cargar la configuración resolviendo JSON y variables de entorno
			_settings = TelegramSettings.Load(section);
		}

		/// <summary>
		/// Inicializa una nueva instancia del proveedor de Telegram con configuración directa (útil para pruebas unitarias).
		/// </summary>
		/// <param name="settings">Opciones de configuración de Telegram.</param>
		public TelegramNotificationProvider(TelegramSettings settings)
		{
			_settings = settings;
		}

		/// <summary>
		/// Envía un lote consolidado de cambios de estado a Telegram formateado en HTML.
		/// </summary>
		/// <param name="batchEvent">Evento agrupado con los cambios ocurridos en el ciclo.</param>
		/// <returns>Resultado del envío con información de éxito o detalle del error.</returns>
		public async Task<NotificationResult> SendAsync(NotificationBatchEvent batchEvent)
		{
			// Si el proveedor está deshabilitado o no hay cambios en el lote, omitir el envío
			if (!IsEnabled || batchEvent == null || batchEvent.Changes == null || batchEvent.Changes.Count == 0)
			{
				return new NotificationResult
				{
					ProviderName = Name,
					Success = false,
					ErrorMessage = "Proveedor no habilitado o lote de cambios vacío."
				};
			}

			try
			{
				// Construir el cuerpo del mensaje en formato HTML
				string messageText = BuildHtmlMessage(batchEvent);

				// Preparar el objeto payload requerido por la API de Telegram
				var payload = new
				{
					chat_id = _settings.ChatId,
					text = messageText,
					parse_mode = "HTML",
					disable_web_page_preview = true
				};

				// Serializar el payload a JSON
				string jsonPayload = JsonSerializer.Serialize(payload);
				using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

				// Construir la URL del endpoint sendMessage de Telegram
				string url = $"https://api.telegram.org/bot{_settings.BotToken}/sendMessage";

				// Realizar la petición POST asíncrona
				using var response = await _httpClient.PostAsync(url, content);
				string responseBody = await response.Content.ReadAsStringAsync();

				// Evaluar si la API de Telegram devolvió un código HTTP de éxito
				if (response.IsSuccessStatusCode)
				{
					return new NotificationResult
					{
						ProviderName = Name,
						Success = true
					};
				}

				// Devolver error si la API de Telegram rechazó la petición
				return new NotificationResult
				{
					ProviderName = Name,
					Success = false,
					ErrorMessage = $"HTTP {(int)response.StatusCode} - {responseBody}"
				};
			}
			catch (Exception ex)
			{
				// Capturar cualquier excepción de red o serialización para proteger el flujo principal
				return new NotificationResult
				{
					ProviderName = Name,
					Success = false,
					ErrorMessage = ex.Message
				};
			}
		}

		/// <summary>
		/// Genera un mensaje formateado en HTML agrupando los cambios de estado de todos los dominios.
		/// </summary>
		/// <param name="batch">Lote de cambios detectados.</param>
		/// <returns>Cadena de texto formateada con etiquetas HTML compatibles con Telegram.</returns>
		private static string BuildHtmlMessage(NotificationBatchEvent batch)
		{
			var sb = new StringBuilder();

			// Cabecera principal del mensaje
			sb.AppendLine("🔔 <b>SiFutbolNoCF: Cambios en Cloudflare</b>");
			sb.AppendLine();

			// Iterar por cada dominio afectado en este ciclo
			for (int i = 0; i < batch.Changes.Count; i++)
			{
				var change = batch.Changes[i];
				string fullname = EscapeHtml(change.Fullname);
				string recordType = EscapeHtml(change.RecordType);
				string originIp = EscapeHtml(change.OriginIp ?? "N/A");

				if (!change.NewProxied)
				{
					// Caso 1: Proxy desactivado (bloqueo detectado)
					sb.AppendLine($"🔴 <b>{fullname}</b> ({recordType})");
					sb.AppendLine("├ Estado proxy: <b>DESACTIVADO 🔓</b>");
					sb.AppendLine($"├ IP de origen: <code>{originIp}</code>");

					if (change.CloudflareIps != null && change.CloudflareIps.Count > 0)
					{
						string ips = EscapeHtml(string.Join(", ", change.CloudflareIps));
						sb.AppendLine($"└ IPs Cloudflare: <code>{ips}</code>");
					}
					else
					{
						sb.AppendLine("└ Motivo: Bloqueo de operadores activo");
					}
				}
				else
				{
					// Caso 2: Proxy reactivado (bloqueo finalizado)
					sb.AppendLine($"✅ <b>{fullname}</b> ({recordType})");
					sb.AppendLine("├ Estado proxy: <b>ACTIVADO 🔒</b>");
					sb.AppendLine($"├ IP de origen: <code>{originIp}</code>");
					sb.AppendLine("└ Protección y CDN de Cloudflare restauradas");
				}

				// Agregar salto de separación entre dominios
				if (i < batch.Changes.Count - 1)
				{
					sb.AppendLine();
				}
			}

			sb.AppendLine();
			// Pie con la fecha y hora UTC del ciclo
			sb.AppendLine($"⏱ <i>{batch.TimestampUtc:yyyy-MM-dd HH:mm:ss} UTC</i>");

			return sb.ToString();
		}

		/// <summary>
		/// Escapa caracteres especiales de HTML (&, <, >) para evitar errores de parseo en Telegram.
		/// </summary>
		/// <param name="text">Texto original a escapar.</param>
		/// <returns>Texto con caracteres HTML escapados de forma segura.</returns>
		private static string EscapeHtml(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return string.Empty;
			}

			return text
				.Replace("&", "&amp;")
				.Replace("<", "&lt;")
				.Replace(">", "&gt;");
		}
	}
}
