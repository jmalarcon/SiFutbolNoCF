using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SiFutbolNoCF.Models;
using SiFutbolNoCF.Models.Notifications;
using SiFutbolNoCF.Services.Notifications;

namespace SiFutbolNoCF.Services
{
	/// <summary>
	/// Servicio estático especializado en orquestar la sincronización, evaluación y conmutación de proxies en Cloudflare.
	/// </summary>
	/// <remarks>
	/// Centraliza la lógica de negocio del dominio: detección de bloqueos, gestión de caché de IPs,
	/// actualización de registros DNS en Cloudflare y cálculo de intervalos adaptativos.
	/// Permanece completamente desacoplado de la interfaz de consola.
	/// </remarks>
	public static class ProxySyncService
	{
		/// <summary>
		/// Resuelve y asigna automáticamente los identificadores de zona de Cloudflare para aquellos dominios que no lo tengan configurado.
		/// </summary>
		/// <param name="domains">Lista de configuraciones de dominio a procesar.</param>
		/// <param name="cfApiToken">Token de autorización con permisos de lectura de zonas.</param>
		/// <returns>Lista con los resultados de la detección para cada dominio procesado.</returns>
		public static async Task<List<ZoneDetectionResult>> ResolveZoneIdsAsync(List<DomainConfig> domains, string cfApiToken)
		{
			var results = new List<ZoneDetectionResult>();

			if (domains == null || domains.Count == 0)
			{
				return results;
			}

			// Iterar por cada dominio configurado para comprobar si requiere auto-detección de ID de zona
			foreach (var dom in domains)
			{
				// Si el dominio ya tiene un ID de zona explícito, omitir la consulta
				if (!string.IsNullOrEmpty(dom.CfZoneId))
				{
					continue;
				}

				string record = string.IsNullOrEmpty(dom.record) ? "@" : dom.record;
				string fullname = (record == "@") ? dom.name : $"{record}.{dom.name}";

				var detection = new ZoneDetectionResult
				{
					DomainName = dom.name,
					Fullname = fullname
				};

				try
				{
					// Consultar la API de Cloudflare para obtener el ID de la zona raíz
					string resolvedZoneId = await CloudflareService.FetchZoneIdAsync(dom.name, cfApiToken);
					dom.CfZoneId = resolvedZoneId;
					detection.ZoneId = resolvedZoneId;
					detection.Success = true;
				}
				catch (Exception ex)
				{
					detection.Success = false;
					detection.ErrorMessage = ex.Message;
				}

				results.Add(detection);
			}

			return results;
		}

