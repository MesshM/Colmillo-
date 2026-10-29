using Colmillo.Core.Macros;
using static Colmillo.Core.Input.InputNative;

namespace Colmillo.Core.Input;

/// <summary>Reproduce un macro con SendInput en un hilo propio. Se puede cancelar.</summary>
public sealed class MacroPlayer
{
    readonly Macro _macro;
    readonly CancellationTokenSource _cts = new();
    readonly HashSet<ushort> _keysDown = [];
    readonly HashSet<int> _buttonsDown = [];

    public bool Running { get; private set; }

    public MacroPlayer(Macro macro) => _macro = macro;

    /// <param name="loop">Repetir hasta que se llame a Stop (WhileHeld y Toggle).</param>
    public void Start(bool loop)
    {
        Running = true;
        var thread = new Thread(() => Run(loop)) { IsBackground = true, Name = "macro" };
        thread.Start();
    }

    public void Stop() => _cts.Cancel();

    void Run(bool loop)
    {
        var token = _cts.Token;
        try
        {
            var times = _macro.Mode == PlaybackMode.Repeat ? Math.Max(1, _macro.RepeatCount) : 1;
            do
            {
                for (var n = 0; n < times && !token.IsCancellationRequested; n++)
                    foreach (var step in _macro.Steps)
                    {
                        if (token.IsCancellationRequested) break;
                        Play(step, token);
                    }
                // Un macro sin demoras en bucle saturaria la cola de entrada.
                if (loop && !token.IsCancellationRequested) token.WaitHandle.WaitOne(15);
            } while (loop && !token.IsCancellationRequested);
        }
        finally
        {
            // Nunca dejar una tecla o un boton "pegado" si el macro se corto a mitad.
            foreach (var vk in _keysDown) Send(Key(vk, up: true));
            foreach (var b in _buttonsDown) Send(Mouse(b, up: true));
            Running = false;
        }
    }

    void Play(MacroStep step, CancellationToken token)
    {
        switch (step.Kind)
        {
            case StepKind.KeyDown:
                Send(Key(step.Vk, up: false));
                _keysDown.Add(step.Vk);
                break;
            case StepKind.KeyUp:
                Send(Key(step.Vk, up: true));
                _keysDown.Remove(step.Vk);
                break;
            case StepKind.MouseDown:
                Send(Mouse(step.Button, up: false));
                _buttonsDown.Add(step.Button);
                break;
            case StepKind.MouseUp:
                Send(Mouse(step.Button, up: true));
                _buttonsDown.Remove(step.Button);
                break;
            case StepKind.Wheel:
                Send(Wheel(step.Button));
                break;
            case StepKind.Delay:
                token.WaitHandle.WaitOne(Math.Max(0, step.DelayMs));
                break;
            case StepKind.Text:
                foreach (var c in step.Text ?? "")
                    Send(Unicode(c, up: false), Unicode(c, up: true));
                break;
        }
    }
}
