using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace PcDashApp.Logos;

/// <summary>
/// T52's picker UI: change a project's logo to a different generic icon or a
/// custom upload, plus toggle the initials overlay. Opened modally (ShowDialog)
/// from ProjectLogosWindow; the caller reads <see cref="ResultAssignment"/> after
/// a true DialogResult.
/// </summary>
public partial class LogoPickerDialog : Window
{
    private static readonly Brush SelectedBrush = new SolidColorBrush(Color.FromRgb(0x1D, 0x7A, 0x96));
    private static readonly Brush UnselectedBrush = Brushes.LightGray;

    private readonly string _projectId;
    private readonly string _projectDisplayName;
    private readonly ILogoAssignmentStore _store;
    private ProjectLogoAssignment _current;
    private readonly List<Border> _swatchBorders = [];

    public ProjectLogoAssignment? ResultAssignment { get; private set; }

    public LogoPickerDialog(string projectId, string projectDisplayName, ProjectLogoAssignment current, ILogoAssignmentStore store)
    {
        InitializeComponent();
        _projectId = projectId;
        _projectDisplayName = projectDisplayName;
        _store = store;
        _current = current;

        ProjectNameText.Text = projectDisplayName;
        InitialsOverlayCheck.IsChecked = current.InitialsOverlay;

        BuildGenericIconRow();
        _ = LoadLibraryAsync();
        RefreshPreview();
    }

    private void BuildGenericIconRow()
    {
        GenericIconPanel.Children.Clear();
        _swatchBorders.Clear();

        foreach (var (id, label) in GenericIconIds.All)
        {
            var swatch = BuildSwatch(
                GenericIconFactory.CreateIcon(id, 28),
                label,
                isSelected: _current.Kind == LogoKind.Generic && _current.Ref == id,
                onClick: () => Select(LogoKind.Generic, id));
            GenericIconPanel.Children.Add(swatch);
        }
    }

    private async Task LoadLibraryAsync()
    {
        LibraryPanel.Children.Clear();
        var library = await _store.GetLibraryAsync();
        if (library.Count == 0)
        {
            LibraryPanel.Children.Add(new TextBlock
            {
                Text = "(no uploads yet -- use \"Upload new image...\" below)",
                Foreground = Brushes.Gray, FontStyle = FontStyles.Italic, Margin = new Thickness(4)
            });
            return;
        }

        foreach (var entry in library)
        {
            var visual = LogoRenderer.Render(new ProjectLogoAssignment(LogoKind.Upload, entry.Id, false), _projectDisplayName, _store, 28);
            var swatch = BuildSwatch(
                visual,
                entry.FileName ?? entry.Id, // fileName can be null for entries uploaded before pc-dash-hub captured it
                isSelected: _current.Kind == LogoKind.Upload && _current.Ref == entry.Id,
                onClick: () => Select(LogoKind.Upload, entry.Id));
            LibraryPanel.Children.Add(swatch);
        }
    }

    private Border BuildSwatch(UIElement iconVisual, string tooltip, bool isSelected, Action onClick)
    {
        var border = new Border
        {
            Width = 40,
            Height = 40,
            Margin = new Thickness(4),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(2),
            BorderBrush = isSelected ? SelectedBrush : UnselectedBrush,
            Background = Brushes.White,
            ToolTip = tooltip,
            Cursor = Cursors.Hand,
            Child = iconVisual
        };
        border.MouseLeftButtonUp += (_, _) =>
        {
            onClick();
            foreach (var b in _swatchBorders)
                b.BorderBrush = UnselectedBrush;
            border.BorderBrush = SelectedBrush;
        };
        _swatchBorders.Add(border);
        return border;
    }

    private void Select(string kind, string reference)
    {
        _current = _current with { Kind = kind, Ref = reference };
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        PreviewHost.Child = LogoRenderer.Render(_current, _projectDisplayName, _store, 46);
    }

    private async void OnUploadClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp",
            Title = "Choose a logo image"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var entry = await _store.AddToLibraryAsync(dialog.FileName);
            Select(LogoKind.Upload, entry.Id);
            await LoadLibraryAsync();
            // Re-select the swatch that now exists in the freshly-rebuilt library panel.
            RefreshPreview();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't add that image: {ex.Message}", "Upload failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        ResultAssignment = _current with { InitialsOverlay = InitialsOverlayCheck.IsChecked ?? false };
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
