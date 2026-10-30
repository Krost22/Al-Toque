using AlToque.Chasqui;
using AlToque.Controls;
using AlToque.Controls.Avatars;
using AlToque.Core.Security;
using AlToque.Core.Transfer;
using AlToque.Localization;
using Avalonia.Media;

namespace AlToque.ViewModels;

/// <summary>Una tarjeta de la ventana flotante: un envío o una recepción. Se refresca 10 veces por segundo como máximo.</summary>
public sealed class TransferViewModel : ViewModelBase
{
    private ChasquiState _chasqui;
    private double _progress;
    private string _title = "";
    private string _detail = "";
    private string _footLeft = "";
    private string _footRight = "";
    private bool _isActive = true;
    private bool _isError;
    private bool _isSuccess;
    private bool _isDeclined;
    private bool _canOpen;
    private TrackMode _track = TrackMode.Hidden;
    private string _avatarId;
    private readonly Action<TransferViewModel>? _retry;
    private readonly Action? _connect;

    public TransferViewModel(TransferInfo info, Action<TransferViewModel> dismiss, Action<TransferInfo> cancel,
        Action<TransferViewModel>? retry = null, Action? connect = null, Action<string>? showInFolder = null)
    {
        Info = info;
        _retry = retry;
        _connect = connect;
        var key = info.PeerId ?? info.PeerName;
        ColorHex = IdentityVisuals.ColorHex(key);
        PeerColor = Brushes2.FromHex(ColorHex);
        _avatarId = AvatarCatalog.Resolve(info.PeerAvatar, key);
        Cancel = new RelayCommand(() => cancel(Info), () => IsActive);
        Dismiss = new RelayCommand(() =>
        {
            if (IsActive)
                cancel(Info);
            dismiss(this);
        });
        OpenFolder = new RelayCommand(() =>
        {
            var target = Info.CompletedPaths.Count == 1 ? Info.CompletedPaths[0] : Info.DestinationFolder;
            if (target is not null)
                showInFolder?.Invoke(target);
        });
        OpenFile = new RelayCommand(() => OpenFileRequested?.Invoke(this));
        Retry = new RelayCommand(() => _retry?.Invoke(this));
        ConnectByCode = new RelayCommand(() => _connect?.Invoke());
        Refresh();
    }

