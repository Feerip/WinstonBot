﻿using Discord;
using WinstonBot.Data;
using WinstonBot.Attributes;
using Microsoft.Extensions.Logging;
using Discord.WebSocket;
using System.Text;

namespace WinstonBot.Commands.HostPvm
{
    internal static class Helpers
    {
        public static int GetUserCount(IEnumerable<RuntimeRole> roles)
        {
            var users = new HashSet<ulong>();
            foreach (RuntimeRole role in roles)
            {
                users = users.Concat(role.Users).ToHashSet();
            }
            return users.Count;
        }

        public static void BuildSignup(IEnumerable<RuntimeRole> roles, BossData.Entry entry, SocketGuild guild, DateTimeOffset? displayTimestamp, out Embed embed, out MessageComponent component)
        {
            var builder = new EmbedBuilder()
                .WithTitle(entry.PrettyName)
                .WithThumbnailUrl(entry.IconUrl)
                .WithColor(entry.EmbedColor);
            if (displayTimestamp != null)
            {
                builder.WithTimestamp(displayTimestamp.Value);
            }

            var componentBuilder = new ComponentBuilder();
            foreach (RuntimeRole role in roles)
            {
                string value = "Empty";
                if (role.Users.Count > 0)
                {
                    value = String.Join(' ', role.Users.Select(id => guild.GetUser(id).Mention));
                }
                builder.AddField($"{role.Definition.Emoji} {role.Definition.Name}", value, inline: true);

                componentBuilder.WithButton(new ButtonBuilder()
                    .WithEmote(new Emoji(role.Definition.Emoji))
                    .WithStyle(ButtonStyle.Secondary)
                    .WithCustomId($"{ChooseRoleAction.Name}_{(int)entry.Id}_{(int)role.Definition.RoleType}"));
            }

            var stringBuilder = new StringBuilder();
            if (displayTimestamp != null)
            {
                stringBuilder.Append($"Starts {TimestampTag.FromDateTime(displayTimestamp.Value.UtcDateTime, TimestampTagStyles.Relative)}");
                stringBuilder.AppendLine();
            }

            int userCount = GetUserCount(roles);
            stringBuilder.Append($"{userCount}/{entry.MaxPlayersOnTeam} Signed up");

            builder.WithDescription(stringBuilder.ToString());

            componentBuilder.WithButton(emote: new Emoji("✅"), customId: $"{CompleteTeamAction.Name}_{(int)entry.Id}", style: ButtonStyle.Success);
            componentBuilder.WithButton(emote: new Emoji("📝"), customId: ListTeamAction.Name, style: ButtonStyle.Primary);

            embed = builder.Build();
            component = componentBuilder.Build();
        }

        public static bool CanAddUserToRole(ulong id, RaidRole role, IEnumerable<RuntimeRole> roles, out List<RaidRole> conflictingRoles)
        {
            var rolesForUser = roles
                .Where(runtimeRole => runtimeRole.HasUser(id))
                .Select(runtimeRole => runtimeRole.Definition.RoleType);

            conflictingRoles = new();
            if (!rolesForUser.Any())
            {
                return true;
            }

            var roleMatrixRowIndex = (int)role;
            foreach (RaidRole existingRole in rolesForUser)
            {
                if (!Data.RoleCompatibilityMatrix[roleMatrixRowIndex, (int)existingRole])
                {
                    conflictingRoles.Add(existingRole);
                }
            }

            return conflictingRoles.Count == 0;
        }

        public static RuntimeRole[] GetRuntimeRoles(Embed? embed = null)
        {
            var roles = new RuntimeRole[Data.Roles.Length];
            for (int i = 0; i < Data.Roles.Length; ++i)
            {
                List<ulong>? users = null;
                if (embed != null)
                {
                    var field = embed.Fields.ElementAt(i);
                    if (field.Value != "Empty")
                    {
                        users = field.Value.Split(' ').Select(mention => Utility.GetUserIdFromMention(mention)).ToList();
                    }
                }

                roles[i] = new RuntimeRole(Data.Roles[i], users);
            }
            return roles;
        }
    }
}
