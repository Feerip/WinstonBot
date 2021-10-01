﻿using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using WinstonBot.Services;
using WinstonBot.Data;
using System.Diagnostics;
using WinstonBot.Commands;
using Discord.Net;
using Newtonsoft.Json;

namespace WinstonBot
{
    public class CommandHandler
    {
        private readonly DiscordSocketClient _client;
        private IServiceProvider _services;
        List<ICommand> _commands;

        // Retrieve client and CommandService instance via ctor
        public CommandHandler(IServiceProvider services, DiscordSocketClient client)
        {
            _client = client;
            _services = services;

            _commands = new List<ICommand>()
            {
                new HostPvmSignup()
            };

            // this is gross.
            // Could pass this into BuildCommand
            var configureCommand = new ConfigCommand(_commands);
            _commands.Add(configureCommand);
        }

        public async Task InstallCommandsAsync()
        {
            // Hook the MessageReceived event into our command handler
            _client.ButtonExecuted += HandleButtonExecuted;
            _client.InteractionCreated += HandleInteractionCreated;

            // Register the commands in all the guilds
            foreach (SocketGuild guild in _client.Guilds)
            {
                var registeredGuildCommands = await guild.GetApplicationCommandsAsync();
                bool anyNeedToBeRegisterd = _commands.Count != registeredGuildCommands.Count ||
                    _commands
                    .Where(cmd => registeredGuildCommands
                        .Where(regCmg => regCmg.Name != cmd.Name)
                        .Any())
                    .Any();

                if (anyNeedToBeRegisterd)
                {
                    try
                    {
                        await guild.DeleteApplicationCommandsAsync();

                        foreach (ICommand command in _commands)
                        {
                            // Check if this command is already registered.
                            if (!registeredGuildCommands.Where(cmd => cmd.Name == command.Name).Any())
                            {
                                await guild.CreateApplicationCommandAsync(command.BuildCommand());
                            }
                        }
                    }
                    catch (ApplicationCommandException ex)
                    {
                        // If our command was invalid, we should catch an ApplicationCommandException. This exception contains the path of the error as well as the error message. You can serialize the Error field in the exception to get a visual of where your error is.
                        var json = JsonConvert.SerializeObject(ex.Error, Formatting.Indented);

                        // You can send this error somewhere or just print it to the console, for this example we're just going to print it.
                        Console.WriteLine(json);
                    }
                }
            }
        }

        private bool UserHasRoleForCommand(SocketUser user, SocketRole requiredRole)
        {
            if (user is SocketGuildUser guildUser)
            {
                return guildUser.Roles.Contains(requiredRole);
            }
            return false;
        }

        private async Task HandleInteractionCreated(SocketInteraction arg)
        {
            if (arg is SocketSlashCommand slashCommand)
            {
                foreach (ICommand guildCommand in _commands)
                {
                    if (guildCommand.Name == slashCommand.Data.Name)
                    {
                        await guildCommand.HandleCommand(slashCommand);
                        return;
                    }
                }
            }
        }

        private async Task HandleButtonExecuted(SocketMessageComponent component)
        {
            foreach (ICommand guildCommand in _commands)
            {
                foreach (IAction action in guildCommand.Actions)
                {
                    if (component.Data.CustomId.StartsWith(action.Name))
                    {
                        // TODO: action could define params and we could parse them in the future.
                        // wouldn't work with the interface though.
                        await action.HandleAction(component);
                        return;
                    }
                }
            }
        }
    }
}
