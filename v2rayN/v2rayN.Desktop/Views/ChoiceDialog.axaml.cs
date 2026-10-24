using Avalonia.Layout;

namespace v2rayN.Desktop.Views;

/// <summary>
/// PattN: a question that offers buttons of its own (design 3.2.4: "Save anyway", "Get new WARP keys…", Cancel), which
/// MessageBoxDialog cannot. It closes with the index of the button clicked, and with nothing when it is closed otherwise.
/// </summary>
public partial class ChoiceDialog : Window
{
    public ChoiceDialog()
        : this(string.Empty, string.Empty, [], -1)
    {
    }

    /// <summary>
    /// The question <paramref name="message" />, titled <paramref name="title" />, with <paramref name="buttons" /> from
    /// left to right; the first is the default one, and the one at <paramref name="cancelIndex" /> answers Esc.
    /// </summary>
    public ChoiceDialog(string title, string message, IReadOnlyList<string> buttons, int cancelIndex)
    {
        InitializeComponent();

        if (Design.IsDesignMode)
        {
            title = "Design Caption";
            message = "Design Message";
        }

        Title = title;
        txtMessage.Text = message;
        for (var index = 0; index < buttons.Count; index++)
        {
            var choice = index;
            var button = new Button
            {
                Content = buttons[index],
                MinWidth = 90,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                IsDefault = index == 0,
                IsCancel = index == cancelIndex,
            };
            button.Click += (_, _) => Close(choice);
            panelButtons.Children.Add(button);
        }

        CanMinimize = false;
    }
}
