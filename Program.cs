﻿using Discord;
using Discord.Commands;
using Discord.Addons.Hosting;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Reflection;
using WinstonBot;
using WinstonBot.Services;
using Microsoft.Extensions.Configuration;

public class Program
{
    public static void Main(string[] args)
        => new Program().MainAsync().GetAwaiter().GetResult();

    public async Task MainAsync()
    {
        IHostBuilder builder = Host.CreateDefaultBuilder()
            .ConfigureHostConfiguration(config => config.AddEnvironmentVariables())
            .ConfigureAppConfiguration((context, configuration) =>
            {
                configuration
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddEnvironmentVariables()
                    .AddJsonFile(Path.Combine("Config", "appsettings.json"), false, true)
                    .AddJsonFile(Path.Combine("Config", $"appsettings.{context.HostingEnvironment.EnvironmentName}.json"), false, true)
                    .Build();
            })
            .ConfigureLogging(logging =>
            {
                logging
                .AddConsole()
                .AddFile("winstonbot.log", append:false)
                .SetMinimumLevel(LogLevel.Trace);
            })
            .ConfigureDiscordHost((context, config) =>
            {
                config.SocketConfig = new DiscordSocketConfig()
                {
                    MessageCacheSize = 1000,
                    LargeThreshold = 250,
                    GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.GuildMembers,
                    LogLevel = LogSeverity.Info,
                    AlwaysDownloadUsers = true,
                    MaxWaitBetweenGuildAvailablesBeforeReady = 2000
                };

                config.Token = context.Configuration["token"];
            })
            .ConfigureServices((context, services) =>
            {
                services
                //.AddSingleton<InteractionService>() // Not used for now
                .AddSingleton<MessageDatabase>()
                //.AddSingleton<EmoteDatabase>() // Deprecated for now
                .AddSingleton<ConfigService>()
                .AddHostedService<CommandHandler>()
                .AddSingleton<CommandScheduler>()
                .AddHostedService<ScheduledCommandService>()
                .AddSingleton<AoDDatabase>()
                .AddHostedService<DBService>()
                .AddHostedService<StatusService>()
                .AddHostedService<BlavikenService>()
                .AddSingleton<WatchCatDB>()
                .AddHostedService<WatchCat>() // depends on WatchCatDB
                .AddSingleton<EventControlDB>()
                .AddSingleton<EventControl>() // depends on EventControlDB
                .AddHostedService<EventControlService>() // depends on EventControlDB, EventControl
                //.AddHostedService<FruitWarsDxpLeaderboardService>()
                ;
            })
            .UseConsoleLifetime();

        using IHost host = builder.Build();

        var programLog = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger<Program>();

        programLog.LogInformation("Starting Winston Bot");

        try
        {
            await host.RunAsync();
        }
        catch (Exception ex)
        {
            programLog.LogError($"Program error: {ex}");
        }

        Environment.ExitCode = 1;
    }
}