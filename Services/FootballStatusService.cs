using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace ManageDns.Services
{
	/// <summary>
	/// Servicio estático encargado de consultar el endpoint oficial de estado de fútbol y procesar las IPs bloqueadas.
	/// </summary>
	/// <remarks>
	/// Descarga el archivo JSON completo y utiliza <see cref="JsonDocument"/> para extraer en memoria
	/// un conjunto <see cref="HashSet{T}"/> de IPs bloqueadas evaluando el último estado registrado.
	/// Está completamente desacoplado de la interfaz de usuario / consola.
	/// </remarks>
	public static class FootballStatusService
	{
		// Instancia estática y única de HttpClient reutilizada para optimizar sockets TCP
		private static readonly HttpClient _httpClient = new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(15)
		};

		/// <summary>
		/// Descarga el archivo JSON desde la URL del servicio y extrae el conjunto de direcciones IP bloqueadas actualmente.
		/// </summary>
		/// <param name="statusUrl">URL del endpoint de estado (ej. 'https://hayahora.futbol/estado/data.json').</param>
		/// <returns>Conjunto de direcciones IP con bloqueo activo en al menos un operador.</returns>
		public static async Task<HashSet<string>> FetchBlockedIpsAsync(string statusUrl)
		{
			// 1. Descargar el contenido en bytes del endpoint oficial
			byte[] jsonBytes = await FetchStatusDataAsync(statusUrl);

			// 2. Extraer el conjunto de IPs bloqueadas evaluando los últimos cambios de estado
			return ExtractBlockedIps(jsonBytes);
		}

		/// <summary>
		/// Descarga los bytes del archivo JSON desde el endpoint remoto.
		/// </summary>
		/// <param name="url">URL completa del endpoint.</param>
		/// <returns>Array de bytes con la respuesta.</returns>
		private static async Task<byte[]> FetchStatusDataAsync(string url)
		{
			// Ejecutar petición HTTP GET asíncrona hacia el endpoint
			var response = await _httpClient.GetAsync(url);

			// Verificar si el servidor devolvió código 200 OK
			if (response.StatusCode == HttpStatusCode.OK)
			{
				// Leer los bytes directamente para parsear con JsonDocument sin conversiones intermedias a string
				return await response.Content.ReadAsByteArrayAsync();
			}

			// Lanzar excepción informativa en caso de respuesta no satisfactoria
			throw new HttpRequestException($"HTTP {(int)response.StatusCode} al consultar el servicio de estado");
		}

		/// <summary>
		/// Analiza el documento JSON y extrae las direcciones IP cuyo último evento en 'stateChanges' indica bloqueo ('state': true).
		/// </summary>
		/// <param name="jsonBytes">Bytes del archivo data.json descargado.</param>
		/// <returns>Conjunto de IPs con comparación de cadenas case-insensitive.</returns>
		public static HashSet<string> ExtractBlockedIps(byte[] jsonBytes)
		{
			// Inicializar el conjunto con comparador case-insensitive para búsquedas rápidas O(1)
			var blockedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			// Si el payload es nulo o está vacío, retornar conjunto vacío
			if (jsonBytes == null || jsonBytes.Length == 0)
			{
				return blockedSet;
			}

			// Parsear el payload JSON en modo de solo lectura de alto rendimiento sin asignar objetos POCO
			using var doc = JsonDocument.Parse(jsonBytes);

			// Obtener el array principal 'data'
			if (doc.RootElement.TryGetProperty("data", out var dataElem) && dataElem.ValueKind == JsonValueKind.Array)
			{
				// Iterar cada entrada de monitorización (IP + ISP)
				foreach (var entry in dataElem.EnumerateArray())
				{
					// Extraer la dirección IP
					if (entry.TryGetProperty("ip", out var ipElem))
					{
						string ip = ipElem.GetString();

						// Si la IP es válida y aún no ha sido marcada como bloqueada
						if (!string.IsNullOrEmpty(ip) && !blockedSet.Contains(ip))
						{
							// Verificar la lista de cambios de estado históricos
							if (entry.TryGetProperty("stateChanges", out var changesElem) && changesElem.ValueKind == JsonValueKind.Array)
							{
								int count = changesElem.GetArrayLength();
								if (count > 0)
								{
									// El último elemento cronológico representa el estado actual de este ISP
									var lastChange = changesElem[count - 1];

									// Si 'state' es true, la IP está bloqueada activamente por este operador
									if (lastChange.TryGetProperty("state", out var stateElem) && stateElem.GetBoolean())
									{
										blockedSet.Add(ip);
									}
								}
							}
						}
					}
				}
			}

			return blockedSet;
		}
	}
}
