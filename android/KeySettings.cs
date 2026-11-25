using System.Collections.Generic;
using System.Linq;
using Android.Views;

namespace Boksol
{
    public sealed partial class MainActivity
    {
        private static readonly (string name, int mask)[] Controls =
        {
            ("↑", 64), ("↓", 128), ("←", 32), ("→", 16), ("A", 1), ("B", 2), ("L", 512), ("R", 256),
            ("START", 8), ("SELECT", 4), ("X", 1024), ("Y", 2048), ("降低光照", 4096), ("提高光照", 8192)
        };
        private int pendingBinding;
        private bool configuringKeys;

        internal Dictionary<Keycode, int> HardwareBindings()
        {
            var bindings = new Dictionary<Keycode, int>(GameDisplay.DefaultKeys);
            foreach (var control in Controls)
            {
                var key = (Keycode)preferences.GetInt("key." + control.mask, 0);
                if (key == Keycode.Unknown) continue;
                foreach (var existing in bindings.Where(pair => pair.Value == control.mask).Select(pair => pair.Key).ToArray())
                    bindings.Remove(existing);
                bindings[key] = control.mask;
            }
            return bindings;
        }

        private void ShowKeySettings()
        {
            if (operation != null) return;
            configuringKeys = true;
            var page = Page();
            page.AddView(Button("返回", () => { configuringKeys = false; pendingBinding = 0; ShowHome(); }));
            page.AddView(Label("键盘与手柄键位", 24, Gold));
            var status = Label("选择一个动作，然后按下要绑定的实体键。", 14, Muted);
            page.AddView(status);
            var bindings = HardwareBindings();
            foreach (var control in Controls)
            {
                string keys = string.Join(" / ", bindings.Where(pair => pair.Value == control.mask).Select(pair => pair.Key));
                page.AddView(Button(control.name + "    " + keys, () =>
                {
                    pendingBinding = control.mask;
                    status.Text = "请按下用于 " + control.name + " 的实体键。";
                }));
            }
            page.AddView(Button("恢复默认", () =>
            {
                var update = preferences.Edit();
                foreach (var control in Controls) update.Remove("key." + control.mask);
                update.Apply();
                pendingBinding = 0;
                ShowKeySettings();
            }));
        }

        private bool CaptureBinding(KeyEvent e)
        {
            if (pendingBinding == 0 || e.KeyCode is Keycode.Back or Keycode.Unknown or Keycode.Home or Keycode.Power)
                return false;
            if (e.Action != KeyEventActions.Up) return true;
            var update = preferences.Edit();
            foreach (var control in Controls)
                if (preferences.GetInt("key." + control.mask, 0) == (int)e.KeyCode)
                    update.Remove("key." + control.mask);
            update.PutInt("key." + pendingBinding, (int)e.KeyCode).Apply();
            pendingBinding = 0;
            ShowKeySettings();
            return true;
        }
    }
}
