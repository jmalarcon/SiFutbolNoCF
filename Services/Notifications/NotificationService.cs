using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SiFutbolNoCF.Models.Notifications;

namespace SiFutbolNoCF.Services.Notifications
{
	/// <summary>
	/// Fachada y orquestador que coordina el envío de alertas a todos los canales de notificación configurados.
	/// </summary>
	/// <remarks>
	/// Inicializa los proveedores conocidos a partir del diccionario de configuración y despacha las alertas de forma concurrente.
	/// Totalmente desacoplado de la interfaz de usuario y consola.
	/// </remarks>
	public class NotificationService
	{
		// Lista de proveedores de notificación registrados en el servicio
		private readonly List<INotificationProvider> _providers = new List<INotificationProvider>();

		/// <summary>
		/// Indica si existe al menos un canal de notificación activo y configurado.
		/// </summary>
		public bool HasEnabledProviders => _providers.Any(p => p.IsEnabled);

		/// <summary>
		/// Inicializa el servicio registrando todos los proveedores disponibles a partir del diccionario de configuración global.
		/// </summary>
		/// <param name="notificationConfigs">Diccionario dinámico con los bloques de configuración de cada canal.</param>
		public NotificationService(Dictionary<string, JsonElement> notificationConfigs)
		{
			// Registrar el proveedor oficial de Telegram
			_providers.Add(new TelegramNotificationProvider(notificationConfigs));

			// Espacio reservado para registrar futuros proveedores:
			// _providers.Add(new DiscordNotificationProvider(notificationConfigs));
			// _providers.Add(new EmailNotificationProvider(notificationConfigs));
		}

		/// <summary>
		/// Inicializa el servicio con una colección específica de proveedores (útil para inyección polimórfica o pruebas unitarias).
		/// </summary>
		/// <param name="providers">Colección de proveedores a utilizar.</param>
		public NotificationService(IEnumerable<INotificationProvider> providers)
		{
			_providers = providers?.ToList() ?? new List<INotificationProvider>();
		}

		/// <summary>
		/// Envía un lote consolidado de cambios de dominios a todos los proveedores activos en paralelo.
		/// </summary>
		/// <param name="changes">Lista de cambios individuales ocurridos en el ciclo actual.</param>
		/// <returns>Lista de resultados obtenidos de cada proveedor de notificación.</returns>
		public async Task<List<NotificationResult>> SendBatchNotificationAsync(List<DomainChangeInfo> changes)
		{
			var results = new List<NotificationResult>();

			// Filtrar únicamente los proveedores que están habilitados y configurados
			var activeProviders = _providers.Where(p => p.IsEnabled).ToList();

			// Si no hay cambios o ningún proveedor está activo, retornar lista vacía
			if (changes == null || changes.Count == 0 || activeProviders.Count == 0)
			{
				return results;
			}

			// Crear el evento de lote con la fecha y hora UTC del momento
			var batchEvent = new NotificationBatchEvent
			{
				Changes = changes,
				TimestampUtc = DateTime.UtcNow
			};

			// Ejecutar el envío a todos los proveedores activos en paralelo para optimizar tiempos
			var sendTasks = activeProviders.Select(async provider =>
			{
				try
				{
					// Invocar el método de envío del proveedor concreto
					return await provider.SendAsync(batchEvent);
				}
				catch (Exception ex)
				{
					// Envolver excepciones imprevistas en un resultado fallido
					return new NotificationResult
					{
						ProviderName = provider.Name,
						Success = false,
						ErrorMessage = ex.Message
					};
				}
			});

			// Esperar la culminación de todos los envíos concurrentes
			var completedResults = await Task.WhenAll(sendTasks);
			results.AddRange(completedResults);

			return results;
		}
	}
}
