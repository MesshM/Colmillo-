using System.IO;

namespace Colmillo.Core;

/// <summary>
/// Registro corto de lo que pasa con la RAM y la grafica (busquedas, reintentos, modulos
/// que dejan de responder), en %APPDATA%\Colmillo\dispositivos.log. Pasado 256 KB se
/// empieza de nuevo.
/// </summary>
public static class DeviceLog
{
    const long MaxBytes = 256 * 1024;
    static readonly object Gate = new();
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Colmillo", "dispositivos.log");

    public static void Write(string message)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes) File.Delete(FilePath);
                File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
