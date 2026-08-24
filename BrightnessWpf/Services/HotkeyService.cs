using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace BrightnessWpf.Services;

/// <summary>
/// 全局热键。使用 HwndSource 接收 WM_HOTKEY（WPF 消息泵原生支持，比 WinForms NativeWindow 更可靠）。
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    private HwndSource? _source;
    private readonly List<int> _registeredIds = new();

    /// <summary>热键触发事件，参数为注册时的 ID。</summary>
    public event Action<int>? HotkeyPressed;

    /// <summary>注册"提升亮度"和"降低亮度"热键（ID 分别为 1、2）。</summary>
    public void Start(string hotkeyIncrease, string hotkeyDecrease)
    {
        _source = new HwndSource(new HwndSourceParameters("HMSimpleBrightnessHotkeyWindow")
        {
            Width = 0,
            Height = 0,
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP
            ExtendedWindowStyle = 0x80                // WS_EX_TOOLWINDOW（消息窗口）
        });
        _source.AddHook(WndProc);
        Register(hotkeyIncrease, 1);
        Register(hotkeyDecrease, 2);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            HotkeyPressed?.Invoke(wParam.ToInt32());
            handled = true;
        }
        return IntPtr.Zero;
    }

    private bool Register(string hotkeyString, int id)
    {
        if (string.IsNullOrWhiteSpace(hotkeyString) || _source == null) return false;
        if (!TryParse(hotkeyString, out uint mods, out uint key) || key == 0) return false;

        bool ok = RegisterHotKey(_source.Handle, id, mods, key);
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
                        key = (uint)Enum.Parse(typeof(FormsKeys), t, true);
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

    // 避免直接依赖 WinForms 的 Keys，用本地枚举 + 值解析
    private enum FormsKeys
    {
        F5 = 0x74,
        F6 = 0x75,
        F1 = 0x70,
        F2 = 0x71,
        F3 = 0x72,
        F4 = 0x73,
        F7 = 0x76,
        F8 = 0x77,
        F9 = 0x78,
        F10 = 0x79,
        F11 = 0x7A,
        F12 = 0x7B,
        A = 0x41, B = 0x42, C = 0x43, D = 0x44, E = 0x45, F = 0x46,
        G = 0x47, H = 0x48, I = 0x49, J = 0x4A, K = 0x4B, L = 0x4C,
        M = 0x4D, N = 0x4E, O = 0x4F, P = 0x50, Q = 0x51, R = 0x52,
        S = 0x53, T = 0x54, U = 0x55, V = 0x56, W = 0x57, X = 0x58,
        Y = 0x59, Z = 0x5A,
        D0 = 0x30, D1 = 0x31, D2 = 0x32, D3 = 0x33, D4 = 0x34,
        D5 = 0x35, D6 = 0x36, D7 = 0x37, D8 = 0x38, D9 = 0x39
    }

    public void Dispose()
    {
        if (_source != null)
        {
            foreach (int id in _registeredIds)
                UnregisterHotKey(_source.Handle, id);
            _registeredIds.Clear();
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
