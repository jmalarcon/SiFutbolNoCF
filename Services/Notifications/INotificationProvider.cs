using System.Threading.Tasks;
using SiFutbolNoCF.Models.Notifications;

namespace SiFutbolNoCF.Services.Notifications
{
	/// <summary>
	/// Define el contrato que debe implementar cualquier canal o proveedor de alertas del sistema.
	/// </summary>
	public interface INotificationProvider
	{
		/// <summary>
		/// Nombre descriptivo del canal de notificación (ej. 'Telegram', 'Discord').
		/// </summary>
		string Name { get; }

		/// <summary>
		/// Indica si el canal de notificación cuenta con credenciales válidas y está habilitado.
		/// </summary>
		bool IsEnabled { get; }

		/// <summary>
		/// Envía un lote consolidado de cambios de estado a través del canal correspondiente.
		/// </summary>
		/// <param name="batchEvent">Evento agrupado con todos los cambios detectados en el ciclo.</param>
		/// <returns>Resultado con el estado de entrega y posibles mensajes de error.</returns>
		Task<NotificationResult> SendAsync(NotificationBatchEvent batchEvent);
	}
}
