using AlToque.Core.Transfer;
using AlToque.Localization;

namespace AlToque.Chasqui;

/// <summary>Estados de Chasqui (sección 11.2).</summary>
public enum ChasquiState
{
    Idle,
    Attentive,
    Preparing,
    Searching,
    Waiting,
    Sending,
    Reconnecting,
    Success,
    Rejected,
    Error,
    Incoming,
    Receiving,
    Received,
}

public static class ChasquiStates
{
    /// <summary>Estados que se reproducen una vez y luego vuelven al reposo.</summary>
    public static bool IsTransient(ChasquiState s) =>
        s is ChasquiState.Success or ChasquiState.Rejected or ChasquiState.Error or ChasquiState.Incoming or ChasquiState.Received;

    /// <summary>Duración del clip de un estado transitorio (catálogo de la sección 11.3).</summary>
    public static TimeSpan ClipDuration(ChasquiState s) => s switch
    {
        ChasquiState.Success => TimeSpan.FromMilliseconds(1200),
        ChasquiState.Rejected => TimeSpan.FromMilliseconds(900),
        ChasquiState.Error => TimeSpan.FromMilliseconds(800),
        ChasquiState.Incoming => TimeSpan.FromMilliseconds(700),
        ChasquiState.Received => TimeSpan.FromMilliseconds(1000),
        _ => TimeSpan.Zero,
    };

    /// <summary>El estado de Chasqui que corresponde a una transferencia.</summary>
    public static ChasquiState For(TransferInfo t) => t.State switch
    {
        TransferState.Preparing or TransferState.Connecting => ChasquiState.Preparing,
        TransferState.WaitingForAccept => t.Direction == TransferDirection.Send ? ChasquiState.Waiting : ChasquiState.Incoming,
        TransferState.Transferring => t.Direction == TransferDirection.Send ? ChasquiState.Sending : ChasquiState.Receiving,
        TransferState.Completed => t.Direction == TransferDirection.Send ? ChasquiState.Success : ChasquiState.Received,
        TransferState.PartiallyCompleted => ChasquiState.Error,
        TransferState.Declined => ChasquiState.Rejected,
        TransferState.Cancelled => ChasquiState.Rejected,
        _ => ChasquiState.Error,
    };

    /// <summary>Texto accesible: la animación nunca es el único canal de información.</summary>
    public static string Describe(ChasquiState s) => S.Spanish
        ? s switch
        {
            ChasquiState.Searching => "Chasqui busca compañeros",
            ChasquiState.Waiting => "Chasqui espera la respuesta",
            ChasquiState.Sending => "Chasqui está enviando",
            ChasquiState.Receiving => "Chasqui está recibiendo",
            ChasquiState.Reconnecting => "Chasqui intenta reconectar",
            ChasquiState.Success or ChasquiState.Received => "Envío completado",
            ChasquiState.Rejected => "Envío rechazado",
            ChasquiState.Error => "Hubo un problema",
            ChasquiState.Incoming => "Llega una solicitud",
            _ => "Chasqui en reposo",
        }
        : s switch
        {
            ChasquiState.Searching => "Chasqui is looking for teammates",
            ChasquiState.Waiting => "Chasqui is waiting for an answer",
            ChasquiState.Sending => "Chasqui is sending",
            ChasquiState.Receiving => "Chasqui is receiving",
            ChasquiState.Reconnecting => "Chasqui is reconnecting",
            ChasquiState.Success or ChasquiState.Received => "Transfer complete",
            ChasquiState.Rejected => "Transfer declined",
            ChasquiState.Error => "Something went wrong",
            ChasquiState.Incoming => "A request is arriving",
            _ => "Chasqui is resting",
        };
}