    public TransferInfo Info { get; }
    public IBrush PeerColor { get; private set; }
    public string ColorHex { get; private set; }
    public string AvatarId { get => _avatarId; private set => Set(ref _avatarId, value); }
    public string Initial => IdentityVisuals.Initial(Info.PeerName);
    public RelayCommand Cancel { get; }
    public RelayCommand Dismiss { get; }
    public RelayCommand OpenFolder { get; }
    public RelayCommand OpenFile { get; }
    public RelayCommand Retry { get; }
    public RelayCommand ConnectByCode { get; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public event Action<TransferViewModel>? OpenFileRequested;

    public ChasquiState Chasqui { get => _chasqui; private set => Set(ref _chasqui, value); }
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public string Title { get => _title; private set => Set(ref _title, value); }
    public string Detail { get => _detail; private set => Set(ref _detail, value); }
    public string FootLeft { get => _footLeft; private set => Set(ref _footLeft, value); }
    public string FootRight { get => _footRight; private set => Set(ref _footRight, value); }
    public bool HasFoot => FootLeft.Length > 0 || FootRight.Length > 0;
    public bool IsError { get => _isError; private set => Set(ref _isError, value); }
    public bool IsSuccess { get => _isSuccess; private set => Set(ref _isSuccess, value); }
    public bool IsDeclined { get => _isDeclined; private set => Set(ref _isDeclined, value); }
    public TrackMode Track { get => _track; private set => Set(ref _track, value); }

    /// <summary>Cara del compañero: concentrado mientras pasa, enamorado al terminar.</summary>
    public AvatarMood PeerMood => Info.State switch
    {
        TransferState.Transferring => AvatarMood.Focused,
        TransferState.Completed => AvatarMood.Love,
        _ => AvatarMood.Idle,
    };

    /// <summary>Rechazos y errores muestran a Chasqui en lugar del avatar.</summary>
    public bool ShowChasqui => IsError || IsDeclined;
    public bool ShowAvatar => !ShowChasqui;
    public bool ShowTrack => Track != TrackMode.Hidden;

    /// <summary>Solo para recepciones terminadas: "Abrir carpeta" y "Abrir".</summary>
    public bool CanOpen { get => _canOpen; private set => Set(ref _canOpen, value); }

    public bool CanOpenFile => CanOpen && Info.CompletedPaths.Count == 1;
    public string OpenFileText => Info.CompletedPaths.Count == 1 ? S.OpenNamed(Path.GetFileName(Info.CompletedPaths[0])) : S.OpenFile;
    public bool CanRetry => IsError && Info.Direction == TransferDirection.Send && Info.SourcePaths.Count > 0 && _retry is not null;
    public bool ShowErrorActions => CanRetry;

    public bool IsActive
    {
        get => _isActive;
        private set
        {
            if (Set(ref _isActive, value))
                Cancel.NotifyCanExecuteChanged();
        }
    }

    public bool ShowProgress => IsActive && Info.State == TransferState.Transferring;

    private string What => Info.FileCount == 1 && Info.FirstName.Length > 0 ? Info.FirstName : S.Items(Math.Max(1, Info.FileCount));

    public void Refresh()
    {
        var info = Info;
        var who = info.PeerName;
        if (info.PeerId is not null)
        {
            ColorHex = IdentityVisuals.ColorHex(info.PeerId);
            PeerColor = Brushes2.FromHex(ColorHex);
        }
        AvatarId = AvatarCatalog.Resolve(info.PeerAvatar, info.PeerId ?? info.PeerName);
        Chasqui = ChasquiStates.For(info);
        Progress = info.Fraction;
        IsActive = !info.IsFinished;
        if (info.IsFinished)
            FinishedAt ??= DateTimeOffset.Now;
        IsSuccess = info.State == TransferState.Completed;
        IsDeclined = info.State == TransferState.Declined && info.ErrorCode is null or Core.Protocol.ErrorCodes.Declined ||
                     info.State == TransferState.Cancelled && info.ErrorCode is null or Core.Protocol.ErrorCodes.Cancelled;
        IsError = !IsDeclined && (info.State is TransferState.Failed or TransferState.PartiallyCompleted or TransferState.Declined ||
                  (info.State == TransferState.Cancelled && info.ErrorCode is not null && info.ErrorCode != Core.Protocol.ErrorCodes.Cancelled));
        CanOpen = info.Direction == TransferDirection.Receive && info.State is TransferState.Completed or TransferState.PartiallyCompleted && info.CompletedPaths.Count > 0;
        var send = info.Direction == TransferDirection.Send;
        Track = info.State switch
        {
            TransferState.WaitingForAccept or TransferState.Connecting or TransferState.Preparing when send => TrackMode.Waiting,
            TransferState.Transferring => TrackMode.Active,
            TransferState.Completed => TrackMode.Done,
            _ => TrackMode.Hidden,
        };

        var speed = info.State == TransferState.Transferring ? info.SampleSpeed() : 0;
        var eta = info.EstimatedRemaining is { } r && r.TotalSeconds >= 1 ? Format.Duration(r) : null;
        var pct = (int)Math.Round(info.Fraction * 100);
        var size = info.TotalBytes > 0 ? Format.Bytes(info.TotalBytes) : null;

        Title = info.State switch
        {
            TransferState.Preparing => S.Preparing,
            TransferState.Connecting => S.ConnectingTo(who),
            TransferState.WaitingForAccept when send => S.WaitingAccept(who),
            TransferState.WaitingForAccept => S.WantsToSend(info.FileCount),
            TransferState.Transferring => send ? S.SendingTo2(who) : S.ReceivingFrom2(who),
            TransferState.Completed => send ? S.SentTitle(who) : S.ReceivedTitle(info.FileCount, who),
            TransferState.PartiallyCompleted => S.Partial(info.FilesDone, info.FilesFailed),
            _ when IsDeclined => send ? S.DeclinedTitle(who) : S.ReceiveCancelled,
            _ when send && info.ErrorCode is "E_UNREACHABLE" or "E_TIMEOUT" => S.CouldNotConnect(who),
            _ => S.ErrorText(info.ErrorCode, who, info.ErrorDetail),
        };

        Detail = info.State switch
        {
            TransferState.Transferring when info.CurrentFile is { } current =>
                S.FileOf(current, Math.Min(info.FileCount, info.FilesDone + 1), info.FileCount),
            TransferState.Completed when !send && info.DestinationFolder is { } folder => S.SavedIn(ShortFolder(folder)),
            TransferState.Completed => size is null ? What : $"{What} · {size}",
            _ when IsDeclined && send => S.DeclinedBody,
            _ when IsError && send && info.ErrorCode is "E_UNREACHABLE" or "E_TIMEOUT" => S.SameNetworkQuestion,
            _ when IsError => "",
            _ => size is null ? What : $"{What} · {size}",
        };

        (FootLeft, FootRight) = info.State switch
        {
            TransferState.WaitingForAccept when send && info.AcceptDeadline is { } d => (S.AutoCancelIn(Remaining(d)), ""),
            TransferState.Transferring => (
                S.BytesOf(Format.Bytes(info.TransferredBytes), size ?? "—") + (speed > 1 ? " · " + Format.Speed(speed) : ""),
                eta is null ? $"{pct} %" : $"{pct} % · {S.Left(eta)}"),
            _ => ("", ""),
        };

        Raise(nameof(PeerMood));
        Raise(nameof(CanOpenFile));
        Raise(nameof(OpenFileText));
        Raise(nameof(ShowProgress));
        Raise(nameof(Initial));
        Raise(nameof(ShowChasqui));
        Raise(nameof(ShowAvatar));
        Raise(nameof(ShowTrack));
        Raise(nameof(HasFoot));
        Raise(nameof(CanRetry));
        Raise(nameof(ShowErrorActions));
    }

    /// <summary>"Descargas › Al Toque": las dos últimas partes de la ruta.</summary>
    private static string ShortFolder(string folder)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        var parent = Path.GetFileName(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(folder)) ?? "");
        return string.IsNullOrEmpty(parent) ? name : parent + " › " + name;
    }

    private static TimeSpan Remaining(DateTimeOffset? deadline) =>
        deadline is { } d && d > DateTimeOffset.Now ? d - DateTimeOffset.Now : TimeSpan.Zero;
}

