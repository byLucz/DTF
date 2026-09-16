# DTF

DiscordTelegramFrontier (DTF) - enables Discord.Net commands to be reused and executed through Telegram.Bot.

## How it works

DTF builds a proxy context with real Discord client/guild/user objects, resolves and validates the command through Discord.Net, then executes its existing logic. The proxy channel renders replies for Telegram.

```
TG update → FrontierService → command resolution → CommandInfo.ExecuteAsync
          → command calls ReplyAsync(embed) → FrontierProxyChannel → Telegram.Bot.SendMessage
```

## Wiring (host)

```csharp
services.AddFrontier(o =>
{
    o.TelegramToken = "<bot token>";
    o.Chat(telegramChatId, discordGuildId);
    o.User(telegramUserId, discordUserId);
    o.ErrorHandler = ex => Console.Error.WriteLine(ex);
});

await provider.GetRequiredService<FrontierService>().StartAsync();
```

`[Frontier]` sends text; `[FrontierAsImage]` sends PNG with cached Discord emoji and works on its own. `ModifyAsync` updates the same message. Image mode uses static emoji frames and renders embed images as links; Linux needs fontconfig and suitable fonts.

Mark a command:
```csharp
[Frontier]
[Command("weather")]
public async Task Weather(params string[] city) { ... await ReplyAsync(embed: e.Build()); }
```

## Limitations (MVP)

- Only text/data commands that reply via `ReplyAsync` / `Context.Channel.SendMessageAsync` are portable.
- Not supported: voice/music, reactions, interactive-button callbacks, commands that cast the channel to `SocketTextChannel`.
- Mappings are optional. Stateless commands (e.g. an API lookup) work in any chat with no mapping. Commands that read `Context.Guild` need a chat→guild map (or `DefaultGuild`); commands with permission checks (`Context.User`) need the Telegram user linked to a Discord id — otherwise `Guild`/`User` are null and such commands degrade or deny.

## Stack

- net10.0, Discord.Net 3.20.1, Telegram.Bot 22.6.0.
