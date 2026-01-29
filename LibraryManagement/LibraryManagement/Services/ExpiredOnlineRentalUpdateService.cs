// Services/ExpiredOnlineRentalUpdateService.cs
using LibraryManagement.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LibraryManagement.Services
{
    /// <summary>
    /// Background service tự động cập nhật status của Online Rentals đã hết hạn
    /// Chạy mỗi 1 giờ
    /// </summary>
    public class ExpiredOnlineRentalUpdateService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ExpiredOnlineRentalUpdateService> _logger;
        private readonly TimeSpan _checkInterval = TimeSpan.FromHours(1); // Kiểm tra mỗi 1 giờ

        public ExpiredOnlineRentalUpdateService(
            IServiceProvider serviceProvider,
            ILogger<ExpiredOnlineRentalUpdateService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ExpiredOnlineRentalUpdateService is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await UpdateExpiredRentals();
                    _logger.LogInformation("Expired online rentals check completed at: {time}", DateTimeOffset.Now);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while updating expired online rentals.");
                }

                // Chờ đến lần check tiếp theo
                await Task.Delay(_checkInterval, stoppingToken);
            }

            _logger.LogInformation("ExpiredOnlineRentalUpdateService is stopping.");
        }

        private async Task UpdateExpiredRentals()
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();

            var expiredRentals = await context.OnlineRentalTransactions
     .Where(o => o.Status == "Active"
         && o.ExpiryDate < DateTime.Now)
     .ToListAsync();

            if (expiredRentals.Any())
            {
                foreach (var rental in expiredRentals)
                {
                    rental.Status = "Expired";
                }

                var updateCount = await context.SaveChangesAsync();
                _logger.LogInformation("Updated {count} expired online rentals to Expired status.", updateCount);
            }
        }
    }
}