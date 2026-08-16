using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ManageDns.Models;

namespace ManageDns.Services
{
	/// <summary>
	/// Servicio estático para la comunicación e interactuación con la API REST v4 de Cloudflare.
	/// </summary>
	/// <remarks>
	/// Gestiona la búsqueda de IDs de zonas, la consulta de registros DNS y la actualización del estado del proxy ("nube naranja/gris").
	/// Está completamente desacoplado de la interfaz de usuario / consola.
	/// </remarks>
	public static class CloudflareService
	{
		// Opciones JSON para deserializar respuestas de Cloudflare sin importar mayúsculas/minúsculas
		private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true
		};

		// Cliente HTTP estático y único reutilizado para optimizar conexiones TCP
		private static readonly HttpClient _httpClient = new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(15)
		};

		/// <summary>
		/// Consulta la API de Cloudflare para buscar el ID de la zona correspondiente al dominio raíz.
		/// </summary>
		/// <param name="domain">Nombre del dominio raíz (ej. 'ejemplo.com').</param>
		/// <param name="apiToken">Token de autenticación de Cloudflare (debe tener permisos de Zone Read).</param>
		/// <returns>Identificador alfanumérico único de la zona.</returns>
		public static async Task<string> FetchZoneIdAsync(string domain, string apiToken)
		{
			// Construir URL del endpoint de zonas con el nombre de dominio codificado para URL
			string queryUrl = $"https://api.cloudflare.com/client/v4/zones?name={Uri.EscapeDataString(domain)}";

			// Crear la solicitud HTTP GET
			using var request = new HttpRequestMessage(HttpMethod.Get, queryUrl);

			// Adjuntar la cabecera de autorización Bearer con el token de API
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

			// Enviar la petición a la API de Cloudflare
			using var response = await _httpClient.SendAsync(request);
			int code = (int)response.StatusCode;

			// Verificar si la llamada fue rechazada por Cloudflare (ej. token inválido o error de permisos)
			if (code != 200)
			{
				throw new Exception($"HTTP {code} al consultar zonas de Cloudflare");
			}

			// Leer el cuerpo de la respuesta en formato JSON
			string responseBody = await response.Content.ReadAsStringAsync();

			// Deserializar la respuesta a modelo fuertemente tipado
			var zonesResponse = JsonSerializer.Deserialize<CloudflareZonesResponse>(responseBody, _jsonOptions);

			// Validar que la respuesta sea exitosa y contenga al menos una zona coincidente
			if (zonesResponse == null || !zonesResponse.success || zonesResponse.result == null || zonesResponse.result.Count == 0)
			{
				throw new Exception("Zona no encontrada en la cuenta asociada.");
			}

			// Retornar el ID de la primera zona encontrada
			return zonesResponse.result[0].id;
		}

		/// <summary>
		/// Consulta el estado actual de un registro DNS específico en la zona indicada de Cloudflare.
		/// </summary>
		/// <param name="domain">Nombre del dominio principal.</param>
		/// <param name="record">Subdominio o '@' para la raíz.</param>
		/// <param name="type">Tipo de registro DNS (A, AAAA, CNAME).</param>
		/// <param name="apiToken">Token de autorización de Cloudflare.</param>
		/// <param name="zoneId">Identificador de la zona en Cloudflare.</param>
		/// <returns>Instancia de <see cref="DnsRecord"/> con los datos actuales, o null si no se encuentra.</returns>
		public static async Task<DnsRecord> FetchDnsRecordAsync(string domain, string record, string type, string apiToken, string zoneId)
		{
			// Determinar el nombre completo del registro (si es '@' o vacío, corresponde al dominio raíz)
			string fullname = (string.IsNullOrEmpty(record) || record == "@") ? domain : $"{record}.{domain}";

			// Construir URL del endpoint filtrando por nombre exacto y tipo de registro
			string endpoint = $"https://api.cloudflare.com/client/v4/zones/{zoneId}/dns_records";
			string queryUrl = $"{endpoint}?name={Uri.EscapeDataString(fullname)}&type={type}";

			// Preparar la petición HTTP GET autenticada
			using var request = new HttpRequestMessage(HttpMethod.Get, queryUrl);
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

			// Ejecutar la consulta
			using var response = await _httpClient.SendAsync(request);
			int httpCode = (int)response.StatusCode;
			string responseBody = await response.Content.ReadAsStringAsync();

			// Comprobar errores HTTP en la llamada
			if (httpCode != 200)
			{
				throw new Exception($"HTTP {httpCode} al consultar Cloudflare");
			}

			// Deserializar la respuesta JSON de Cloudflare
			var cfResponse = JsonSerializer.Deserialize<CloudflareResponse>(responseBody, _jsonOptions);

			// Verificar si se encontró el registro en la zona
			if (cfResponse == null || !cfResponse.success || cfResponse.result == null || cfResponse.result.Count == 0)
			{
				return null;
			}

			// Devolver el primer registro coincidente
			return cfResponse.result[0];
		}

		/// <summary>
		/// Actualiza el estado del proxy y TTL en Cloudflare para un registro DNS si el estado actual difiere del deseado.
		/// </summary>
		/// <param name="domain">Nombre del dominio principal.</param>
		/// <param name="record">Subdominio o '@' para la raíz.</param>
		/// <param name="type">Tipo de registro DNS.</param>
		/// <param name="currentRecord">Registro actual devuelto previamente por la API.</param>
		/// <param name="desiredProxy">Indica si el proxy debe activarse (true) o desactivarse (false).</param>
		/// <param name="apiToken">Token de autorización de Cloudflare.</param>
		/// <param name="zoneId">Identificador de la zona en Cloudflare.</param>
		/// <returns>True si el registro fue actualizado en Cloudflare; false si ya tenía el estado deseado.</returns>
		public static async Task<bool> ApplyDnsRecordUpdateAsync(string domain, string record, string type, DnsRecord currentRecord, bool desiredProxy, string apiToken, string zoneId)
		{
			// Si el registro ya se encuentra en el estado deseado, no realizar llamada HTTP y devolver false
			if (currentRecord.proxied == desiredProxy)
			{
				return false;
			}

			// Construir el nombre completo del registro
			string fullname = (string.IsNullOrEmpty(record) || record == "@") ? domain : $"{record}.{domain}";
			string endpoint = $"https://api.cloudflare.com/client/v4/zones/{zoneId}/dns_records";

			// Preparar el payload JSON: TTL en 1 (automático) si está en proxy, o 300 (5 min) si se expone el origen
			var payload = new
			{
				type = type,
				name = fullname,
				content = currentRecord.content,
				proxied = desiredProxy,
				ttl = desiredProxy ? 1 : 300
			};

			// Serializar el objeto anónimo a formato JSON
			string jsonPayload = JsonSerializer.Serialize(payload, _jsonOptions);
			string updateUrl = $"{endpoint}/{currentRecord.id}";

			// Crear la solicitud HTTP PUT hacia el endpoint del registro específico
			using var request = new HttpRequestMessage(HttpMethod.Put, updateUrl);
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
			request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

			// Enviar la actualización a Cloudflare
			using var response = await _httpClient.SendAsync(request);
			int updateCode = (int)response.StatusCode;
			string respBody = await response.Content.ReadAsStringAsync();

			// Si la actualización fue exitosa, retornar true indicando que hubo cambio
			if (updateCode == 200)
			{
				return true;
			}

			// Parsear el mensaje de error devuelto por Cloudflare para lanzar una excepción informativa
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
