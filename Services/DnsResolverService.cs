using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace SiFutbolNoCF.Services
{
	/// <summary>
	/// Servicio estático responsable de la resolución de nombres DNS para dominios y subdominios.
	/// </summary>
	/// <remarks>
	/// Resuelve recursivamente alias CNAME y registros A/AAAA hasta obtener la lista final de direcciones IP públicas.
	/// Está completamente desacoplado de la interfaz de usuario / consola.
	/// </remarks>
	public static class DnsResolverService
	{
		/// <summary>
		/// Resuelve las direcciones IP asociadas a un nombre de host utilizando la BCL estándar de .NET.
		/// </summary>
		/// <param name="host">Nombre de host o dominio completo a resolver (ej. 'ejemplo.com' o 'www.ejemplo.com').</param>
		/// <returns>Lista de direcciones IP en formato cadena, sin duplicados.</returns>
		public static async Task<List<string>> ResolveHostIpsAsync(string host)
		{
			// Validar que el nombre de host no sea nulo ni esté en blanco para evitar excepciones innecesarias
			if (string.IsNullOrWhiteSpace(host))
			{
				return new List<string>();
			}

			try
			{
				// Consultar la resolución DNS nativa del sistema operativo que sigue cadenas CNAME hasta llegar a las IPs
				IPAddress[] addresses = await Dns.GetHostAddressesAsync(host);

				// Convertir cada objeto IPAddress a cadena de texto y eliminar posibles duplicados devueltos por el resolver
				return addresses.Select(a => a.ToString()).Distinct().ToList();
			}
			catch (SocketException)
			{
				// Capturar fallo de socket cuando el host no existe en DNS o no responde, retornando lista vacía controlada
				return new List<string>();
			}
		}
	}
}
