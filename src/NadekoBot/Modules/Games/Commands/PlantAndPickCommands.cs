using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Attributes;
using NadekoBot.Extensions;
using NadekoBot.Services;
using NadekoBot.Services.Database.Models;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using System.Collections.Immutable;

namespace NadekoBot.Modules.Games
{
    public partial class Games
    {
        /// <summary>
        /// Flower picking/planting idea is given to me by its
        /// inceptor Violent Crumble from Game Developers League discord server
        /// (he has !cookie and !nom) Thanks a lot Violent!
        /// Check out GDL (its a growing gamedev community):
        /// https://discord.gg/0TYNJfCU4De7YIk8
        /// </summary>
        [Group]
        public class PlantPickCommands : NadekoSubmodule
        {
            private static ConcurrentHashSet<ulong> generationChannels { get; }
            //channelid/message
            private static ConcurrentDictionary<ulong, List<IUserMessage>> plantedFlowers { get; } = new ConcurrentDictionary<ulong, List<IUserMessage>>();
            //channelId/last generation
            private static ConcurrentDictionary<ulong, DateTime> lastGenerations { get; } = new ConcurrentDictionary<ulong, DateTime>();

            private static ConcurrentHashSet<ulong> usersRecentlyPicked { get; } = new ConcurrentHashSet<ulong>();

            private static string lastMessagesFilePath = "data/lastMessages.xml";
            private static ConcurrentDictionary<ulong, DateTime> lastMessages { get; set; } = new ConcurrentDictionary<ulong, DateTime>();
            private static bool messageUpdate = false;

            private static Logger _log { get; }

            static PlantPickCommands()
            {

#if !GLOBAL_NADEKO
                NadekoBot.Client.MessageReceived += PotentialFlowerGeneration;
#endif
                generationChannels = new ConcurrentHashSet<ulong>(NadekoBot.AllGuildConfigs
                    .SelectMany(c => c.GenerateCurrencyChannelIds.Select(obj => obj.ChannelId)));

                if (File.Exists(lastMessagesFilePath))
                {
                    FileStream lastMessagesFile = new FileStream(lastMessagesFilePath, FileMode.OpenOrCreate);
                    var xElem = XElement.Load(lastMessagesFile);
                    Dictionary<ulong, DateTime> tempDic = xElem.Descendants("item")
                                        .ToDictionary(x => (ulong)x.Attribute("id"), x => (DateTime)x.Attribute("value"));

                    foreach (KeyValuePair<ulong, DateTime> entry in tempDic)
                    {
                        lastMessages.AddOrUpdate(entry.Key, entry.Value, (id, old) => entry.Value);
                    }
                    //ConcurrentDictionary<ulong, DateTime> lastMessages = new ConcurrentDictionary<ulong, DateTime>(tempDic);

                    lastMessagesFile.Dispose();
                }
                
                var saveToFileTimer = new System.Threading.Timer(async (e) =>
                {
                    await SaveToXML();
                }, null, 0, Convert.ToInt32(TimeSpan.FromMinutes(5).TotalMilliseconds));
            }

            public static async Task SaveToXML()
            {
                while (true)
                {
                    if (messageUpdate)
                    {
                        XElement xElem = new XElement(
                                        "items",
                                        lastMessages.Select(x => new XElement("item", new XAttribute("id", x.Key), new XAttribute("value", x.Value)))
                                     );
                        var xml = xElem.ToString();

                        if (!File.Exists(lastMessagesFilePath))
                        {
                            File.WriteAllText(lastMessagesFilePath, "");
                        }

                        FileStream lastMessagesFile = new FileStream(lastMessagesFilePath, FileMode.Truncate);
                        xElem.Save(lastMessagesFile, SaveOptions.None);
                        lastMessagesFile.Flush();
                        lastMessagesFile.Dispose();

                        messageUpdate = false;
                    }

                    await Task.Delay(Convert.ToInt32(TimeSpan.FromMinutes(5).TotalMilliseconds));
                }
            }

