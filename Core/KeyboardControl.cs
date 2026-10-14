using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace VoicePTT.Core;

public sealed class KeyboardControl : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint LlkhfInjected = 0x10;
    private readonly HookProc _callback;
    private readonly Dispatcher _dispatcher;
    private IntPtr _hook;
    private HotkeySpec _hotkey = new(0x56, true, true, false, false);
    private bool _hotkeyDown;
    private bool _recording;
    private int _suppressReleaseVk;
    private int? _heldOutputVk;

    public event Action? HotkeyPressed;
    public event Action? HotkeyReleased;
    public event Action<HotkeySpec?>? HotkeyRecorded;

    public KeyboardControl(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _callback = HookCallback;
        _hook = SetWindowsHookEx(WhKeyboardLl, _callback, IntPtr.Zero, 0);
        if (_hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install the global keyboard listener.");
    }

    public void SetHotkey(HotkeySpec hotkey)
    {
        _hotkey = hotkey;
        _hotkeyDown = false;
    }

    public void BeginRecording() => _recording = true;

    public void CancelRecording() => _recording = false;

    public bool IsOutputHeld => _heldOutputVk is not null;

    public void PressOutput(int vk)
    {
        if (_heldOutputVk == vk) return;
        ReleaseOutput();
        SendKey(vk, false);
        _heldOutputVk = vk;
    }

    public void ReleaseOutput()
    {
        if (_heldOutputVk is not int vk) return;
        _heldOutputVk = null;
        SendKey(vk, true);
    }

    private IntPtr HookCallback(int code, IntPtr message, IntPtr parameter)
    {
        if (code < 0) return CallNextHookEx(_hook, code, message, parameter);
        var msg = message.ToInt32();
        var down = msg is WmKeyDown or WmSysKeyDown;
        var up = msg is WmKeyUp or WmSysKeyUp;
        if (!down && !up) return CallNextHookEx(_hook, code, message, parameter);
        var data = Marshal.PtrToStructure<KbdLlHookStruct>(parameter);
        if ((data.flags & LlkhfInjected) != 0) return CallNextHookEx(_hook, code, message, parameter);
        var vk = (int)data.vkCode;

        if (up && vk == _suppressReleaseVk)
        {
            _suppressReleaseVk = 0;
            return (IntPtr)1;
        }

        if (_recording && down && !IsModifier(vk))
        {
            _recording = false;
            _suppressReleaseVk = vk;
            HotkeySpec? shortcut = vk == 0x1B ? null : new HotkeySpec(vk, IsDown(0x11), IsDown(0x12), IsDown(0x10),
                IsDown(0x5B) || IsDown(0x5C));
            _dispatcher.BeginInvoke(() => HotkeyRecorded?.Invoke(shortcut));
            return (IntPtr)1;
        }

        if (vk == _hotkey.Vk)
        {
            if (down && (_hotkeyDown || MatchesModifiers(_hotkey)))
            {
                if (!_hotkeyDown)
                {
                    _hotkeyDown = true;
                    _dispatcher.BeginInvoke(() => HotkeyPressed?.Invoke());
                }
                return (IntPtr)1;
            }
            if (up && _hotkeyDown)
            {
                _hotkeyDown = false;
                _dispatcher.BeginInvoke(() => HotkeyReleased?.Invoke());
                return (IntPtr)1;
            }
        }
        return CallNextHookEx(_hook, code, message, parameter);
    }

    private static bool MatchesModifiers(HotkeySpec spec) =>
        IsDown(0x11) == spec.Ctrl && IsDown(0x12) == spec.Alt && IsDown(0x10) == spec.Shift &&
        (IsDown(0x5B) || IsDown(0x5C)) == spec.Win;

    private static bool IsModifier(int vk) => vk is 0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C;
    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private static void SendKey(int vk, bool release)
    {
        var mapped = MapVirtualKey((uint)vk, 4);
        var flags = mapped == 0 ? 0u : 0x0008u;
        if ((mapped & 0xFF00) != 0) flags |= 0x0001;
        if (release) flags |= 0x0002;
        var input = new Input
        {
            type = 1,
            ki = new KeyBdInput
            {
                wVk = mapped == 0 ? (ushort)vk : (ushort)0,
                wScan = (ushort)(mapped & 0xFF),
                dwFlags = flags,
                dwExtraInfo = IntPtr.Zero
            }
        };
        if (SendInput(1, ref input, Marshal.SizeOf<Input>()) != 1)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not send the PTT key.");
    }

    public void Dispose()
    {
        try { ReleaseOutput(); } catch { /* Continue shutdown. */ }
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyBdInput
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public KeyBdInput ki;
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, ref Input input, int size);
}
