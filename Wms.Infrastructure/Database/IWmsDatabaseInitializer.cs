namespace Wms.Infrastructure.Database;

public interface IWmsDatabaseInitializer
{
    Task InitializeAsync(WmsSeedProfile profile, CancellationToken cancellationToken = default);
}
