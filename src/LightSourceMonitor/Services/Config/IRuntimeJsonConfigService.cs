using LightSourceMonitor.Models;

namespace LightSourceMonitor.Services.Config;

/// <summary>
/// 运行时通用配置（<c>config/*.json</c>），与 SQLite 中的趋势/告警数据分离。
/// </summary>
public interface IRuntimeJsonConfigService
{
    Task<AcquisitionConfig> LoadAcquisitionAsync(CancellationToken cancellationToken = default);
    Task SaveAcquisitionAsync(AcquisitionConfig config, CancellationToken cancellationToken = default);

    Task<EmailConfig> LoadEmailAsync(CancellationToken cancellationToken = default);
    Task SaveEmailAsync(EmailConfig config, CancellationToken cancellationToken = default);

    Task<TmsConfig> LoadTmsAsync(CancellationToken cancellationToken = default);
    Task SaveTmsAsync(TmsConfig config, CancellationToken cancellationToken = default);
}
