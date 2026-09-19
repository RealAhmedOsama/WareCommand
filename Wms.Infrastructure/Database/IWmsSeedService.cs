namespace Wms.Infrastructure.Database;

public interface IWmsSeedService
{
    Task SeedAsync(WmsSeedProfile profile, CancellationToken cancellationToken = default);
}
