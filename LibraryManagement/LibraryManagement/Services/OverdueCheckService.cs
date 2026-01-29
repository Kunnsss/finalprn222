// Services/OverdueCheckService.cs - Background Service for checking overdue rentals
using LibraryManagement.Services;

namespace LibraryManagement.Services
{
    public class OverdueCheckService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<OverdueCheckService> _logger;

        public OverdueCheckService(IServiceProvider serviceProvider, ILogger<OverdueCheckService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var rentalService = scope.ServiceProvider.GetRequiredService<IRentalService>();
                    await rentalService.UpdateOverdueStatusAsync();

                    _logger.LogInformation("Overdue rentals checked at: {time}", DateTimeOffset.Now);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred checking overdue rentals");
                }

                // Check every 6 hours
                await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
            }
        }
    }
}
