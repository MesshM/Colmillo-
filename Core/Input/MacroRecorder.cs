using System.Diagnostics;
using System.Runtime.InteropServices;
using Colmillo.Core.Macros;
using static Colmillo.Core.Input.InputNative;

namespace Colmillo.Core.Input;

/// <summary>
/// Graba teclado (y opcionalmente botones del mouse) con hooks de bajo nivel.
/// Los hooks viven solo mientras se graba.
/// </summary>
public sealed class MacroRecorder : IDisposable
{
    readonly HookProc _kbProc;
    readonly HookProc _msProc;
    readonly Stopwatch _clock = new();
    readonly HashSet<uint> _down = [];
    IntPtr _kbHook, _msHook;
    long _last;

    public List<MacroStep> Steps { get; } = [];
    public bool RecordDelays { get; set; } = true;
    public bool RecordMouse { get; set; }
    public event Action? Changed;

    public MacroRecorder()
    {
        _kbProc = OnKey;
        _msProc = OnMouse;
    }

    public void Start()
    {
        Steps.Clear();
        _down.Clear();
        _clock.Restart();
        _last = 0;
        var mod = GetModuleHandle(null);
        _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, mod, 0);
        if (RecordMouse) _msHook = SetWindowsHookEx(WH_MOUSE_LL, _msProc, mod, 0);
    }

    /// <summary>Detiene la grabacion y devuelve los pasos.</summary>
    /// <param name="trimStopClick">Quitar el ultimo clic: es el que se hizo sobre "Detener".</param>
    public List<MacroStep> Stop(bool trimStopClick)
    {
        Unhook();
        if (trimStopClick && RecordMouse)
        {
            var i = Steps.FindLastIndex(s => s.Kind == StepKind.MouseDown && s.Button == 1);
            if (i >= 0) Steps.RemoveRange(i, Steps.Count - i);
            while (Steps.Count > 0 && Steps[^1].Kind == StepKind.Delay) Steps.RemoveAt(Steps.Count - 1);
        }
        return [.. Steps];
    }

    void Add(MacroStep step)
    {
        var now = _clock.ElapsedMilliseconds;
        if (RecordDelays && Steps.Count > 0 && now - _last > 0)
            Steps.Add(new MacroStep { Kind = StepKind.Delay, DelayMs = (int)(now - _last) });
        _last = now;
        Steps.Add(step);
        Changed?.Invoke();
    }

    IntPtr OnKey(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var msg = (int)wParam;
            if ((info.flags & LLKHF_INJECTED) == 0)
            {
                if (msg is WM_KEYDOWN or WM_SYSKEYDOWN && _down.Add(info.vkCode))
                    Add(new MacroStep { Kind = StepKind.KeyDown, Vk = (ushort)info.vkCode });
                else if (msg is WM_KEYUP or WM_SYSKEYUP && _down.Remove(info.vkCode))
                    Add(new MacroStep { Kind = StepKind.KeyUp, Vk = (ushort)info.vkCode });
            }
        }
        return CallNextHookEx(_kbHook, code, wParam, lParam);
    }

    IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if ((info.flags & LLMHF_INJECTED) == 0)
            {
                var x = (int)(info.mouseData >> 16) == 1 ? 4 : 5;
                MacroStep? step = (int)wParam switch
                {
                    WM_LBUTTONDOWN => new() { Kind = StepKind.MouseDown, Button = 1 },
                    WM_LBUTTONUP => new() { Kind = StepKind.MouseUp, Button = 1 },
                    WM_RBUTTONDOWN => new() { Kind = StepKind.MouseDown, Button = 2 },
                    WM_RBUTTONUP => new() { Kind = StepKind.MouseUp, Button = 2 },
                    WM_MBUTTONDOWN => new() { Kind = StepKind.MouseDown, Button = 3 },
                    WM_MBUTTONUP => new() { Kind = StepKind.MouseUp, Button = 3 },
                    WM_XBUTTONDOWN => new() { Kind = StepKind.MouseDown, Button = x },
                    WM_XBUTTONUP => new() { Kind = StepKind.MouseUp, Button = x },
                    WM_MOUSEWHEEL => new() { Kind = StepKind.Wheel, Button = (short)(info.mouseData >> 16) / 120 },
                    _ => null,
                };
                if (step is not null) Add(step);
            }
        }
        return CallNextHookEx(_msHook, code, wParam, lParam);
    }

    void Unhook()
    {
        if (_kbHook != IntPtr.Zero) UnhookWindowsHookEx(_kbHook);
        if (_msHook != IntPtr.Zero) UnhookWindowsHookEx(_msHook);
        _kbHook = _msHook = IntPtr.Zero;
    }

    public void Dispose() => Unhook();
}
