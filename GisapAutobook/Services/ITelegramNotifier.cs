namespace GisapAutobook.Services;

public interface ITelegramNotifier
{
    Task NotifyAsync(string htmlMessage, CancellationToken ct = default);
}
