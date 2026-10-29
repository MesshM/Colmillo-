using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Colmillo.Core.Input;
using Colmillo.Core.Macros;
using Keys = Colmillo.Core.Input.Keys;

namespace Colmillo.UI.Pages;

public partial class MacrosPage : PageBase
{
    // Mas pasos que esto no entran comodos en pantalla y no hacen falta para editar.
    const int MaxVisibleSteps = 400;

    Macro? _macro;
    MacroRecorder? _recorder;

    public MacrosPage()
    {
        InitializeComponent();
        Unloaded += (_, _) => _recorder?.Dispose();
        Reload();
    }

    public override void Reload()
    {
        Loading = true;
        var keep = _macro?.Id;
        List.Items.Clear();
        foreach (var m in Settings.Macros)
            List.Items.Add(new ListBoxItem { Content = MacroRow(m), Tag = m });
        Empty.Visibility = Settings.Macros.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var index = Settings.Macros.FindIndex(m => m.Id == keep);
        Loading = false;
        List.SelectedIndex = index >= 0 ? index : Settings.Macros.Count > 0 ? 0 : -1;
        if (List.SelectedIndex < 0) Show(null);
    }

    StackPanel MacroRow(Macro m)
    {
        var s = new StackPanel();
        s.Children.Add(new TextBlock { Text = m.Name, FontSize = 13.5, FontWeight = FontWeights.Bold, Foreground = Res("Ink800"), TextTrimming = TextTrimming.CharacterEllipsis });
        s.Children.Add(new TextBlock { Text = $"{m.Steps.Count} pasos", FontSize = 12, Foreground = Res("Ink400") });
        return s;
    }

