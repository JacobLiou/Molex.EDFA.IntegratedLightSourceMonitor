using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LightSourceMonitor.Models;
using Microsoft.Extensions.Logging;

namespace LightSourceMonitor.Services.Config;

public sealed class RuntimeJsonConfigService : IRuntimeJsonConfigService
{
    public const string AcquisitionFileName = "AcquisitionConfig.json";
    public const string EmailFileName = "EmailConfig.json";
    public const string TmsFileName = "TmsConfig.json";
    public const string UiFileName = "UiConfig.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly ILogger<RuntimeJsonConfigService> _logger;
    private readonly string _configDirectory;
    private readonly SemaphoreSlim _ioLock = new(1, 1);

    public RuntimeJsonConfigService(ILogger<RuntimeJsonConfigService> logger)
    {
        _logger = logger;
        _configDirectory = Path.Combine(AppContext.BaseDirectory, "config");
    }

    private string PathFor(string fileName) => Path.Combine(_configDirectory, fileName);

    private async Task EnsureConfigDirectoryAsync(CancellationToken cancellationToken)
    {
        await Task.Run(() => Directory.CreateDirectory(_configDirectory), cancellationToken);
    }

    private async Task<T> ReadAsync<T>(string fileName, Func<T> factory, CancellationToken cancellationToken)
        where T : class, new()
    {
        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            await EnsureConfigDirectoryAsync(cancellationToken);
            var path = PathFor(fileName);
            if (!File.Exists(path))
            {
                _logger.LogInformation("Config file missing, using defaults: {Path}", path);
                return factory();
            }

            await using var stream = File.OpenRead(path);
            var model = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
            return model ?? factory();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read config {File}, using defaults", fileName);
            return factory();
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private async Task WriteAsync<T>(string fileName, T value, CancellationToken cancellationToken)
    {
        await _ioLock.WaitAsync(cancellationToken);
        var path = PathFor(fileName);
        var tmp = path + ".tmp";
        try
        {
            await EnsureConfigDirectoryAsync(cancellationToken);
            await using (var stream = File.Create(tmp))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (File.Exists(path))
                File.Replace(tmp, path, null);
            else
                File.Move(tmp, path);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* ignore */ }
            throw;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public Task<AcquisitionConfig> LoadAcquisitionAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(AcquisitionFileName, () => new AcquisitionConfig(), cancellationToken);

    public Task SaveAcquisitionAsync(AcquisitionConfig config, CancellationToken cancellationToken = default) =>
        WriteAsync(AcquisitionFileName, config, cancellationToken);

    public Task<EmailConfig> LoadEmailAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(EmailFileName, () => new EmailConfig(), cancellationToken);

    public Task SaveEmailAsync(EmailConfig config, CancellationToken cancellationToken = default) =>
        WriteAsync(EmailFileName, config, cancellationToken);

    public Task<TmsConfig> LoadTmsAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(TmsFileName, () => new TmsConfig(), cancellationToken);

    public Task SaveTmsAsync(TmsConfig config, CancellationToken cancellationToken = default) =>
        WriteAsync(TmsFileName, config, cancellationToken);

    public async Task<UiConfig> LoadUiAsync(CancellationToken cancellationToken = default)
    {
        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            await EnsureConfigDirectoryAsync(cancellationToken);
            var path = PathFor(UiFileName);
            if (!File.Exists(path))
            {
                _logger.LogInformation("Config file missing, using defaults: {Path}", path);
                return new UiConfig();
            }

            string json;
            await using (var stream = File.OpenRead(path))
            using (var reader = new StreamReader(stream))
                json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            var model = JsonSerializer.Deserialize<UiConfig>(json, JsonOptions) ?? new UiConfig();
            using (var doc = JsonDocument.Parse(json))
            {
                if (!doc.RootElement.TryGetProperty("languageSetByUser", out _))
                    model.LanguageSetByUser = true;
            }

            return model;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read config {File}, using defaults", UiFileName);
            return new UiConfig();
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public Task SaveUiAsync(UiConfig config, CancellationToken cancellationToken = default) =>
        WriteAsync(UiFileName, config, cancellationToken);
}
