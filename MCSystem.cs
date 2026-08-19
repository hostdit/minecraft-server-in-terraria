using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace MinecraftServer
{
    public class MCSystem : ModSystem
    {
        public static MCServer Server;

        private static int _originX;
        private static int _originY;
        private static bool _originSet;

        private const int GroundY = 4;
        private const int RangeMinX = -32;
        private const int RangeMaxX = 47;
        private const int RangeMinY = 4;
        private const int RangeMaxY = 44;

        private readonly Dictionary<(int, int), int> _fromMinecraft = new Dictionary<(int, int), int>();
        private readonly Dictionary<(int, int), int> _snapshot = new Dictionary<(int, int), int>();
        private int _tick;

        private static readonly int[] MirrorPalette =
        {
            1, 4, 5, 12, 17, 20, 24, 35, 41, 42, 45, 49, 57, 89, 155, 133
        };

        public override void Load()
        {
            Server = new MCServer();
        }

        public override void Unload()
        {
            Server?.Stop();
            Server = null;
        }

        public override void OnWorldLoad()
        {
            _originSet = false;
            _fromMinecraft.Clear();
            _snapshot.Clear();
        }

        public override void OnWorldUnload()
        {
            Server?.Stop();
            _originSet = false;
        }

        public override void PostUpdateWorld()
        {
            if (Server == null) return;

            if (!_originSet && Main.LocalPlayer != null && Main.LocalPlayer.active)
            {
                _originX = (int)(Main.LocalPlayer.Center.X / 16f);
                _originY = (int)(Main.LocalPlayer.Center.Y / 16f);
                _originSet = true;
                Mod.Logger.Info($"origin set to tile {_originX},{_originY}");
            }

            DrainEvents();

            _tick++;
            if (_tick % 6 == 0) MirrorTilesToMinecraft();
        }

        private static int TileX(int blockX) => _originX + blockX;
        private static int TileY(int blockY) => _originY - (blockY - GroundY);
        private static int BlockX(int tileX) => tileX - _originX;
        private static int BlockY(int tileY) => GroundY - (tileY - _originY);

        private static int TileTypeFor(int blockId)
        {
            switch (blockId)
            {
                case 1: return TileID.Stone;
                case 2: return TileID.Grass;
                case 3: return TileID.Dirt;
                case 4: return TileID.GrayBrick;
                case 5: return TileID.WoodBlock;
                case 12: return TileID.Sand;
                case 17: return TileID.LivingWood;
                case 20: return TileID.Glass;
                case 24: return TileID.Sandstone;
                case 35: return TileID.Cloud;
                case 41: return TileID.GoldBrick;
                case 42: return TileID.IronBrick;
                case 45: return TileID.RedBrick;
                case 49: return TileID.Obsidian;
                case 57: return TileID.DiamondGemspark;
                case 89: return TileID.MartianConduitPlating;
                default: return TileID.Stone;
            }
        }

        private static int BlockIdFor(int tileType)
        {
            switch (tileType)
            {
                case TileID.Stone: return 1;
                case TileID.Grass: return 2;
                case TileID.Dirt: return 3;
                case TileID.GrayBrick: return 4;
                case TileID.WoodBlock: return 5;
                case TileID.Sand: return 12;
                case TileID.Glass: return 20;
                case TileID.Sandstone: return 24;
                case TileID.Cloud: return 35;
                case TileID.GoldBrick: return 41;
                case TileID.IronBrick: return 42;
                case TileID.RedBrick: return 45;
                case TileID.Obsidian: return 49;
                default:
                    int index = Math.Abs(tileType) % MirrorPalette.Length;
                    return MirrorPalette[index];
            }
        }

        private void DrainEvents()
        {
            int handled = 0;
            while (handled < 64 && Server.Events.TryDequeue(out MCEvent e))
            {
                handled++;
                try
                {
                    switch (e.Kind)
                    {
                        case MCEventKind.Joined:
                            Main.NewText(e.Text + " connected", Color.LightGreen);
                            break;
                        case MCEventKind.Left:
                            Main.NewText(e.Text + " disconnected", Color.Orange);
                            break;
                        case MCEventKind.Chat:
                            Main.NewText("<mc> " + e.Text, Color.White);
                            break;
                        case MCEventKind.Status:
                            Mod.Logger.Info(e.Text);
                            break;
                        case MCEventKind.Placed:
                            PlaceFromMinecraft(e);
                            break;
                        case MCEventKind.Broke:
                            BreakFromMinecraft(e);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Mod.Logger.Warn("event error: " + ex.Message);
                }
            }
        }

        private void PlaceFromMinecraft(MCEvent e)
        {
            if (!_originSet) return;
            if (e.BlockX < RangeMinX || e.BlockX > RangeMaxX) return;
            if (e.BlockY < RangeMinY || e.BlockY > RangeMaxY) return;

            int x = TileX(e.BlockX);
            int y = TileY(e.BlockY);
            if (!WorldGen.InWorld(x, y, 10)) return;

            int type = TileTypeFor(e.BlockId);
            WorldGen.PlaceTile(x, y, type, true, true);
            _fromMinecraft[(x, y)] = e.BlockId;
            _snapshot[(x, y)] = type;

            if (Main.netMode != NetmodeID.SinglePlayer)
                NetMessage.SendTileSquare(-1, x, y, 1);
        }

        private void BreakFromMinecraft(MCEvent e)
        {
            if (!_originSet) return;
            int x = TileX(e.BlockX);
            int y = TileY(e.BlockY);
            if (!WorldGen.InWorld(x, y, 10)) return;
            if (!_fromMinecraft.ContainsKey((x, y))) return;

            WorldGen.KillTile(x, y, false, false, true);
            _fromMinecraft.Remove((x, y));
            _snapshot.Remove((x, y));

            if (Main.netMode != NetmodeID.SinglePlayer)
                NetMessage.SendTileSquare(-1, x, y, 1);
        }

        private void MirrorTilesToMinecraft()
        {
            if (!_originSet || Server == null || !Server.Playing) return;

            var current = new Dictionary<(int, int), int>();

            for (int bx = RangeMinX; bx <= RangeMaxX; bx++)
            {
                for (int by = RangeMinY; by <= RangeMaxY; by++)
                {
                    int x = TileX(bx);
                    int y = TileY(by);
                    if (!WorldGen.InWorld(x, y, 10)) continue;

                    Tile tile = Main.tile[x, y];
                    if (tile.HasTile) current[(x, y)] = tile.TileType;
                }
            }

            foreach (var entry in current)
            {
                if (_snapshot.TryGetValue(entry.Key, out int previous) && previous == entry.Value)
                    continue;
                int bx = BlockX(entry.Key.Item1);
                int by = BlockY(entry.Key.Item2);
                Server.SendBlockChange(bx, by, 0, BlockIdFor(entry.Value));
            }

            foreach (var entry in _snapshot)
            {
                if (current.ContainsKey(entry.Key)) continue;
                int bx = BlockX(entry.Key.Item1);
                int by = BlockY(entry.Key.Item2);
                Server.SendBlockChange(bx, by, 0, 0);
            }

            _snapshot.Clear();
            foreach (var entry in current) _snapshot[entry.Key] = entry.Value;
        }
    }
}
