using Microsoft.Extensions.Hosting;

namespace EventBus.HostedServices;

public class EventBusHostedService : IHostedService
{
    private readonly Func<Task> _subscribeAction;

    public EventBusHostedService(Func<Task> subscribeAction)
    {
        _subscribeAction = subscribeAction;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _subscribeAction();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
