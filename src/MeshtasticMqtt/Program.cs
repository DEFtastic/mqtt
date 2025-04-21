namespace MeshtasticMqtt;

using MeshtasticMqtt;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

public class Program
{
    public static async Task Main(string[] args)
    {
        await Host.CreateDefaultBuilder(args)
            .ConfigureServices((hostContext, services) =>
            {
                services.AddHostedService<MqttServerManager>();
                services.AddSingleton<ClientDatabase>();
            })
            .RunConsoleAsync();
    }
}