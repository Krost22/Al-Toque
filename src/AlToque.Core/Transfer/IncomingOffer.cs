using AlToque.Core.Security;

namespace AlToque.Core.Transfer;

public enum OfferResponseKind
{
    Accept,
    Decline,
    DeclineAndBlock,
}

public sealed record OfferResponse(OfferResponseKind Kind, string? Folder = null);

/// <summary>
/// Solicitud entrante que la interfaz muestra al usuario. Se responde una sola vez con <see cref="Accept"/> o
/// <see cref="Decline"/>; si el emisor se retira o vence el plazo, <see cref="Withdrawn"/> se dispara.
/// </summary>
public sealed class IncomingOffer
{
    private readonly TaskCompletionSource<OfferResponse> _response = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _withdrawn = new();

    internal IncomingOffer(TransferInfo transfer, ReceivePlan plan, string peerId, bool trusted, DateTimeOffset deadline)
    {
        Transfer = transfer;
        Plan = plan;
        PeerId = peerId;
        Trusted = trusted;
        Deadline = deadline;
    }

    public TransferInfo Transfer { get; }
    public ReceivePlan Plan { get; internal set; }
    public string PeerId { get; }
    public string PeerName => Transfer.PeerName;
    public bool Trusted { get; }
    public string PeerColor => IdentityVisuals.ColorHex(PeerId);
    public string PeerSymbols => IdentityVisuals.SymbolSequence(PeerId);
    public string PeerShortCode => IdentityVisuals.ShortCode(PeerId);
    public DateTimeOffset Deadline { get; }
    public string? Message => Plan.Message;
    public int FileCount => Plan.Files.Count;
    public long TotalBytes => Plan.TotalBytes;
    public string DestinationFolder => Plan.Root;

    /// <summary>Se cancela si el emisor se retira, se corta la conexión o vence el plazo.</summary>
    public CancellationToken Withdrawn => _withdrawn.Token;

    internal Task<OfferResponse> Response => _response.Task;

    public bool IsAnswered => _response.Task.IsCompleted;

    /// <param name="folder">Otra carpeta de destino elegida en la tarjeta, o <c>null</c> para la predeterminada.</param>
    public bool Accept(string? folder = null) => _response.TrySetResult(new OfferResponse(OfferResponseKind.Accept, folder));

    public bool Decline(bool block = false) =>
        _response.TrySetResult(new OfferResponse(block ? OfferResponseKind.DeclineAndBlock : OfferResponseKind.Decline));

    internal void Withdraw()
    {
        try
        {
            _withdrawn.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
