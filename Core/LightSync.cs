namespace Colmillo.Core;

/// <summary>
/// Arranque simultaneo de un efecto en mouse, RAM y grafica ("todos juntos"). Cada uno
/// prepara su cambio (la RAM tarda ~0.4 s, la grafica ~0.5 s, el mouse unos ms) y espera
/// aca antes del comando que arranca la animacion; cuando llegan todos, salen juntos. Antes
/// cada uno arrancaba al terminar su preparacion y la respiracion quedaba desfasada.
/// </summary>
public sealed class LightSync(int participants)
{
    static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(3);
    readonly Barrier _barrier = new(participants);

    /// <summary>Llega al punto de arranque y espera a los demas (como mucho 3 s).</summary>
    public void Arrive()
    {
        try { _barrier.SignalAndWait(MaxWait); }
        catch (InvalidOperationException) { }
    }

    /// <summary>Este dispositivo no va a llegar (no esta conectado): los demas no lo esperan.</summary>
    public void Leave()
    {
        try { _barrier.RemoveParticipant(); }
        catch (InvalidOperationException) { }
    }

    /// <summary>Corre <paramref name="work"/> y, si nunca llego al arranque, se retira.</summary>
    public static async Task Join(LightSync? sync, Func<Action, Task> work)
    {
        var arrived = false;
        try
        {
            await work(() =>
            {
                arrived = true;
                sync?.Arrive();
            });
        }
        finally
        {
            if (!arrived) sync?.Leave();
        }
    }
}
