using System.Windows;
using FischMacroCS.Core;

namespace FischMacroCS;

public partial class AppraisalDialog : Window
{
    public string ItemName => ItemBox.Text.Trim();
    public string Mutation => MutationBox.Text.Trim();
    public int Limit { get; private set; }

    public AppraisalDialog(Settings settings)
    {
        InitializeComponent();
        ItemBox.Text = settings.AppraisalItem;
        MutationBox.Text = settings.AppraisalMutation;
        LimitBox.Text = settings.AppraisalLimit.ToString();
        Help.Text = $"Press {settings.ToggleHotkey.ToUpperInvariant()} or End to stop.";
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (ItemName.Length == 0 || Mutation.Length == 0 || !int.TryParse(LimitBox.Text, out var limit) || limit < 0)
        {
            Help.Text = "Enter an item, a mutation, and a whole-number limit of 0 or more.";
            return;
        }
        Limit = limit;
        DialogResult = true;
    }
}