public sealed class OfferFileViewModel : ViewModelBase
{
    private bool _isChecked;

    public OfferFileViewModel(PlannedFile file, Action? changed = null)
    {
        FileId = file.Offer.FileId;
        Name = file.DisplayPath;
        Size = Format.Bytes(file.Offer.Size);
        Dangerous = file.Dangerous;
        Warning = file.MisleadingName ? (file.HadHiddenCharacters ? S.HiddenCharsWarning : S.MisleadingWarning)
            : file.Dangerous ? S.ProgramUnchecked : null;
        Skipped = file.Skip;
        var category = FileKinds.Categorize(file.Name);
        Icon = HistoryItemViewModel.IconFor(file.Name);
        TileClass = Dangerous ? "error" : category is FileCategory.Text or FileCategory.Other or FileCategory.Document ? "plain" : "";
        // Los programas llegan desmarcados: se reciben solo si el usuario los marca.
        _isChecked = !Dangerous && !Skipped;
        _changed = changed;
    }

    private readonly Action? _changed;

    public string FileId { get; }
    public string Name { get; }
    public string Size { get; }
    public Ic Icon { get; }
    public string TileClass { get; }
    public bool IsError => TileClass == "error";
    public bool IsPlain => TileClass == "plain";
    public bool Dangerous { get; }
    public string? Warning { get; }
    public bool HasWarning => Warning is not null;
    public bool Skipped { get; }
    public bool CanToggle => !Skipped;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (Set(ref _isChecked, value))
                _changed?.Invoke();
        }
    }
}

/// <summary>Tarjeta de solicitud entrante: grande para desconocidos, compacta para los de confianza.</summary>
public sealed class OfferViewModel : ViewModelBase
{
    private const int MaxListed = 60;
    private string _countdown = "";
    private double _countdownFraction = 1;
    private string _folder;
    private readonly TimeSpan _window;

