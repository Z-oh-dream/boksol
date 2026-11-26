// SPDX-License-Identifier: MPL-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Boksol
{
    internal static class GbaOfficialText
    {
        internal const string Boktai2Original = "65856f9d2eff795ae2aa5c3162e6a5f093dad40bedcb148be5a7d72c8d8ee7e3";
        internal const string Boktai3Original = "0b170fc99def2ca5bdebbfd642327e135304adba1e3e0bfd5d66cdd298adb90f";
        private const int RomBase = 0x08000000;
        private const int Padding = 0xF00000;
        private const int FontHeader = 0x1C;
        private const int FirstNewGlyph = 0x601;
        private const int OriginalFontArchiveSize = FontHeader + 0x200C + FirstNewGlyph * 128;

        public static bool Supports(string game, string hash)
        {
            return game == "boktai2" && hash == Boktai2Original || game == "boktai3" && hash == Boktai3Original;
        }

        public static byte[] Apply(Game game, GameImage image)
        {
            if (!Supports(game.Id, image.Hash)) return image.Content;
            bool sequel = game.Id == "boktai2";
            int fontPointer = sequel ? 0x5B0DB4 : 0x614D90;
            int textIndex = sequel ? 0xCCA6AC : 0xD6B1F8;
            int textBase = sequel ? 0xCD1640 : 0xD74DDC;
            var entries = OfficialText.Entries(game.Id);
            var codes = new Dictionary<char, int>();
            foreach (var entry in entries.Values)
                foreach (char character in entry.Chinese)
                    if (character >= 128 && !codes.ContainsKey(character)) codes.Add(character, FirstNewGlyph + codes.Count);

            int font = checked((int)ReadWord(image.Content, fontPointer) - RomBase);
            int fontEnd = checked(Padding + OriginalFontArchiveSize + codes.Count * 128);
            if (font < 0 || font + OriginalFontArchiveSize > Padding || fontEnd > image.Content.Length ||
                ReadWord(image.Content, font + 12) != FontHeader || ReadWord(image.Content, font + FontHeader + 8) != 0x200C)
                throw new InvalidDataException("原版 GBA ROM 的联机字体位置不匹配。");

            var result = (byte[])image.Content.Clone();
            Buffer.BlockCopy(image.Content, font, result, Padding, OriginalFontArchiveSize);
            int wideGlyphCount = FirstNewGlyph + codes.Count;
            result[Padding + FontHeader + 2] = (byte)wideGlyphCount;
            result[Padding + FontHeader + 3] = (byte)(wideGlyphCount >> 8);
            WriteWord(result, fontPointer, (uint)(RomBase + Padding));
            foreach (var glyph in codes)
            {
                int target = Padding + FontHeader + 0x200C + glyph.Value * 128;
                WriteGlyph(result, target, OfficialText.Glyphs[glyph.Key]);
            }

            int cursor = fontEnd;
            foreach (var pair in entries)
            {
                var entry = pair.Value;
                int index = textIndex + pair.Key * 4;
                if (ReadWord(image.Content, index) != (0x80000000U | (uint)(entry.Offset - textBase)))
                    throw new InvalidDataException("原版 GBA ROM 的联机文本位置不匹配。");
                if (entry.Original == entry.Chinese) continue;
                byte[] text = Encode(image.Content, entry, codes);
                if (text.Length > result.Length - cursor) throw new InvalidDataException("内置 GBA 文本超出 ROM 空间。");
                Buffer.BlockCopy(text, 0, result, cursor, text.Length);
                WriteWord(result, index, 0x80000000U | (uint)(cursor - textBase));
                cursor += text.Length;
            }
            for (int i = Padding; i < cursor; i++)
                if (image.Content[i] != 0) throw new InvalidDataException("原版 GBA ROM 的文本预留空间不匹配。");
            return result;
        }

        private static byte[] Encode(byte[] rom, OfficialText.TextEntry entry, Dictionary<char, int> codes)
        {
            int end = entry.Offset;
            var commands = new StringBuilder();
            while (rom[end] != 0)
            {
                if (rom[end] < 0x80) commands.Append((char)rom[end++]);
                else { commands.Append('\ufffd'); end += 2; }
            }
            string script = commands.ToString();
            string text = Regex.Replace(entry.Chinese, @"\{VAR=(\d+)\}", "<VAR=$1>");
            if (script.Contains("<WEIGHT>START")) text = text.Replace("START", "<WEIGHT>START</WEIGHT>");
            if (script.Contains("<WEIGHT>B")) text = text.Replace("B键", "<WEIGHT>B</WEIGHT>键");
            if (script.Contains("<WEIGHT><VAR=")) text = Regex.Replace(text, @"<VAR=\d+>", "<WEIGHT>$0</WEIGHT>");
            var prefix = Regex.Match(script, @"^<EXTEND=\d+>");
            if (prefix.Success) text = prefix.Value + text;

            using (var encoded = new MemoryStream())
            {
                int leading = entry.Offset;
                while (rom[leading] == ' ' || (rom[leading] == 0x80 && rom[leading + 1] == 0))
                {
                    encoded.WriteByte(rom[leading]);
                    if (rom[leading++] == 0x80) encoded.WriteByte(rom[leading++]);
                }
                foreach (char character in text)
                {
                    if (character < 128) encoded.WriteByte((byte)character);
                    else
                    {
                        int code = 0x8000 + codes[character];
                        encoded.WriteByte((byte)(code >> 8));
                        encoded.WriteByte((byte)code);
                    }
                }
                encoded.WriteByte(0);
                return encoded.ToArray();
            }
        }

        private static void WriteGlyph(byte[] target, int offset, ushort[] rows)
        {
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x += 2)
                {
                    int index = offset + ((y / 8) * 2 + x / 8) * 32 + (y % 8) * 4 + (x % 8) / 2;
                    int left = (rows[y] & (0x8000 >> x)) != 0 ? 1 : 4;
                    int right = (rows[y] & (0x4000 >> x)) != 0 ? 1 : 4;
                    target[index] = (byte)(left | (right << 4));
                }
        }

        private static uint ReadWord(byte[] data, int offset)
        {
            return (uint)(data[offset] | data[offset + 1] << 8 | data[offset + 2] << 16 | data[offset + 3] << 24);
        }

        private static void WriteWord(byte[] data, int offset, uint value)
        {
            for (int i = 0; i < 4; i++) data[offset + i] = (byte)(value >> (i * 8));
        }
    }
}
