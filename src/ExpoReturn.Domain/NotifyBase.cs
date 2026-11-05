using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ExpoReturn.Domain;

/// <summary>
/// Notificación de cambios (BCL, sin dependencia de UI). Permite que WPF hoy y Blazor mañana
/// enlacen el mismo objeto de dominio sin capas de mapeo adicionales.
/// </summary>
public abstract class NotifyBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    /// <summary>Usado por Clone(): la copia no hereda los suscriptores del original.</summary>
    protected void ClearSubscribers() => PropertyChanged = null;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