    public OfferViewModel(IncomingOffer offer, Action<IncomingOffer>? alwaysAccept = null, Action<IncomingOffer>? pairInstead = null)
    {
        Offer = offer;
        _folder = offer.DestinationFolder;
        _window = offer.Deadline - DateTimeOffset.Now;
        PeerColor = Brushes2.FromHex(offer.PeerColor);
        PeerColorHex = offer.PeerColor;
        PeerAvatarId = AvatarCatalog.Resolve(offer.Transfer.PeerAvatar, offer.PeerId);
        Initial = IdentityVisuals.Initial(offer.PeerName);
        var size = Format.Bytes(offer.TotalBytes);
        Summary = offer.FileCount == 1 && offer.Plan.Files.Count == 1
            ? S.WantsToSendNamed(offer.Plan.Files[0].Name, size)
            : S.WantsToSendSized(offer.FileCount, size);
        Files = offer.Plan.Files.Take(MaxListed).Select(f => new OfferFileViewModel(f, OnSelectionChanged)).ToList();
        if (offer.Plan.Dirs.Count > 0 && Files.Count == 0)
            Files = offer.Plan.Dirs.Take(MaxListed).Select(d => new OfferFileViewModel(new PlannedFile
            {
                Offer = new Core.Protocol.OfferFile { FileId = "d", Path = string.Join('/', d), Size = 0 },
                Components = d,
                FinalPath = "",
            })).ToList();
        MoreText = offer.Plan.Files.Count > MaxListed ? S.AndMore(offer.Plan.Files.Count - MaxListed) : null;
        SymbolItems = offer.PeerSymbols.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Accept = new RelayCommand(() =>
        {
            var skip = Files.Where(f => !f.IsChecked && f.CanToggle && f.FileId != "d").Select(f => f.FileId).ToHashSet(StringComparer.Ordinal);
            // Los que no se listan (más de 60) se reciben como el resto.
            Offer.Accept(Folder == offer.DestinationFolder ? null : Folder, skip);
            Answered?.Invoke();
        }, () => CheckedCount > 0 || Files.Count == 0);
        Decline = new RelayCommand(() =>
        {
            Offer.Decline();
            Answered?.Invoke();
        });
        DeclineAndBlock = new RelayCommand(() =>
        {
            Offer.Decline(block: true);
            Answered?.Invoke();
        });
        AlwaysAccept = new RelayCommand(() =>
        {
            alwaysAccept?.Invoke(Offer);
            Accept.Execute(null);
        });
        PairInstead = new RelayCommand(() =>
        {
            Offer.Decline();
            Answered?.Invoke();
            pairInstead?.Invoke(Offer);
        });
        Tick();
    }

    public IncomingOffer Offer { get; }

    /// <summary>El usuario respondió: la tarjeta se cierra.</summary>
    public event Action? Answered;

    public string PeerName => Offer.PeerName;
    public IBrush PeerColor { get; }
    public string PeerColorHex { get; }
    public string PeerAvatarId { get; }
    public string Initial { get; }
    public string Symbols => Offer.PeerSymbols;
    public IReadOnlyList<string> SymbolItems { get; }
    public bool Trusted => Offer.Trusted;
    public bool Unverified => !Offer.Trusted;

    /// <summary>Un compañero de confianza que manda pocas cosas sin riesgo: tarjeta compacta.</summary>
    public bool IsCompact => Offer.Trusted && Offer.Plan.Files.Count <= 3 && !Offer.Plan.HasDangerous && !Offer.Plan.HasMisleadingNames && Offer.Message is null;
    public bool IsFull => !IsCompact;
    public string Summary { get; }
    public IReadOnlyList<OfferFileViewModel> Files { get; }
    public string? MoreText { get; }
    public bool HasMore => MoreText is not null;
    public string? Message => Offer.Message;
    public bool HasMessage => Offer.Message is not null;
    public bool HasDangerous => Offer.Plan.HasDangerous;
    public RelayCommand Accept { get; }
    public RelayCommand Decline { get; }
    public RelayCommand DeclineAndBlock { get; }
    public RelayCommand AlwaysAccept { get; }
    public RelayCommand PairInstead { get; }
    public string AlwaysAcceptText => S.AlwaysAcceptFrom(PeerName);
    public string UnverifiedText => S.UnverifiedExplain(PeerName);

    public int CheckedCount => Files.Count(f => f.IsChecked);
    public string AcceptLabel => S.AcceptCount(CheckedCount, Files.Count);

    private void OnSelectionChanged()
    {
        Raise(nameof(CheckedCount));
        Raise(nameof(AcceptLabel));
        Accept.NotifyCanExecuteChanged();
    }

    public string Folder
    {
        get => _folder;
        set
        {
            if (Set(ref _folder, value))
                Raise(nameof(FolderDisplay));
        }
    }

    public string FolderDisplay
    {
        get
        {
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(Folder));
            var parent = Path.GetFileName(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Folder)) ?? "");
            return string.IsNullOrEmpty(parent) ? name : parent + " › " + name;
        }
    }

    public string Countdown { get => _countdown; private set => Set(ref _countdown, value); }

    /// <summary>Tiempo que queda, de 1 a 0: la barra fina de arriba se vacía.</summary>
    public double CountdownFraction { get => _countdownFraction; private set => Set(ref _countdownFraction, value); }

    public void Tick()
    {
        var left = Offer.Deadline - DateTimeOffset.Now;
        if (left < TimeSpan.Zero)
            left = TimeSpan.Zero;
        Countdown = S.ExpiresIn(left);
        CountdownFraction = _window.TotalSeconds > 0 ? Math.Clamp(left.TotalSeconds / _window.TotalSeconds, 0, 1) : 0;
    }
}
