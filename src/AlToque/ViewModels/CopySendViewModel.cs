using System.Collections.ObjectModel;
using AlToque.Controls;
using AlToque.Controls.Avatars;
using AlToque.Core.Security;
using AlToque.Localization;

namespace AlToque.ViewModels;

public enum CopySendMode
{
    /// <summary>Se copió algo: botón para enviarlo.</summary>
    Offer,

    /// <summary>¿A quién? Lista de compañeros.</summary>
    Pick,

    /// <summary>Hecho (o un aviso corto, como «no hay nada copiado»).</summary>
    Done,
}

/// <summary>La tarjeta de «Enviar lo copiado»: el botón al copiar, la lista para elegir y el «Enviado a…».</summary>
public sealed class CopySendViewModel : ViewModelBase
{
    private CopySendMode _mode;
    private string _title = "";
    private string _preview = "";
    private Ic _previewIcon = Ic.FileText;
    private string? _targetName;
    private bool _isError;

    public CopySendViewModel(Action send, Action choose, Action close)
    {
        Send = new RelayCommand(send);
        Choose = new RelayCommand(choose);
        Close = new RelayCommand(close);
    }

    public RelayCommand Send { get; }
    public RelayCommand Choose { get; }
    public RelayCommand Close { get; }
    public ObservableCollection<CopyChoiceViewModel> Choices { get; } = [];

    /// <summary>Cuándo se muestra lo actual (para retirarla sola).</summary>
    public DateTimeOffset ShownAt { get; private set; } = DateTimeOffset.Now;

    public CopySendMode Mode
    {
        get => _mode;
        private set
        {
            Set(ref _mode, value);
            ShownAt = DateTimeOffset.Now;
            Raise(nameof(IsOffer));
            Raise(nameof(IsPick));
            Raise(nameof(IsDone));
        }
    }

    public bool IsOffer => Mode == CopySendMode.Offer;
    public bool IsPick => Mode == CopySendMode.Pick;
    public bool IsDone => Mode == CopySendMode.Done;
    public string Title { get => _title; private set => Set(ref _title, value); }
    public string Preview { get => _preview; private set => Set(ref _preview, value); }
    public bool HasPreview => Preview.Length > 0;
    public Ic PreviewIcon { get => _previewIcon; private set => Set(ref _previewIcon, value); }
    public bool IsError { get => _isError; private set => Set(ref _isError, value); }

    public string? TargetName
    {
        get => _targetName;
        private set
        {
            Set(ref _targetName, value);
            Raise(nameof(HasTarget));
            Raise(nameof(SendText));
        }
    }

    public bool HasTarget => TargetName is not null;
    public string SendText => TargetName is { } name ? S.CopySendTo(name) : S.CopySendChoose;

    /// <summary>Hay algo copiado: «¿Enviar esto?» con el botón (al destinatario fijo, o para elegir).</summary>
    public void ShowOffer(string preview, Ic icon, string? targetName)
    {
        Title = S.CopySendOfferTitle;
        SetPreview(preview, icon);
        TargetName = targetName;
        IsError = false;
        Mode = CopySendMode.Offer;
    }

    public void ShowPick(string preview, Ic icon, IEnumerable<CopyChoiceViewModel> choices)
    {
        Title = S.CopySendPickTitle;
        SetPreview(preview, icon);
        Choices.Clear();
        foreach (var c in choices)
            Choices.Add(c);
        IsError = false;
        Mode = CopySendMode.Pick;
    }

    public void ShowDone(string text, bool error = false)
    {
        Title = text;
        SetPreview("", Ic.Check);
        IsError = error;
        Mode = CopySendMode.Done;
    }

    private void SetPreview(string preview, Ic icon)
    {
        Preview = preview;
        PreviewIcon = icon;
        Raise(nameof(HasPreview));
    }
}

/// <summary>Un compañero en la lista de «¿A quién?».</summary>
public sealed class CopyChoiceViewModel(string deviceId, string name, string? avatar, bool online, Action<string> pick)
{
    public string DeviceId { get; } = deviceId;
    public string Name { get; } = name;
    public string AvatarId { get; } = AvatarCatalog.Resolve(avatar, deviceId);
    public string ColorHex { get; } = IdentityVisuals.ColorHex(deviceId);
    public string Initial { get; } = IdentityVisuals.Initial(name);
    public bool IsOnline { get; } = online;
    public string Status => IsOnline ? S.CopySendOnline : S.CopySendLater;
    public RelayCommand Pick { get; } = new(() => pick(deviceId));
}
