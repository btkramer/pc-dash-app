using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PcDashApp.Logos;

/// <summary>
/// Renders a <see cref="ProjectLogoAssignment"/> (generic icon or uploaded image,
/// optionally with the T39 initials-overlay badge) into a WPF visual. Shared by the
/// picker's own swatches and any tile that needs to show a project's current logo
/// (today: ProjectLogosWindow's list; later: wherever this app ends up rendering
/// agent-monitor-mode tiles itself, if it does).
/// </summary>
public static class LogoRenderer
{
    public static UIElement Render(ProjectLogoAssignment assignment, string projectDisplayName,
        ILogoAssignmentStore store, double size)
    {
        var grid = new Grid { Width = size, Height = size };

        UIElement baseVisual = assignment.Kind == LogoKind.Upload
            ? RenderUpload(assignment.Ref, store, size)
            : GenericIconFactory.CreateIcon(assignment.Ref, size * 0.85);

        grid.Children.Add(baseVisual);

        if (assignment.InitialsOverlay)
            grid.Children.Add(RenderInitialsBadge(projectDisplayName, size));

        return grid;
    }

    private static UIElement RenderUpload(string libraryLogoId, ILogoAssignmentStore store, double size)
    {
        var path = store.ResolveLibraryFilePath(libraryLogoId);
        if (path is null || !File.Exists(path))
            // Library entry missing locally (e.g. hub-backed store hasn't downloaded it yet) --
            // fall back to a generic icon rather than showing nothing/broken image.
            return GenericIconFactory.CreateIcon(GenericIconIds.Folder, size * 0.85);

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();

        return new Image
        {
            Source = bitmap,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform
        };
    }

    private static UIElement RenderInitialsBadge(string projectDisplayName, double size)
    {
        var initials = GetInitials(projectDisplayName);
        var badgeSize = size * 0.42;

        var badge = new Border
        {
            Width = badgeSize,
            Height = badgeSize,
            CornerRadius = new CornerRadius(badgeSize / 2),
            Background = Brushes.White,
            BorderBrush = Brushes.DimGray,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = new TextBlock
            {
                Text = initials,
                FontSize = badgeSize * 0.42,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.DimGray,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        return badge;
    }

    /// <summary>Up to 2 characters: first letter of up to the first two
    /// hyphen/space/underscore-separated words (e.g. "pc-dash-app" -> "PD",
    /// "cei-master" -> "CM"), or the first two letters of a single word.</summary>
    private static string GetInitials(string displayName)
    {
        var parts = displayName.Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return "?";
        if (parts.Length == 1)
            return parts[0].Length >= 2 ? parts[0][..2].ToUpperInvariant() : parts[0].ToUpperInvariant();
        return string.Concat(parts[0][0], parts[1][0]).ToUpperInvariant();
    }
}
