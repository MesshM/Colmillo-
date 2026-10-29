using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Colmillo.Core;
using Colmillo.Shell;

namespace Colmillo.UI.Pages;

public partial class SettingsPage : PageBase
{
    public SettingsPage()
    {
        InitializeComponent();
        Reload();
    }

    public override void Reload()
    {
        Loading = true;
        ThemeSystem.IsChecked = Settings.Theme == "sistema";
        ThemeLight.IsChecked = Settings.Theme == "claro";
        ThemeDark.IsChecked = Settings.Theme == "oscuro";
        Startup.IsChecked = Settings.StartWithWindows;
        Tray.IsChecked = Settings.CloseToTray;
        var v = typeof(App).Assembly.GetName().Version;
        VersionText.Text = $"Colmillo {v?.Major}.{v?.Minor}.{v?.Build}";
        BuildInfo();
        Loading = false;
    }

    void BuildInfo()
    {
        Info.Children.Clear();
        Info.RowDefinitions.Clear();
        (string, string)[] rows =
        [
            ("Estado", Device.Connected ? "Conectado por USB" : "Desconectado"),
            ("Firmware", Device.Connected ? Device.Firmware : "-"),
            ("Memoria interna", "1 perfil (DPI, luz y botones)"),
            ("Synapse", DeviceService.SynapseRunning() ? "Abierto (conviene cerrarlo)" : "Cerrado"),
            ("Memoria de Colmillo", $"{SystemIntegration.WorkingSetMb()} MB en uso"),
        ];
        for (var i = 0; i < rows.Length; i++)
        {
            Info.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
            var k = Text(rows[i].Item1, "Sub");
            k.VerticalAlignment = VerticalAlignment.Center;
            var v = Text(rows[i].Item2, "Body");
            Grid.SetRow(k, i);
            Grid.SetRow(v, i);
            Grid.SetColumn(v, 1);
            Info.Children.Add(k);
            Info.Children.Add(v);
        }
    }

    void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (Loading) return;
        Settings.Theme = (string)((RadioButton)sender).Tag;
        App.Current.ApplyTheme();
        App.Current.SaveSoon();
    }

    void Startup_Changed(object sender, RoutedEventArgs e)
    {
        if (Loading) return;
        Settings.StartWithWindows = Startup.IsChecked == true;
        SystemIntegration.SetStartWithWindows(Settings.StartWithWindows);
        App.Current.SaveSoon();
    }

    void Tray_Changed(object sender, RoutedEventArgs e)
    {
        if (Loading) return;
        Settings.CloseToTray = Tray.IsChecked == true;
        App.Current.SaveSoon();
    }

    async void Reapply_Click(object sender, RoutedEventArgs e)
    {
        await Device.ConnectAndApplyAsync();
        BuildInfo();
    }

    async void Factory_Click(object sender, RoutedEventArgs e)
    {
        if (!await Confirm("Restaurar valores de fábrica",
                "Se vuelven a poner las etapas de DPI, la luz y todos los botones como vienen de fábrica. Los macros no se borran.",
                "Restaurar")) return;
        Settings.Device = new DeviceProfile();
        App.Current.SaveSoon();
        await Device.ConnectAndApplyAsync();
        BuildInfo();
    }

    UpdateInfo? _update;

    /// <summary>Unico momento en que Colmillo se conecta a internet: cuando el usuario lo pide.</summary>
    async void Updates_Click(object sender, RoutedEventArgs e)
    {
        CheckButton.IsEnabled = false;
        InstallButton.Visibility = Visibility.Collapsed;
        UpdateText.Text = "Buscando en GitHub...";
        try
        {
            _update = await Updater.CheckAsync();
            if (_update is null)
            {
                UpdateText.Text = "Ya tienes la última versión.";
            }
            else
            {
                UpdateText.Text = $"Hay una versión nueva: {_update.Tag}. Se descarga, se verifica y se instala sola.";
                InstallButton.Content = $"Instalar {_update.Tag}";
                InstallButton.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            UpdateText.Text = "No se pudo consultar GitHub. Revisa la conexión e intenta de nuevo.";
        }
        finally
        {
            CheckButton.IsEnabled = true;
        }
    }

    async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_update is null) return;
        InstallButton.IsEnabled = CheckButton.IsEnabled = false;
        try
        {
            var progress = new Progress<double>(p => UpdateText.Text = $"Descargando {_update.Tag}... {(int)(p * 100)} %");
            var setup = await Updater.DownloadAsync(_update, progress);
            UpdateText.Text = "Instalando... Colmillo se vuelve a abrir solo.";
            App.Current.InstallUpdate(setup);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or InvalidDataException or IOException)
        {
            UpdateText.Text = ex is InvalidDataException ? ex.Message : "No se pudo descargar la actualización. Intenta de nuevo.";
            InstallButton.IsEnabled = CheckButton.IsEnabled = true;
        }
    }

    void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Colmillo");
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true })?.Dispose();
    }
}
