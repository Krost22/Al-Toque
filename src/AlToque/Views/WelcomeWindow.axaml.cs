using System.ComponentModel;
using AlToque.Chasqui;
using AlToque.Controls;
using AlToque.Controls.Avatars;
using AlToque.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AlToque.Views;

/// <summary>Primera ejecución: Chasqui se presenta y pide el nombre y el avatar con los que te verán.</summary>
public partial class WelcomeWindow : Window
{
    public WelcomeWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Opened += (_, _) =>
        {
            this.FindControl<ChasquiView>("Chasqui")!.State = ChasquiState.Incoming;
            if (this.FindControl<StackPanel>("Sections") is { } sections)
                Fx.Stagger(sections.Children, stepMs: 60, dy: 14);
            var box = this.FindControl<TextBox>("AliasBox")!;
            box.Focus();
            box.SelectAll();
        };
        DataContextChanged += (_, _) =>
        {
            if (DataContext is WelcomeViewModel vm)
                vm.PropertyChanged += OnVmChanged;
        };
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WelcomeViewModel.AvatarId))
        {
            this.FindControl<AvatarView>("Preview")?.Celebrate();
            this.FindControl<ChasquiView>("Chasqui")?.Cheer();
        }
    }

    public bool ReduceMotion
    {
        set => this.FindControl<ChasquiView>("Chasqui")!.ReduceMotion = value;
    }

    /// <summary>Nombre y avatar elegidos.</summary>
    public event Action<string, string>? Started;

    private void OnStart(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WelcomeViewModel vm)
            Started?.Invoke(vm.Alias, vm.AvatarId);
        Close();
    }
}
