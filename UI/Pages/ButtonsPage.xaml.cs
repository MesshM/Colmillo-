using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Colmillo.Core.Input;
using Colmillo.Core.Razer;
using Colmillo.UI.Controls;
using Microsoft.Win32;

namespace Colmillo.UI.Pages;

public partial class ButtonsPage : PageBase
{
    // Posicion de cada boton sobre el dibujo (lienzo de 200x320).
    static readonly Dictionary<byte, Point> Spots = new()
    {
        [1] = new(60, 72), [2] = new(140, 72), [9] = new(100, 38), [3] = new(100, 63),
        [10] = new(100, 88), [5] = new(10, 138), [4] = new(10, 176), [96] = new(100, 292),
    };

    bool _hypershift;
    CobraButtons.Info? _selected;

    public ButtonsPage()
    {
        InitializeComponent();
        KindBox.ItemsSource = ActionCatalog.Kinds;
        Drawing.ShowEffect(P.Effect, ColorPicker.Parse(P.Color1), ColorPicker.Parse(P.Color2), P.Brightness);
        Reload();
    }

    public override void Reload()
    {
        Loading = true;
        LayerHint.Text = _hypershift
            ? "Activa mientras mantienes apretado el botón asignado a Hypershift."
            : P.Buttons.Values.Any(b => b.Kind == ActionKind.Hypershift) ? "" : "Para usar la otra capa, asigna Hypershift a algún botón.";
        var keep = _selected;
        List.Items.Clear();
        foreach (var info in CobraButtons.All)
            List.Items.Add(Row(info));
        List.SelectedIndex = keep is null ? 1 : Array.FindIndex(CobraButtons.All, b => b.Id == keep.Id);
        Loading = false;
        DrawMarkers();
    }

    ButtonAction Current(CobraButtons.Info info) => P.Resolve(info.Id, _hypershift);

    string Describe(ButtonAction a) =>
        ActionCatalog.Describe(a, id => Settings.Macros.FirstOrDefault(m => m.Id == id)?.Name);

