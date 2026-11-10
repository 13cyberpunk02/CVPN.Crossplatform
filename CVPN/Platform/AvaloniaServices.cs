using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CVPN.Platform;

namespace CVPN.Platform;

// Реализации, общие для всех платформ: Avalonia сама переводит их
// в WinAPI, X11/Wayland/портал или Android.

public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    public void Post(Action action) => Dispatcher.UIThread.Post(action);

    public IUiTimer CreateTimer(TimeSpan interval, Action tick) => new Timer(interval, tick);

    private sealed class Timer : IUiTimer
    {
        private readonly DispatcherTimer _timer;

        public Timer(TimeSpan interval, Action tick)
        {
            _timer = new DispatcherTimer { Interval = interval };
            _timer.Tick += (_, _) => tick();
        }

        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();
    }
}

/// <summary>Выбор файла через StorageProvider: системный диалог на десктопе, SAF на Android.</summary>
public sealed class AvaloniaFilePicker(Func<TopLevel?> topLevel) : IFilePicker
{
    public async Task<PickedFile?> OpenAsync(
        string title, IReadOnlyList<FileFilter> filters, string? startDirectory = null)
    {
        var storage = topLevel()?.StorageProvider;
        if (storage is null || !storage.CanOpen) return null;

        IStorageFolder? start = null;
        if (!string.IsNullOrEmpty(startDirectory))
        {
            try
            {
                start = await storage.TryGetFolderFromPathAsync(startDirectory);
            }
            catch
            {
                // начальная папка - не повод отказывать в выборе
            }
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = start,
            FileTypeFilter = filters
                .Select(f => new FilePickerFileType(f.Name) { Patterns = f.Patterns.ToList() })
                .ToList()
        });

        var file = files.FirstOrDefault();
        if (file is null) return null;

        return new PickedFile(file.Name, file.TryGetLocalPath(), file.OpenReadAsync);
    }
}

public sealed class AvaloniaClipboard(Func<TopLevel?> topLevel) : IClipboardService
{
    public async Task SetTextAsync(string text)
    {
        var clipboard = topLevel()?.Clipboard
                        ?? throw new InvalidOperationException("clipboard is not available");

        await clipboard.SetTextAsync(text);
    }

    public async Task<string?> GetTextAsync()
    {
        var clipboard = topLevel()?.Clipboard;
        return clipboard is null ? null : await clipboard.TryGetTextAsync();
    }
}

/// <summary>Открытие файлов, папок и ссылок программой по умолчанию.</summary>
public sealed class AvaloniaLauncher(Func<TopLevel?> topLevel, bool canOpenFiles) : ILauncher
{
    public bool CanOpenFiles => canOpenFiles;

    public async Task<bool> OpenFileAsync(string path)
    {
        var launcher = topLevel()?.Launcher;
        return launcher is not null && await launcher.LaunchFileInfoAsync(new FileInfo(path));
    }

    public async Task<bool> OpenFolderAsync(string path)
    {
        var launcher = topLevel()?.Launcher;
        return launcher is not null && await launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
    }

    public async Task<bool> OpenUrlAsync(string url)
    {
        var launcher = topLevel()?.Launcher;
        return launcher is not null && Uri.TryCreate(url, UriKind.Absolute, out var uri)
                                    && await launcher.LaunchUriAsync(uri);
    }
}

/// <summary>Сервисы, которые UI-проект собирает сам и отдаёт голове приложения.</summary>
public sealed record CommonServices(
    IUiDispatcher Ui,
    IFilePicker Files,
    IClipboardService Clipboard,
    Func<TopLevel?> TopLevel);
