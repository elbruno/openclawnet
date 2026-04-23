using MudBlazor;

namespace OpenClawNet.Web.Theme;

/// <summary>
/// MudBlazor theme tuned to match the existing Bootstrap 5 palette and typography
/// used elsewhere in the app. Keeps the Helvetica/Arial font stack (no Roboto/Material
/// fonts) so MudBlazor components blend with Bootstrap-rendered content.
///
/// Color mapping (Bootstrap → MudBlazor):
///   primary   #1b6ec2  (custom Bootstrap primary from app.css)
///   secondary #6c757d  (Bootstrap default)
///   info      #0dcaf0  (Bootstrap default)
///   success   #198754  (Bootstrap default)
///   warning   #ffc107  (Bootstrap default)
///   error     #dc3545  (Bootstrap danger)
///   background/surface match Bootstrap body / table-light defaults.
/// </summary>
public static class AppTheme
{
    private static readonly string[] FontStack =
        { "'Helvetica Neue'", "Helvetica", "Arial", "sans-serif" };

    public static readonly MudTheme Default = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#1b6ec2",
            PrimaryDarken = "#1861ac",
            PrimaryLighten = "#258cfb",
            Secondary = "#6c757d",
            Tertiary = "#006bb7",
            Info = "#0dcaf0",
            Success = "#198754",
            Warning = "#ffc107",
            Error = "#dc3545",
            Dark = "#212529",

            Background = "#ffffff",
            Surface = "#ffffff",
            AppbarBackground = "#1b6ec2",
            AppbarText = "#ffffff",
            DrawerBackground = "#f8f9fa",
            DrawerText = "#212529",

            TextPrimary = "#212529",
            TextSecondary = "#6c757d",
            ActionDefault = "#6c757d",
            LinesDefault = "#dee2e6",
            TableLines = "#dee2e6",
            TableStriped = "rgba(0,0,0,0.04)",
            TableHover = "rgba(0,0,0,0.06)",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = FontStack,
                FontSize = "0.9rem",
                LineHeight = "1.4",
            },
            H1 = new H1Typography { FontFamily = FontStack },
            H2 = new H2Typography { FontFamily = FontStack },
            H3 = new H3Typography { FontFamily = FontStack },
            H4 = new H4Typography { FontFamily = FontStack },
            H5 = new H5Typography { FontFamily = FontStack },
            H6 = new H6Typography { FontFamily = FontStack },
            Body1 = new Body1Typography { FontFamily = FontStack },
            Body2 = new Body2Typography { FontFamily = FontStack },
            Button = new ButtonTypography { FontFamily = FontStack },
            Caption = new CaptionTypography { FontFamily = FontStack },
            Subtitle1 = new Subtitle1Typography { FontFamily = FontStack },
            Subtitle2 = new Subtitle2Typography { FontFamily = FontStack },
            Overline = new OverlineTypography { FontFamily = FontStack },
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "4px",
        },
    };
}
