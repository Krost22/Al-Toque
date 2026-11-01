namespace AlToque.Core.Storage;

/// <summary>Modo de recepción persistente. "Todos durante 10 minutos" es temporal y no se guarda.</summary>
public enum ReceiveMode
{
    /// <summary>No se anuncia ni acepta conexiones: el equipo es invisible.</summary>
    Nobody = 0,

    /// <summary>Solo compañeros emparejados.</summary>
    TrustedOnly = 1,

    /// <summary>Cualquiera, marcado como no verificado. Solo existe en memoria.</summary>
    Everyone = 2,
}

public enum ConflictPolicy
{
    Rename = 0,
    Replace = 1,
    Skip = 2,
}

public sealed class AppSettings
{
    public int Version { get; set; } = 1;

    /// <summary>Alias visible en la red. Nunca se usa el nombre de usuario del sistema.</summary>
    public string Alias { get; set; } = DefaultAlias();

    /// <summary>Personaje elegido para el avatar; <c>null</c> usa el que corresponde a este equipo.</summary>
    public string? Avatar { get; set; }

    /// <summary>"system" sigue la preferencia de Windows; "full" o "reduced" la fuerzan.</summary>
    public string AnimationMode { get; set; } = "system";

    public string ReceiveFolder { get; set; } = KnownFolders.DefaultReceiveFolder();

    public ReceiveMode ReceiveMode { get; set; } = ReceiveMode.TrustedOnly;

    public ConflictPolicy ConflictPolicy { get; set; } = ConflictPolicy.Rename;

    /// <summary>"auto", "es" o "en".</summary>
    public string Language { get; set; } = "auto";

    public bool ReduceMotion { get; set; }

    public bool Sounds { get; set; } = true;

    public bool StartWithSystem { get; set; } = true;

    public bool ShellIntegration { get; set; } = true;

    /// <summary>Puerto TCP preferido; si está ocupado se usa otro libre y se anuncia.</summary>
    public int TcpPort { get; set; } = AppInfo.DefaultTcpPort;

    public bool ExcludeVirtualAdapters { get; set; } = true;

    /// <summary>Recibir también en redes marcadas como públicas (desactivado por defecto, por seguridad).</summary>
    public bool AllowPublicNetworks { get; set; }

    /// <summary>Ya se pidió al firewall de Windows que permita Al Toque en redes públicas.</summary>
    public bool PublicFirewallRuleAdded { get; set; }

    public bool WelcomeDone { get; set; }

    /// <summary>Ya se mostró el tutorial guiado en esta PC.</summary>
    public bool TutorialDone { get; set; }

    /// <summary>Ya se activó "recibir sin preguntar" para los compañeros emparejados antes de que fuera lo normal.</summary>
    public bool AutoAcceptMigrated { get; set; }

    /// <summary>La ventana principal se mantiene encima de las demás.</summary>
    public bool KeepOnTop { get; set; }

    /// <summary>Descargar las versiones nuevas en segundo plano e instalarlas al cerrar o reiniciar.</summary>
    public bool AutoUpdate { get; set; } = true;

    /// <summary>Versión que corrió la última vez, para celebrar una actualización.</summary>
    public string? LastVersion { get; set; }

    public static string DefaultAlias()
    {
        var name = Environment.MachineName;
        if (string.IsNullOrWhiteSpace(name))
            return "Mi PC";
        // DESKTOP-ABC123 → "Desktop-ABC123": legible sin datos personales.
        return name.Length > 1 ? char.ToUpperInvariant(name[0]) + name[1..].ToLowerInvariant() : name;
    }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    public void Normalize()
    {
        Alias = TextLimits.CleanAlias(Alias) ?? DefaultAlias();
        if (string.IsNullOrWhiteSpace(ReceiveFolder))
            ReceiveFolder = KnownFolders.DefaultReceiveFolder();
        if (ReceiveMode == ReceiveMode.Everyone || !Enum.IsDefined(ReceiveMode))
            ReceiveMode = ReceiveMode.TrustedOnly;
        if (!Enum.IsDefined(ConflictPolicy))
            ConflictPolicy = ConflictPolicy.Rename;
        if (Language is not ("auto" or "es" or "en"))
            Language = "auto";
        if (TcpPort is < 1024 or > 65535)
            TcpPort = AppInfo.DefaultTcpPort;
    }
}
