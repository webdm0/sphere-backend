namespace SphereBackend.Services
{
    public class CleanupService : BackgroundService
    {
        private readonly IServiceProvider _services;

        public CleanupService(IServiceProvider services)
        {
            _services = services;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _services.CreateScope();
                    var cleanupService = scope.ServiceProvider.GetRequiredService<IAppCleanupService>();
                    await cleanupService.CleanupAsync(stoppingToken);
                }
                catch (Exception)
                {
                }

                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
    }
}
