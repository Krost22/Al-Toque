namespace AlToque.Core.Storage;

/// <summary>Compañero emparejado. La identidad es la clave pública, nunca la IP ni el nombre.</summary>
public sealed class TrustedPeer
{
    public string DeviceId { get; set; } = "";

    /// <summary>SubjectPublicKeyInfo en base64, fijada durante el emparejamiento.</summary>
    public string PublicKey { get; set; } = "";

    /// <summary>Alias que anunciaba el compañero al emparejar.</summary>
    public string Alias { get; set; } = "";

    /// <summary>Nombre local opcional que le pone el usuario.</summary>
    public string? LocalName { get; set; }

    public string? Os { get; set; }

    /// <summary>Último avatar que mostró el compañero (solo decorativo).</summary>
    public string? Avatar { get; set; }

    public DateTimeOffset PairedAt { get; set; }

    /// <summary>Recibir sus envíos sin preguntar (activado por defecto; los programas siempre preguntan).</summary>
    public bool AutoAccept { get; set; } = true;

    public bool Favorite { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(LocalName) ? Alias : LocalName!;
}

/// <summary>Última dirección conocida de un compañero: solo una pista para reconectar, sin valor de identidad.</summary>
public sealed class PeerCacheEntry
{
    public string DeviceId { get; set; } = "";
    public string? Alias { get; set; }
    public string? Os { get; set; }
    public string? Address { get; set; }
    public int Port { get; set; }
    public DateTimeOffset LastSeen { get; set; }
}

public sealed class HistoryEntry
{
    public DateTimeOffset Time { get; set; }

    /// <summary>"sent" o "received".</summary>
    public string Direction { get; set; } = "sent";

    public string PeerId { get; set; } = "";
    public string PeerName { get; set; } = "";
    public int FileCount { get; set; }
    public long TotalBytes { get; set; }

    /// <summary>Hasta 3 nombres de ejemplo.</summary>
    public List<string> SampleNames { get; set; } = [];

    /// <summary>Código de resultado: "ok", "partial", "declined", "cancelled", "failed"…</summary>
    public string Result { get; set; } = "ok";

    public string? Folder { get; set; }
    public string? FirstFile { get; set; }

    public const int MaxPaths = 500;

    /// <summary>Rutas de origen de un envío, para "Reintentar".</summary>
    public List<string>? Paths { get; set; }

    public string? Avatar { get; set; }
}