		/// <summary>
		/// Evalúa el estado de bloqueo y sincroniza el proxy de Cloudflare para un dominio individual.
		/// </summary>
		/// <param name="dom">Configuración del dominio a procesar.</param>
		/// <param name="cfApiToken">Token de autorización de Cloudflare.</param>
		/// <param name="blockedIps">Conjunto de direcciones IP con bloqueo activo en este ciclo.</param>
		/// <returns>Resultado detallado de la sincronización del dominio.</returns>
		public static async Task<DomainSyncResult> ProcessDomainAsync(DomainConfig dom, string cfApiToken, HashSet<string> blockedIps)
		{
			string record = string.IsNullOrEmpty(dom.record) ? "@" : dom.record;
			string type = string.IsNullOrEmpty(dom.type) ? "A" : dom.type;
			string fullname = (record == "@") ? dom.name : $"{record}.{dom.name}";

			var result = new DomainSyncResult
			{
				Domain = dom,
				Record = record,
				RecordType = type,
				Fullname = fullname
			};

			// 1. Consultar el estado actual del registro DNS en Cloudflare
			DnsRecord currentRecord;
			try
			{
				currentRecord = await CloudflareService.FetchDnsRecordAsync(dom.name, record, type, cfApiToken, dom.CfZoneId);
			}
			catch (Exception ex)
			{
				result.Status = DomainSyncStatus.Error;
				result.ErrorMessage = $"Error al consultar Cloudflare: {ex.Message}";
				return result;
			}

			// Si el registro no existe en la zona, notificar y terminar procesamiento del dominio
			if (currentRecord == null)
			{
				result.Status = DomainSyncStatus.DnsRecordNotFound;
				result.ErrorMessage = $"Registro {fullname} no encontrado en Cloudflare.";
				return result;
			}

			// 2. Evaluar el estado del proxy y determinar si está bloqueado
			bool currentProxied = currentRecord.proxied;
			result.PreviousProxied = currentProxied;
			result.OriginIp = currentRecord.content;

			bool desiredProxy = true;
			string statusLine = string.Empty;
			List<string> relevantIps = new List<string>();
			bool isBlocked = false;

			if (currentProxied)
			{
				// ESCENARIO 1: El proxy está activo en Cloudflare (nube naranja).
				// El DNS público resuelve a las IPs de la CDN de Cloudflare.
				var resolvedIps = await DnsResolverService.ResolveHostIpsAsync(fullname);
				relevantIps = resolvedIps;

				if (resolvedIps.Count > 0)
				{
					// Comprobar si alguna de las IPs de Cloudflare está incluida en la lista de bloqueadas
					isBlocked = resolvedIps.Any(ip => blockedIps.Contains(ip));
					if (isBlocked)
					{
						// Hay bloqueo activo: persistir IPs en caché antes de desactivar el proxy
						IpCacheService.SetIps(fullname, resolvedIps);
						desiredProxy = false;
						statusLine = $"🔴 Estado: BLOQUEADO (IPs CF: {string.Join(", ", resolvedIps)}). Estado proxy deseado: DESACTIVAR.";
					}
					else
					{
						// No hay bloqueo: asegurar que la caché esté limpia y mantener proxy activado
						IpCacheService.RemoveIps(fullname);
						desiredProxy = true;
						statusLine = $"✅ Estado: no bloqueado (IPs CF: {string.Join(", ", resolvedIps)}). Estado proxy deseado: ACTIVAR.";
					}
				}
				else
				{
					// Si falló la resolución DNS, conservar el estado actual para evitar modificaciones no deseadas
					statusLine = $"⚠️ No se pudieron resolver IPs por DNS para {fullname}, se mantendrá el estado actual.";
					desiredProxy = currentProxied;
					result.Status = DomainSyncStatus.DnsResolutionFailed;
				}
			}
			else
			{
				// ESCENARIO 2: El proxy está desactivado (nube gris).
				// Consultamos las IPs de Cloudflare persistidas en la caché de disco.
				var cachedIps = IpCacheService.GetIps(fullname);
				if (cachedIps != null && cachedIps.Count > 0)
				{
					relevantIps = cachedIps;

					// Comprobar si las IPs de Cloudflare correspondientes continúan bloqueadas
					isBlocked = cachedIps.Any(ip => blockedIps.Contains(ip));
					if (isBlocked)
					{
						// Bloqueo continuo: mantener proxy desactivado
						desiredProxy = false;
						statusLine = $"🔴 Estado: BLOQUEO ACTIVO en Cloudflare (IPs CF: {string.Join(", ", cachedIps)}). Estado proxy deseado: DESACTIVAR.";
					}
					else
					{
						// Bloqueo finalizado: reactivar proxy de Cloudflare
						desiredProxy = true;
						statusLine = $"✅ Estado: Cloudflare libre de bloqueos (IPs CF: {string.Join(", ", cachedIps)}). Estado proxy deseado: ACTIVAR.";
					}
				}
				else
				{
					// Arranque en frío sin historial en caché: resolver el host por DNS directamente
					var resolvedIps = await DnsResolverService.ResolveHostIpsAsync(fullname);
					relevantIps = resolvedIps;
					isBlocked = resolvedIps.Count > 0 && resolvedIps.Any(ip => blockedIps.Contains(ip));

					if (isBlocked)
					{
						desiredProxy = false;
						statusLine = $"🔴 Estado: BLOQUEADO (IPs: {string.Join(", ", resolvedIps)}). Estado proxy deseado: DESACTIVAR.";
					}
					else
					{
						desiredProxy = true;
						statusLine = "✅ Estado: no bloqueado. Estado proxy deseado: ACTIVAR.";
					}
				}
			}

			result.DesiredProxied = desiredProxy;
			result.RelevantIps = relevantIps;
			result.StatusLine = statusLine;
			result.IsBlocked = isBlocked;

			// 3. Aplicar actualización en Cloudflare si el estado deseado difiere del actual
			try
			{
				bool updated = await CloudflareService.ApplyDnsRecordUpdateAsync(
					dom.name, record, type, currentRecord, desiredProxy, cfApiToken, dom.CfZoneId);

				if (updated)
				{
					// Si se reactivó el proxy con éxito, eliminar las IPs de la caché
					if (desiredProxy)
					{
						IpCacheService.RemoveIps(fullname);
					}

					result.Status = DomainSyncStatus.Updated;
					result.ChangeInfo = new DomainChangeInfo
					{
						Domain = dom.name,
						Record = record,
						Fullname = fullname,
						RecordType = type,
						PreviousProxied = currentRecord.proxied,
						NewProxied = desiredProxy,
						OriginIp = currentRecord.content,
						CloudflareIps = relevantIps ?? new List<string>(),
						Reason = statusLine
					};
				}
				else
				{
					result.Status = DomainSyncStatus.NoChange;
				}
			}
			catch (Exception ex)
			{
				result.Status = DomainSyncStatus.Error;
				result.ErrorMessage = $"Error al actualizar Cloudflare: {ex.Message}";
			}

			return result;
		}

