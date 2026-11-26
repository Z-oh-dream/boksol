// SPDX-License-Identifier: MPL-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Boksol
{
    internal static class DsOfficialText
    {
        private const int Width = 256;
        private const int RgbMask = 0xFFFFFF;
        private static readonly Lazy<Field[]> fields = new Lazy<Field[]>(ReadFields);

        public static bool Apply(int[] original, int[] localized)
        {
            bool matched = false;
            foreach (Field field in fields.Value)
            {
                if (!field.TryMatch(original, out var text)) continue;
                if (!matched) Array.Copy(original, localized, original.Length);
                field.Paint(original, localized, text);
                matched = true;
            }
            return matched;
        }

        private static Field[] ReadFields()
        {
            var result = new List<Field>();
            var regions = new Dictionary<string, Field>(StringComparer.Ordinal);
            var catalog = OfficialText.Entries("boktaids");
            using (var reader = new StreamReader(OfficialText.Resource("ds-fields.tsv")))
            {
                for (string line = reader.ReadLine(); line != null; line = reader.ReadLine())
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length != 12) throw new InvalidDataException("内置 DS 文本定位表格式无效。");
                    int x = Number(parts[0]), y = Number(parts[1]), w = Number(parts[2]), h = Number(parts[3]);
                    int background = Int32.Parse(parts[10], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    string key = String.Join("/", parts, 0, 5) + "/" + parts[10];
                    if (!regions.TryGetValue(key, out Field field))
                    {
                        field = new Field(x, y, w, h, parts[4] == "W", background);
                        regions.Add(key, field);
                        result.Add(field);
                    }
                    var fingerprint = UInt64.Parse(parts[9], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    int foreground = Int32.Parse(parts[11], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    string text = catalog[Number(parts[7])].Chinese.Substring(Number(parts[8]));
                    var pixels = Rasterize(text, w, h, Number(parts[6]), parts[5][0]);
                    field.Translations.Add(fingerprint, new Translation(foreground, pixels));
                }
            }
            return result.ToArray();
        }

        private static int Number(string value) { return Int32.Parse(value, CultureInfo.InvariantCulture); }

        private static int Advance(char character, int size) { return character < 128 ? (size * 2 + 1) / 3 : size; }

        private static List<string> Wrap(string text, int width, int size)
        {
            var lines = new List<string>();
            foreach (string part in text.Split('\n'))
            {
                int start = 0, used = 0;
                for (int i = 0; i < part.Length; i++)
                {
                    int advance = Advance(part[i], size);
                    if (used > 0 && used + advance > width)
                    {
                        lines.Add(part.Substring(start, i - start));
                        start = i;
                        used = 0;
                    }
                    used += advance;
                }
                lines.Add(part.Substring(start));
            }
            return lines;
        }

        private static int[] Rasterize(string text, int width, int height, int size, char placement)
        {
            var lines = Wrap(text, width, size);
            while (lines.Count * size > height && size > 6) lines = Wrap(text, width, --size);
            if (lines.Count * size > height || size > width)
                throw new InvalidDataException("内置 DS 译文超出显示区域。");
            var pixels = new List<int>();
            int y = placement == 'T' ? 1 : (height - lines.Count * size) / 2;
            foreach (string line in lines)
            {
                int advance = 0;
                foreach (char character in line) advance += Advance(character, size);
                int x = placement == 'C' ? (width - advance) / 2 : 0;
                foreach (char character in line)
                {
                    int cell = Advance(character, size);
                    if (OfficialText.Glyphs.TryGetValue(character, out var glyph))
                    {
                        for (int row = 0; row < size && y + row < height; row++)
                        {
                            int bits = glyph[(row * 16 + 8) / size];
                            for (int col = 0; col < cell; col++)
                            {
                                int source = character < 128 ? 3 + (col * 10 + 5) / cell : (col * 16 + 8) / cell;
                                if ((bits & (0x8000 >> source)) != 0) pixels.Add((y + row) * Width + x + col);
                            }
                        }
                    }
                    x += cell;
                }
                y += size;
            }
            return pixels.ToArray();
        }

        private sealed class Translation
        {
            public readonly int Foreground;
            public readonly int[] Ink;

            public Translation(int foreground, int[] ink)
            {
                Foreground = unchecked((int)0xFF000000) | foreground;
                Ink = ink;
            }
        }

        private sealed class Field
        {
            private readonly int start, width, height, background;
            private readonly bool lightText;
            public readonly Dictionary<ulong, Translation> Translations = new Dictionary<ulong, Translation>();

            public Field(int x, int y, int width, int height, bool lightText, int background)
            {
                if (x < 0 || y < 0 || width < 1 || height < 1 || x + width > Width || y + height > 384)
                    throw new InvalidDataException("内置 DS 文本区域无效。");
                start = y * Width + x;
                this.width = width;
                this.height = height;
                this.lightText = lightText;
                this.background = background;
            }

            private bool Ink(int rgb)
            {
                return lightText ? rgb != background && (rgb >> 16) >= 75 :
                    rgb == 0xA26969 || rgb == 0xE39A82 || rgb == 0xF3C3A2;
            }

            public bool TryMatch(int[] pixels, out Translation translation)
            {
                translation = null;
                int last = start + (height - 1) * Width;
                if ((pixels[start] & RgbMask) != background && (pixels[start + width - 1] & RgbMask) != background &&
                    (pixels[last] & RgbMask) != background && (pixels[last + width - 1] & RgbMask) != background)
                    return false;
                ulong hash = 0xCBF29CE484222325;
                unchecked
                {
                    for (int y = 0; y < height; y++)
                    {
                        int offset = start + y * Width;
                        for (int x = 0; x < width; x++)
                            hash = (hash ^ (Ink(pixels[offset + x] & RgbMask) ? 1UL : 0UL)) * 0x100000001B3UL;
                    }
                }
                return Translations.TryGetValue(hash, out translation);
            }

            public void Paint(int[] original, int[] localized, Translation text)
            {
                int fill = unchecked((int)0xFF000000) | background;
                for (int y = 0; y < height; y++)
                {
                    int offset = start + y * Width;
                    for (int x = 0; x < width; x++)
                        if (Ink(original[offset + x] & RgbMask)) localized[offset + x] = fill;
                }
                foreach (int offset in text.Ink)
                    if ((localized[start + offset] & RgbMask) == background)
                        localized[start + offset] = text.Foreground;
            }
        }
    }
}
