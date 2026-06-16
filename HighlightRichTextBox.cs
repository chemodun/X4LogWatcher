using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace X4LogWatcher
{
  /// <summary>
  /// Attached properties that populate a RichTextBox Document so that every occurrence of
  /// SearchTerm is highlighted with a yellow background Run.  Replacing the overlaid
  /// TextBox+TextBlock pair with a single RichTextBox gives native text selection with no
  /// dual-rendering artefacts.
  /// </summary>
  public static class HighlightRichTextBox
  {
    public static readonly DependencyProperty TextProperty =
      DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(HighlightRichTextBox),
        new PropertyMetadata(null, OnPropertyChanged));

    /// <summary>
    /// Stores the FormattedText-measured content width (text + PagePadding) after each Rebuild.
    /// The manual horizontal ScrollBar reads this to compute its Maximum without relying on
    /// FlowDocument.PageWidth (which is kept at 50000 to prevent line wrapping).
    /// </summary>
    public static readonly DependencyProperty ContentWidthProperty =
      DependencyProperty.RegisterAttached(
        "ContentWidth", typeof(double), typeof(HighlightRichTextBox),
        new PropertyMetadata(0.0));

    public static double GetContentWidth(DependencyObject d) => (double)d.GetValue(ContentWidthProperty);
    public static void SetContentWidth(DependencyObject d, double value) => d.SetValue(ContentWidthProperty, value);

    public static readonly DependencyProperty SearchTermProperty =
      DependencyProperty.RegisterAttached(
        "SearchTerm", typeof(string), typeof(HighlightRichTextBox),
        new PropertyMetadata(null, OnPropertyChanged));

    public static readonly DependencyProperty MatchCaseProperty =
      DependencyProperty.RegisterAttached(
        "MatchCase", typeof(bool), typeof(HighlightRichTextBox),
        new PropertyMetadata(false, OnPropertyChanged));

    public static string GetText(DependencyObject d) => (string)d.GetValue(TextProperty);
    public static void SetText(DependencyObject d, string value) => d.SetValue(TextProperty, value);

    public static string GetSearchTerm(DependencyObject d) => (string)d.GetValue(SearchTermProperty);
    public static void SetSearchTerm(DependencyObject d, string value) => d.SetValue(SearchTermProperty, value);

    public static bool GetMatchCase(DependencyObject d) => (bool)d.GetValue(MatchCaseProperty);
    public static void SetMatchCase(DependencyObject d, bool value) => d.SetValue(MatchCaseProperty, value);

    private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
      if (d is RichTextBox rtb) Rebuild(rtb);
    }

    private static void Rebuild(RichTextBox rtb)
    {
      string text = GetText(rtb) ?? string.Empty;
      string term = GetSearchTerm(rtb) ?? string.Empty;
      bool matchCase = GetMatchCase(rtb);

      // FlowDocument's default line-height uses Windows font metrics
      // (usWinAscent + usWinDescent), which is 15-20 % taller than what TextBlock uses
      // (FontFamily.LineSpacing * FontSize, the OpenType sTypo metrics).  Force
      // BlockLineHeight at the sTypo value so each row is exactly as tall as a TextBlock.
      double lineHeight = rtb.FontSize * (rtb.FontFamily?.LineSpacing ?? 1.0);
      var para = new Paragraph
      {
        Margin = new Thickness(0),
        LineHeight = lineHeight,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
      };

      if (string.IsNullOrEmpty(term))
      {
        if (text.Length > 0) para.Inlines.Add(new Run(text));
      }
      else
      {
        var cmp = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int start = 0;
        while (start < text.Length)
        {
          int idx = text.IndexOf(term, start, cmp);
          if (idx < 0)
          {
            para.Inlines.Add(new Run(text[start..]));
            break;
          }
          if (idx > start)
            para.Inlines.Add(new Run(text[start..idx]));
          para.Inlines.Add(new Run(text[idx..(idx + term.Length)])
          {
            Background = Brushes.Yellow,
            Foreground = Brushes.Black,
          });
          start = idx + term.Length;
        }
      }

      // Measure text width via FormattedText and store as ContentWidth so the per-tab manual
      // horizontal ScrollBar can compute its Maximum without reading innerSv.ExtentWidth.
      // PageWidth stays at 50000 — a large value that prevents FlowDocument from ever wrapping
      // text (FlowDocument's text engine measures slightly wider than FormattedText, so any
      // attempt to set PageWidth = measured_width + small_margin causes the last word to wrap).
      double contentWidth = 50.0;
      if (text.Length > 0)
      {
        try
        {
          var ff = rtb.FontFamily ?? SystemFonts.MessageFontFamily;
          double fs = rtb.FontSize;
          if (double.IsNaN(fs) || fs <= 0) fs = SystemFonts.MessageFontSize;
          double dpi = 1.0;
          try { dpi = VisualTreeHelper.GetDpi(rtb).PixelsPerDip; } catch { }
          var tf = new Typeface(ff, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
          var ft = new FormattedText(text, CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, tf, fs, Brushes.Black, dpi);
          contentWidth = ft.Width + 4; // +2 for PagePadding left + 2 for right
        }
        catch { }
      }
      SetContentWidth(rtb, contentWidth);

      rtb.Document = new FlowDocument(para)
      {
        PagePadding = new Thickness(2, 0, 2, 0),
        PageWidth = 50000,
      };
    }
  }
}