            private static Task PotentialFlowerGeneration(SocketMessage imsg)
            {
                var msg = imsg as SocketUserMessage;
                if (msg == null || msg.IsAuthor() || msg.Author.IsBot)
                    return Task.CompletedTask;

                var channel = imsg.Channel as ITextChannel;
                if (channel == null)
                    return Task.CompletedTask;

                var dailyGen = Task.Run(async () =>
                {
                    try
                    {
                        var lastMessage = lastMessages.GetOrAdd(msg.Author.Id, DateTime.MinValue);
                        if (DateTime.Today > lastMessage)
                        {
                            int awardAmount = 3;

                            await CurrencyHandler.AddCurrencyAsync(msg.Author, "Daily participation", awardAmount, false).ConfigureAwait(false);
                            await msg.Author.SendConfirmAsync("Hey! Thanks for interacting with the server today!", "You've been awarded " + awardAmount + " " + NadekoBot.BotConfig.CurrencySign + " for your participation!").ConfigureAwait(false);

                            lastMessages.AddOrUpdate(msg.Author.Id, DateTime.Now, (id, old) => DateTime.Now);
                            messageUpdate = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.Warn(ex);
                    }
                });

                if (!generationChannels.Contains(channel.Id))
                    return Task.CompletedTask;

                var _ = Task.Run(async () =>
                {
                    try
                    {
                        var lastGeneration = lastGenerations.GetOrAdd(channel.Id, DateTime.MinValue);
                        var rng = new NadekoRandom();

                        //todo i'm stupid :rofl: wtg kwoth. real async programming :100: :ok_hand: :100: :100: :thumbsup:
                        if (DateTime.Now - TimeSpan.FromSeconds(NadekoBot.BotConfig.CurrencyGenerationCooldown) < lastGeneration) //recently generated in this channel, don't generate again
                            return;

                        var num = rng.Next(1, 101) + NadekoBot.BotConfig.CurrencyGenerationChance * 100;

                        if (num > 100)
                        {
                            lastGenerations.AddOrUpdate(channel.Id, DateTime.Now, (id, old) => DateTime.Now);

                            var dropAmount = NadekoBot.BotConfig.CurrencyDropAmount;

                            if (dropAmount > 0)
                            {
                                var msgs = new IUserMessage[dropAmount];
                                var prefix = NadekoBot.ModulePrefixes[typeof(Games).Name];
                                var toSend = dropAmount == 1 
                                    ? GetLocalText(channel, "curgen_sn", NadekoBot.BotConfig.CurrencySign, prefix)
                                    : GetLocalText(channel, "curgen_pl", dropAmount, NadekoBot.BotConfig.CurrencySign, prefix);
                                var file = GetRandomCurrencyImage();
                                using (var fileStream = file.Value.ToStream())
                                {
                                    var sent = await channel.SendFileAsync(
                                        fileStream,
                                        file.Key,
                                        toSend).ConfigureAwait(false);

                                    msgs[0] = sent;
                                }

                                plantedFlowers.AddOrUpdate(channel.Id, msgs.ToList(), (id, old) => { old.AddRange(msgs); return old; });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogManager.GetCurrentClassLogger().Warn(ex);
                    }
                });
                return Task.CompletedTask;
            }

            public static string GetLocalText(ITextChannel channel, string key, params object[] replacements) =>
                NadekoTopLevelModule.GetTextStatic(key,
                    NadekoBot.Localization.GetCultureInfo(channel.GuildId),
                    typeof(Games).Name.ToLowerInvariant(),
                    replacements);

            [NadekoCommand, Usage, Description, Aliases]
            [RequireContext(ContextType.Guild)]
            public async Task Pick()
            {
                var channel = (ITextChannel)Context.Channel;

                if (!(await channel.Guild.GetCurrentUserAsync()).GetPermissions(channel).ManageMessages)
                    return;

                List<IUserMessage> msgs;

                try { await Context.Message.DeleteAsync().ConfigureAwait(false); } catch { }
                if (!plantedFlowers.TryRemove(channel.Id, out msgs))
                    return;

                await Task.WhenAll(msgs.Where(m => m != null).Select(toDelete => toDelete.DeleteAsync())).ConfigureAwait(false);

                await CurrencyHandler.AddCurrencyAsync((IGuildUser)Context.User, $"Picked {NadekoBot.BotConfig.CurrencyPluralName}", msgs.Count, false).ConfigureAwait(false);
                var msg = await ReplyConfirmLocalized("picked", msgs.Count + NadekoBot.BotConfig.CurrencySign)
                    .ConfigureAwait(false);
                msg.DeleteAfter(10);
            }

            [NadekoCommand, Usage, Description, Aliases]
            [RequireContext(ContextType.Guild)]
            public async Task Plant(int amount = 1)
            {
                if (amount < 1)
                    return;

                var removed = await CurrencyHandler.RemoveCurrencyAsync((IGuildUser)Context.User, $"Planted a {NadekoBot.BotConfig.CurrencyName}", amount, false).ConfigureAwait(false);
                if (!removed)
                {
                    await ReplyErrorLocalized("not_enough", NadekoBot.BotConfig.CurrencySign).ConfigureAwait(false);
                    return;
                }

                var imgData = GetRandomCurrencyImage();

                //todo upload all currency images to transfer.sh and use that one as cdn
                //and then 

                var msgToSend = GetText("planted",
                    Format.Bold(Context.User.ToString()), 
                    amount + NadekoBot.BotConfig.CurrencySign, 
                    Prefix);

                IUserMessage msg;
                using (var toSend = imgData.Value.ToStream())
                {
                    msg = await Context.Channel.SendFileAsync(toSend, imgData.Key, msgToSend).ConfigureAwait(false);
                }

                var msgs = new IUserMessage[amount];
                msgs[0] = msg;

                plantedFlowers.AddOrUpdate(Context.Channel.Id, msgs.ToList(), (id, old) =>
                {
                    old.AddRange(msgs);
                    return old;
                });
            }

            [NadekoCommand, Usage, Description, Aliases]
            [RequireContext(ContextType.Guild)]
            [RequireUserPermission(GuildPermission.ManageMessages)]
            public async Task GenCurrency()
            {
                var channel = (ITextChannel)Context.Channel;

                bool enabled;
                using (var uow = DbHandler.UnitOfWork())
                {
                    var guildConfig = uow.GuildConfigs.For(channel.Id, set => set.Include(gc => gc.GenerateCurrencyChannelIds));

                    var toAdd = new GCChannelId() { ChannelId = channel.Id };
                    if (!guildConfig.GenerateCurrencyChannelIds.Contains(toAdd))
                    {
                        guildConfig.GenerateCurrencyChannelIds.Add(toAdd);
                        generationChannels.Add(channel.Id);
                        enabled = true;
                    }
                    else
                    {
                        guildConfig.GenerateCurrencyChannelIds.Remove(toAdd);
                        generationChannels.TryRemove(channel.Id);
                        enabled = false;
                    }
                    await uow.CompleteAsync();
                }
                if (enabled)
                {
                    await ReplyConfirmLocalized("curgen_enabled").ConfigureAwait(false);
                }
                else
                {
                    await ReplyConfirmLocalized("curgen_disabled").ConfigureAwait(false);
                }
            }

            private static KeyValuePair<string, ImmutableArray<byte>> GetRandomCurrencyImage()
            {
                var rng = new NadekoRandom();
                var images = NadekoBot.Images.Currency;

                return images[rng.Next(0, images.Length)];
            }
        }
    }
}