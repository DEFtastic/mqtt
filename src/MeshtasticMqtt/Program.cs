// Program.cs
using MeshtasticMqtt;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

await Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        services.AddHostedService<MqttServerManager>();
        services.AddSingleton<ClientDatabase>();
    })
    .RunConsoleAsync();