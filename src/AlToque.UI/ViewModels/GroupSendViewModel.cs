using System.Collections.ObjectModel;
using AlToque.Chasqui;
using AlToque.Controls;
using AlToque.Controls.Avatars;
using AlToque.Core.Security;
using AlToque.Core.Transfer;
using AlToque.Localization;

namespace AlToque.ViewModels;

/// <summary>
/// Lo mismo enviado a varios compañeros a la vez: una sola tarjeta con el progreso de todos, quién ya lo recibió y quién
/// no, y «Reintentar» solo con los que fallaron. Cada envío es independiente (y queda en el chat de cada uno).
/// </summary>
public sealed class GroupSendViewModel : ViewModelBase
{
    private readonly string? _groupName;
    private readonly IReadOnlyList<string> _paths;
    private readonly Action<GroupSendViewModel, IReadOnlyList<string>>? _retry;
    private readonly Action<TransferInfo> _cancel;
    private ChasquiState _chasqui = ChasquiState.Preparing;
    private double _progress;
    private string _title = "";
    private string _detail = "";
    private string _footLeft = "";
    private string _footRight = "";
    private TrackMode _track = TrackMode.Waiting;
    private bool _isActive = true;
    private int _failedCount;

    /// <param name="groupName">Si se envió a un grupo, su nombre (para el título).</param>
    /// <param name="retry">Volver a enviar los mismos archivos a esos compañeros.</param>
    public GroupSendViewModel(IReadOnlyList<(string PeerId, string Name, string? Avatar)> recipients, IReadOnlyList<string> paths, string? groupName,
        Action<GroupSendViewModel> dismiss, Action<TransferInfo> cancel, Action<GroupSendViewModel, IReadOnlyList<string>>? retry = null)
    {
        _groupName = groupName;
        _paths = paths;
        _retry = retry;
        _cancel = cancel;
        foreach (var (id, name, avatar) in recipients)
            Recipients.Add(new GroupRecipientViewModel(id, name, avatar));
        Dismiss = new RelayCommand(() =>
        {
            foreach (var r in Recipients.Where(r => r.Info is { IsFinished: false }))
                _cancel(r.Info!);
            dismiss(this);
        });
        RetryFailed = new RelayCommand(() =>
        {
            var failed = Recipients.Where(r => r.IsFailed).Select(r => r.PeerId).ToList();
            if (failed.Count > 0)
                _retry?.Invoke(this, failed);
        });
        Refresh();
    }

    public ObservableCollection<GroupRecipientViewModel> Recipients { get; } = [];
    public IReadOnlyList<string> Paths => _paths;
    public RelayCommand Dismiss { get; }
    public RelayCommand RetryFailed { get; }
    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>El color del final de la pista: el del primero que recibe.</summary>
    public string ColorHex => Recipients.Count > 0 ? Recipients[0].ColorHex : "#6366F1";

    public ChasquiState Chasqui { get => _chasqui; private set => Set(ref _chasqui, value); }
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public string Title { get => _title; private set => Set(ref _title, value); }
    public string Detail { get => _detail; private set => Set(ref _detail, value); }
    public string FootLeft { get => _footLeft; private set => Set(ref _footLeft, value); }
    public string FootRight { get => _footRight; private set => Set(ref _footRight, value); }
    public TrackMode Track { get => _track; private set => Set(ref _track, value); }
    public bool ShowTrack => Track != TrackMode.Hidden;
    public bool IsActive { get => _isActive; private set => Set(ref _isActive, value); }
    public bool IsError => !IsActive && _failedCount > 0;
    public bool IsSuccess => !IsActive && _failedCount == 0;
    public bool CanRetry => !IsActive && _failedCount > 0 && _retry is not null;
    public string RetryText => S.RetryFailedOnes(_failedCount);

    /// <summary>Uno de los envíos arrancó (o ya no se pudo ubicar al compañero).</summary>
    public void Attach(string peerId, TransferInfo? info)
    {
        if (Recipients.FirstOrDefault(r => r.PeerId == peerId) is { } r)
            r.Attach(info);
        Refresh();
    }

