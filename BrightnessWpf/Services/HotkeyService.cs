using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace BrightnessWpf.Services;

/// <summary>
/// 全局热键。迁移自原版 HotkeyMessageWindow（RegisterHotKey + 消息窗口）。
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    private readonly HotkeyWindow _window;
    private readonly List<int> _registeredIds = new();

    /// <summary>热键触发事件，参数为注册时的 ID。</summary>
    public event Action<int>? HotkeyPressed;

    public HotkeyService()
    {
        _window = new HotkeyWindow(this);
    }

    /// <summary>注册热键，如 "Ctrl+F5"。</summary>
    public bool Register(string hotkeyString, int id)
    {
        if (string.IsNullOrWhiteSpace(hotkeyString)) return false;
        if (!TryParse(hotkeyString, out uint mods, out uint key) || key == 0) return false;

        bool ok = RegisterHotKey(_window.Handle, id, mods, key);
        if (ok) _registeredIds.Add(id);
        return ok;
    }

    private static bool TryParse(string hotkeyString, out uint mods, out uint key)
    {
        mods = 0;
        key = 0;
        try
        {
            foreach (var part in hotkeyString.Split('+'))
            {
                var t = part.Trim();
                switch (t.ToLower())
                {
                    case "ctrl":
                    case "control":
                        mods |= MOD_CONTROL;
                        break;
                    case "alt":
                        mods |= MOD_ALT;
                        break;
                    case "shift":
                        mods |= MOD_SHIFT;
                        break;
                    case "win":
                    case "windows":
                        mods |= MOD_WIN;
                        break;
                    default:
                        key = (uint)Enum.Parse(typeof(Forms.Keys), t, true);
                        break;
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        foreach (int id in _registeredIds)
            UnregisterHotKey(_window.Handle, id);
        _registeredIds.Clear();
        _window.Destroy();
    }

    // 消息窗口，接收 WM_HOTKEY
    private sealed class HotkeyWindow : Forms.NativeWindow
    {
        private readonly HotkeyService _owner;

        public HotkeyWindow(HotkeyService owner)
        {
            _owner = owner;
            CreateHandle(new Forms.CreateParams
            {
                ExStyle = 0x80,           // WS_EX_TOOLWINDOW（消息窗口）
                Style = unchecked((int)0x80000000), // WS_POPUP
                Width = 0,
                Height = 0
            });
        }

        protected override void WndProc(ref Forms.Message m)
        {
            if (m.Msg == WM_HOTKEY)
                _owner.HotkeyPressed?.Invoke(m.WParam.ToInt32());
            base.WndProc(ref m);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
