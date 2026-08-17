using System;
using System.Text.Json;

namespace SiFutbolNoCF.Models.Notifications
{
	/// <summary>
	/// Parámetros específicos para el envío de alertas mediante un Bot de Telegram.
	/// </summary>
	public class TelegramSettings
	{
		// Opciones JSON para deserializar la configuración ignorando mayúsculas y minúsculas
		private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true
		};

		// Textos comodín por defecto que deben descartarse si no se han editado
		private const string TokenPlaceholder = "TU_TELEGRAM_BOT_TOKEN";
		private const string ChatIdPlaceholder = "TU_TELEGRAM_CHAT_ID";

		/// <summary>
		/// Indica si el canal de Telegram está habilitado para enviar alertas.
		/// </summary>
		public bool? Enabled { get; set; }

		/// <summary>
		/// Token secreto del bot de Telegram proporcionado por BotFather.
		/// </summary>
		public string BotToken { get; set; }

		/// <summary>
		/// Identificador numérico único del chat personal o grupo de destino.
		/// </summary>
		public string ChatId { get; set; }

		/// <summary>
		/// Carga y resuelve la configuración de Telegram a partir del bloque JSON y variables de entorno.
		/// </summary>
		/// <param name="jsonConfig">Elemento JSON correspondiente a la sección de Telegram, o null.</param>
		/// <returns>Instancia validada de <see cref="TelegramSettings"/>.</returns>
		public static TelegramSettings Load(JsonElement? jsonConfig)
		{
			var settings = new TelegramSettings();

			// 1. Deserializar del JSON si el elemento está presente y es un objeto válido
			if (jsonConfig.HasValue && jsonConfig.Value.ValueKind == JsonValueKind.Object)
			{
				try
				{
					string rawJson = jsonConfig.Value.GetRawText();
					settings = JsonSerializer.Deserialize<TelegramSettings>(rawJson, _jsonOptions) ?? new TelegramSettings();
				}
				catch
				{
					// Ignorar error de deserialización para continuar con valores por defecto
				}
			}

			// 2. Resolver Token considerando variables de entorno y descartando comodines
			string envToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")
			                  ?? Environment.GetEnvironmentVariable("TELEGRAMBOTTOKEN");

			if (!string.IsNullOrEmpty(envToken))
			{
				settings.BotToken = envToken;
			}
			else if (string.Equals(settings.BotToken, TokenPlaceholder, StringComparison.OrdinalIgnoreCase))
			{
				settings.BotToken = null;
			}

			// 3. Resolver ChatId considerando variables de entorno y descartando comodines
			string envChatId = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID")
			                   ?? Environment.GetEnvironmentVariable("TELEGRAMCHATID");

			if (!string.IsNullOrEmpty(envChatId))
			{
				settings.ChatId = envChatId;
			}
			else if (string.Equals(settings.ChatId, ChatIdPlaceholder, StringComparison.OrdinalIgnoreCase))
			{
				settings.ChatId = null;
			}

			// 4. Resolver estado Enabled considerando variables de entorno
			string envEnabled = Environment.GetEnvironmentVariable("TELEGRAM_ENABLED")
			                    ?? Environment.GetEnvironmentVariable("TELEGRAMENABLED");

			if (!string.IsNullOrEmpty(envEnabled))
			{
				settings.Enabled = !envEnabled.Trim().Equals("false", StringComparison.OrdinalIgnoreCase) &&
				                   !envEnabled.Trim().Equals("0", StringComparison.OrdinalIgnoreCase) &&
				                   !envEnabled.Trim().Equals("no", StringComparison.OrdinalIgnoreCase) &&
				                   !envEnabled.Trim().Equals("off", StringComparison.OrdinalIgnoreCase);
			}
			else if (!settings.Enabled.HasValue)
			{
				// Si no se definió explícitamente pero hay credenciales válidas, habilitar por defecto
				bool hasValidCredentials = !string.IsNullOrWhiteSpace(settings.BotToken) &&
				                          !string.IsNullOrWhiteSpace(settings.ChatId);
				settings.Enabled = hasValidCredentials;
			}

			return settings;
		}
	}
}
