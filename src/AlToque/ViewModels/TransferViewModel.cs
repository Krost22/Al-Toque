using AlToque.Chasqui;
using AlToque.Core.Security;
using AlToque.Core.Transfer;
using AlToque.Localization;
using AlToque.Platform;
using Avalonia.Media;

namespace AlToque.ViewModels;

/// <summary>Una fila de la ventana flotante de Chasqui. Se refresca 10 veces por segundo como máximo.</summary>
public sealed class TransferViewModel : ViewModelBase
{
    private ChasquiState _chasqui;
    private double _progress;
    private string _title = "";
    private string _detail = "";
    private bool _isActive = true;
    private bool _isError;
    private bool _isSuccess;
    private bool _canOpen;

    public TransferViewModel(TransferInfo info, Action<TransferViewModel> dismiss, Action<TransferInfo> cancel)
    {
        Info = info;
        PeerColor = Brushes2.ForDevice(info.PeerId ?? info.PeerName);
        Cancel = new RelayCommand(() => cancel(Info), () => IsActive);
        Dismiss = new RelayCommand(() => dismiss(this));
        OpenFolder = new RelayCommand(() =>
        {
            var target = Info.CompletedPaths.Count == 1 ? Info.CompletedPaths[0] : Info.DestinationFolder;
            if (target is not null)
                Opener.ShowInFolder(target);
        });
        OpenFile = new RelayCommand(() => OpenFileRequested?.Invoke(this));
        Refresh();
    }

    public TransferInfo Info { get; }
    public IBrush PeerColor { get; private set; }
    public string Initial => IdentityVisuals.Initial(Info.PeerName);
    public RelayCommand Cancel { get; }
    public RelayCommand Dismiss { get; }
    public RelayCommand OpenFolder { get; }
    public RelayCommand OpenFile { get; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public event Action<TransferViewModel>? OpenFileRequested;

    public ChasquiState Chasqui { get => _chasqui; private set => Set(ref _chasqui, value); }
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public string Title { get => _title; private set => Set(ref _title, value); }
    public string Detail { get => _detail; private set => Set(ref _detail, value); }
    public bool IsError { get => _isError; private set => Set(ref _isError, value); }
    public bool IsSuccess { get => _isSuccess; private set => Set(ref _isSuccess, value); }

    /// <summary>Solo para recepciones terminadas: "Abrir carpeta" y "Abrir".</summary>
    public bool CanOpen { get => _canOpen; private set => Set(ref _canOpen, value); }

    public bool CanOpenFile => CanOpen && Info.CompletedPaths.Count == 1;

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
            PeerColor = Brushes2.ForDevice(info.PeerId);
        Chasqui = ChasquiStates.For(info);
        Progress = info.Fraction;
        IsActive = !info.IsFinished;
        if (info.IsFinished)
            FinishedAt ??= DateTimeOffset.Now;
        IsSuccess = info.State == TransferState.Completed;
        IsError = info.State is TransferState.Failed or TransferState.PartiallyCompleted ||
                  (info.State == TransferState.Cancelled && info.ErrorCode is not null && info.ErrorCode != Core.Protocol.ErrorCodes.Cancelled);
        CanOpen = info.Direction == TransferDirection.Receive && info.State is TransferState.Completed or TransferState.PartiallyCompleted && info.CompletedPaths.Count > 0;
        Raise(nameof(CanOpenFile));
        Raise(nameof(ShowProgress));
        Raise(nameof(Initial));

        var speed = info.State == TransferState.Transferring ? info.SampleSpeed() : 0;
        var eta = info.EstimatedRemaining is { } r && r.TotalSeconds >= 1 ? Format.Duration(r) : null;
        var pct = (int)Math.Round(info.Fraction * 100);
        var send = info.Direction == TransferDirection.Send;

        Title = info.State switch
        {
            TransferState.Preparing => S.Preparing,
            TransferState.Connecting => S.ConnectingTo(who),
            TransferState.WaitingForAccept when send => S.WaitingFor(who, Remaining(info.AcceptDeadline)),
            TransferState.WaitingForAccept => S.WantsToSend(info.FileCount),
            TransferState.Transferring => send ? S.SendingTo(who, pct, eta) : S.ReceivingFrom(who, pct, eta),
            TransferState.Completed => send ? S.SentOk(who, What) : S.ReceivedOk(What, who),
            TransferState.PartiallyCompleted => S.Partial(info.FilesDone, info.FilesFailed),
            TransferState.Declined => send ? S.ErrorText(info.ErrorCode, who, info.ErrorDetail) : S.ReceiveCancelled,
            TransferState.Cancelled => info.ErrorCode is null or Core.Protocol.ErrorCodes.Cancelled
                ? (send ? S.Cancelled : S.ReceiveCancelled)
                : S.ErrorText(info.ErrorCode, who, info.ErrorDetail),
            _ => S.ErrorText(info.ErrorCode, who, info.ErrorDetail),
        };

        var parts = new List<string> { What };
        if (info.TotalBytes > 0)
            parts.Add(Format.Bytes(info.TotalBytes));
        if (speed > 1)
            parts.Add(Format.Speed(speed));
        if (info.CurrentFile is { } current && info.FileCount > 1 && info.State == TransferState.Transferring)
            parts[0] = current;
        Detail = string.Join(" · ", parts);
    }

