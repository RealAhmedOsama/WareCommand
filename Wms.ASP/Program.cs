using Wms.Application.DependencyInjection;
using Wms.Infrastructure.Database;
using Wms.Infrastructure.DependencyInjection;

namespace Wms.ASP;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var databaseProvider = WmsDatabaseProviderParser.Parse(
            builder.Configuration["Wms:DatabaseProvider"]);
        var seedProfile = WmsSeedProfileResolver.Resolve(
            builder.Configuration["Wms:SeedProfile"],
            builder.Environment.IsDevelopment());

        builder.Services.AddControllersWithViews();
        builder.Services.AddWmsInfrastructure(
            builder.Configuration.GetConnectionString("DefaultConnection"),
            databaseProvider);
        builder.Services.AddWmsApplication();

        var app = builder.Build();

        await InitializeDatabaseAsync(app.Services, seedProfile);

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Home/Error");
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthorization();

        app.MapControllerRoute(
            "default",
            "{controller=Dashboard}/{action=Index}/{id?}");

        app.Run();
    }

    private static async Task InitializeDatabaseAsync(
        IServiceProvider services,
        WmsSeedProfile seedProfile)
    {
        await using var scope = services.CreateAsyncScope();
        var initializer = scope.ServiceProvider.GetRequiredService<IWmsDatabaseInitializer>();
        await initializer.InitializeAsync(seedProfile);
    }
}
