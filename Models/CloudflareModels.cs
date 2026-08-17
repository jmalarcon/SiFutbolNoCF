using System.Collections.Generic;

namespace SiFutbolNoCF.Models
{
	/// <summary>
	/// Representa la respuesta estándar devuelta por la API de Cloudflare al consultar registros DNS.
	/// </summary>
	public class CloudflareResponse
	{
		/// <summary>
		/// Colección de registros DNS devueltos por la consulta.
		/// </summary>
		public List<DnsRecord> result { get; set; }

		/// <summary>
		/// Indica si la petición HTTP a la API de Cloudflare fue procesada con éxito.
		/// </summary>
		public bool success { get; set; }

		/// <summary>
		/// Lista de errores reportados por Cloudflare en caso de fallo.
		/// </summary>
		public List<CloudflareError> errors { get; set; }
	}

	/// <summary>
	/// Estructura detallada de un registro DNS individual en Cloudflare.
	/// </summary>
	public class DnsRecord
	{
		/// <summary>
		/// Identificador alfanumérico único del registro DNS en Cloudflare.
		/// </summary>
		public string id { get; set; }

		/// <summary>
		/// Nombre completo calificado del registro DNS (ej. 'www.ejemplo.com').
		/// </summary>
		public string name { get; set; }

		/// <summary>
		/// Tipo de registro DNS (ej. 'A', 'AAAA', 'CNAME').
		/// </summary>
		public string type { get; set; }

		/// <summary>
		/// Contenido destino del registro DNS (dirección IP de origen o nombre de host de destino).
		/// </summary>
		public string content { get; set; }

		/// <summary>
		/// Indica si el proxy de Cloudflare ("nube naranja") está activo para este registro.
		/// </summary>
		public bool proxied { get; set; }

		/// <summary>
		/// Tiempo de vida (TTL) del registro DNS en segundos (1 = automático en registros proxied).
		/// </summary>
		public int ttl { get; set; }
	}

	/// <summary>
	/// Representa un error individual devuelto por la API de Cloudflare.
	/// </summary>
	public class CloudflareError
	{
		/// <summary>
		/// Código numérico de error retornado por Cloudflare.
		/// </summary>
		public int code { get; set; }

		/// <summary>
		/// Mensaje descriptivo del error en inglés.
		/// </summary>
		public string message { get; set; }
	}

	/// <summary>
	/// Representa la respuesta de la API de Cloudflare al buscar zonas de una cuenta.
	/// </summary>
	public class CloudflareZonesResponse
	{
		/// <summary>
		/// Colección de zonas pertenecientes a la cuenta consultada.
		/// </summary>
		public List<CloudflareZone> result { get; set; }

		/// <summary>
		/// Indica si la consulta de zonas fue exitosa.
		/// </summary>
		public bool success { get; set; }
	}

	/// <summary>
	/// Representa los datos básicos de una zona registrada en Cloudflare.
	/// </summary>
	public class CloudflareZone
	{
		/// <summary>
		/// Identificador único de la zona en Cloudflare.
		/// </summary>
		public string id { get; set; }

		/// <summary>
		/// Nombre del dominio raíz correspondiente a la zona.
		/// </summary>
		public string name { get; set; }
	}
}
