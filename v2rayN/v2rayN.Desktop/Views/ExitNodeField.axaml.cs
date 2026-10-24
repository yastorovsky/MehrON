namespace v2rayN.Desktop.Views;

/// <summary>
/// PattN: the exit-node list of the Aether editor and the WARP keys window, for an <see cref="ExitNodeFieldViewModel" />:
/// freedom first, then the names of the profiles Xray can build as a hop, and under it the hint or why the name chosen
/// cannot be used, in the error colour.
/// </summary>
public partial class ExitNodeField : ReactiveUserControl<ExitNodeFieldViewModel>
{
    public ExitNodeField()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            // The list first: a list that is replaced picks nothing until its entry is set again. Handed over as it is, not
            // through OneWayBind, which gives a list without an ItemTemplate ReactiveUI's view-model template
            // (AutoDataTemplateBindingHook), showing every name as nothing.
            this.WhenAnyValue(v => v.ViewModel!.Labels)
                .Subscribe(labels => cmbNode.ItemsSource = labels)
                .DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedIndex, v => v.cmbNode.SelectedIndex).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.SupportingText, v => v.txtSupporting.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.SupportingText, v => v.txtSupporting.IsVisible, text => !string.IsNullOrEmpty(text)).DisposeWith(disposables);
            this.WhenAnyValue(v => v.ViewModel!.HasProblem)
                .Subscribe(problem =>
                {
                    if (problem)
                    {
                        txtSupporting.Foreground = Brushes.IndianRed;
                    }
                    else
                    {
                        txtSupporting.ClearValue(TextBlock.ForegroundProperty);
                    }
                })
                .DisposeWith(disposables);
        });
    }
}
