using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.BackgroundJobs;

/// <summary>
/// Tác vụ "Hệ thống" 4.4.1, 4.4.2.1, 4.4.2.2 (DEC-50): mỗi phút tổng hợp chứng từ có thuốc liên thông và gửi gói tin đang chờ.
/// Mất mạng thì gói tin nằm chờ trong IntegrationLogs, bán hàng vẫn bình thường; có mạng lại thì tự gửi (SYS-11).
/// </summary>
public sealed class DqgSyncWorker : BackgroundService
{
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<DqgSyncWorker> _logger;

    public DqgSyncWorker(IServiceScopeFactory scopes, ILogger<DqgSyncWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartDelay, stoppingToken);
            using var timer = new PeriodicTimer(Interval);
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Ứng dụng đang dừng
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var sync = scope.ServiceProvider.GetRequiredService<IDqgSyncService>();
            var result = await sync.RunAsync(ct);
            if (result.Queued + result.Sent + result.Failed > 0)
                _logger.LogInformation("Liên thông DQG: tổng hợp {Queued}, gửi được {Sent}, lỗi {Failed}, đang chờ {Waiting}, có mạng: {Online}",
                    result.Queued, result.Sent, result.Failed, result.Waiting, result.Online);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Lỗi một lần chạy (ví dụ CSDL tạm không kết nối được) không làm dừng tác vụ; lần sau chạy lại.
            _logger.LogError(ex, "Liên thông DQG: lần chạy bị lỗi");
        }
    }
}