		/// <summary>
		/// Ejecuta un ciclo completo de sincronización de dominios, descarga de IPs bloqueadas y envío de alertas.
		/// </summary>
		/// <param name="config">Configuración resuelta de la aplicación.</param>
		/// <param name="notificationService">Instancia opcional del servicio de notificaciones.</param>
		/// <returns>Resultado consolidado del ciclo de comprobación.</returns>
		public static async Task<CycleSyncResult> ExecuteCycleAsync(AppSettings config, NotificationService notificationService = null)
		{
			var cycleResult = new CycleSyncResult();

			// 1. Descargar el estado de IPs bloqueadas desde el endpoint oficial
			try
			{
				cycleResult.BlockedIps = await FootballStatusService.FetchBlockedIpsAsync(config.StatusUrl);
			}
			catch (Exception ex)
			{
				cycleResult.BlockedIpsError = ex.Message;
				cycleResult.BlockedIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			}

			// 2. Procesar de forma secuencial cada uno de los dominios configurados
			if (config.Domains != null)
			{
				foreach (var dom in config.Domains)
				{
					var domResult = await ProcessDomainAsync(dom, config.CfApiToken, cycleResult.BlockedIps);
					cycleResult.DomainResults.Add(domResult);

					// Si este dominio tiene un bloqueo activo, marcar la bandera global del ciclo
					if (domResult.IsBlocked)
					{
						cycleResult.AnyDomainBlocked = true;
					}

					// Si se aplicó un cambio de proxy, agregarlo a la lista de cambios del ciclo
					if (domResult.Status == DomainSyncStatus.Updated && domResult.ChangeInfo != null)
					{
						cycleResult.Changes.Add(domResult.ChangeInfo);
					}
				}
			}

			// 3. Enviar notificación consolidada si ocurrieron cambios de proxy en el ciclo
			if (cycleResult.Changes.Count > 0 && notificationService != null && notificationService.HasEnabledProviders)
			{
				try
				{
					cycleResult.NotificationResults = await notificationService.SendBatchNotificationAsync(cycleResult.Changes);
				}
				catch
				{
					// Evitar que fallos en proveedores de notificación interrumpan el resultado del ciclo
				}
			}

			return cycleResult;
		}

		/// <summary>
		/// Ejecuta una conmutación directa e inmediata de proxy en Cloudflare para un registro específico (modo one-off).
		/// </summary>
		/// <param name="domain">Dominio raíz en Cloudflare (ej. 'ejemplo.com').</param>
		/// <param name="record">Registro o subdominio (ej. 'www' o '@').</param>
		/// <param name="type">Tipo de registro DNS (ej. 'A', 'CNAME').</param>
		/// <param name="activateCfProxy">Estado deseado del proxy de Cloudflare.</param>
		/// <param name="apiToken">Token de autorización con permisos de edición DNS.</param>
		/// <param name="zoneId">ID de zona de Cloudflare.</param>
		/// <param name="notificationService">Instancia opcional del servicio de notificaciones.</param>
		/// <returns>Resultado estructurado de la operación manual.</returns>
		public static async Task<OneOffSyncResult> ExecuteOneOffAsync(
			string domain,
			string record,
			string type,
			bool activateCfProxy,
			string apiToken,
			string zoneId,
			NotificationService notificationService = null)
		{
			string fullname = (string.IsNullOrEmpty(record) || record == "@") ? domain : $"{record}.{domain}";

			var result = new OneOffSyncResult
			{
				Fullname = fullname,
				RecordType = type
			};

			try
			{
				// 1. Consultar el registro existente en Cloudflare
				var currentRecord = await CloudflareService.FetchDnsRecordAsync(domain, record, type, apiToken, zoneId);
				if (currentRecord == null)
				{
					result.Success = false;
					result.ErrorMessage = "Registro no encontrado en Cloudflare.";
					return result;
				}

				result.PreviousProxied = currentRecord.proxied;
				result.NewProxied = activateCfProxy;
				result.OriginIp = currentRecord.content;

				// 2. Aplicar la actualización en Cloudflare
				bool updated = await CloudflareService.ApplyDnsRecordUpdateAsync(
					domain, record, type, currentRecord, activateCfProxy, apiToken, zoneId);

				result.Updated = updated;
				result.Success = true;

				// 3. Si se actualizó y se activó el proxy manualmente, limpiar la caché local
				if (updated)
				{
					if (activateCfProxy)
					{
						IpCacheService.RemoveIps(fullname);
					}

					// 4. Intentar enviar notificación si hay proveedores disponibles
					if (notificationService != null && notificationService.HasEnabledProviders)
					{
						try
						{
							var oneOffChanges = new List<DomainChangeInfo>
							{
								new DomainChangeInfo
								{
									Domain = domain,
									Record = record,
									Fullname = fullname,
									RecordType = type,
									PreviousProxied = currentRecord.proxied,
									NewProxied = activateCfProxy,
									OriginIp = currentRecord.content,
									CloudflareIps = new List<string>(),
									Reason = "Ejecución manual One-off"
								}
							};

							result.NotificationResults = await notificationService.SendBatchNotificationAsync(oneOffChanges);
						}
						catch
						{
							// Ignorar excepciones al enviar alertas en modo directo
						}
					}
				}
			}
			catch (Exception ex)
			{
				result.Success = false;
				result.ErrorMessage = ex.Message;
			}

			return result;
		}

