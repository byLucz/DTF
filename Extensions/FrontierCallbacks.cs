using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;

namespace DiscordTelegramFrontier
{
    public sealed class FrontierStaleCallbackException : Exception
    {
        public FrontierStaleCallbackException(Exception inner) : base("Telegram callback query is too old.", inner) { }
    }

    internal static class FrontierCallbacks
    {
        public static bool IsStale(Exception ex)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (current is FrontierStaleCallbackException)
                    return true;
                if (current is ApiRequestException { ErrorCode: 400 } api
                    && (api.Message.Contains("query is too old", StringComparison.OrdinalIgnoreCase)
                        || api.Message.Contains("query ID is invalid", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
            return false;
        }

        public static async Task<bool> AnswerAsync(ITelegramBotClient bot, string callbackId, string text, bool alert, CancellationToken cancellationToken)
        {
            try
            {
                await bot.AnswerCallbackQuery(callbackId, text, alert, cancellationToken: cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (ApiRequestException ex) when (IsStale(ex))
            {
                return false;
            }
        }
    }
}