    private static TimeSpan Remaining(DateTimeOffset? deadline) =>
        deadline is { } d && d > DateTimeOffset.Now ? d - DateTimeOffset.Now : TimeSpan.Zero;
}

public sealed class OfferFileViewModel
{
    public OfferFileViewModel(PlannedFile file)
    {
        Name = file.DisplayPath;
        var ext = FileKinds.Extension(file.Name);
        Badge = ext.Length is > 0 and <= 5 ? ext.ToLowerInvariant() : "•";
        Size = Format.Bytes(file.Offer.Size);
        Dangerous = file.Dangerous;
        Warning = file.MisleadingName ? (file.HadHiddenCharacters ? S.HiddenCharsWarning : S.MisleadingWarning)
            : file.Dangerous ? S.ExecutableWarning : null;
        BadgeBrush = Brushes2.FromHex(FileKinds.Categorize(file.Name) switch
        {
            FileCategory.Program or FileCategory.Script or FileCategory.Shortcut or FileCategory.DiskImage => "#E5484D",
            FileCategory.Video => "#8B5CF6",
            FileCategory.Image => "#0EA5E9",
            FileCategory.Audio => "#EC4899",
            FileCategory.Document => "#3B82F6",
            FileCategory.Archive => "#F59E0B",
            FileCategory.Text => "#64748B",
            _ => "#7C7C92",
        });
        Skipped = file.Skip;
    }

    public string Name { get; }
    public string Badge { get; }
    public IBrush BadgeBrush { get; }
    public string Size { get; }
    public bool Dangerous { get; }
    public string? Warning { get; }
    public bool HasWarning => Warning is not null;
    public bool Skipped { get; }
}

/// <summary>Tarjeta de solicitud entrante (sección 10.3).</summary>
public sealed class OfferViewModel : ViewModelBase
{
    private const int MaxListed = 60;
    private string _countdown = "";
    private string _folder;

    public OfferViewModel(IncomingOffer offer)
    {
        Offer = offer;
        _folder = offer.DestinationFolder;
        PeerColor = Brushes2.FromHex(offer.PeerColor);
        Initial = IdentityVisuals.Initial(offer.PeerName);
        Summary = $"{S.WantsToSend(offer.FileCount)} · {Format.Bytes(offer.TotalBytes)}";
        Files = offer.Plan.Files.Take(MaxListed).Select(f => new OfferFileViewModel(f)).ToList();
        if (offer.Plan.Dirs.Count > 0 && Files.Count == 0)
            Files = offer.Plan.Dirs.Take(MaxListed).Select(d => new OfferFileViewModel(new PlannedFile
            {
                Offer = new Core.Protocol.OfferFile { FileId = "d", Path = string.Join('/', d), Size = 0 },
                Components = d,
                FinalPath = "",
            })).ToList();
        MoreText = offer.Plan.Files.Count > MaxListed ? S.AndMore(offer.Plan.Files.Count - MaxListed) : null;
        Accept = new RelayCommand(() =>
        {
            Offer.Accept(Folder == offer.DestinationFolder ? null : Folder);
            Answered?.Invoke();
        });
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
        Tick();
    }

    public IncomingOffer Offer { get; }

    /// <summary>El usuario respondió: la tarjeta se cierra.</summary>
    public event Action? Answered;

    public string PeerName => Offer.PeerName;
    public IBrush PeerColor { get; }
    public string Initial { get; }
    public string Symbols => Offer.PeerSymbols;
    public bool Trusted => Offer.Trusted;
    public bool Unverified => !Offer.Trusted;
    public string VerificationText => Offer.Trusted ? "✓ " + S.Verified : "⚠ " + S.Unverified;
    public IBrush VerificationBrush => Brushes2.FromHex(Offer.Trusted ? "#1F9D55" : "#B7791F");
    public string Summary { get; }
    public IReadOnlyList<OfferFileViewModel> Files { get; }
    public string? MoreText { get; }
    public bool HasMore => MoreText is not null;
    public string? Message => Offer.Message is { } m ? $"“{m}”" : null;
    public bool HasMessage => Offer.Message is not null;
    public bool HasDangerous => Offer.Plan.HasDangerous;
    public RelayCommand Accept { get; }
    public RelayCommand Decline { get; }
    public RelayCommand DeclineAndBlock { get; }

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
            return string.IsNullOrEmpty(parent) ? name : parent + "/" + name;
        }
    }

    public string Countdown { get => _countdown; private set => Set(ref _countdown, value); }

    public void Tick()
    {
        var left = Offer.Deadline - DateTimeOffset.Now;
        Countdown = S.ExpiresIn(left > TimeSpan.Zero ? left : TimeSpan.Zero);
    }
}
