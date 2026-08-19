using System;
using System.Collections.Generic;
using System.Text;

namespace MinecraftServer
{
    public static class MCProtocol
    {
        public const int Port = 25565;
        public const int ViewDist = 2;
        public const string Motd = "Hosted in Terraria";
        public const string PlayerUuid = "069a79f4-44e9-4726-a5be-fca90e38aaf5";

        public static void WriteVarInt(List<byte> output, int value)
        {
            uint v = unchecked((uint)value);
            while (true)
            {
                byte b = (byte)(v & 0x7F);
                v >>= 7;
                if (v != 0)
                {
                    output.Add((byte)(b | 0x80));
                }
                else
                {
                    output.Add(b);
                    return;
                }
            }
        }

        public static bool ReadVarInt(byte[] buffer, int length, ref int pos, out int value)
        {
            uint result = 0;
            int shift = 0;
            value = 0;
            while (true)
            {
                if (pos >= length) return false;
                byte b = buffer[pos++];
                result |= (uint)(b & 0x7F) << shift;
                shift += 7;
                if ((b & 0x80) == 0) break;
                if (shift > 35) return false;
            }
            value = unchecked((int)result);
            return true;
        }

        public static void WriteString(List<byte> output, string text)
        {
            byte[] raw = Encoding.UTF8.GetBytes(text);
            WriteVarInt(output, raw.Length);
            output.AddRange(raw);
        }

        public static void WriteInt(List<byte> output, int value)
        {
            output.Add((byte)((value >> 24) & 0xFF));
            output.Add((byte)((value >> 16) & 0xFF));
            output.Add((byte)((value >> 8) & 0xFF));
            output.Add((byte)(value & 0xFF));
        }

        public static void WriteUShort(List<byte> output, ushort value)
        {
            output.Add((byte)((value >> 8) & 0xFF));
            output.Add((byte)(value & 0xFF));
        }

        public static void WriteLong(List<byte> output, ulong value)
        {
            for (int i = 7; i >= 0; i--) output.Add((byte)((value >> (i * 8)) & 0xFF));
        }

        public static void WriteDouble(List<byte> output, double value)
        {
            WriteLong(output, (ulong)BitConverter.DoubleToInt64Bits(value));
        }

        public static void WriteFloat(List<byte> output, float value)
        {
            byte[] raw = BitConverter.GetBytes(value);
            for (int i = 3; i >= 0; i--) output.Add(raw[i]);
        }

        public static double ReadDouble(byte[] buffer, ref int pos)
        {
            byte[] raw = new byte[8];
            for (int i = 0; i < 8; i++) raw[i] = buffer[pos + 7 - i];
            pos += 8;
            return BitConverter.ToDouble(raw, 0);
        }

        public static float ReadFloat(byte[] buffer, ref int pos)
        {
            byte[] raw = new byte[4];
            for (int i = 0; i < 4; i++) raw[i] = buffer[pos + 3 - i];
            pos += 4;
            return BitConverter.ToSingle(raw, 0);
        }

        public static ulong ReadLong(byte[] buffer, ref int pos)
        {
            ulong value = 0;
            for (int i = 0; i < 8; i++) value = (value << 8) | buffer[pos + i];
            pos += 8;
            return value;
        }

        public static ulong EncodePosition(int x, int y, int z)
        {
            return ((ulong)(x & 0x3FFFFFF) << 38)
                 | ((ulong)(y & 0xFFF) << 26)
                 | (ulong)(z & 0x3FFFFFF);
        }

        public static void DecodePosition(ulong packed, out int x, out int y, out int z)
        {
            long sx = (long)(packed >> 38);
            long sy = (long)((packed >> 26) & 0xFFF);
            long sz = (long)(packed & 0x3FFFFFF);
            if (sx >= (1L << 25)) sx -= 1L << 26;
            if (sy >= (1L << 11)) sy -= 1L << 12;
            if (sz >= (1L << 25)) sz -= 1L << 26;
            x = (int)sx;
            y = (int)sy;
            z = (int)sz;
        }

        public static byte[] BuildSection()
        {
            byte[] data = new byte[12544];
            int[,] layers = { { 0, 7 }, { 1, 3 }, { 2, 3 }, { 3, 2 } };
            for (int layer = 0; layer < 4; layer++)
            {
                int y = layers[layer, 0];
                int value = layers[layer, 1] << 4;
                int baseIndex = y * 512;
                for (int i = 0; i < 256; i++)
                {
                    data[baseIndex + i * 2] = (byte)(value & 0xFF);
                    data[baseIndex + i * 2 + 1] = (byte)((value >> 8) & 0xFF);
                }
            }
            for (int i = 10240; i < 12288; i++) data[i] = 0xFF;
            for (int i = 12288; i < 12544; i++) data[i] = 1;
            return data;
        }
    }

    public enum MCEventKind
    {
        Joined,
        Left,
        Placed,
        Broke,
        Moved,
        Chat,
        Status
    }

    public struct MCEvent
    {
        public MCEventKind Kind;
        public string Text;
        public double X, Y, Z;
        public int BlockX, BlockY, BlockZ;
        public int BlockId;
    }
}
