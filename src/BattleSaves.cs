// SPDX-License-Identifier: MPL-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Boksol
{
    internal sealed class BattleSave
    {
        public readonly string Id, Name, Path;
        public readonly bool IsBuiltIn;

        public BattleSave(string id, string name, string path, bool builtIn = false)
        {
            Id = id;
            Name = name;
            Path = path;
            IsBuiltIn = builtIn;
        }

        public override string ToString() { return Name; }
    }

    internal static class BattleSaves
    {
        private const string BuiltInId = "preset";
        private const int MaxBytes = 1024 * 1024;
        private const string Boktai2CheckpointSave = "86878d00c60ae48ad3457b1b3c92090328fe122497c5c3b0de1359237ea60489";
        private const string Boktai3CheckpointSave = "5fbcb935c9003c58df9f6b6a08520f1c996f3e7b1e4fa48034a5f28c69364fc6";
        private static readonly Assembly App = typeof(BattleSaves).Assembly;

        private static string PresetName(Game game) { return "battle." + game.Id + ".sav"; }

        private static bool SupportsPreset(Game game, string hash)
        {
            return hash == game.KnownHash || GbaOfficialText.Supports(game.Id, hash);
        }

        public static BattleSave[] List(string data, Game game, string hash)
        {
            if (String.IsNullOrEmpty(hash)) return new BattleSave[0];
            string folder = Path.Combine(data, "game-data", game.Id, hash);
            var saves = new List<BattleSave>();
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<string, string, string> include = (id, name, path) =>
            {
                if (File.Exists(path) && found.Add(Path.GetFullPath(path)) && !IsSettingsFile(game, hash, path))
                    saves.Add(new BattleSave(id, name, path));
            };

            string library = Library(data, game, hash);
            if (Directory.Exists(library))
                foreach (string path in Directory.GetFiles(library, "*.sav").OrderByDescending(File.GetLastWriteTimeUtc))
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    include("import:" + name, "已导入 · " + name, path);
                }

            if (Directory.Exists(folder))
            {
                var locals = Directory.GetDirectories(folder, "local-*").OrderByDescending(Directory.GetLastWriteTimeUtc);
                foreach (string path in locals)
                {
                    string name = Path.GetFileName(path);
                    include(name, "本地新档 · " + name.Substring("local-".Length), Path.Combine(path, game.Id + ".sav"));
                }
                foreach (string path in Directory.GetDirectories(folder, "solo-*").OrderByDescending(Directory.GetLastWriteTimeUtc))
                {
                    string name = Path.GetFileName(path);
                    include(name, "旧版单机档 · " + name.Substring("solo-".Length), Path.Combine(path, game.Id + ".sav"));
                }
            }
            include("current", "本地游戏存档", Path.Combine(folder, "solo", game.Id + ".sav"));
            if (hash == game.KnownHash)
                include("portable", "便携存档 · " + game.Id + ".sav", Path.Combine(data, "game-data", game.Id + ".sav"));
            if (game.SavePath != null) include("legacy", "原设置 · " + Path.GetFileName(game.SavePath), game.SavePath);
            if (hash == game.OriginalHash && File.Exists(game.RomPath))
            {
                string sidecar = Path.ChangeExtension(game.RomPath, ".sav");
                include("sidecar", "ROM 同目录 · " + Path.GetFileName(sidecar), sidecar);
                if (!game.IsDs)
                {
                    string oldSave = Path.ChangeExtension(game.RomPath, ".sa1");
                    include("sidecar-sa1", "ROM 同目录 · " + Path.GetFileName(oldSave), oldSave);
                }
            }
            if (SupportsPreset(game, hash) && App.GetManifestResourceInfo(PresetName(game)) != null)
                saves.Add(new BattleSave(BuiltInId, "内置对战存档", null, true));
            return saves.ToArray();
        }

        public static BattleSave Default(string data, Game game, string hash)
        {
            var saves = List(data, game, hash);
            return saves.FirstOrDefault(s => s.Id == game.BattleSaveId) ??
                saves.FirstOrDefault(s => s.IsBuiltIn) ?? (game.BattleSaveId == null ? saves.FirstOrDefault() : null);
        }

        public static BattleSave Import(string data, Game game, string hash, string source)
        {
            byte[] content = ReadFile(game, hash, source);
            string folder = Library(data, game, hash);
            Directory.CreateDirectory(folder);
            string suffix = Game.Hash(content).Substring(0, 10);
            string name = Path.GetFileNameWithoutExtension(source);
            if (name.Length > 44) name = name.Substring(0, 44);
            string key = name.TrimEnd(' ', '.') + " (" + suffix + ")";
            string copy = Path.Combine(folder, key + ".sav");
            if (!File.Exists(copy)) Game.AtomicWrite(copy, content);
            return new BattleSave("import:" + key, "已导入 · " + key, copy);
        }

        public static byte[] Read(Game game, BattleSave save, string hash)
        {
            if (save == null) throw new InvalidOperationException("请先选择、新建或导入对战存档。");
            if (!save.IsBuiltIn) return ReadFile(game, hash, save.Path);
            if (!SupportsPreset(game, hash)) throw new InvalidDataException("内置对战档与当前 ROM 版本不匹配。");
            byte[] preset = ReadPreset(game, hash);
            if (preset == null) throw new InvalidDataException("当前版本没有随附对战存档。");
            return preset;
        }

        public static byte[] ReadPreset(Game game, string hash)
        {
            if (!SupportsPreset(game, hash)) return null;
            byte[] preset = ReadResource(PresetName(game));
            if (preset != null && hash == GbaOfficialText.Boktai2Original && preset.Length == 32768)
            {
                // The original cartridge uses 8 KiB EEPROM. The Chinese SRAM preset pads the same records to 32 KiB.
                if (preset.Skip(8192).Any(value => value != 0xFF)) return null;
                Array.Resize(ref preset, 8192);
            }
            return preset == null ? null : Validate(game, preset);
        }

        public static byte[] ReadStart(Game game, byte[] save, string hash, bool isHost)
        {
            // Checkpoints contain loaded profile state, so each one is bound to its source battery.
            if (hash != game.KnownHash) return null;
            string name = "battle." + game.Id + ".start";
            if (game.IsDs)
            {
                byte[] preset = ReadResource(PresetName(game));
                if (preset == null || !save.SequenceEqual(preset)) return null;
                name += isHost ? ".host" : ".guest";
            }
            else if (Game.Hash(save) != (game.Id == "boktai2" ? Boktai2CheckpointSave : Boktai3CheckpointSave))
            {
                return null;
            }
            return ReadResource(name);
        }

        private static byte[] ReadResource(string name)
        {
            using (var resource = App.GetManifestResourceStream(name))
            {
                if (resource == null) return null;
                using (var bytes = new MemoryStream())
                {
                    resource.CopyTo(bytes);
                    return bytes.ToArray();
                }
            }
        }

        private static bool IsSettingsFile(Game game, string hash, string path)
        {
            if (game.IsDs || hash != game.KnownHash) return false;
            try
            {
                if (new FileInfo(path).Length != (game.Id == "boktai2" ? 32768 : 8192)) return false;
                return GbaInitialSettings.IsSettingsOnly(game, hash, File.ReadAllBytes(path));
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private static byte[] ReadFile(Game game, string hash, string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("所选存档已移动或删除。");
            var info = new FileInfo(path);
            if (info.Length == 0 || info.Length > MaxBytes) throw new InvalidDataException("存档长度无效。");
            byte[] bytes = Validate(game, File.ReadAllBytes(path));
            if (GbaInitialSettings.IsSettingsOnly(game, hash, bytes))
                throw new InvalidDataException("这个文件只包含地区和时钟配置。请选择已有进度的存档或内置对战档。");
            return bytes;
        }

        private static byte[] Validate(Game game, byte[] bytes)
        {
            bool size = game.IsDs ? bytes.Length == 65536 : bytes.Length == 512 || bytes.Length == 8192 || bytes.Length == 32768 ||
                bytes.Length == 65536 || bytes.Length == 65552 || bytes.Length == 131072 || bytes.Length == 131088;
            if (!size) throw new InvalidDataException("请选择此作的原生电池存档（.sav / .sa1）。");
            if (bytes.All(b => b == 0xFF) || bytes.All(b => b == 0))
                throw new InvalidDataException("存档为空。先进入原作，创建并保存一个游戏档。");
            return bytes;
        }

        private static string Library(string data, Game game, string hash)
        {
            return Path.Combine(data, "save-library", game.Id, hash);
        }
    }
}
