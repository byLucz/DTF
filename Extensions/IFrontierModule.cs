using System.Threading.Tasks;

namespace DiscordTelegramFrontier
{
    public interface IFrontierModule
    {
        Task<bool> HandleAsync(FrontierUpdateContext context);
    }

    public delegate Task<bool> FrontierUpdateDelegate(FrontierUpdateContext context);

    public interface IFrontierMiddleware
    {
        Task<bool> InvokeAsync(FrontierUpdateContext context, FrontierUpdateDelegate next);
    }
}
