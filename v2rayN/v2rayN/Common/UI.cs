using Microsoft.Win32;

namespace v2rayN.Common;

internal class UI
{
    private static readonly string caption = Global.AppName;

    public static void Show(string msg)
    {
        MessageBox.Show(msg, caption, MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK);
    }

    public static MessageBoxResult ShowYesNo(string msg)
    {
        return MessageBox.Show(msg, caption, MessageBoxButton.YesNo, MessageBoxImage.Question);
    }

    public static int ShowChoice(string? title, string msg, IReadOnlyList<string> buttons, int cancelIndex)
    {
        var owner = System.Windows.Application.Current.Windows.OfType<System.Windows.Window>().FirstOrDefault(window => window.IsActive)
                    ?? System.Windows.Application.Current.MainWindow;
        var dialog = new Views.ChoiceDialog(title ?? caption, msg, buttons, cancelIndex);
        if (owner != null && owner != dialog && owner.IsVisible)
        {
            dialog.Owner = owner;
        }
        dialog.ShowDialog();
        return dialog.Choice;
    }


    public static bool? OpenFileDialog(out string fileName, string filter)
    {
        fileName = string.Empty;

        var fileDialog = new OpenFileDialog
        {
            Multiselect = false,
            Filter = filter
        };

        if (fileDialog.ShowDialog() != true)
        {
            return false;
        }
        fileName = fileDialog.FileName;

        return true;
    }

    public static bool? SaveFileDialog(out string fileName, string filter)
    {
        fileName = string.Empty;

        SaveFileDialog fileDialog = new()
        {
            Filter = filter,
            FilterIndex = 2,
            RestoreDirectory = true
        };
        if (fileDialog.ShowDialog() != true)
        {
            return false;
        }

        fileName = fileDialog.FileName;

        return true;
    }
}
