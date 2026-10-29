using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Colmillo.Core.Macros;
using Colmillo.Core.Razer;
using static Colmillo.Core.Input.InputNative;

namespace Colmillo.Core.Input;

/// <summary>
/// Ejecuta las acciones que el Cobra no puede guardar (macros, abrir programas).
/// El firmware manda una tecla F13-F24; un hook de teclado de bajo nivel la
/// intercepta, la traga y corre la accion. El hook solo existe mientras haya
/// alguna accion asignada: sin macros, Colmillo no mira el teclado.
/// </summary>
public sealed class SoftwareActions : IDisposable
{
    readonly Dictionary<uint, ButtonAction> _bindings = [];
    readonly HashSet<uint> _held = [];
    readonly Dictionary<uint, MacroPlayer> _running = [];
    readonly HookProc _proc;
    IntPtr _hook;
    Func<string, Macro?> _findMacro = _ => null;

    public SoftwareActions() => _proc = Callback;

    /// <param name="bindings">Uso HID (0x68-0x73) -> accion.</param>
    public void Update(IReadOnlyDictionary<byte, ButtonAction> bindings, Func<string, Macro?> findMacro)
    {
        StopAll();
        _bindings.Clear();
        _findMacro = findMacro;
        // Uso HID F13 (0x68) -> VK_F13 (0x7C): los dos rangos son contiguos.
        foreach (var (usage, action) in bindings)
            _bindings[(uint)(0x7C + usage - CobraButtons.FirstSoftwareKey)] = action;

        if (_bindings.Count > 0 && _hook == IntPtr.Zero)
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        else if (_bindings.Count == 0 && _hook != IntPtr.Zero)
            Unhook();
    }

    IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (info.dwExtraInfo != Signature && _bindings.TryGetValue(info.vkCode, out var action))
            {
                var msg = (int)wParam;
                if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
                {
                    // Windows repite el keydown mientras se mantiene: solo cuenta el primero.
                    if (_held.Add(info.vkCode)) Pressed(info.vkCode, action);
                }
                else if (msg is WM_KEYUP or WM_SYSKEYUP)
                {
                    _held.Remove(info.vkCode);
                    Released(info.vkCode, action);
                }
                return 1; // tragada: la tecla F13+ nunca llega a las aplicaciones
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    void Pressed(uint vk, ButtonAction action)
    {
        if (action.Kind == ActionKind.Launch)
        {
            Launch(action.LaunchPath);
            return;
        }
        if (action.MacroId is null || _findMacro(action.MacroId) is not { } macro) return;

        if (_running.Remove(vk, out var current) && current.Running)
        {
            current.Stop();
            // En Toggle, la segunda pulsacion solo detiene.
            if (macro.Mode == PlaybackMode.Toggle) return;
        }

        var player = new MacroPlayer(macro);
        _running[vk] = player;
        player.Start(loop: macro.Mode is PlaybackMode.WhileHeld or PlaybackMode.Toggle);
    }

    void Released(uint vk, ButtonAction action)
    {
        if (action.MacroId is null || _findMacro(action.MacroId) is not { } macro) return;
        if (macro.Mode == PlaybackMode.WhileHeld && _running.Remove(vk, out var player))
            player.Stop();
    }

    static void Launch(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        // Se lanza fuera del hilo del hook: si Process.Start tarda, Windows desinstala
        // los hooks que superan LowLevelHooksTimeout.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or FileNotFoundException)
            {
                // Ruta borrada o movida: no hay a quien avisar desde la bandeja.
            }
        });
    }

    void StopAll()
    {
        foreach (var p in _running.Values) p.Stop();
        _running.Clear();
        _held.Clear();
    }

    void Unhook()
    {
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    public void Dispose()
    {
        StopAll();
        if (_hook != IntPtr.Zero) Unhook();
    }
}
