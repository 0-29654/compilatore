using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ABMessageBox;

public partial class ABMessageBoxWindow : Window
{
    private MessageBoxResult _result;
    private readonly MessageBoxResult _defaultResult;

    public ABMessageBoxWindow(string message, string caption, MessageBoxButton buttons, MessageBoxImage image, MessageBoxResult defaultResult)
    {
        InitializeComponent();

        CaptionText.Text = string.IsNullOrWhiteSpace(caption) ? "Messaggio" : caption;
        MessageText.Text = message ?? string.Empty;
        _defaultResult = NormalizeDefault(buttons, defaultResult);
        _result = GetCloseResult(buttons, _defaultResult);

        ConfigureIcon(image);
        ConfigureButtons(buttons);

        Loaded += (_, _) =>
        {
            if (ButtonsPanel.Children.OfType<Button>().FirstOrDefault(b => b.Tag is MessageBoxResult r && r == _defaultResult) is Button preferred)
            {
                preferred.Focus();
                Keyboard.Focus(preferred);
            }
        };
    }

    public MessageBoxResult Result => _result;

    private void ConfigureIcon(MessageBoxImage image)
    {
        switch (image)
        {
            case MessageBoxImage.Error:
                IconText.Text = "×";
                IconText.Foreground = new SolidColorBrush(Color.FromRgb(192, 28, 28));
                IconCircle.Background = new SolidColorBrush(Color.FromRgb(252, 232, 232));
                break;
            case MessageBoxImage.Warning:
                IconText.Text = "!";
                IconText.Foreground = new SolidColorBrush(Color.FromRgb(173, 110, 0));
                IconCircle.Background = new SolidColorBrush(Color.FromRgb(255, 244, 214));
                break;
            case MessageBoxImage.Question:
                IconText.Text = "?";
                IconText.Foreground = new SolidColorBrush(Color.FromRgb(45, 103, 178));
                IconCircle.Background = new SolidColorBrush(Color.FromRgb(232, 238, 247));
                break;
            case MessageBoxImage.None:
                IconText.Text = "i";
                IconText.Foreground = new SolidColorBrush(Color.FromRgb(85, 85, 85));
                IconCircle.Background = new SolidColorBrush(Color.FromRgb(238, 238, 238));
                break;
            default:
                IconText.Text = "i";
                IconText.Foreground = new SolidColorBrush(Color.FromRgb(45, 103, 178));
                IconCircle.Background = new SolidColorBrush(Color.FromRgb(232, 238, 247));
                break;
        }
    }

    private void ConfigureButtons(MessageBoxButton buttons)
    {
        ButtonsPanel.Children.Clear();

        switch (buttons)
        {
            case MessageBoxButton.OKCancel:
                AddButton("OK", MessageBoxResult.OK);
                AddButton("Annulla", MessageBoxResult.Cancel);
                break;
            case MessageBoxButton.YesNo:
                AddButton("Sì", MessageBoxResult.Yes);
                AddButton("No", MessageBoxResult.No);
                break;
            case MessageBoxButton.YesNoCancel:
                AddButton("Sì", MessageBoxResult.Yes);
                AddButton("No", MessageBoxResult.No);
                AddButton("Annulla", MessageBoxResult.Cancel);
                break;
            default:
                AddButton("OK", MessageBoxResult.OK);
                break;
        }
    }

    private void AddButton(string text, MessageBoxResult result)
    {
        bool primary = result == _defaultResult;
        var button = new Button
        {
            Content = text,
            MinWidth = 92,
            Height = 35,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(15, 0, 15, 0),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            Tag = result,
            IsDefault = primary,
            IsCancel = result == MessageBoxResult.Cancel,
            Foreground = primary ? Brushes.White : new SolidColorBrush(Color.FromRgb(40, 40, 40)),
            Background = primary ? new SolidColorBrush(Color.FromRgb(32, 99, 181)) : Brushes.White,
            BorderBrush = primary ? new SolidColorBrush(Color.FromRgb(32, 99, 181)) : new SolidColorBrush(Color.FromRgb(190, 190, 190)),
            BorderThickness = new Thickness(1)
        };

        button.Click += Button_Click;
        ButtonsPanel.Children.Add(button);
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is MessageBoxResult result)
        {
            _result = result;
            DialogResult = true;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static MessageBoxResult NormalizeDefault(MessageBoxButton buttons, MessageBoxResult requested)
    {
        bool valid = buttons switch
        {
            MessageBoxButton.OK => requested == MessageBoxResult.OK,
            MessageBoxButton.OKCancel => requested is MessageBoxResult.OK or MessageBoxResult.Cancel,
            MessageBoxButton.YesNo => requested is MessageBoxResult.Yes or MessageBoxResult.No,
            MessageBoxButton.YesNoCancel => requested is MessageBoxResult.Yes or MessageBoxResult.No or MessageBoxResult.Cancel,
            _ => false
        };

        if (valid) return requested;
        return buttons switch
        {
            MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel => MessageBoxResult.Yes,
            _ => MessageBoxResult.OK
        };
    }

    private static MessageBoxResult GetCloseResult(MessageBoxButton buttons, MessageBoxResult defaultResult) => buttons switch
    {
        MessageBoxButton.OK => MessageBoxResult.OK,
        MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
        MessageBoxButton.YesNo => MessageBoxResult.No,
        MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
        _ => defaultResult
    };
}
