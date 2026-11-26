// SPDX-License-Identifier: MPL-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Boksol
{
    internal sealed partial class Lobby
    {
        private void AddRomSlots(Panel panel)
        {
            for (int i = 0; i < romSlots.Length; i++)
            {
                Game game = Game.All[i];
                int y = 113 + 35 * i;
                Place(panel, Caption(game.Title, 11, Ink), 2, y, 208, 32);
                romSlots[i] = Caption(String.Empty, 10, Quiet);
                romSlots[i].AccessibleName = game.Title + " ROM 导入情况";
                Place(panel, romSlots[i], 224, y, 136, 32);
            }
        }

        private void RefreshRomSlots()
        {
            for (int i = 0; i < romSlots.Length; i++)
            {
                Game game = Game.All[i];
                bool imported = game.MatchesRom(game.RomPath);
                romSlots[i].Text = imported ? "已导入" : "未导入";
                romSlots[i].ForeColor = imported ? Mint : Quiet;
            }
        }

        private void ChooseRoms()
        {
            if (busy || emulator != null) return;
            using (var dialog = new OpenFileDialog
            {
                Filter = "游戏 ROM (*.gba; *.nds)|*.gba;*.nds", Title = "导入 ROM（可多选）",
                InitialDirectory = root, Multiselect = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string[] errors = ImportRoms(dialog.FileNames);
                if (errors.Length > 0)
                    MessageBox.Show(this, String.Join(Environment.NewLine, errors), "ROM 导入结果");
            }
        }

        private string[] ImportRoms(IEnumerable<string> paths)
        {
            var errors = new List<string>();
            var selected = new Dictionary<Game, List<string>>();
            foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    Game game = Game.IdentifyRom(path);
                    if (!selected.TryGetValue(game, out List<string> files))
                        selected.Add(game, files = new List<string>());
                    files.Add(path);
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is InvalidDataException)
                {
                    errors.Add(Path.GetFileName(path) + " · " + error.Message);
                }
            }

            bool changed = false;
            foreach (var entry in selected)
            {
                Game game = entry.Key;
                if (entry.Value.Count != 1)
                {
                    errors.Add(game.Title + " · 选了多份 ROM，请为此槽位选定一份。");
                    continue;
                }
                string path = entry.Value[0];
                if (String.Equals(game.RomPath, path, StringComparison.OrdinalIgnoreCase)) continue;
                game.RomPath = path;
                game.PatchPath = game.SavePath = game.BattleSaveId = game.OriginalHash = null;
                changed = true;
            }
            if (changed) StoreSettings();
            RefreshRomSlots();
            if (changed) ChangeGame();
            return errors.ToArray();
        }
    }
}