    ListBoxItem Row(CobraButtons.Info info)
    {
        var custom = P.Buttons.TryGetValue(Core.DeviceProfile.Key(info.Id, _hypershift), out var a) && a.Kind != ActionKind.Default;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var badge = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(15),
            Background = Res(custom ? "AccentSoft" : "Fog"),
            Margin = new Thickness(0, 0, 12, 0),
            Child = new TextBlock
            {
                Text = info.Short.Length > 3 ? info.Short[..3] : info.Short,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = Res(custom ? "Accent600" : "Ink500"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = info.Name, FontSize = 13.5, FontWeight = FontWeights.Bold, Foreground = Res("Ink800") });
        texts.Children.Add(new TextBlock
        {
            Text = info.Remappable ? Describe(Current(info)) : "Clic izquierdo (fijo)",
            FontSize = 12,
            Foreground = Res(custom ? "Accent600" : "Ink400"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(texts, 1);
        grid.Children.Add(badge);
        grid.Children.Add(texts);
        return new ListBoxItem { Content = grid, Tag = info, IsEnabled = info.Remappable };
    }

    void DrawMarkers()
    {
        Markers.Children.Clear();
        foreach (var (id, pt) in Spots)
        {
            var selected = _selected?.Id == id;
            var dot = new Ellipse
            {
                Width = selected ? 18 : 12,
                Height = selected ? 18 : 12,
                Fill = selected ? Res("CtaFill") : new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                Stroke = selected ? Brushes.White : new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)),
                StrokeThickness = selected ? 2.5 : 1,
                Cursor = Cursors.Hand,
                ToolTip = CobraButtons.Get(id).Name,
            };
            if (id == 96) dot.StrokeDashArray = [2, 2];
            Canvas.SetLeft(dot, pt.X - dot.Width / 2);
            Canvas.SetTop(dot, pt.Y - dot.Height / 2);
            dot.MouseLeftButtonUp += (_, _) =>
                List.SelectedIndex = Array.FindIndex(CobraButtons.All, b => b.Id == id);
            Markers.Children.Add(dot);
        }
    }

    void Layer_Checked(object sender, RoutedEventArgs e)
    {
        if (List is null) return;
        _hypershift = LayerHyper.IsChecked == true;
        Reload();
    }

    void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_swappingRow || List.SelectedItem is not ListBoxItem { Tag: CobraButtons.Info info }) return;
        _selected = info;
        var wasLoading = Loading;
        Loading = true;
        EditorTitle.Text = info.Name;
        EditorSub.Text = _hypershift ? "Capa Hypershift" : "Capa normal";
        var current = P.Buttons.TryGetValue(Core.DeviceProfile.Key(info.Id, _hypershift), out var a) ? a : new ButtonAction();
        KindBox.SelectedItem = ActionCatalog.Kinds.First(k => k.Value == current.Kind);
        BuildDetail(current);
        Loading = wasLoading;
        DrawMarkers();
    }

    ButtonAction Editing()
    {
        var key = Core.DeviceProfile.Key(_selected!.Id, _hypershift);
        if (!P.Buttons.TryGetValue(key, out var a)) P.Buttons[key] = a = new ButtonAction();
        return a;
    }

    void Kind_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (Loading || _selected is null || KindBox.SelectedItem is not ActionCatalog.Option<ActionKind> kind) return;
        var a = Editing();
        a.Kind = kind.Value;
        // Valores iniciales razonables para que el boton haga algo apenas se elige el tipo.
        if (a.Kind is ActionKind.MouseButton or ActionKind.Turbo && a.Button == 0) a.Button = 1;
        if (a.Kind == ActionKind.Media && a.MediaUsage == 0) a.MediaUsage = 0xCD;
        if (a.Kind == ActionKind.Dpi && a.DpiOp == 0) a.DpiOp = 6;
        if (a.Kind == ActionKind.Macro && a.MacroId is null) a.MacroId = Settings.Macros.FirstOrDefault()?.Id;
        BuildDetail(a);
        Save();
    }

    void Save()
    {
        RefreshRow();
        Commit(Device.ApplyButtonsAsync);
    }

    void RefreshRow()
    {
        if (_selected is null) return;
        var index = Array.FindIndex(CobraButtons.All, b => b.Id == _selected.Id);
        // Reemplazar la fila re-selecciona; sin esto el editor se reconstruiria en
        // medio de un arrastre del slider.
        _swappingRow = true;
        List.Items[index] = Row(_selected);
        List.SelectedIndex = index;
        _swappingRow = false;
    }

    bool _swappingRow;

    void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        P.Buttons.Remove(Core.DeviceProfile.Key(_selected.Id, _hypershift));
        Loading = true;
        KindBox.SelectedItem = ActionCatalog.Kinds[0];
        BuildDetail(new ButtonAction());
        Loading = false;
        Save();
    }

    // ---------- Paneles de detalle por tipo ----------

    void BuildDetail(ButtonAction a)
    {
        Detail.Children.Clear();
        switch (a.Kind)
        {
            case ActionKind.Default:
                Note($"Hace lo de fábrica: {Describe(_selected!.Factory)}.");
                break;
            case ActionKind.Disabled:
                Note("El botón no hace nada.");
                break;
            case ActionKind.MouseButton:
                Combo("BOTON", ActionCatalog.MouseButtons, a.Button, v => a.Button = v);
                break;
            case ActionKind.Media:
                Combo("FUNCION", ActionCatalog.Media, a.MediaUsage, v => a.MediaUsage = v);
                break;
            case ActionKind.Dpi:
                Combo("FUNCION", ActionCatalog.Dpi, a.DpiOp, v => a.DpiOp = v);
                break;
            case ActionKind.DoubleClick:
                Note("Un clic del botón manda un doble clic izquierdo.");
                break;
            case ActionKind.Hypershift:
                Note("Mientras lo mantienes, los demas botones usan la capa Hypershift.");
                break;
            case ActionKind.Key:
                KeyCapture(a);
                break;
            case ActionKind.Turbo:
                Combo("BOTON QUE SE REPITE", ActionCatalog.MouseButtons.Take(5).ToArray(), a.Button, v => a.Button = v);
                Rate(a);
                break;
            case ActionKind.Macro:
                MacroChoice(a);
                break;
            case ActionKind.Launch:
                LaunchChoice(a);
                break;
        }
    }

    void Note(string text) => Detail.Children.Add(Text(text, "Sub"));

    void Combo<T>(string label, ActionCatalog.Option<T>[] options, T value, Action<T> set)
    {
        Detail.Children.Add(Text(label, "Label"));
        var box = new ComboBox
        {
            ItemsSource = options,
            SelectedItem = options.FirstOrDefault(o => EqualityComparer<T>.Default.Equals(o.Value, value)) ?? options[0],
        };
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is ActionCatalog.Option<T> o)
            {
                set(o.Value);
                Save();
            }
        };
        Detail.Children.Add(box);
    }

    void KeyCapture(ButtonAction a)
    {
        Detail.Children.Add(Text("TECLA O ATAJO", "Label"));
        var box = new TextBox
        {
            Text = a.KeyUsage == 0 && a.Modifiers == 0 ? "" : Keys.Combo(a.Modifiers, a.KeyUsage),
            IsReadOnly = true,
            IsReadOnlyCaretVisible = false,
            Cursor = Cursors.Hand,
            ToolTip = "Haz clic y presiona la tecla o la combinacion",
        };
        box.GotKeyboardFocus += (_, _) =>
        {
            if (box.Text == "") box.Text = "Presiona una tecla...";
        };
        box.PreviewKeyDown += (_, e) =>
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var vk = (ushort)KeyInterop.VirtualKeyFromKey(key);
            var mods = (byte)((Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? 1 : 0)
                              | (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 2 : 0)
                              | (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ? 4 : 0)
                              | (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows) ? 8 : 0));
            var modBit = Keys.ModifierBit(vk);
            if (modBit != 0)
            {
                // Modificador solo (p. ej. un boton que hace de Shift): uso 0 + su bit, como Synapse.
                a.Modifiers = (byte)(mods | (modBit & 0x0F));
                a.KeyUsage = 0;
            }
            else if (Keys.ToHid(vk) is { } hid)
            {
                a.Modifiers = mods;
                a.KeyUsage = hid;
            }
            else return;
            box.Text = Keys.Combo(a.Modifiers, a.KeyUsage);
            Save();
        };
        Detail.Children.Add(box);
        Detail.Children.Add(new TextBlock
        {
            Text = "Haz clic en el campo y presiona la combinacion. Se graba en el mouse.",
            Style = (Style)FindResource("Sub"),
            FontSize = 12,
            Margin = new Thickness(2, 8, 0, 0),
        });
    }

    void Rate(ButtonAction a)
    {
        var label = Text($"PULSACIONES POR SEGUNDO: {a.TurboRate}", "Label");
        label.Margin = new Thickness(2, 16, 0, 6);
        Detail.Children.Add(label);
        var slider = new Slider { Minimum = 2, Maximum = 30, Value = a.TurboRate, IsSnapToTickEnabled = true, TickFrequency = 1 };
        slider.ValueChanged += (_, e) =>
        {
            a.TurboRate = (int)e.NewValue;
            label.Text = $"PULSACIONES POR SEGUNDO: {a.TurboRate}";
            Save();
        };
        Detail.Children.Add(slider);
    }

    void MacroChoice(ButtonAction a)
    {
        if (Settings.Macros.Count == 0)
        {
            Note("Todavía no hay macros. Crea uno en la seccion Macros y vuelve acá.");
            return;
        }
        var options = Settings.Macros.Select(m => new ActionCatalog.Option<string>(m.Name, m.Id)).ToArray();
        Combo("MACRO", options, a.MacroId ?? options[0].Value, v => a.MacroId = v);
        Note("El Cobra no guarda macros: este botón funciona mientras Colmillo este en la bandeja.");
        ((TextBlock)Detail.Children[^1]).Margin = new Thickness(2, 10, 0, 0);
    }

    void LaunchChoice(ButtonAction a)
    {
        Detail.Children.Add(Text("PROGRAMA O ARCHIVO", "Label"));
        var row = new DockPanel();
        var browse = new Button { Style = (Style)FindResource("Outline"), Content = "Elegir...", Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(browse, Dock.Right);
        var path = new TextBox { Text = a.LaunchPath ?? "", IsReadOnly = true };
        browse.Click += (_, _) =>
        {
            var dlg = new OpenFileDialog { Title = "Elegir programa", Filter = "Programas|*.exe;*.lnk;*.bat;*.cmd|Todos los archivos|*.*" };
            if (dlg.ShowDialog() != true) return;
            a.LaunchPath = dlg.FileName;
            path.Text = dlg.FileName;
            Save();
        };
        row.Children.Add(browse);
        row.Children.Add(path);
        Detail.Children.Add(row);
        Note("Funciona mientras Colmillo este en la bandeja.");
        ((TextBlock)Detail.Children[^1]).Margin = new Thickness(2, 10, 0, 0);
    }
}
