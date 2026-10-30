using System.ComponentModel;
using AlToque.Controls;
using AlToque.ViewModels;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AlToque.Views;

public partial class ConnectWindow : Window
{
    private ConnectViewModel? _vm;

    public ConnectWindow()
    {
        AvaloniaXamlLoader.Load(this);
        AutoHeight.Attach(this);
        Opened += (_, _) => this.FindControl<TextBox>("InputBox")?.Focus();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null)
                _vm.PropertyChanged -= OnVmChanged;
            _vm = DataContext as ConnectViewModel;
            if (_vm is not null)
                _vm.PropertyChanged += OnVmChanged;
        };
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ConnectViewModel.Status) when _vm is { IsError: true, HasStatus: true }:
                // Código equivocado o equipo que no responde: el campo dice "no" con la cabeza.
                if (this.FindControl<TextBox>("InputBox") is { } box)
                    _ = Fx.Shake(box);
                break;
            case nameof(ConnectViewModel.HasTarget) when _vm?.HasTarget == true:
                if (this.FindControl<Border>("FoundCard") is { } card)
                    _ = Fx.Pop(card, from: 0.9, ms: 350);
                break;
            case nameof(ConnectViewModel.ShowMyCode) when _vm?.ShowMyCode == true:
                if (this.FindControl<Border>("MyCodeCard") is { } code)
                    _ = Fx.Enter(code, dy: 12);
                break;
        }
    }
}
