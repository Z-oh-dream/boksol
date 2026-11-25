using System;
using System.Collections.Generic;
using Android.Graphics;
using Android.Views;
using Android.Widget;

namespace Boksol
{
    internal sealed class GameDisplay : View
    {
        private readonly MainActivity owner;
        private readonly CoreSession session;
        private readonly int[] pixels;
        private readonly Bitmap bitmap;
        private readonly RectF screen = new RectF();
        private readonly Paint paint = new Paint();
        private readonly Dictionary<Keycode, int> bindings;
        private readonly HashSet<Keycode> held = new HashSet<Keycode>();
        private int virtualKeys, touch = -1, touchPointer = -1;

        internal static readonly Dictionary<Keycode, int> DefaultKeys = new Dictionary<Keycode, int>
        {
            [Keycode.ButtonA] = 1, [Keycode.ButtonB] = 2, [Keycode.ButtonSelect] = 4, [Keycode.ButtonStart] = 8,
            [Keycode.DpadRight] = 16, [Keycode.DpadLeft] = 32, [Keycode.DpadUp] = 64, [Keycode.DpadDown] = 128,
            [Keycode.ButtonR1] = 256, [Keycode.ButtonL1] = 512, [Keycode.ButtonX] = 1024, [Keycode.ButtonY] = 2048,
            [Keycode.Z] = 1, [Keycode.X] = 2, [Keycode.Del] = 4, [Keycode.Enter] = 8,
            [Keycode.S] = 256, [Keycode.A] = 512, [Keycode.V] = 1024, [Keycode.C] = 2048,
            [Keycode.F11] = 4096, [Keycode.F12] = 8192
        };

        public GameDisplay(MainActivity owner, CoreSession session) : base(owner)
        {
            this.owner = owner;
            this.session = session;
            bindings = owner.HardwareBindings();
            pixels = new int[session.Width * session.Height];
            bitmap = Bitmap.CreateBitmap(session.Width, session.Height, Bitmap.Config.Argb8888);
            ContentDescription = session.IsDs ? "DS 双屏游戏画面，下屏可触控" : "GBA 游戏画面";
        }

        protected override void OnSizeChanged(int width, int height, int oldWidth, int oldHeight)
        {
            base.OnSizeChanged(width, height, oldWidth, oldHeight);
            float ratio = Math.Min(width / (float)session.Width, height / (float)session.Height);
            float left = (width - session.Width * ratio) / 2;
            float top = (height - session.Height * ratio) / 2;
            screen.Set(left, top, width - left, height - top);
        }

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            canvas.DrawColor(Color.Black);
            if (session.CopyPixels(pixels))
            {
                bitmap.SetPixels(pixels, 0, session.Width, 0, 0, session.Width, session.Height);
                canvas.DrawBitmap(bitmap, null, screen, paint);
            }
            if (!session.Completion.IsCompleted) PostInvalidateDelayed(16);
        }

        public override bool OnTouchEvent(MotionEvent e)
        {
            if (!session.IsDs || screen.IsEmpty) return false;
            int index = e.ActionIndex;
            if (e.ActionMasked is MotionEventActions.Down or MotionEventActions.PointerDown)
            {
                if (touchPointer >= 0) return true;
                float x = e.GetX(index), y = e.GetY(index);
                if (!screen.Contains(x, y) || y < screen.CenterY()) return false;
                touchPointer = e.GetPointerId(index);
                SetTouch(x, y);
            }
            else if (e.ActionMasked == MotionEventActions.Move)
            {
                index = e.FindPointerIndex(touchPointer);
                if (index >= 0) SetTouch(e.GetX(index), e.GetY(index));
            }
            else if (e.ActionMasked is MotionEventActions.Up or MotionEventActions.Cancel ||
                (e.ActionMasked == MotionEventActions.PointerUp && e.GetPointerId(index) == touchPointer))
            {
                touchPointer = -1;
                touch = -1;
                SendInput();
            }
            return true;
        }

        private void SetTouch(float x, float y)
        {
            int px = Math.Clamp((int)((x - screen.Left) * 256 / screen.Width()), 0, 255);
            int py = Math.Clamp((int)((y - screen.CenterY()) * 384 / screen.Height()), 0, 191);
            touch = px | (py << 8);
            SendInput();
        }

        internal LinearLayout MakeControls()
        {
            var controls = new LinearLayout(owner) { Orientation = Orientation.Vertical };
            controls.AddView(MakeRow(("L", 512), ("↑", 64), ("R", 256), ("A", 1), ("B", 2)));
            controls.AddView(MakeRow(("←", 32), ("↓", 128), ("→", 16),
                session.IsDs ? ("X", 1024) : ("光 −", 4096), session.IsDs ? ("Y", 2048) : ("光 +", 8192)));
            controls.AddView(MakeRow(("SELECT", 4), ("START", 8)));
            return controls;
        }

        private LinearLayout MakeRow(params (string title, int mask)[] buttons)
        {
            var row = new LinearLayout(owner) { Orientation = Orientation.Horizontal };
            foreach (var (title, mask) in buttons)
            {
                var button = owner.Button(title, () =>
                {
                    if (mask >= 4096) AdjustLight(mask);
                    else _ = session.Press(mask, 100, System.Threading.CancellationToken.None);
                });
                button.SetPadding(0, 0, 0, 0);
                button.SetOnTouchListener(new HoldButton(
                    () =>
                    {
                        if (mask >= 4096) AdjustLight(mask);
                        else { virtualKeys |= mask; SendInput(); }
                    },
                    () =>
                    {
                        virtualKeys &= ~mask;
                        SendInput();
                    }));
                row.AddView(button, new LinearLayout.LayoutParams(0, owner.Dp(46), 1));
            }
            return row;
        }

        internal bool HandleKey(KeyEvent e)
        {
            if (!bindings.TryGetValue(e.KeyCode, out int mask)) return false;
            if (mask >= 4096)
            {
                if (session.IsDs) return false;
                if (e.Action == KeyEventActions.Down && e.RepeatCount == 0) AdjustLight(mask);
            }
            else
            {
                if (!session.IsDs && mask is 1024 or 2048) return false;
                if (e.Action == KeyEventActions.Down) held.Add(e.KeyCode);
                else if (e.Action == KeyEventActions.Up) held.Remove(e.KeyCode);
                SendInput();
            }
            return true;
        }

        internal void ReleaseInput()
        {
            held.Clear();
            virtualKeys = 0;
            touch = touchPointer = -1;
            SendInput();
        }

        private void AdjustLight(int mask) => session.AdjustSunlight(mask == 4096 ? -1 : 1);
        private void SendInput()
        {
            int keys = virtualKeys;
            foreach (Keycode key in held) keys |= bindings[key];
            session.Input(keys, touch);
        }

        private sealed class HoldButton : Java.Lang.Object, IOnTouchListener
        {
            private readonly Action press, release;

            public HoldButton(Action press, Action release)
            {
                this.press = press;
                this.release = release;
            }

            public bool OnTouch(View view, MotionEvent e)
            {
                if (e.ActionMasked == MotionEventActions.Down)
                {
                    view.Pressed = true;
                    press();
                }
                else if (e.ActionMasked is MotionEventActions.Up or MotionEventActions.Cancel)
                {
                    view.Pressed = false;
                    release();
                }
                return true;
            }
        }
    }
}
