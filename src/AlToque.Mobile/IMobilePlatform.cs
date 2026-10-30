namespace AlToque.Mobile;

/// <summary>Lo que cambia entre el teléfono y la vista previa de escritorio.</summary>
public interface IMobilePlatform
{
    /// <summary>Carpeta privada de la app: identidad, ajustes, historial.</summary>
    string DataDirectory { get; }

    /// <summary>Carpeta privada temporal: copias de lo que se va a enviar.</summary>
    string CacheDirectory { get; }

    /// <summary>Descargas públicas del teléfono (null: la del sistema).</summary>
    string? DownloadsDirectory { get; }

    /// <summary>Nombre con el que empieza el equipo ("Pixel 8").</summary>
    string DeviceName { get; }

    /// <summary>Avisa al sistema de los archivos nuevos para que aparezcan en Galería y Archivos.</summary>
    void PublishReceived(IReadOnlyList<string> paths);

    /// <summary>Abre lo recibido: el archivo si se puede, si no la app de descargas.</summary>
    void OpenReceived(string? path);

    void OpenUrl(string url);

    /// <summary>Hay transferencias en curso: que el sistema no congele la app.</summary>
    void SetBusy(bool busy);
}
