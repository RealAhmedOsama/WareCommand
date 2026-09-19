using Wms.Application.DependencyInjection;
using Wms.Infrastructure.Database;
using Wms.Infrastructure.DependencyInjection;

namespace Wms.ASP;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddControllersWithViews();
        builder.Services.AddWmsInfrastructure(builder.Configuration.GetConnectionString("DefaultConnection"));
        builder.Services.AddWmsApplication();

        var app = builder.Build();

        await InitializeDatabaseAsync(app.Services);

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

    private static async Task InitializeDatabaseAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var initializer = scope.ServiceProvider.GetRequiredService<IWmsDatabaseInitializer>();
        await initializer.InitializeAsync(WmsSeedProfile.WebDemo);
    }
}
