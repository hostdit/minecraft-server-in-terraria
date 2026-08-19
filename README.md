# Minecraft server in Terraria

A working Minecraft 1.8.9 server implemented entirely as a tModLoader mod. Real client with a real protocol and no server software. The process listening on port 25565 is Terraria.

It runs both ways. Place a block in Minecraft and a tile appears in Terraria at the matching spot. Mine a tile in Terraria and the block disappears in Minecraft. Two worlds, one 3D and one 2D, editing each other in real time.

## Why

Tenth one. Excel, Outlook, OBS, Obsidian, Blender, PowerPoint, VLC, Word, The Sims 4, now Terraria.

Nobody has made Terraria speak the Minecraft protocol as far as I'm aware.

## What it does

- Server list ping with MOTD, player count and a favicon
- Offline-mode login, no encryption
- Flat world, 5x5 chunks of bedrock, dirt and grass
- Creative mode, so you can fly and place things
- Keep-alives
- **Minecraft to Terraria:** blocks you place become tiles, blocks you break remove them
- **Terraria to Minecraft:** tiles you place or mine are mirrored back as block changes
- A block type mapping in both directions, so stone stays stone and wood stays wood
- Chat commands for start, stop, status and favicon

## What it doesn't do

Almost everything else. No entities, no mobs, no real physics, no second player, no world saving on the Minecraft side.

Three limits specific to this build:

**Depth is flattened.** Terraria is 2D and Minecraft is 3D, so the mirror uses Minecraft's X and Y and ignores Z. Blocks at different depths land on the same tile, and tiles mirrored back always arrive at Z zero.

**Only a window is mirrored.** A region roughly 80 by 40 around your Terraria player, not the whole world.

**Terraria's own tile updates are not filtered.** Grass spreading or sand falling counts as a change and will be sent to Minecraft like anything else.

It's a server in the sense that a client connects to it and receives a world. Set your expectations accordingly.

## Requirements

Terraria and tModLoader, both on Steam. tModLoader is a free separate download if you own Terraria.

Minecraft Java 1.8.9, protocol 47.

Built and tested against **tModLoader v2026.6.3.6** on **Terraria v1.4.4.9**.

## Install

**1.** Clone into your ModSources folder. The path matters, tModLoader only looks here:

- macOS: `~/Library/Application Support/Terraria/tModLoader/ModSources/`
- Windows: `Documents\My Games\Terraria\tModLoader\ModSources\`

```
cd <ModSources>
git clone https://github.com/hostdit/minecraft-server-in-terraria MinecraftServer
```

The folder must be called `MinecraftServer` to match the namespace.

**2.** Install the **.NET 8.0 SDK**. tModLoader bundles the runtime for playing mods but not the compiler for building them. On macOS:

```
brew install --cask dotnet-sdk@8
```

**3.** Launch tModLoader, go to **Workshop > Develop Mods**, find MinecraftServer and click **Build + Reload**.

**4.** Load any world. In chat, press Enter and type:

```
/mcstart
```

**5.** In Minecraft 1.8.9: Multiplayer, Direct Connect, `localhost:25565`.

Optional: drop a 64x64 PNG named `favicon.png` in your Terraria save folder to set the server icon. Run `/mcfavicon` and it prints every path it checks, marked found or missing, so you don't have to guess.

### Commands

| Command | What it does |
| --- | --- |
| `/mcstart` | Bind port 25565 and start serving |
| `/mcstop` | Stop the server |
| `/mcstatus` | Player, connection state, packet counts, last error |
| `/mcfavicon` | Reload favicon.png and list the paths searched |

## How it works

tModLoader mods are C# assemblies loaded into Terraria's own process, so `System.Net.Sockets` gives you a TCP server.

**Threading.** The socket runs on a background thread with a blocking accept and read loop. Terraria's world state is not thread safe, so the socket thread only pushes events onto a `ConcurrentQueue`. `ModSystem.PostUpdateWorld` drains that queue on the game thread, where touching `Main.tile` is safe. Sends go behind a lock because the game thread also writes to the socket when mirroring tiles back.

**VarInts.** The protocol's variable width integer format, used for every packet length and ID.

**Chunks.** The reason this targets 1.8.9. In 1.8 a chunk section is a flat array of `(id << 4) | meta` shorts, then block light, then sky light. 12,544 bytes for one section plus biome data, sent uncompressed. Modern versions use palette-encoded, bit packed longs and expect zlib. The join sequence is 25 chunks, so 313,600 bytes in one burst.

**Coordinates.** Origin is wherever your Terraria player stands when the world loads. Minecraft X maps to Terraria X directly. Minecraft Y is inverted, because Terraria's Y axis grows downward and Minecraft's grows upward, so `tileY = originY - (blockY - 4)`.

**The mirror.** Every sixth game tick the mod walks the mapped region, reads `HasTile` and `TileType` for each position, and compares against a snapshot from the previous pass. Anything added, changed or removed becomes a clientbound `0x23` Block Change.

| Packet | Direction | What it does |
| --- | --- | --- |
| `0x04` / `0x06` | in | player position |
| `0x07` Player Digging | in | removes the mirrored tile |
| `0x08` Player Block Placement | in | places a tile |
| `0x23` Block Change | out | mirrors a Terraria tile into the world |

## Notes from building ten of these

C# has `System.Net.Sockets`, `BitConverter`, `ConcurrentQueue` and real byte arrays. Nothing had to be faked. Compare that with VBA, which has no unsigned right shift and no way to see the bytes of a double or VLC's Lua 5.1, which has no `string.pack`.

The actual work was deciding which dimension to throw away. Minecraft has three, Terraria has two. I went with X and Y, so a wall mirrors (almost) perfectly. Terraria doesn't know Z exists and I chose not to tell it (I could have made the world appear on the floor but what good is that).

Terraria's Y axis also runs the opposite way to Minecraft's, which is what makes a world build itself upside down...

## Previous episodes

- Excel: [github.com/hostdit/minecraft-server-in-excel](https://github.com/hostdit/minecraft-server-in-excel)
- Outlook: [github.com/hostdit/minecraft-server-in-outlook](https://github.com/hostdit/minecraft-server-in-outlook)
- OBS: [github.com/hostdit/minecraft-server-in-obs](https://github.com/hostdit/minecraft-server-in-obs)
- Obsidian: [github.com/hostdit/minecraft-server-in-obsidian](https://github.com/hostdit/minecraft-server-in-obsidian)
- Blender: [github.com/hostdit/minecraft-server-in-blender](https://github.com/hostdit/minecraft-server-in-blender)
- PowerPoint: [github.com/hostdit/minecraft-server-in-powerpoint](https://github.com/hostdit/minecraft-server-in-powerpoint)
- VLC: [github.com/hostdit/minecraft-server-in-vlc](https://github.com/hostdit/minecraft-server-in-vlc)
- Word: [github.com/hostdit/minecraft-server-in-word](https://github.com/hostdit/minecraft-server-in-word)
- The Sims 4: [github.com/hostdit/minecraft-server-in-sims4](https://github.com/hostdit/minecraft-server-in-sims4)

## Licence

MIT. Do what you like with it.
