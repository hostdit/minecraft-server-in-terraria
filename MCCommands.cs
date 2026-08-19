using System.IO;
using Terraria.ModLoader;

namespace MinecraftServer
{
    public class MCStartCommand : ModCommand
    {
        public override CommandType Type => CommandType.Chat;
        public override string Command => "mcstart";
        public override string Description => "Start the Minecraft server";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            if (MCSystem.Server == null)
            {
                caller.Reply("server object missing");
                return;
            }
            if (MCSystem.Server.Start())
                caller.Reply("mcserver listening on 25565");
            else
                caller.Reply("already running, or bind failed: " + MCSystem.Server.LastError);
        }
    }

    public class MCStopCommand : ModCommand
    {
        public override CommandType Type => CommandType.Chat;
        public override string Command => "mcstop";
        public override string Description => "Stop the Minecraft server";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            MCSystem.Server?.Stop();
            caller.Reply("mcserver stopped");
        }
    }

    public class MCStatusCommand : ModCommand
    {
        public override CommandType Type => CommandType.Chat;
        public override string Command => "mcstatus";
        public override string Description => "Minecraft server status";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            var server = MCSystem.Server;
            if (server == null || !server.Running)
            {
                caller.Reply("stopped");
                return;
            }
            caller.Reply($"running  player={(string.IsNullOrEmpty(server.Username) ? "-" : server.Username)}  playing={server.Playing}");
            caller.Reply($"packets in {server.PacketsIn}  out {server.PacketsOut}");
            if (!string.IsNullOrEmpty(server.LastError))
                caller.Reply("last error: " + server.LastError);
        }
    }

    public class MCFaviconCommand : ModCommand
    {
        public override CommandType Type => CommandType.Chat;
        public override string Command => "mcfavicon";
        public override string Description => "Reload favicon.png and show where it is looked for";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            caller.Reply(MCServer.LoadFavicon());
            caller.Reply("checked, in order:");
            foreach (string path in MCServer.FaviconPaths())
            {
                caller.Reply((File.Exists(path) ? "  found  " : "  missing ") + path);
            }
            caller.Reply("must be exactly 64x64 or the client ignores it");
        }
    }
}