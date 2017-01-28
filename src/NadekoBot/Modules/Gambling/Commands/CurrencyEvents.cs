using Discord;
using Discord.Commands;
using NadekoBot.Attributes;
using NadekoBot.Extensions;
using NadekoBot.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NadekoBot.Modules.Gambling
{
    public partial class Gambling
    {
        [Group]
        public class CurrencyEvents : ModuleBase
        {
            public enum CurrencyEvent
            {
                Reaction
            }
            //flower reaction event
            public static readonly ConcurrentHashSet<ulong> _flowerReactionAwardedUsers = new ConcurrentHashSet<ulong>();

            [NadekoCommand, Usage, Description, Aliases]
            [RequireContext(ContextType.Guild)]
            [OwnerOnly]
            public async Task StartEvent(CurrencyEvent e)
            {
                var channel = (ITextChannel)Context.Channel;
                try
                {

                    switch (e)
                    {
                        case CurrencyEvent.Reaction:
                            await FlowerReactionEvent(Context).ConfigureAwait(false);
                            break;
                        default:
                            break;
                    }
                }
                catch { }
            }


            public static async Task FlowerReactionEvent(CommandContext Context)
            {
                var msg = await Context.Channel.SendConfirmAsync("Reaction event started!",
                    "Add 👌 reaction to this message to get 20" + NadekoBot.BotConfig.CurrencySign,
                    footer: "This event is active for 24 hours.")
                                               .ConfigureAwait(false);
                try { await msg.AddReactionAsync("👌").ConfigureAwait(false); }
                catch
                {
                    try { await msg.AddReactionAsync("👌").ConfigureAwait(false); }
                    catch
                    {
                        try { await msg.DeleteAsync().ConfigureAwait(false); }
                        catch { }
                    }
                }
                using (msg.OnReaction(async (r) =>
                 {
                     try
                     {
                         if (r.Emoji.Name == "👌" && r.User.IsSpecified && ((DateTime.UtcNow - r.User.Value.CreatedAt).TotalDays > 5) && _flowerReactionAwardedUsers.Add(r.User.Value.Id))
                         {
                             try { await CurrencyHandler.AddCurrencyAsync(r.User.Value, "Reaction Event", 20, false).ConfigureAwait(false); } catch { }
                         }
                     }
                     catch { }
                 }))
                {
                    await Task.Delay(TimeSpan.FromHours(24)).ConfigureAwait(false);
                    try { await msg.DeleteAsync().ConfigureAwait(false); } catch { }
                    _flowerReactionAwardedUsers.Clear();
                }
            }
        }
    }
}
