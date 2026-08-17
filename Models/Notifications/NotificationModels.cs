using System;
using System.Collections.Generic;

namespace SiFutbolNoCF.Models.Notifications
{
	/// <summary>
	/// Describe el cambio de estado individual sufrido por un registro DNS en Cloudflare.
	/// </summary>
	public class DomainChangeInfo
	{
		/// <summary>
		/// Nombre del dominio raíz configurado (ej. 'ejemplo.com').
		/// </summary>
		public string Domain { get; set; }

		/// <summary>
		/// Nombre del registro o subdominio evaluado (ej. 'www' o '@').
		/// </summary>
		public string Record { get; set; }

		/// <summary>
		/// Nombre calificado completo del host (ej. 'www.ejemplo.com').
		/// </summary>
		public string Fullname { get; set; }

		/// <summary>
		/// Tipo de registro DNS (ej. 'A', 'AAAA', 'CNAME').
		/// </summary>
		public string RecordType { get; set; }

		/// <summary>
		/// Estado previo del proxy en Cloudflare antes de la comprobación.
		/// </summary>
		public bool PreviousProxied { get; set; }

		/// <summary>
		/// Nuevo estado asignado al proxy de Cloudflare tras detectar el cambio.
		/// </summary>
		public bool NewProxied { get; set; }

		/// <summary>
		/// Dirección IP de origen real del servidor web configurada en Cloudflare.
		/// </summary>
		public string OriginIp { get; set; }

		/// <summary>
		/// Lista de direcciones IP de Cloudflare resueltas o recordadas en la caché.
		/// </summary>
		public List<string> CloudflareIps { get; set; } = new List<string>();

		/// <summary>
		/// Motivo textual explicativo del cambio aplicado.
		/// </summary>
		public string Reason { get; set; }
	}

	/// <summary>
	/// Agrupa todos los cambios de proxy ocurridos en una misma iteración del ciclo de comprobación.
	/// </summary>
	public class NotificationBatchEvent
	{
		/// <summary>
		/// Lista de cambios individuales ocurridos en este ciclo.
		/// </summary>
		public List<DomainChangeInfo> Changes { get; set; } = new List<DomainChangeInfo>();

		/// <summary>
		/// Marca de tiempo en formato UTC en la que finalizó el ciclo de comprobación.
		/// </summary>
		public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
	}

	/// <summary>
	/// Representa el resultado del intento de envío a través de un proveedor concreto de notificaciones.
	/// </summary>
	public class NotificationResult
	{
		/// <summary>
		/// Nombre descriptivo del canal de notificación (ej. 'Telegram').
		/// </summary>
		public string ProviderName { get; set; }

		/// <summary>
		/// Indica si el mensaje se entregó con éxito al proveedor.
		/// </summary>
		public bool Success { get; set; }

		/// <summary>
		/// Mensaje de error detallado en caso de fallo en la comunicación.
		/// </summary>
		public string ErrorMessage { get; set; }
	}
}
