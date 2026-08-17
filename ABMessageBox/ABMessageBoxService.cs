using System.Windows;

namespace ABMessageBox;

/// <summary>
/// MessageBox personalizzata riutilizzabile nel Compilatore alunno.
/// Mantiene le firme principali della MessageBox WPF per permettere
/// la sostituzione senza cambiare la logica esistente.
/// </summary>
public static class ABMessageBox
{
    public static MessageBoxResult Show(string message)
        => Show(null, message, "Messaggio", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK);

    public static MessageBoxResult Show(string message, string caption)
        => Show(null, message, caption, MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK);

    public static MessageBoxResult Show(string message, string caption, MessageBoxButton buttons)
        => Show(null, message, caption, buttons, MessageBoxImage.None, GetDefaultResult(buttons));

    public static MessageBoxResult Show(string message, string caption, MessageBoxButton buttons, MessageBoxImage image)
        => Show(null, message, caption, buttons, image, GetDefaultResult(buttons));

    public static MessageBoxResult Show(string message, string caption, MessageBoxButton buttons, MessageBoxImage image, MessageBoxResult defaultResult)
        => Show(null, message, caption, buttons, image, defaultResult);

    public static MessageBoxResult Show(Window owner, string message)
        => Show(owner, message, "Messaggio", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK);

    public static MessageBoxResult Show(Window owner, string message, string caption)
        => Show(owner, message, caption, MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK);

    public static MessageBoxResult Show(Window owner, string message, string caption, MessageBoxButton buttons)
        => Show(owner, message, caption, buttons, MessageBoxImage.None, GetDefaultResult(buttons));

    public static MessageBoxResult Show(Window owner, string message, string caption, MessageBoxButton buttons, MessageBoxImage image)
        => Show(owner, message, caption, buttons, image, GetDefaultResult(buttons));

    public static MessageBoxResult Show(Window? owner, string message, string caption, MessageBoxButton buttons, MessageBoxImage image, MessageBoxResult defaultResult)
    {
        var window = new ABMessageBoxWindow(message, caption, buttons, image, defaultResult);

        Window? resolvedOwner = owner;
        if (resolvedOwner == null && Application.Current?.Windows is not null)
        {
            foreach (Window candidate in Application.Current.Windows)
            {
                if (candidate.IsActive && candidate != window)
                {
                    resolvedOwner = candidate;
                    break;
                }
            }
        }

        if (resolvedOwner != null && resolvedOwner.IsVisible)
        {
            try { window.Owner = resolvedOwner; } catch { }
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        window.ShowDialog();
        return window.Result;
    }

    private static MessageBoxResult GetDefaultResult(MessageBoxButton buttons) => buttons switch
    {
        MessageBoxButton.YesNo => MessageBoxResult.Yes,
        MessageBoxButton.YesNoCancel => MessageBoxResult.Yes,
        _ => MessageBoxResult.OK
    };
}
