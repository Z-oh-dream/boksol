using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Boksol
{
    internal sealed class Game
    {
        public static readonly Game[] All = {
            new Game("boktai2", "续・我们的太阳", "太阳少年强戈", "U32J", false,
                "f555858d6e3b70c8b4a86e5fa10d9385bca1e6bb5a67581863114756790665a3", "简中 2012-02-06",
                "标题菜单向右选 LINK → 通信对战。载入各自的存档，设置对战规则，双方待机后由玩家 1 开始。"),
            new Game("boktai3", "新・我们的太阳", "逆袭的萨巴塔", "U33J", false,
                "9b1ca6a6007c3278dd265d42a3a91ed1380fda155b6692958fae66f9c9b9d3aa", "简中 2016-07-12",
                "标题菜单向右选 LINK。载入各自的存档，在通信菜单选择本作对战。"),
            new Game("boktaids", "我们的太阳DS", "强戈 & 萨巴塔", "ALKJ", true,
                "0918875a071382be3155be36de8a7cd34613eba250e0b32bc3be20b8998b550c", "原版日文",
                "标题菜单 → WIRELESS → 通信対戦 → DSワイヤレス対戦，选择已有剧情存档。")
        };

        public readonly string Id, Title, Subtitle, Code, KnownHash, KnownLanguage, Guide;
        public readonly bool IsDs;
        public string Extension { get { return IsDs ? ".nds" : ".gba"; } }
        public string Platform { get { return IsDs ? "NINTENDO DS" : "GAME BOY ADVANCE"; } }
        public string RomPath, PatchPath, SavePath, BattleSaveId, OriginalHash;

        private Game(string id, string title, string subtitle, string code, bool isDs, string hash, string language, string guide)
        {
            Id = id; Title = title; Subtitle = subtitle; Code = code; IsDs = isDs;
            KnownHash = hash; KnownLanguage = language; Guide = guide;
        }

        public void Validate(byte[] header)
        {
            int offset = IsDs ? 12 : 172;
            if (header.Length < offset + 4 || Encoding.ASCII.GetString(header, offset, 4) != Code)
                throw new InvalidDataException("此文件的游戏编号与所选游戏不符，需要 " + Code + "。");
        }

        public static Game IdentifyRom(string path)
        {
            string extension = Path.GetExtension(path);
            var header = new byte[176];
            using (var rom = File.OpenRead(path))
            {
                if (rom.Length < header.Length || rom.Length > Patch.MaxSize ||
                    rom.Read(header, 0, header.Length) != header.Length)
                    throw new InvalidDataException("ROM 文件长度无效。");
            }
            foreach (Game game in All)
            {
                if (String.Equals(extension, game.Extension, StringComparison.OrdinalIgnoreCase) &&
                    Encoding.ASCII.GetString(header, game.IsDs ? 12 : 172, 4) == game.Code)
                    return game;
            }
            throw new InvalidDataException("文件的游戏编号或扩展名与受支持的作品不匹配。");
        }

        public bool MatchesRom(string path)
        {
            if (!File.Exists(path)) return false;
            try { return IdentifyRom(path) == this; }
            catch (InvalidDataException) { return false; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        public string ReadHash(string path)
        {
            using (var rom = File.OpenRead(path))
            {
                var header = new byte[176];
                if (rom.Read(header, 0, header.Length) != header.Length)
                    throw new InvalidDataException("ROM 文件不完整。");
                Validate(header);
                rom.Position = 0;
                using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(rom));
            }
        }

        public PreparedRom Prepare(string dataRoot, string profile)
        {
            return Stage(dataRoot, profile, ReadImage());
        }

        public PreparedRom PrepareNew(string dataRoot, GameImage image, string profile)
        {
            var rom = WriteImage(dataRoot, profile, image);
            string battery = Path.ChangeExtension(rom.Path, ".sav");
            if (!File.Exists(battery))
            {
                byte[] initial = GbaInitialSettings.Create(this, image.Hash);
                if (initial != null) AtomicWrite(battery, initial, replaceExisting: false);
            }
            return rom;
        }

        public PreparedRom PreparePractice(string dataRoot, GameImage image, byte[] save)
        {
            var rom = WriteImage(dataRoot, "practice-" + Hash(save).Substring(0, 16), image);
            string battery = Path.ChangeExtension(rom.Path, ".sav");
            if (!File.Exists(battery)) AtomicWrite(battery, save, replaceExisting: false);
            return rom;
        }

        public PreparedRom PrepareBattle(string dataRoot, string role, GameImage image, byte[] save, byte[] start = null)
        {
            string branch = role + "-battle-" + Hash(save).Substring(0, 16);
            var rom = WriteImage(dataRoot, branch, image);
            string saveName = System.IO.Path.ChangeExtension(rom.Path, ".sav");
            AtomicWrite(saveName, save);
            if (!IsDs)
            {
                string linked = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(rom.Path),
                    Id + (role == "host" ? "-1.sav" : "-2.sav"));
                AtomicWrite(linked, save);
            }
            if (start != null)
            {
                string name = Id + (IsDs ? ".ml1" : "-01.sgm");
                AtomicWrite(Path.Combine(Path.GetDirectoryName(rom.Path), name), start);
                rom.HasBattleStart = true;
            }
            return rom;
        }

        public GameImage ReadImage()
        {
            if (!File.Exists(RomPath)) throw new FileNotFoundException("请先选择本地 ROM。");
            long inputSize = new FileInfo(RomPath).Length;
            if (inputSize < 176 || inputSize > Patch.MaxSize) throw new InvalidDataException("ROM 文件大小超出支持范围。");
            byte[] content = File.ReadAllBytes(RomPath);
            Validate(content);
            string inputHash = Hash(content);
            if (!String.IsNullOrEmpty(PatchPath))
            {
                if (new FileInfo(PatchPath).Length > Patch.MaxSize) throw new InvalidDataException("补丁过大。");
                content = Patch.Apply(content, File.ReadAllBytes(PatchPath));
                Validate(content);
            }
            string hash = PatchPath == null ? inputHash : Hash(content);
            return new GameImage(content, hash, inputHash == hash);
        }

        public PreparedRom Stage(string dataRoot, string profile, GameImage image)
        {
            string role = profile;
            byte[] selectedSave = null;
            if (SavePath != null)
            {
                var save = new FileInfo(SavePath);
                if (!save.Exists || save.Length < 1 || save.Length > 1024 * 1024)
                    throw new InvalidDataException("所选存档不存在或大小无效。");
                selectedSave = File.ReadAllBytes(save.FullName);
                profile += "-" + Hash(selectedSave).Substring(0, 16);
            }
            var rom = WriteImage(dataRoot, profile, image);
            string folder = Path.GetDirectoryName(rom.Path);
            string versionFolder = Path.GetDirectoryName(folder);
            ImportSave(folder, image, selectedSave,
                role == "solo" ? null : Path.Combine(versionFolder, "solo", Id + ".sav"));
            if (!IsDs && role != "solo")
            {
                string initial = Path.Combine(folder, Id + ".sav");
                string network = Path.Combine(folder, Id + (role == "host" ? "-1.sav" : "-2.sav"));
                if (File.Exists(network) && (!File.Exists(initial) || File.GetLastWriteTimeUtc(network) > File.GetLastWriteTimeUtc(initial)))
                    AtomicWrite(initial, File.ReadAllBytes(network));
            }
            return rom;
        }

        private PreparedRom WriteImage(string dataRoot, string profile, GameImage image)
        {
            string folder = Path.Combine(dataRoot, "game-data", Id, image.Hash, profile);
            Directory.CreateDirectory(folder);
            string copy = Path.Combine(folder, Id + Extension);
            if (!File.Exists(copy) || ReadHash(copy) != image.Hash) AtomicWrite(copy, image.Content);
            return new PreparedRom(copy, image.Hash);
        }

        private void ImportSave(string folder, GameImage image, byte[] selectedSave, string soloSave)
        {
            string output = Path.Combine(folder, Id + ".sav");
            if (File.Exists(output)) return;
            if (selectedSave != null) { AtomicWrite(output, selectedSave, replaceExisting: false); return; }
            string source = soloSave;
            if (!File.Exists(source) && image.Unmodified)
            {
                source = Path.ChangeExtension(RomPath, ".sav");
                if (!IsDs && !File.Exists(source)) source = Path.ChangeExtension(RomPath, ".sa1");
            }
            byte[] initial = File.Exists(source) ? File.ReadAllBytes(source) : null;
            if (initial == null && soloSave == null)
                initial = BattleSaves.ReadPreset(this, image.Hash) ?? GbaInitialSettings.Create(this, image.Hash);
            if (initial != null) AtomicWrite(output, initial, replaceExisting: false);
        }

        public static void AtomicWrite(string path, byte[] bytes, bool replaceExisting = true)
        {
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temp, bytes);
                if (replaceExisting && File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            catch (IOException) when (!replaceExisting && File.Exists(path)) { }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        public static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
        public static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(bytes)); }
    }

    internal sealed class GameImage
    {
        public readonly byte[] Content;
        public readonly string Hash;
        public readonly bool Unmodified;
        public GameImage(byte[] content, string hash, bool unmodified)
        { Content = content; Hash = hash; Unmodified = unmodified; }
    }

    internal sealed class PreparedRom
    {
        public bool HasBattleStart;
        public readonly string Path, Hash;
        public PreparedRom(string path, string hash) { Path = path; Hash = hash; }
    }

    internal static class Patch
    {
        public const int MaxSize = 160 * 1024 * 1024;

        public static byte[] Apply(byte[] source, byte[] patch)
        {
            if (patch.Length >= 8 && Encoding.ASCII.GetString(patch, 0, 5) == "PATCH") return Ips(source, patch);
            if (patch.Length >= 18 && Encoding.ASCII.GetString(patch, 0, 4) == "BPS1") return Bps(source, patch);
            throw new InvalidDataException("支持 IPS 和 BPS 补丁。其他格式可在外部应用后导入生成的 ROM。");
        }

        private static byte[] Ips(byte[] source, byte[] patch)
        {
            using (var input = new BinaryReader(new MemoryStream(patch, false)))
            using (var output = new MemoryStream(source.Length))
            {
                output.Write(source, 0, source.Length);
                input.BaseStream.Position = 5;
                int offset;
                while ((offset = Big(input, 3)) != 0x454f46)
                {
                    int count = Big(input, 2);
                    output.Position = offset;
                    if (count == 0)
                    {
                        count = Big(input, 2);
                        var fill = new byte[count];
                        byte value = input.ReadByte();
                        for (int i = 0; i < count; i++) fill[i] = value;
                        output.Write(fill, 0, count);
                    }
                    else
                    {
                        byte[] data = input.ReadBytes(count);
                        if (data.Length != count) throw new InvalidDataException("IPS 补丁记录不完整。");
                        output.Write(data, 0, count);
                    }
                }
                long tail = input.BaseStream.Length - input.BaseStream.Position;
                if (tail == 3) output.SetLength(Big(input, 3));
                else if (tail != 0) throw new InvalidDataException("IPS 补丁尾部格式错误。");
                return output.ToArray();
            }
        }

        private static int Big(BinaryReader input, int width)
        {
            int number = 0;
            for (int i = 0; i < width; i++) number = (number << 8) | input.ReadByte();
            return number;
        }

        private static byte[] Bps(byte[] source, byte[] patch)
        {
            int footer = patch.Length - 12;
            if (Crc(patch, patch.Length - 4) != UInt32At(patch, patch.Length - 4))
                throw new InvalidDataException("BPS 补丁校验失败。");
            if (Crc(source, source.Length) != UInt32At(patch, footer))
                throw new InvalidDataException("BPS 需要不同版本的原始 ROM，源文件校验值不匹配。");
            int cursor = 4;
            int inputSize = Number(patch, ref cursor, footer);
            int outputSize = Number(patch, ref cursor, footer);
            int metadata = Number(patch, ref cursor, footer);
            if (inputSize != source.Length || outputSize > MaxSize || metadata > footer - cursor)
                throw new InvalidDataException("BPS 补丁大小字段无效。");
            cursor += metadata;
            var target = new byte[outputSize];
            int position = 0;
            long sourceOffset = 0, targetOffset = 0;
            while (position < target.Length)
            {
                int action = Number(patch, ref cursor, footer);
                int length = (action >> 2) + 1;
                if (length > target.Length - position) throw new InvalidDataException("BPS 写入超出目标 ROM。");
                switch (action & 3)
                {
                    case 0:
                        if (position + length > source.Length) throw new InvalidDataException("BPS 读取超出原始 ROM。");
                        Buffer.BlockCopy(source, position, target, position, length);
                        break;
                    case 1:
                        if (length > footer - cursor) throw new InvalidDataException("BPS 补丁记录不完整。");
                        Buffer.BlockCopy(patch, cursor, target, position, length);
                        cursor += length;
                        break;
                    case 2:
                        sourceOffset += Offset(Number(patch, ref cursor, footer));
                        if (sourceOffset < 0 || sourceOffset + length > source.Length) throw new InvalidDataException("BPS 源偏移无效。");
                        Buffer.BlockCopy(source, (int)sourceOffset, target, position, length);
                        sourceOffset += length;
                        break;
                    case 3:
                        targetOffset += Offset(Number(patch, ref cursor, footer));
                        if (targetOffset < 0 || targetOffset >= position) throw new InvalidDataException("BPS 目标偏移无效。");
                        for (int i = 0; i < length; i++) target[position + i] = target[targetOffset++];
                        break;
                }
                position += length;
            }
            if (cursor != footer || Crc(target, target.Length) != UInt32At(patch, footer + 4))
                throw new InvalidDataException("BPS 目标文件校验失败。");
            return target;
        }

        private static int Number(byte[] data, ref int cursor, int end)
        {
            long value = 0, shift = 1;
            while (cursor < end && shift <= (1L << 28))
            {
                byte digit = data[cursor++];
                value += (digit & 127) * shift;
                if ((digit & 128) != 0 && value <= Int32.MaxValue) return (int)value;
                shift <<= 7;
                value += shift;
            }
            throw new InvalidDataException("BPS 数值编码无效。");
        }

        private static int Offset(int value) { return (value & 1) == 0 ? (value >> 1) : -(value >> 1); }

        private static uint UInt32At(byte[] bytes, int index)
        {
            return (uint)bytes[index] | (uint)bytes[index + 1] << 8 | (uint)bytes[index + 2] << 16 | (uint)bytes[index + 3] << 24;
        }

        private static readonly uint[] CrcTable = CreateTable();
        private static uint[] CreateTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < table.Length; i++)
            {
                uint value = i;
                for (int bit = 0; bit < 8; bit++) value = ((value & 1) != 0 ? 0xedb88320u : 0) ^ (value >> 1);
                table[i] = value;
            }
            return table;
        }

        private static uint Crc(byte[] data, int length)
        {
            uint value = UInt32.MaxValue;
            for (int i = 0; i < length; i++) value = CrcTable[(value ^ data[i]) & 255] ^ (value >> 8);
            return ~value;
        }
    }
}
