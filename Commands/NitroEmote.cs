﻿using Microsoft.Extensions.Logging;
using WinstonBot.Attributes;

namespace WinstonBot.Commands
{
    [Command("do-emote", "Posts an animated emote for plebs without nitro.")]
    internal class NitroEmote : CommandBase
    {
        // TODO: can we inject services into providers?
        [CommandOption("emote", "The name of the emote")]
        public string EmoteName { get; set; }

        public NitroEmote(ILogger logger) : base(logger) { }

        public override async Task HandleCommand(CommandContext context)
        {
            var emote = Utility.TryGetEmote(context.Client, EmoteName);
            if (emote != null)
            {
                string animatedSymbol = emote.Animated ? "a" : string.Empty;
                string emoteString = $"<{animatedSymbol}:{emote.Name}:{emote.Id}>";
                await context.RespondAsync(emoteString);
            }
        }
    }
}
