using System.ComponentModel;
using AlToque.Controls;
using AlToque.Controls.Avatars;
using AlToque.ViewModels;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AlToque.Views;

public partial class PairingWindow : Window
{
    private PairingViewModel? _vm;

    public PairingWindow()
    {
        AvaloniaXamlLoader.Load(this);
        AutoHeight.Attach(this);
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null)
                _vm.PropertyChanged -= OnVmChanged;
            _vm = DataContext as PairingViewModel;
            if (_vm is not null)
                _vm.PropertyChanged += OnVmChanged;
        };
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PairingViewModel.HasPrompt) when _vm?.HasPrompt == true:
                // Los dígitos caen uno tras otro.
                Dispatcher.UIThread.Post(() =>
                {
                    if (this.FindControl<StackPanel>("Digits") is { } digits)
                    {
                        var i = 0;
                        foreach (var box in digits.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("digitbox")))
                            _ = Fx.Pop(box, delayMs: 60 * i++, from: 0.4);
                    }
                }, DispatcherPriority.Loaded);
                break;
            case nameof(PairingViewModel.IsDone) when _vm?.IsDone == true:
                Dispatcher.UIThread.Post(Celebrate, DispatcherPriority.Loaded);
                break;
            case nameof(PairingViewModel.IsFailed) when _vm?.IsFailed == true:
                if (this.FindControl<Button>("NoMatchButton") is { } b)
                    _ = Fx.Shake(b);
                break;
        }
    }

    /// <summary>Los dos avatares se acercan, aparece el escudo y celebran juntos.</summary>
    private void Celebrate()
    {
        var me = this.FindControl<AvatarView>("DoneMe");
        var peer = this.FindControl<AvatarView>("DonePeer");
        if (me is not null)
            _ = Fx.Enter(me, dy: 0);
        if (peer is not null)
            _ = Fx.Enter(peer, delayMs: 80, dy: 0);
        if (this.FindControl<Border>("DoneShield") is { } shield)
            _ = Fx.Pop(shield, delayMs: 250, from: 0.2, ms: 500);
        DispatcherTimer.RunOnce(() =>
        {
            me?.Celebrate();
            peer?.Celebrate();
        }, TimeSpan.FromMilliseconds(350));
    }
}
