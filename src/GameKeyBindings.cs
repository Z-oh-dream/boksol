using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Boksol
{
    internal enum GameButton
    {
        None = 0,
        A = 1, B = 2, Select = 4, Start = 8,
        Right = 16, Left = 32, Up = 64, Down = 128,
        R = 256, L = 512, X = 1024, Y = 2048,
        SunlightDown = 4096, SunlightUp = 8192
    }

    internal sealed class GameKeyBindings
    {
        private static readonly Dictionary<GameButton, Keys> Defaults = new Dictionary<GameButton, Keys>
        {
            { GameButton.Up, Keys.Up }, { GameButton.Down, Keys.Down },
            { GameButton.Left, Keys.Left }, { GameButton.Right, Keys.Right },
            { GameButton.A, Keys.Z }, { GameButton.B, Keys.X },
            { GameButton.L, Keys.A }, { GameButton.R, Keys.S },
            { GameButton.Start, Keys.Enter }, { GameButton.Select, Keys.Back },
            { GameButton.X, Keys.V }, { GameButton.Y, Keys.C },
            { GameButton.SunlightDown, Keys.F11 }, { GameButton.SunlightUp, Keys.F12 }
        };

        private readonly Dictionary<GameButton, Keys> keys = new Dictionary<GameButton, Keys>(Defaults);

        public Keys this[GameButton button] { get { return keys[button]; } }

        public GameButton Find(Keys key, bool isDs)
        {
            foreach (var binding in keys)
            {
                if (binding.Value != key) continue;
                switch (binding.Key)
                {
                    case GameButton.X:
                    case GameButton.Y: return isDs ? binding.Key : GameButton.None;
                    case GameButton.SunlightDown:
                    case GameButton.SunlightUp: return isDs ? GameButton.None : binding.Key;
                    default: return binding.Key;
                }
            }
            return GameButton.None;
        }

        public bool TryBind(GameButton button, Keys key)
        {
            if (!Defaults.ContainsKey(button) || !IsBindable(key)) return false;
            Keys previous = keys[button];
            GameButton other = keys.FirstOrDefault(binding => binding.Value == key).Key;
            if (other != GameButton.None) keys[other] = previous;
            keys[button] = key;
            return true;
        }

        public void Reset()
        {
            foreach (var binding in Defaults) keys[binding.Key] = binding.Value;
        }

        public void Load(string path)
        {
            string[] lines;
            try { lines = File.ReadAllLines(path, Encoding.UTF8); }
            catch (IOException) { return; }
            catch (UnauthorizedAccessException) { return; }
            foreach (string line in lines)
            {
                string[] fields = line.Split('=');
                GameButton button;
                Keys key;
                if (fields.Length == 2 && Enum.TryParse(fields[0], out button) && Enum.TryParse(fields[1], out key))
                    TryBind(button, key);
            }
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            Game.AtomicWrite(path, Encoding.UTF8.GetBytes(String.Join("\n", keys.Select(binding =>
                binding.Key + "=" + binding.Value))));
        }

        public static string DisplayName(Keys key)
        {
            switch (key)
            {
                case Keys.Up: return "↑";
                case Keys.Down: return "↓";
                case Keys.Left: return "←";
                case Keys.Right: return "→";
                case Keys.Back: return "Backspace";
                case Keys.Enter: return "Enter";
                case Keys.ShiftKey: return "Shift";
                case Keys.ControlKey: return "Ctrl";
                default: return new KeysConverter().ConvertToString(key);
            }
        }

        private static bool IsBindable(Keys key)
        {
            return (key >= Keys.A && key <= Keys.Z) || (key >= Keys.D0 && key <= Keys.D9) ||
                (key >= Keys.NumPad0 && key <= Keys.Divide) || (key >= Keys.F1 && key <= Keys.F12 && key != Keys.F10) ||
                (key >= Keys.PageUp && key <= Keys.Down) ||
                (key >= Keys.OemSemicolon && key <= Keys.Oemtilde) ||
                (key >= Keys.OemOpenBrackets && key <= Keys.OemQuotes) || key == Keys.OemBackslash ||
                key == Keys.Space || key == Keys.Enter || key == Keys.Back || key == Keys.Insert ||
                key == Keys.Delete || key == Keys.ShiftKey || key == Keys.ControlKey;
        }
    }
}
