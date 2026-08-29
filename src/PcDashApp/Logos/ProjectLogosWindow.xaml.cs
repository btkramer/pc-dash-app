using System.Windows;
using System.Windows.Controls;

namespace PcDashApp.Logos;

/// <summary>
/// Demo/management surface for T52 so the picker is actually exercisable on real
/// Windows before agent-monitor mode's own project tracking exists (that's a
/// separate, not-yet-built concern -- see the window's own on-screen note). Each
/// row: current logo + project id, "Change..." opens <see cref="LogoPickerDialog"/>.
/// </summary>
public partial class ProjectLogosWindow : Window
{
    private readonly ILogoAssignmentStore _store;

    public ProjectLogosWindow(ILogoAssignmentStore store)
    {
        InitializeComponent();
        _store = store;
        _ = RefreshListAsync();
    }

    private async void OnAddProjectClick(object sender, RoutedEventArgs e)
    {
        var id = NewProjectIdBox.Text.Trim();
        if (id.Length == 0)
            return;

        await _store.GetOrAutoAssignAsync(id); // first appearance -> random generic icon
        NewProjectIdBox.Clear();
        await RefreshListAsync();
    }

    private async Task RefreshListAsync()
    {
        ProjectListPanel.Children.Clear();
        var ids = await _store.GetKnownProjectIdsAsync();

        if (ids.Count == 0)
        {
            ProjectListPanel.Children.Add(new TextBlock
            {
                Text = "(no projects yet)", Foreground = System.Windows.Media.Brushes.Gray,
                FontStyle = FontStyles.Italic, Margin = new Thickness(4)
            });
            return;
        }

        foreach (var id in ids)
        {
            var assignment = await _store.GetOrAutoAssignAsync(id);
            ProjectListPanel.Children.Add(BuildRow(id, assignment));
        }
    }

    private UIElement BuildRow(string projectId, ProjectLogoAssignment assignment)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconHost = new Border
        {
            Width = 36, Height = 36, BorderBrush = System.Windows.Media.Brushes.LightGray,
            BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 0),
            Child = LogoRenderer.Render(assignment, projectId, _store, 34)
        };
        Grid.SetColumn(iconHost, 0);

        var nameText = new TextBlock { Text = projectId, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(nameText, 1);

        var changeButton = new Button { Content = "Change...", Padding = new Thickness(8, 2, 8, 2) };
        changeButton.Click += async (_, _) =>
        {
            var dialog = new LogoPickerDialog(projectId, projectId, assignment, _store) { Owner = this };
            if (dialog.ShowDialog() == true && dialog.ResultAssignment is { } newAssignment)
            {
                await _store.SetAssignmentAsync(projectId, newAssignment);
                await RefreshListAsync();
            }
        };
        Grid.SetColumn(changeButton, 2);

        row.Children.Add(iconHost);
        row.Children.Add(nameText);
        row.Children.Add(changeButton);
        return row;
    }
}