		/// <summary>
		/// Calcula el número de segundos de espera para el siguiente ciclo según la hora y el estado de bloqueo.
		/// </summary>
		/// <param name="isAdaptive">Indica si el modo adaptativo está activado.</param>
		/// <param name="baseIntervalSeconds">Intervalo base configurado en segundos.</param>
		/// <param name="isBlocked">Indica si hay al menos un dominio bloqueado activamente.</param>
		/// <param name="blockStartTime">Momento en el que se inició el bloqueo activo, o null si no hay bloqueo.</param>
		/// <returns>Estructura con los segundos calculados y el motivo explicativo.</returns>
		public static DelayInfo CalculateNextDelay(bool isAdaptive, int baseIntervalSeconds, bool isBlocked, DateTime? blockStartTime)
		{
			// Si el modo adaptativo no está activo, usar siempre el intervalo base fijo
			if (!isAdaptive)
			{
				return new DelayInfo
				{
					DelaySeconds = baseIntervalSeconds,
					Reason = "Intervalo fijo"
				};
			}

			// Caso 1: Hay un bloqueo activo (partido de fútbol en curso)
			if (isBlocked && blockStartTime.HasValue)
			{
				// Calcular los minutos transcurridos desde que se detectó el inicio del bloqueo
				double minutesSinceBlock = (DateTime.Now - blockStartTime.Value).TotalMinutes;

				// Si lleva menos de 100 minutos bloqueado, aplicar pausa larga (los partidos duran más de 105 minutos)
				if (minutesSinceBlock < 100)
				{
					return new DelayInfo
					{
						DelaySeconds = 100 * 60, // 6000 segundos
						Reason = "Bloqueo activo (partido en curso, pausa de 100 min)"
					};
				}

				// Superados los 100 minutos de bloqueo, volver a intervalo corto para detectar el fin del partido
				return new DelayInfo
				{
					DelaySeconds = baseIntervalSeconds,
					Reason = "Bloqueo prolongado (> 100 min, comprobación frecuente)"
				};
			}

			// Caso 2: No hay bloqueo activo. Evaluar la franja horaria local
			DateTime now = DateTime.Now;
			int hour = now.Hour;

			// Franja valle: de 01:00 a 14:00 (no hay partidos de fútbol en directo antes de las 14:00)
			if (hour >= 1 && hour < 14)
			{
				// Calcular la hora objetivo de las 14:00 de hoy
				DateTime targetTime = new DateTime(now.Year, now.Month, now.Day, 14, 0, 0, now.Kind);
				double secondsUntilTarget = (targetTime - now).TotalSeconds;

				if (secondsUntilTarget > 0)
				{
					int waitSeconds = (int)secondsUntilTarget;
					int waitMinutes = waitSeconds / 60;
					int waitHours = waitMinutes / 60;
					return new DelayInfo
					{
						DelaySeconds = Math.Max(waitSeconds, baseIntervalSeconds),
						Reason = $"Franja valle (01:00 - 14:00, pausa directa hasta las 14:00: {waitHours}h {waitMinutes % 60}m)"
					};
				}
			}

			// Franja activa: de 14:00 a 01:00 (horario habitual de emisión de partidos)
			return new DelayInfo
			{
				DelaySeconds = baseIntervalSeconds,
				Reason = "Franja activa (14:00 - 01:00, comprobación frecuente)"
			};
		}
	}
}
