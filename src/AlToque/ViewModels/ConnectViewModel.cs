using AlToque.Core.Agent;
using AlToque.Core.Protocol;
using AlToque.Core.Security;
using AlToque.Core.Transfer;
using AlToque.Localization;
using Avalonia.Media;

namespace AlToque.ViewModels;

/// <summary>Conexión manual por código o IP, para redes que bloquean el descubrimiento (Wi-Fi de invitados).</summary>
public sealed class ConnectViewModel : ViewModelBase
{
    private readonly AppController _app;
    private string _input = "";
    private string? _status;
    private bool _isError;
    private bool _busy;
    private PeerTarget? _target;
    private string _foundName = "";
    private IBrush? _foundColor;
    private string _foundColorHex = "#888888";
    private string _foundAvatarId = Controls.Avatars.AvatarCatalog.InitialId;
    private string _foundSymbols = "";
    private bool _foundTrusted;
    private bool _showMyCode;
    private IReadOnlyList<string>? _pending;

    public ConnectViewModel(AppController app)
    {
        _app = app;
        Find = new RelayCommand(async () => await FindAsync(), () => !Busy);
        Send = new RelayCommand(async () =>
        {
            if (_target is null)
                return;
            await _app.SendToTargetAsync(_target, _pending);
        }, () => _target is not null);
        ToggleMyCode = new RelayCommand(() => ShowMyCode = !ShowMyCode);
    }

    public RelayCommand Find { get; }
    public RelayCommand Send { get; }
    public RelayCommand ToggleMyCode { get; }

    public string Input
    {
        get => _input;
        set
        {
            if (Set(ref _input, value))
            {
                Target = null;
                Status = null;
            }
        }
    }

    public string? Status { get => _status; private set { if (Set(ref _status, value)) Raise(nameof(HasStatus)); } }
    public bool HasStatus => Status is not null;
    public bool IsError { get => _isError; private set => Set(ref _isError, value); }

    public bool Busy
    {
        get => _busy;
        private set
        {
            if (Set(ref _busy, value))
                Find.NotifyCanExecuteChanged();
        }
    }

    public PeerTarget? Target
    {
        get => _target;
        private set
        {
            _target = value;
            Raise(nameof(HasTarget));
            Send.NotifyCanExecuteChanged();
        }
    }

    public bool HasTarget => _target is not null;
    public string FoundName { get => _foundName; private set => Set(ref _foundName, value); }
    public IBrush? FoundColor { get => _foundColor; private set => Set(ref _foundColor, value); }
    public string FoundColorHex { get => _foundColorHex; private set => Set(ref _foundColorHex, value); }
    public string FoundAvatarId { get => _foundAvatarId; private set => Set(ref _foundAvatarId, value); }
    public Controls.Avatars.AvatarRing FoundRing => FoundTrusted ? Controls.Avatars.AvatarRing.Verified : Controls.Avatars.AvatarRing.Unverified;
    public string FoundSymbols { get => _foundSymbols; private set => Set(ref _foundSymbols, value); }
    public string FoundInitial => IdentityVisuals.Initial(FoundName);
    public bool FoundTrusted
    {
        get => _foundTrusted;
        private set
        {
            if (Set(ref _foundTrusted, value))
            {
                Raise(nameof(FoundVerification));
                Raise(nameof(FoundRing));
            }
        }
    }
    public string FoundVerification => FoundTrusted ? "✓ " + S.Verified : "⚠ " + S.Unverified;

    public bool HasPending => _pending is { Count: > 0 };
    public string SendLabel => S.SendFilesTo(FoundName) + (HasPending ? $" ({S.Items(_pending!.Count)})" : "…");

    public bool ShowMyCode
    {
        get => _showMyCode;
        set
        {
            if (Set(ref _showMyCode, value))
            {
                Raise(nameof(MyCode));
                Raise(nameof(MyAddresses));
            }
        }
    }

    public string MyCode => _app.Agent.MyConnectionCode ?? "—";
    public string MyAddresses => string.Join("  ·  ", _app.Agent.MyAddresses.Select(a => _app.Agent.Server.Port == Core.AppInfo.DefaultTcpPort ? a.ToString() : $"{a}:{_app.Agent.Server.Port}"));

    public void Prepare(IReadOnlyList<string>? pending, bool showMyCode)
    {
        _pending = pending;
        ShowMyCode = showMyCode;
        Raise(nameof(HasPending));
        Raise(nameof(SendLabel));
    }

    private async Task FindAsync()
    {
        var text = Input.Trim();
        if (text.Length == 0)
            return;
        Busy = true;
        Target = null;
        IsError = false;
        Status = S.ConnectProbe + "…";
        try
        {
            var target = AgentHost.TargetForCode(text) ?? await AgentHost.TargetForAddressAsync(text);
            if (target is null)
            {
                Fail(S.ConnectInvalid);
                return;
            }
            var (id, key, alias, _) = await _app.Agent.Client.ProbeAsync(target, CancellationToken.None);
            var trusted = _app.Agent.Trust.IsTrustedKey(key);
            var name = _app.Agent.Trust.Find(id)?.DisplayName ?? (alias.Length > 0 ? alias : IdentityVisuals.ShortCode(id));
            FoundName = name;
            FoundColor = Brushes2.ForDevice(id);
            FoundColorHex = IdentityVisuals.ColorHex(id);
            FoundAvatarId = Controls.Avatars.AvatarCatalog.Resolve(_app.Agent.Peers.Find(id)?.Avatar, id);
            FoundSymbols = IdentityVisuals.SymbolSequence(id);
            FoundTrusted = trusted;
            Raise(nameof(FoundInitial));
            Raise(nameof(SendLabel));
            // A partir de aquí la clave queda fijada: si cambia antes de enviar, no se envía nada.
            Target = target with
            {
                DisplayName = name,
                ExpectedDeviceId = id,
                PinnedKey = trusted ? key : null,
                Code = null,
            };
            Status = S.ConnectFound;
        }
        catch (PeerConnectException ex)
        {
            Fail(ex.Failure switch
            {
                ConnectFailure.IdentityMismatch => S.ConnectWrongCode,
                ConnectFailure.Rejected => S.ConnectRejected,
                _ => S.ConnectNotFound,
            });
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or ProtocolException or System.Security.Authentication.AuthenticationException)
        {
            Fail(S.ConnectRejected);
        }
        finally
        {
            Busy = false;
        }
    }

    private void Fail(string message)
    {
        IsError = true;
        Status = message;
    }
}
