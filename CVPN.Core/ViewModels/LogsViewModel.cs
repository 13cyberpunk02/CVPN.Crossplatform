using System.Collections.ObjectModel;
using System.Windows.Input;
using CVPN.Core;
using CVPN.Localization;
using CVPN.Shared;

namespace CVPN.ViewModels;

/// <summary>
/// Страница логов. Сама коллекция живёт в оболочке - в неё пишет ядро,
/// и она нужна независимо от того, открыта ли страница. Здесь остаются
/// только действия над ней.
/// </summary>
public sealed class LogsViewModel : PageViewModel
{
    public LogsViewModel(MainViewModel shell) : base(shell)
    {
        Clear = new RelayCommand(shell.Log.Clear, () => shell.Log.Count > 0);
        Copy = new RelayCommand(async () => await CopyToClipboardAsync(), () => shell.Log.Count > 0);
        OpenFolder = new RelayCommand(async () => await ShowFolderAsync(), () => CanOpenFolder);
    }

    public ObservableCollection<string> Log => Shell.Log;

    public ICommand Clear { get; }
    public ICommand Copy { get; }
    public ICommand OpenFolder { get; }

    /// <summary>На Android папку открыть нечем - кнопку прячем.</summary>
    public bool CanOpenFolder => Shell.Platform.Launcher.CanOpenFiles;

    private async Task CopyToClipboardAsync()
    {
        var text = string.Join(Environment.NewLine, Log);

        try
        {
            await Shell.Platform.Clipboard.SetTextAsync(text);
            Shell.Notify(Loc.T("Logs_CopiedLines", Log.Count));
        }
        catch (Exception ex)
        {
            Shell.Notify(Loc.T("Logs_CopyFailed", ex.Message));
        }
    }

    /// <summary>Открывает папку с логами - то, что просят приложить к issue.</summary>
    private async Task ShowFolderAsync()
    {
        try
        {
            FileLog.Current.Prepare();

            if (!await Shell.Platform.Launcher.OpenFolderAsync(FileLog.Current.Directory))
                Shell.Notify(Loc.T("Logs_FolderFailed", FileLog.Current.Directory));
        }
        catch (Exception ex)
        {
            Shell.Notify(Loc.T("Logs_FolderFailed", ex.Message));
        }
    }
}
