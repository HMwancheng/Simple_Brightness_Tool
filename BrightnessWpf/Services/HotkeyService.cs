using System.Runtime.InteropServices;

namespace BrightnessWpf.Services;

/// <summary>
/// 全局热键。用低级键盘钩子（WH_KEYBOARD_LL）监听，与托盘滚轮用的鼠标钩子同机制，
/// 比 RegisterHotKey + 消息窗口在 WPF 下更可靠。
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;    // Alt
    private const int VK_SHIFT = 0x10;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;

    private uint _incMods, _incKey, _decMods, _decKey;
    private bool _hasInc, _hasDec;

    /// <summary>热键触发事件，参数 1=提升亮度，2=降低亮度。</summary>
    public event Action<int>? HotkeyPressed;

    public HotkeyService()
    {
        _proc = HookCallback;
    }

    public void Start(string hotkeyIncrease, string hotkeyDecrease)
    {
        _hasInc = TryParse(hotkeyIncrease, out _incMods, out _incKey) && _incKey != 0;
        _hasDec = TryParse(hotkeyDecrease, out _decMods, out _decKey) && _decKey != 0;

        using var process = System.Diagnostics.Process.GetCurrentProcess();
        using var module = process.MainModule;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module?.ModuleName ?? "user32"), 0);

        Log($"Start: inc={hotkeyIncrease}(has={_hasInc},mods={_incMods:X},key={_incKey:X}) dec={hotkeyDecrease}(has={_hasDec},mods={_decMods:X},key={_decKey:X}) hook={_hookId}");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

            uint mods = 0;
            if ((GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0) mods |= 0x0002; // Ctrl
            if ((GetAsyncKeyState(VK_MENU) & 0x8000) != 0) mods |= 0x0001;    // Alt
            if ((GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0) mods |= 0x0004;   // Shift
            if ((GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0) mods |= 0x0008; // Win

            Log($"KeyDown vk={info.vkCode:X} mods={mods:X}");

            if (_hasInc && info.vkCode == _incKey && mods == _incMods)
            {
                Log("MATCH increase");
                HotkeyPressed?.Invoke(1);
                return (IntPtr)1; // 吞掉按键，不传给前台应用
            }
            if (_hasDec && info.vkCode == _decKey && mods == _decMods)
            {
                Log("MATCH decrease");
                HotkeyPressed?.Invoke(2);
                return (IntPtr)1;
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static void Log(string msg)
    {
        try
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(AppContext.BaseDirectory, "hotkey_debug.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {msg}\r\n");
        }
        catch { }
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
                        mods |= 0x0002;
                        break;
                    case "alt":
                        mods |= 0x0001;
                        break;
                    case "shift":
                        mods |= 0x0004;
                        break;
                    case "win":
                    case "windows":
                        mods |= 0x0008;
                        break;
                    default:
                        key = (uint)Enum.Parse(typeof(HotkeyKeys), t, true);
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

    private enum HotkeyKeys
    {
        F1 = 0x70, F2 = 0x71, F3 = 0x72, F4 = 0x73, F5 = 0x74, F6 = 0x75,
        F7 = 0x76, F8 = 0x77, F9 = 0x78, F10 = 0x79, F11 = 0x7A, F12 = 0x7B,
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
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
