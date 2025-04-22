namespace MeshtasticMqtt;

using MeshtasticMqtt;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;

public class Program
{
    public static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateLogger();

        try
        {
            Log.Information("Starting up");

            // ✨ Build manually instead of RunConsoleAsync
            var host = Host.CreateDefaultBuilder(args)
                .UseSerilog()
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<MqttServerManager>();
                    services.AddSingleton<ClientDatabase>();
                })
                .Build(); // ✨ Build it here

            // ✨ Initialize the database BEFORE running
            var clientDatabase = host.Services.GetRequiredService<ClientDatabase>();
            clientDatabase.InitializeDatabase();

            // ✨ Now run
            await host.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Host terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}