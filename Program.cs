﻿using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using WinstonBot;
using WinstonBot.Services;

public class Program
{
    private DiscordSocketClient _client;
    private CommandHandler _commandHandler;
    private EmoteDatabase _emoteDatabase;
    private ConfigService _configService;
    private IServiceProvider _services;

    public static void Main(string[] args)
        => new Program().MainAsync().GetAwaiter().GetResult();

    public IServiceProvider BuildServiceProvider() => new ServiceCollection()
        .AddSingleton(_client)
        .AddSingleton(_emoteDatabase)
        .AddSingleton(_configService)
        .BuildServiceProvider();

    public async Task MainAsync()
    {
        _client = new DiscordSocketClient(new DiscordSocketConfig()
        {
            MessageCacheSize = 1000
        });
        _client.Log += this.Log;

        var token = File.ReadAllText(Path.Combine("Config", "token.txt"));
        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();

        Console.WriteLine("after start");
        _emoteDatabase = new EmoteDatabase();

        _configService = new ConfigService(Path.Combine("Config", "config.json"));

        _client.Ready += ClientReady;

        _services = BuildServiceProvider();

        await Task.Delay(-1);
    }

    private async Task ClientReady()
    {
        Console.WriteLine("Client ready");

        _commandHandler = new CommandHandler(_services, _client);
        await _commandHandler.InstallCommandsAsync();
    }

    private Task Log(LogMessage msg)
    {
        Console.WriteLine(msg.ToString());
        return Task.CompletedTask;
    }
}