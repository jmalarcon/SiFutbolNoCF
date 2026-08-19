using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SiFutbolNoCF.Services
{
	/// <summary>
	/// Servicio estático encargado de la gestión y persistencia local en disco de las IPs de Cloudflare conocidas para cada dominio.
	/// </summary>
	/// <remarks>
	/// Permite conservar las IPs asignadas por Cloudflare a cada dominio incluso cuando el proxy esté desactivado,
	/// evitando la pérdida de estado ante caídas del proceso o reinicios del servidor.
	/// </remarks>
	public static class IpCacheService
	{
		// Opciones de serialización JSON con sangrado para mantener el archivo legible
		private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true,
			WriteIndented = true
		};

		// Ruta absoluta del archivo local de caché en disco
		private static readonly string _cacheFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".sifutbolnocf.cache.json");

		// Diccionario en memoria que indexa las IPs conocidas por nombre de dominio
		private static Dictionary<string, List<string>> _cache;

		/// <summary>
		/// Constructor estático que inicializa la colección en memoria y carga el archivo de caché si existe.
		/// </summary>
		static IpCacheService()
		{
			// Inicializar el diccionario con comparador case-insensitive para dominios
			_cache = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

			// Cargar los datos almacenados previamente en disco si existen
			Load();
		}

		/// <summary>
		/// Carga el contenido del archivo de caché desde disco a memoria si existe.
		/// </summary>
		public static void Load()
		{
			// Si el archivo no existe en disco, salir sin realizar acciones
			if (!File.Exists(_cacheFilePath))
			{
				return;
			}

			try
			{
				// Leer el contenido textual del archivo de estado
				string json = File.ReadAllText(_cacheFilePath);

				// Deserializar el JSON a la estructura de diccionario en memoria
				var data = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json, _jsonOptions);
				if (data != null)
				{
					// Reemplazar la colección en memoria asegurando el comparador case-insensitive
					_cache = new Dictionary<string, List<string>>(data, StringComparer.OrdinalIgnoreCase);
				}
			}
			catch
			{
				// Si el archivo estuviese corrupto, reiniciar con una colección vacía en memoria
				_cache = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
			}
		}

		/// <summary>
		/// Guarda el estado actual de la caché de memoria en el archivo de disco o lo elimina si está vacía.
		/// </summary>
		public static void Save()
		{
			try
			{
				// Si no hay entradas en memoria, eliminar el archivo en disco si existe
				if (_cache == null || _cache.Count == 0)
				{
					if (File.Exists(_cacheFilePath))
					{
						File.Delete(_cacheFilePath);
					}
					return;
				}

				// Convertir la colección en memoria a formato JSON
				string json = JsonSerializer.Serialize(_cache, _jsonOptions);

				// Escribir en el archivo de caché en disco de forma atómica
				File.WriteAllText(_cacheFilePath, json);
			}
			catch
			{
				// Ignorar fallos de escritura o borrado para no interrumpir el flujo principal
			}
		}

		/// <summary>
		/// Obtiene la lista de IPs de Cloudflare cacheadas para un nombre de dominio determinado.
		/// </summary>
		/// <param name="domain">Nombre completo del dominio o subdominio.</param>
		/// <returns>Lista de IPs conocidas, o null si no existe registro previo.</returns>
		public static List<string> GetIps(string domain)
		{
			// Validar que el dominio no sea nulo ni vacío
			if (string.IsNullOrWhiteSpace(domain))
			{
				return null;
			}

			// Buscar en la caché en memoria y retornar si tiene entradas válidas
			if (_cache.TryGetValue(domain, out var ips) && ips != null && ips.Count > 0)
			{
				return ips;
			}

			// No se encontraron IPs registradas previamente para este dominio
			return null;
		}

		/// <summary>
		/// Actualiza las IPs de Cloudflare asociadas a un dominio y persiste los cambios en disco.
		/// </summary>
		/// <param name="domain">Nombre completo del dominio o subdominio.</param>
		/// <param name="ips">Colección de direcciones IP detectadas.</param>
		public static void SetIps(string domain, List<string> ips)
		{
			// Validar que el dominio y las IPs sean válidos
			if (string.IsNullOrWhiteSpace(domain) || ips == null || ips.Count == 0)
			{
				return;
			}

			// Guardar en la colección en memoria
			_cache[domain] = ips;

			// Persistir inmediatamente en el archivo de disco para tolerar reinicios imprevistos
			Save();
		}

		/// <summary>
		/// Elimina las IPs de Cloudflare asociadas a un dominio y actualiza o elimina el archivo en disco.
		/// </summary>
		/// <param name="domain">Nombre completo del dominio o subdominio.</param>
		public static void RemoveIps(string domain)
		{
			// Validar que el dominio no sea nulo ni vacío
			if (string.IsNullOrWhiteSpace(domain))
			{
				return;
			}

			// Si el dominio existe en la colección, eliminarlo y persistir cambios
			if (_cache.Remove(domain))
			{
				Save();
			}
		}
	}
}