    void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Loading) return;
        Show((List.SelectedItem as ListBoxItem)?.Tag as Macro);
    }

    void Show(Macro? m)
    {
        _macro = m;
        Editor.Visibility = m is null ? Visibility.Collapsed : Visibility.Visible;
        if (m is null) return;
        Loading = true;
        NameBox.Text = m.Name;
        ModeOnce.IsChecked = m.Mode == PlaybackMode.Once;
        ModeHeld.IsChecked = m.Mode == PlaybackMode.WhileHeld;
        ModeToggle.IsChecked = m.Mode == PlaybackMode.Toggle;
        ModeRepeat.IsChecked = m.Mode == PlaybackMode.Repeat;
        RepeatBox.Text = $"{m.RepeatCount}";
        RepeatBox.Visibility = m.Mode == PlaybackMode.Repeat ? Visibility.Visible : Visibility.Collapsed;
        Loading = false;
        RenderSteps();
    }

    void Changed()
    {
        if (Loading || _macro is null) return;
        App.Current.SaveSoon();
        if (List.SelectedItem is ListBoxItem item) item.Content = MacroRow(_macro);
        // El hook busca el macro por id en cada pulsacion: no hace falta re-asignar botones.
    }

    void New_Click(object sender, RoutedEventArgs e)
    {
        var m = new Macro { Name = $"Macro {Settings.Macros.Count + 1}" };
        Settings.Macros.Add(m);
        _macro = m;
        App.Current.SaveSoon();
        Reload();
    }

    async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_macro is null) return;
        var used = P.Buttons.Values.Any(b => b.MacroId == _macro.Id);
        var msg = used
            ? $"\"{_macro.Name}\" está asignado a un botón. Si lo borras, ese botón no va a hacer nada hasta que le asignes otra cosa."
            : $"Se borra \"{_macro.Name}\".";
        var macro = _macro;
        if (!await Confirm("Borrar macro", msg, "Borrar")) return;
        Settings.Macros.Remove(macro);
        _macro = null;
        App.Current.SaveSoon();
        if (used) _ = Device.ApplyButtonsAsync();
        Reload();
    }

    void Name_Changed(object sender, TextChangedEventArgs e)
    {
        if (Loading || _macro is null) return;
        _macro.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? "Sin nombre" : NameBox.Text.Trim();
        Changed();
    }

    void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (Loading || _macro is null) return;
        _macro.Mode = Enum.Parse<PlaybackMode>((string)((RadioButton)sender).Tag);
        RepeatBox.Visibility = _macro.Mode == PlaybackMode.Repeat ? Visibility.Visible : Visibility.Collapsed;
        Changed();
    }

    void Repeat_Changed(object sender, TextChangedEventArgs e)
    {
        if (Loading || _macro is null) return;
        if (int.TryParse(RepeatBox.Text, out var n)) _macro.RepeatCount = Math.Clamp(n, 1, 1000);
        Changed();
    }

    // ---------- Grabacion ----------

    void Record_Click(object sender, RoutedEventArgs e)
    {
        if (_macro is null) return;
        if (_recorder is null)
        {
            _recorder = new MacroRecorder { RecordDelays = RecDelays.IsChecked == true, RecordMouse = RecMouse.IsChecked == true };
            _recorder.Changed += () => Dispatcher.BeginInvoke(() =>
            {
                RecordText.Text = $"Detener ({_recorder?.Steps.Count ?? 0})";
            });
            _recorder.Start();
            RecordText.Text = "Detener (0)";
            RecordDot.Fill = Res("Danger");
            // Sin foco en un TextBox: si no, lo grabado tambien se escribiria en el nombre.
            Keyboard.ClearFocus();
        }
        else
        {
            var steps = _recorder.Stop(trimStopClick: true);
            _recorder.Dispose();
            _recorder = null;
            _macro.Steps.AddRange(steps);
            RecordText.Text = "Grabar";
            RecordDot.Fill = Res("OnCta");
            RenderSteps();
            Changed();
        }
    }

    // ---------- Pasos manuales ----------

    void AddKey_Click(object sender, RoutedEventArgs e)
    {
        KeyCatcher.Visibility = Visibility.Visible;
        KeyCatcher.Text = "Presiona una tecla...";
        KeyCatcher.Focus();
    }

    void KeyCatcher_KeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (_macro is null) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var vk = (ushort)KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return;
        AddSteps(new MacroStep { Kind = StepKind.KeyDown, Vk = vk }, new MacroStep { Kind = StepKind.KeyUp, Vk = vk });
        KeyCatcher.Visibility = Visibility.Collapsed;
    }

    void KeyCatcher_LostFocus(object sender, RoutedEventArgs e) => KeyCatcher.Visibility = Visibility.Collapsed;

    void AddText_Click(object sender, RoutedEventArgs e) =>
        AddSteps(new MacroStep { Kind = StepKind.Text, Text = "" });

    void AddDelay_Click(object sender, RoutedEventArgs e) =>
        AddSteps(new MacroStep { Kind = StepKind.Delay, DelayMs = 50 });

    void AddClick_Click(object sender, RoutedEventArgs e) =>
        AddSteps(new MacroStep { Kind = StepKind.MouseDown, Button = 1 }, new MacroStep { Kind = StepKind.MouseUp, Button = 1 });

    void AddSteps(params MacroStep[] steps)
    {
        if (_macro is null) return;
        _macro.Steps.AddRange(steps);
        RenderSteps();
        Changed();
    }

    void RenderSteps()
    {
        Steps.Items.Clear();
        if (_macro is null) return;
        NoSteps.Visibility = _macro.Steps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        for (var i = 0; i < _macro.Steps.Count && i < MaxVisibleSteps; i++)
            Steps.Items.Add(StepRow(_macro.Steps[i]));
        if (_macro.Steps.Count > MaxVisibleSteps)
            Steps.Items.Add(Text($"... y {_macro.Steps.Count - MaxVisibleSteps} pasos más", "Sub"));
    }

    UIElement StepRow(MacroStep step)
    {
        var (icon, text) = step.Kind switch
        {
            StepKind.KeyDown => ("", $"Presionar {Keys.Name(step.Vk)}"),
            StepKind.KeyUp => ("", $"Soltar {Keys.Name(step.Vk)}"),
            StepKind.MouseDown => ("", $"Presionar {MouseName(step.Button)}"),
            StepKind.MouseUp => ("", $"Soltar {MouseName(step.Button)}"),
            StepKind.Wheel => ("", step.Button > 0 ? $"Rueda arriba x{step.Button}" : $"Rueda abajo x{-step.Button}"),
            StepKind.Delay => ("", "Esperar"),
            StepKind.Text => ("", "Escribir"),
            _ => ("", ""),
        };

        var grid = new Grid { Margin = new Thickness(4, 2, 4, 2), Height = 34 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });

        var ic = new TextBlock { Style = (Style)FindResource("Icon"), Text = icon, FontSize = 13, Foreground = Res(step.Kind == StepKind.Delay ? "Ink400" : "Accent600") };
        var label = new TextBlock { Style = (Style)FindResource("Body"), Text = text, FontSize = 13, Margin = new Thickness(4, 0, 10, 0) };
        Grid.SetColumn(label, 1);
        grid.Children.Add(ic);
        grid.Children.Add(label);

        if (step.Kind == StepKind.Delay)
        {
            var box = new TextBox { Text = $"{step.DelayMs}", Width = 80, Height = 28, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Milisegundos" };
            box.TextChanged += (_, _) =>
            {
                if (int.TryParse(box.Text, out var ms)) step.DelayMs = Math.Clamp(ms, 0, 600_000);
                Changed();
            };
            var wrap = new StackPanel { Orientation = Orientation.Horizontal };
            wrap.Children.Add(box);
            wrap.Children.Add(new TextBlock { Style = (Style)FindResource("Sub"), Text = "ms", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
            Grid.SetColumn(wrap, 2);
            grid.Children.Add(wrap);
        }
        else if (step.Kind == StepKind.Text)
        {
            var box = new TextBox { Text = step.Text ?? "", Height = 28 };
            box.TextChanged += (_, _) =>
            {
                step.Text = box.Text;
                Changed();
            };
            Grid.SetColumn(box, 2);
            grid.Children.Add(box);
        }

        var del = new Button { Style = (Style)FindResource("IconButton"), Content = "", ToolTip = "Quitar paso", Width = 28, Height = 28 };
        del.Click += (_, _) =>
        {
            _macro?.Steps.Remove(step);
            RenderSteps();
            Changed();
        };
        Grid.SetColumn(del, 3);
        grid.Children.Add(del);
        return grid;
    }

    static string MouseName(int b) => b switch
    {
        1 => "clic izquierdo",
        2 => "clic derecho",
        3 => "clic de rueda",
        4 => "botón atrás",
        _ => "botón adelante",
    };

    // ---------- Prueba ----------

    void Test_Click(object sender, RoutedEventArgs e)
    {
        if (_macro is null || _macro.Steps.Count == 0) return;
        var macro = _macro;
        var left = 3;
        TestButton.IsEnabled = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            left--;
            TestText.Text = left > 0 ? $"Empieza en {left}..." : "Probar en 3 s";
            if (left > 0) return;
            timer.Stop();
            TestButton.IsEnabled = true;
            // Los modos con bucle se prueban una sola vuelta: no hay boton que los detenga.
            new MacroPlayer(new Macro { Steps = macro.Steps, Mode = macro.Mode == PlaybackMode.Repeat ? PlaybackMode.Repeat : PlaybackMode.Once, RepeatCount = macro.RepeatCount })
                .Start(loop: false);
        };
        TestText.Text = "Empieza en 3...";
        timer.Start();
    }
}
