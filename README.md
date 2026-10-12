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

commandService.AddFrontierUserReaders();
await commandService.AddModulesAsync(assembly, provider);

await provider.GetRequiredService<FrontierService>().StartAsync();
```

`AddFrontierUserReaders()` must run before `AddModulesAsync`, otherwise `IUser` parameters keep the default Discord reader.

In groups commands are accepted only with `/` (`/weather London`); in private chats plain text works too. Set `o.GroupsRequireSlash = false` to accept plain text everywhere.

`[Frontier]` sends text; `[FrontierAsImage]` sends PNG with cached Discord emoji and works on its own. `ModifyAsync` updates the same message. Linux needs fontconfig and suitable fonts for image mode.

Text mode sends the embed image as a Telegram photo, or as an animation for `.gif`/`.mp4`/`.webm`; if Telegram cannot fetch it, the link is appended to the text. Custom Discord emoji are dropped, short inline fields are joined into one line.

Image mode draws the embed image and thumbnail into the PNG (falls back to the link if the download fails), inline fields are laid out in up to 3 columns like in Discord, emoji use static frames.

Both modes translate Discord formatting in messages and embeds: emphasis, links, code, spoilers, quotes, lists and headings. PNG spoilers stay masked and links are visual only; Telegram text uses bold headings and italic subtext.

Mark a command:
```csharp
[Frontier]
[Command("weather")]
public async Task Weather(params string[] city) { ... await ReplyAsync(embed: e.Build()); }
```

## Buttons

`[Frontier(Buttons = true)]` (or `[FrontierAsImage(Buttons = true)]`) renders message components as a Telegram inline keyboard. A press runs the matching `[ComponentInteraction]` handler through the host `InteractionService`, the same handler Discord uses.

```csharp
[Frontier(Buttons = true)]
[Command("panel")]
public Task Panel() => ReplyAsync(embed: e, components: new ComponentBuilder().WithButton("Refresh", "refresh").Build());

public class PanelInteractions : InteractionModuleBase<IInteractionContext>
{
    [ComponentInteraction("refresh")]
    public async Task Refresh()
    {
        await DeferAsync();
        await ModifyOriginalResponseAsync(m => m.Embed = Build());
    }
}
```

Requirements:
- Register `InteractionService` in DI. Without it buttons are not rendered and nothing else changes.
- Handlers that should work in Telegram must be in modules typed `InteractionModuleBase<IInteractionContext>`, not `SocketInteractionContext`, and must not cast the interaction to `SocketMessageComponent`; use `IComponentInteraction`. Discord passes `SocketInteractionContext`, which also implements `IInteractionContext`, so the same module keeps working there.

Mapping:
- Buttons become callback buttons, link buttons become URL buttons, disabled buttons stay visible and do nothing.
- A string select menu becomes one button per option; the press passes that option as the selected value.
- `DeferAsync` closes the Telegram loading state. `RespondAsync` and `FollowupAsync` send a new message, `UpdateAsync` and `ModifyOriginalResponseAsync` edit the message, components included. Messages sent from a handler can have buttons too.
- `ephemeral` replies are sent to the chat as regular messages; Telegram groups have no private replies.
- Failures without a host reply are shown as a Telegram alert: precondition reason, "no handler" or a generic error.

Callback data is a short `dtf:` key mapped to the Discord `custom_id`, so the 64-byte Telegram limit does not apply to it. Keys live in memory for `FrontierOptions.ButtonLifetime` (1 hour by default, extended on every press) and are dropped on restart; a press on an expired key answers that the button is stale. Callback data without the `dtf:` prefix is left to other modules.

Proxy messages get process-unique IDs with the top bit set, so they never collide with each other across chats or with Discord snowflakes when the host keys state by message ID.

## Users

`IUser` parameters accept only Telegram mentions in Telegram (`@username` or a mention of a user without username); anything else fails with a readable reason. A linked Telegram user resolves to the Discord user, otherwise to `FrontierUser` with the Telegram name. Usernames are matched against recently seen chat members.

`Context.User` stays `null` for unlinked senders, use `Context.Sender()` to get the author on both platforms.

Parse, precondition and not-found failures are sent to the chat with their reason and are not reported to `ErrorHandler`; real exceptions are. Repeated polling errors are reported once per 10 minutes.

## Modules

Telegram modules implement `IFrontierModule` and register through `AddFrontierModule<T>()`; pass update types for buttons or other events. `AddFrontierMiddleware<T>()` adds shared checks. Modules are created only for matching updates, within one DI scope per update. Full Telegram.Bot access remains available through the context.

`FrontierService.Bot` is the shared Telegram client for sends outside an update (notifications, alerts). It is created once from `TelegramToken`, or taken from DI when an `ITelegramBotClient` is registered, and retries `429 Too Many Requests` up to `FrontierOptions.RetryCount` times (3 by default).

Use `ProcessUpdateAsync` with an external receiver, or `EnableDiscordCommands = false` without Discord services. DTF rendering and edits are available to modules through `CreateReplyChannel()`.

## Limitations (MVP)

- Only text/data commands that reply via `ReplyAsync` / `Context.Channel.SendMessageAsync` are portable.
- Not supported: voice/music, reactions, modals, user/role/channel select menus, commands that cast the channel to `SocketTextChannel`.
- Mappings are optional. Stateless commands (e.g. an API lookup) work in any chat with no mapping. Commands that read `Context.Guild` need a chat→guild map (or `DefaultGuild`); commands with permission checks (`Context.User`) need the Telegram user linked to a Discord id, otherwise `Guild`/`User` are null and such commands degrade or deny.
- Discord mentions, roles, channels and `<t:...>` timestamps in replies are not converted yet.

## Stack

- net10.0, Discord.Net 3.20.1, Telegram.Bot 22.6.0.

Package version: `1.3.0`. Run `dotnet pack DTF.csproj -c Release` from the DTF directory to create `.nupkg` and `.snupkg` in `artifacts/`; this does not publish them.
