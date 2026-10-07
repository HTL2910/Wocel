using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Wocel.Capture.Desktop.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private int _selectedTabIndex;
    private bool _createPublicLink;
    private string _statusText = "Ready to capture";

    public MainWindowViewModel(
        Action? capture = null,
        Action? showHistory = null,
        Action? showLog = null,
        Action? showSettings = null,
        Action? signIn = null)
    {
        CaptureCommand = new DelegateCommand(capture ?? (() => { }));
        ShowHistoryCommand = new DelegateCommand(showHistory ?? (() => SelectedTabIndex = 0));
        ShowLogCommand = new DelegateCommand(showLog ?? (() => SelectedTabIndex = 1));
        ShowSettingsCommand = new DelegateCommand(showSettings ?? (() => SelectedTabIndex = 2));
        SignInCommand = new DelegateCommand(signIn ?? (() => { }));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ICommand CaptureCommand { get; }
    public ICommand ShowHistoryCommand { get; }
    public ICommand ShowLogCommand { get; }
    public ICommand ShowSettingsCommand { get; }
    public ICommand SignInCommand { get; }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => Set(ref _selectedTabIndex, value);
    }

    public bool CreatePublicLink
    {
        get => _createPublicLink;
        set
        {
            if (Set(ref _createPublicLink, value))
            {
                OnPropertyChanged(nameof(SharingDisclosure));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => Set(ref _statusText, value);
    }

    public string SharingDisclosure => CreatePublicLink
        ? "Anyone with the link can view the uploaded image."
        : "Uploads stay private until you explicitly create a public link.";

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
