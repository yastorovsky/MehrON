using Avalonia.Automation;
using Avalonia.Layout;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

/// <summary>
/// PattN: the preset field of design 3.3 (PattNG PresetField), for a <see cref="PresetFieldViewModel" />: the text, an
/// arrow whose menu lists "default", a separator and the presets by name, and under the text the name of the preset it
/// holds. Only the arrow opens the menu; a pick that would replace a value of the user's own asks first. Set
/// <see cref="IsMultiline" /> for JSON such as a finalMask, and <see cref="ShowsText" /> false where the window edits the
/// text in a box of its own.
/// </summary>
public partial class PresetField : ReactiveUserControl<PresetFieldViewModel>
{
    private bool _isMultiline;
    private bool _showsText = true;

    public PresetField()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            this.Bind(ViewModel, vm => vm.Text, v => v.txtValue.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.HeldName, v => v.txtHeld.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.HeldName, v => v.txtHeld.IsVisible, name => !string.IsNullOrEmpty(name)).DisposeWith(disposables);

            var handler = new SingleReplaceableDisposable();
            handler.DisposeWith(disposables);
            this.WhenAnyValue(v => v.ViewModel)
                .KeepNotNull()
                .Subscribe(vm =>
                {
                    ToolTip.SetTip(btnChoose, vm.ChooseTip);
                    AutomationProperties.SetName(btnChoose, vm.ChooseTip);
                    btnChoose.Flyout = MenuOf(vm);
                    handler.Create(vm.ReplaceInteraction.RegisterHandler(async interaction =>
                    {
                        var (setting, pick) = interaction.Input;
                        var choice = await UI.ShowChoice(
                            string.Format(ResUI.TbPresetReplaceTitle, setting),
                            string.Format(ResUI.TbPresetReplaceMessage, setting, pick),
                            [ResUI.TbPresetReplace, ResUI.TbCancel],
                            cancelIndex: 1);
                        interaction.SetOutput(choice == 0);
                    }));
                })
                .DisposeWith(disposables);
        });
    }

    /// <summary>
    /// Whether the text runs over several lines, as a finalMask's JSON does. Its long lines wrap, as PattNG's field does:
    /// the theme's horizontal scroll bar lies over the text, and would hide the last line of a preset's JSON.
    /// </summary>
    public bool IsMultiline
    {
        get => _isMultiline;
        set
        {
            _isMultiline = value;
            txtValue.AcceptsReturn = value;
            txtValue.TextWrapping = value ? TextWrapping.Wrap : TextWrapping.NoWrap;
            txtValue.MinLines = value ? 3 : 1;
            txtValue.MaxLines = value ? 8 : 1;
            txtValue.Classes.Set("TextArea", value);
        }
    }

    /// <summary>
    /// Whether the field shows its own text box, as it does unless told otherwise. Without it, where the window edits the
    /// text in a box of its own (AddServerWindow's finalMask, in a flyout), the arrow stands alone and the name of the
    /// preset the text holds sits beside it, so both are in sight while the flyout is closed.
    /// </summary>
    public bool ShowsText
    {
        get => _showsText;
        set
        {
            _showsText = value;
            txtValue.IsVisible = value;
            Grid.SetRow(txtHeld, value ? 1 : 0);
            Grid.SetColumn(txtHeld, value ? 0 : 2);
            Grid.SetColumnSpan(txtHeld, value ? 3 : 1);
            txtHeld.VerticalAlignment = value ? VerticalAlignment.Top : VerticalAlignment.Center;
        }
    }

    /// <summary>The menu of the arrow: "default" first, set apart from the presets.</summary>
    private static MenuFlyout MenuOf(PresetFieldViewModel vm)
    {
        var menu = new MenuFlyout();
        for (var index = 0; index < vm.PickNames.Count; index++)
        {
            if (index == 1)
            {
                menu.Items.Add(new Separator());
            }
            var pick = index;
            var item = new MenuItem { Header = vm.PickNames[index] };
            item.Click += async (_, _) => await vm.ChooseAtAsync(pick);
            menu.Items.Add(item);
        }
        return menu;
    }
}
