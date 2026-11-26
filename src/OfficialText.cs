// SPDX-License-Identifier: MPL-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Boksol
{
    internal static class OfficialText
    {
        private static readonly Lazy<Dictionary<string, SortedDictionary<int, TextEntry>>> catalog =
            new Lazy<Dictionary<string, SortedDictionary<int, TextEntry>>>(ReadCatalog);
        private static readonly Lazy<SortedDictionary<char, ushort[]>> glyphs =
            new Lazy<SortedDictionary<char, ushort[]>>(ReadGlyphs);

        public static SortedDictionary<int, TextEntry> Entries(string game) { return catalog.Value[game]; }
        public static SortedDictionary<char, ushort[]> Glyphs { get { return glyphs.Value; } }

        public static bool Supports(Game game, string hash)
        {
            return game.IsDs ? hash == game.KnownHash : GbaOfficialText.Supports(game.Id, hash);
        }

        internal static Stream Resource(string name)
        {
            return typeof(OfficialText).Assembly.GetManifestResourceStream("boksol.text." + name)
                ?? throw new InvalidDataException("缺少内置联机文本资源。");
        }

        private static Dictionary<string, SortedDictionary<int, TextEntry>> ReadCatalog()
        {
            var result = new Dictionary<string, SortedDictionary<int, TextEntry>>(StringComparer.Ordinal);
            using (var reader = new StreamReader(Resource("official-tpl.tsv"), new UTF8Encoding(false, true)))
            {
                for (string line = reader.ReadLine(); line != null; line = reader.ReadLine())
                {
                    string[] fields = line.Split('\t');
                    if (fields.Length != 5) throw new InvalidDataException("内置联机文本格式无效。");
                    var entry = new TextEntry(fields[3].Replace("\\n", "\n"), fields[4].Replace("\\n", "\n"),
                        fields[2] == "-" ? -1 : Int32.Parse(fields[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    if (!result.TryGetValue(fields[0], out var game))
                    {
                        game = new SortedDictionary<int, TextEntry>();
                        result.Add(fields[0], game);
                    }
                    game.Add(Int32.Parse(fields[1], CultureInfo.InvariantCulture), entry);
                }
            }
            return result;
        }

        private static SortedDictionary<char, ushort[]> ReadGlyphs()
        {
            var result = new SortedDictionary<char, ushort[]>();
            using (var reader = new BinaryReader(Resource("glyphs.bin")))
            {
                if (reader.ReadUInt32() != 0x31544C42) throw new InvalidDataException("内置字形格式无效。");
                int count = reader.ReadUInt16();
                for (int i = 0; i < count; i++)
                {
                    char character = (char)reader.ReadUInt16();
                    var rows = new ushort[16];
                    for (int y = 0; y < rows.Length; y++) rows[y] = reader.ReadUInt16();
                    result.Add(character, rows);
                }
            }
            return result;
        }

        internal sealed class TextEntry
        {
            public readonly string Original, Chinese;
            public readonly int Offset;

            public TextEntry(string original, string chinese, int offset)
            {
                Original = original;
                Chinese = chinese;
                Offset = offset;
            }
        }
    }
}
