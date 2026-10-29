using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Colmillo.Core.Razer;

namespace Colmillo.UI.Pages;

public partial class PerformancePage : PageBase
{
    public PerformancePage()
    {
        InitializeComponent();
        Reload();
    }

    public override void Reload()
    {
        Loading = true;
        BuildStages();
        Poll125.IsChecked = P.PollingRate == 125;
        Poll500.IsChecked = P.PollingRate == 500;
        Poll1000.IsChecked = P.PollingRate >= 1000;
        Loading = false;
    }

    void BuildStages()
    {
        P.ActiveStage = Math.Clamp(P.ActiveStage, 1, P.DpiStages.Count);
        Chips.Children.Clear();
        Rows.Children.Clear();

        for (var i = 0; i < P.DpiStages.Count; i++)
        {
            var index = i;
            var chip = new RadioButton
            {
                Style = (Style)FindResource("Segment"),
                GroupName = "stage",
                Content = $"{P.DpiStages[i]}",
                IsChecked = i + 1 == P.ActiveStage,
                MinWidth = 76,
                Margin = new Thickness(0, 0, 8, 8),
            };
            chip.Checked += (_, _) =>
            {
                if (Loading) return;
                P.ActiveStage = index + 1;
                UpdateBig();
                Commit(Device.ApplyPerformanceAsync);
            };
            Chips.Children.Add(chip);
            Rows.Children.Add(StageRow(index, chip));
        }
        AddStage.IsEnabled = P.DpiStages.Count < CobraMouse.MaxStages;
        UpdateBig();
    }

    UIElement StageRow(int index, RadioButton chip)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

        var label = Text($"Etapa {index + 1}", "Body");
        var slider = new Slider
        {
            Minimum = CobraMouse.MinDpi,
            Maximum = CobraMouse.MaxDpi,
            SmallChange = CobraMouse.DpiStep,
            LargeChange = 400,
            TickFrequency = CobraMouse.DpiStep,
            IsSnapToTickEnabled = true,
            Value = P.DpiStages[index],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 14, 0),
        };
        var box = new TextBox { Text = $"{P.DpiStages[index]}", TextAlignment = TextAlignment.Center };
        var remove = new Button
        {
            Style = (Style)FindResource("IconButton"),
            Content = "",
            ToolTip = "Quitar etapa",
            IsEnabled = P.DpiStages.Count > 1,
            Margin = new Thickness(8, 0, 0, 0),
        };

        void Set(int dpi)
        {
            dpi = Math.Clamp(dpi / CobraMouse.DpiStep * CobraMouse.DpiStep, CobraMouse.MinDpi, CobraMouse.MaxDpi);
            P.DpiStages[index] = dpi;
            chip.Content = $"{dpi}";
            UpdateBig();
            Commit(Device.ApplyPerformanceAsync);
        }

        slider.ValueChanged += (_, e) =>
        {
            if (Loading) return;
            box.Text = $"{(int)e.NewValue}";
            Set((int)e.NewValue);
        };
        box.LostFocus += (_, _) =>
        {
            if (int.TryParse(box.Text, out var v)) slider.Value = v;
            box.Text = $"{(int)slider.Value}";
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Keyboard.ClearFocus();
        };
        remove.Click += (_, _) =>
        {
            P.DpiStages.RemoveAt(index);
            if (P.ActiveStage > P.DpiStages.Count) P.ActiveStage = P.DpiStages.Count;
            Reload();
            Commit(Device.ApplyPerformanceAsync);
        };

        Grid.SetColumn(slider, 1);
        Grid.SetColumn(box, 2);
        Grid.SetColumn(remove, 3);
        grid.Children.Add(label);
        grid.Children.Add(slider);
        grid.Children.Add(box);
        grid.Children.Add(remove);
        return grid;
    }

    void UpdateBig()
    {
        BigDpi.Text = $"{P.DpiStages[P.ActiveStage - 1]}";
        BigDpiLabel.Text = $"DPI ACTIVO · ETAPA {P.ActiveStage} DE {P.DpiStages.Count}";
    }

    void AddStage_Click(object sender, RoutedEventArgs e)
    {
        if (P.DpiStages.Count >= CobraMouse.MaxStages) return;
        var next = Math.Min(P.DpiStages.Max() * 2, CobraMouse.MaxDpi);
        P.DpiStages.Add(next);
        Reload();
        Commit(Device.ApplyPerformanceAsync);
    }

    void Poll_Checked(object sender, RoutedEventArgs e)
    {
        if (Loading) return;
        P.PollingRate = int.Parse((string)((RadioButton)sender).Tag);
        Commit(Device.ApplyPerformanceAsync);
    }
}
