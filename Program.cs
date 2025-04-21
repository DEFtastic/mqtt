// Program.cs
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

await Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        services.AddSingleton<MqttServerManager>();
        services.AddSingleton<ClientDatabase>();
    })
    .RunConsoleAsync();