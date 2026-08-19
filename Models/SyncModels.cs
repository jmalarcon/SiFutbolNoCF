using System;
using System.Collections.Generic;
using SiFutbolNoCF.Models.Notifications;

namespace SiFutbolNoCF.Models
{
	/// <summary>
	/// Estado resultante de la sincronización individual de un dominio en Cloudflare.
	/// </summary>
	public enum DomainSyncStatus
	{
		/// <summary>
		/// El registro ya tiene el estado de proxy deseado y no requiere cambios.
		/// </summary>
		NoChange,

		/// <summary>
		/// El registro se actualizó satisfactoriamente en Cloudflare.
		/// </summary>
		Updated,

		/// <summary>
		/// El registro DNS especificado no existe en la zona de Cloudflare.
		/// </summary>
		DnsRecordNotFound,

		/// <summary>
		/// No se pudieron resolver direcciones IP por DNS y se mantuvo el estado actual.
		/// </summary>
		DnsResolutionFailed,

		/// <summary>
		/// Se produjo un error no controlado durante la comprobación o actualización.
		/// </summary>
		Error
	}

	/// <summary>
	/// Representa el resultado detallado de la sincronización de un dominio o subdominio individual.
	/// </summary>
	public class DomainSyncResult
	{
		/// <summary>
		/// Configuración del dominio asociado.
		/// </summary>
		public DomainConfig Domain { get; set; }

		/// <summary>
		/// Nombre del registro o subdominio (ej. '@' o 'www').
		/// </summary>
		public string Record { get; set; }

		/// <summary>
		/// Tipo de registro DNS (ej. 'A', 'CNAME').
		/// </summary>
		public string RecordType { get; set; }

		/// <summary>
		/// Nombre de host calificado completo (ej. 'www.ejemplo.com').
		/// </summary>
		public string Fullname { get; set; }

		/// <summary>
		/// Estado final del procesamiento.
		/// </summary>
		public DomainSyncStatus Status { get; set; }

		/// <summary>
		/// Estado del proxy antes de procesar el registro.
		/// </summary>
		public bool PreviousProxied { get; set; }

		/// <summary>
		/// Estado del proxy deseado tras evaluar bloqueos.
		/// </summary>
		public bool DesiredProxied { get; set; }

		/// <summary>
		/// Dirección IP o destino de origen configurado en Cloudflare.
		/// </summary>
		public string OriginIp { get; set; }

		/// <summary>
		/// Direcciones IP de Cloudflare resueltas por DNS o recuperadas de la caché.
		/// </summary>
		public List<string> RelevantIps { get; set; } = new List<string>();

		/// <summary>
		/// Mensaje explicativo del estado del bloqueo y proxy deseado.
		/// </summary>
		public string StatusLine { get; set; }

		/// <summary>
		/// Mensaje de error en caso de fallo durante el procesamiento.
		/// </summary>
		public string ErrorMessage { get; set; }

		/// <summary>
		/// Información del cambio si se actualizó el registro en Cloudflare.
		/// </summary>
		public DomainChangeInfo ChangeInfo { get; set; }

		/// <summary>
		/// Indica si este dominio se encuentra afectado por un bloqueo activo en este ciclo.
		/// </summary>
		public bool IsBlocked { get; set; }
	}

	/// <summary>
	/// Resumen estructurado con todos los resultados obtenidos tras ejecutar un ciclo de comprobación.
	/// </summary>
	public class CycleSyncResult
	{
		/// <summary>
		/// Conjunto de direcciones IP con bloqueo activo detectadas en este ciclo.
		/// </summary>
		public HashSet<string> BlockedIps { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Mensaje de error al descargar el estado de IPs bloqueadas, si lo hubo.
		/// </summary>
		public string BlockedIpsError { get; set; }

		/// <summary>
		/// Lista de resultados individuales por cada dominio configurado.
		/// </summary>
		public List<DomainSyncResult> DomainResults { get; set; } = new List<DomainSyncResult>();

		/// <summary>
		/// Lista de cambios de proxy aplicados durante este ciclo.
		/// </summary>
		public List<DomainChangeInfo> Changes { get; set; } = new List<DomainChangeInfo>();

		/// <summary>
		/// Resultados del envío de alertas a través de los canales configurados.
		/// </summary>
		public List<NotificationResult> NotificationResults { get; set; } = new List<NotificationResult>();

		/// <summary>
		/// Indica si existe al menos un dominio bloqueado activamente en este ciclo.
		/// </summary>
		public bool AnyDomainBlocked { get; set; }
	}

	/// <summary>
	/// Resultado de la ejecución manual de conmutación de proxy en modo directo (one-off).
	/// </summary>
	public class OneOffSyncResult
	{
		/// <summary>
		/// Nombre calificado completo del host modificado.
		/// </summary>
		public string Fullname { get; set; }

		/// <summary>
		/// Tipo de registro DNS procesado.
		/// </summary>
		public string RecordType { get; set; }

		/// <summary>
		/// Indica si la operación finalizó sin excepciones.
		/// </summary>
		public bool Success { get; set; }

		/// <summary>
		/// Indica si el estado del proxy cambió en Cloudflare.
		/// </summary>
		public bool Updated { get; set; }

		/// <summary>
		/// Estado del proxy antes de la operación.
		/// </summary>
		public bool PreviousProxied { get; set; }

		/// <summary>
		/// Nuevo estado asignado al proxy de Cloudflare.
		/// </summary>
		public bool NewProxied { get; set; }

		/// <summary>
		/// Dirección IP o destino de origen del registro.
		/// </summary>
		public string OriginIp { get; set; }

		/// <summary>
		/// Mensaje de error en caso de fallo.
		/// </summary>
		public string ErrorMessage { get; set; }

		/// <summary>
		/// Resultados del envío de notificaciones asociadas al cambio manual.
		/// </summary>
		public List<NotificationResult> NotificationResults { get; set; } = new List<NotificationResult>();
	}

	/// <summary>
	/// Información sobre el tiempo de espera calculado para el siguiente ciclo.
	/// </summary>
	public class DelayInfo
	{
		/// <summary>
		/// Segundos a esperar antes de la próxima comprobación.
		/// </summary>
		public int DelaySeconds { get; set; }

		/// <summary>
		/// Motivo explicativo de la duración calculada.
		/// </summary>
		public string Reason { get; set; }
	}

	/// <summary>
	/// Resultado de la resolución o auto-detección del ID de zona de Cloudflare.
	/// </summary>
	public class ZoneDetectionResult
	{
		/// <summary>
		/// Nombre del dominio raíz.
		/// </summary>
		public string DomainName { get; set; }

		/// <summary>
		/// Nombre calificado del host (ej. 'www.midominio.com').
		/// </summary>
		public string Fullname { get; set; }

		/// <summary>
		/// Identificador de zona resuelto desde Cloudflare.
		/// </summary>
		public string ZoneId { get; set; }

		/// <summary>
		/// Indica si la detección se completó con éxito.
		/// </summary>
		public bool Success { get; set; }

		/// <summary>
		/// Mensaje de error si la resolución falló.
		/// </summary>
		public string ErrorMessage { get; set; }
	}
}
