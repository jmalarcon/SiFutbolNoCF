using System;
using System.Collections.Generic;
using System.Text.Json;

namespace SiFutbolNoCF.Models
{
	/// <summary>
	/// Representa las opciones de configuración global cargadas para la aplicación SiFutbolNoCF.
	/// </summary>
	/// <remarks>
	/// Los valores se resuelven combinando archivos JSON locales, JSON base y variables de entorno.
	/// </remarks>
	public class AppSettings
	{
		/// <summary>
		/// Token de autorización de la API de Cloudflare con permisos de edición de DNS.
		/// </summary>
		public string CfApiToken { get; set; }

		/// <summary>
		/// Intervalo de tiempo en segundos entre cada comprobación periódica en el modo demonio.
		/// </summary>
		public int IntervalSeconds { get; set; }

		/// <summary>
		/// Indica si se debe ajustar el intervalo de comprobación de forma adaptativa según la hora y el estado de bloqueo.
		/// </summary>
		public bool? AdaptiveInterval { get; set; }

		/// <summary>
		/// Nivel de detalle de los mensajes en consola durante el modo demonio ("ChangesOnly" o "Full").
		/// </summary>
		public string Verbosity { get; set; }

		/// <summary>
		/// URL del endpoint oficial de estado que devuelve las IPs bloqueadas por los operadores.
		/// </summary>
		public string StatusUrl { get; set; }

		/// <summary>
		/// Diccionario desacoplado con las secciones de configuración para cada canal de alerta (ej. 'Telegram', 'Discord').
		/// </summary>
		public Dictionary<string, JsonElement> Notifications { get; set; } = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Colección de dominios y subdominios configurados para monitorización y conmutación de proxy.
		/// </summary>
		public List<DomainConfig> Domains { get; set; }
	}

	/// <summary>
	/// Representa la configuración específica de un subdominio o registro DNS individual a monitorizar.
	/// </summary>
	public class DomainConfig
	{
		/// <summary>
		/// Nombre del dominio raíz registrado en Cloudflare (ej. 'ejemplo.com').
		/// </summary>
		public string name { get; set; }

		/// <summary>
		/// Nombre del registro o subdominio a modificar (ej. 'www', 'api' o '@' para la raíz).
		/// </summary>
		public string record { get; set; }

		/// <summary>
		/// Tipo de registro DNS en Cloudflare (ej. 'A', 'AAAA', 'CNAME').
		/// </summary>
		public string type { get; set; }

		/// <summary>
		/// Identificador único de la zona en Cloudflare. Si es nulo o vacío, el sistema intentará auto-detectarlo.
		/// </summary>
		public string CfZoneId { get; set; }
	}
}