    public bool Contains(TransferInfo info) => Recipients.Any(r => ReferenceEquals(r.Info, info));

    public void Refresh()
    {
        foreach (var r in Recipients)
            r.Refresh();
        var n = Recipients.Count;
        var done = Recipients.Count(r => r.IsDone);
        _failedCount = Recipients.Count(r => r.IsFailed);
        var active = Recipients.Any(r => r.IsActive);
        IsActive = active;
        if (!active)
            FinishedAt ??= DateTimeOffset.Now;

        var infos = Recipients.Select(r => r.Info).OfType<TransferInfo>().ToList();
        var total = infos.Sum(i => Math.Max(0, i.TotalBytes));
        var sent = infos.Sum(i => i.State == TransferState.Completed ? Math.Max(0, i.TotalBytes) : Math.Max(0, i.TransferredBytes));
        Progress = total > 0 ? Math.Clamp((double)sent / total, 0, 1) : active ? 0 : 1;
        var transferring = infos.Any(i => i.State == TransferState.Transferring);
        Track = active ? transferring ? TrackMode.Active : TrackMode.Waiting : _failedCount == 0 ? TrackMode.Done : TrackMode.Hidden;
        Chasqui = active ? transferring ? ChasquiState.Sending : ChasquiState.Waiting : _failedCount == 0 ? ChasquiState.Success : ChasquiState.Error;

        Title = active
            ? _groupName is { } g ? S.SendingToGroup(g) : S.SendingToMany(n)
            : _failedCount == 0 ? _groupName is { } g2 ? S.SentToGroup(g2) : S.SentToMany(n)
            : S.SentToSome(done, n);
        var first = infos.FirstOrDefault();
        var count = first?.FileCount ?? _paths.Count;
        var what = count == 1 ? Path.GetFileName(Path.TrimEndingDirectorySeparator(first?.FirstName is { Length: > 0 } name ? name : _paths[0])) : S.Items(Math.Max(1, count));
        var size = first?.TotalBytes > 0 ? Format.Bytes(first.TotalBytes) : null;
        Detail = size is null ? what : $"{what} · {size}";
        FootLeft = S.ReadyOf(done, n);
        FootRight = active && total > 0 ? $"{(int)Math.Round(Progress * 100)} %" : "";

        Raise(nameof(ShowTrack));
        Raise(nameof(IsError));
        Raise(nameof(IsSuccess));
        Raise(nameof(CanRetry));
        Raise(nameof(RetryText));
        Raise(nameof(ColorHex));
    }
}

/// <summary>Un destinatario del envío a varios: su avatar con una marca de llegó / no llegó.</summary>
public sealed class GroupRecipientViewModel : ViewModelBase
{
    private bool _unreachable;

    public GroupRecipientViewModel(string peerId, string name, string? avatar)
    {
        PeerId = peerId;
        Name = name;
        ColorHex = IdentityVisuals.ColorHex(peerId);
        AvatarId = AvatarCatalog.Resolve(avatar, peerId);
        Initial = IdentityVisuals.Initial(name);
    }

    public string PeerId { get; }
    public string Name { get; }
    public string ColorHex { get; }
    public string AvatarId { get; }
    public string Initial { get; }
    public TransferInfo? Info { get; private set; }

    public bool IsDone => Info?.State == TransferState.Completed;
    public bool IsFailed => _unreachable || Info is { IsFinished: true, State: not TransferState.Completed };
    public bool IsActive => !_unreachable && (Info is null || !Info.IsFinished);
    public bool IsWaiting => IsActive && Info?.State != TransferState.Transferring;
    public double Opacity => IsWaiting ? 0.55 : 1;

    public string Tip => IsDone ? S.RecipientDone(Name) : IsFailed ? S.RecipientFailed(Name)
        : IsWaiting ? S.RecipientWaiting(Name) : S.RecipientSending(Name);

    public void Attach(TransferInfo? info)
    {
        Info = info;
        _unreachable = info is null;
    }

    public void Refresh() => RaiseAll();
}
