using System;

namespace DiscordTelegramFrontier
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class FrontierAttribute : Attribute
    {
    }
}
