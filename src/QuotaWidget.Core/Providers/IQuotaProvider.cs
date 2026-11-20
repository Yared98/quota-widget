using QuotaWidget.Config;
using QuotaWidget.Model;

namespace QuotaWidget.Providers;

public interface IQuotaProvider
{
    Task<QuotaSnapshot> ReadAsync(ProviderConfig cfg, DateTimeOffset now, CancellationToken ct);
}
