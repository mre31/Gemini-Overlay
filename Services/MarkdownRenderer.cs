using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using System.Diagnostics;
using System.Windows.Shapes;

namespace Gemeni.Services
{
    public class MarkdownRenderer
    {
        private FlowDocument _currentDocument = null;
        private bool _isFirstMarkdownRender = true;
        private RichTextBox _targetRichTextBox;
        private readonly Dispatcher _dispatcher;

        public MarkdownRenderer(RichTextBox targetRichTextBox)
        {
            _targetRichTextBox = targetRichTextBox;
            _dispatcher = _targetRichTextBox.Dispatcher;
        }

        public void ParseAndDisplayMarkdown(string markdown, bool scrollToEnd = false)
        {
            try
            {
                bool wasAtEnd = scrollToEnd;
                double scrollPosition = 0;
                
                if (_targetRichTextBox.Document != null && !wasAtEnd)
                {
                    var scrollViewer = FindScrollViewer(_targetRichTextBox);
                    if (scrollViewer != null) 
                    {
                        scrollPosition = scrollViewer.VerticalOffset;
                    }
                }
                
                if (_isFirstMarkdownRender || _currentDocument == null)
                {
                    _isFirstMarkdownRender = false;
                    
                    _targetRichTextBox.Document.Blocks.Clear();
                    _currentDocument = new FlowDocument();
                    
                    _currentDocument.Foreground = new SolidColorBrush(Colors.White);
                    _currentDocument.Background = Brushes.Transparent;
                    _currentDocument.FontFamily = new FontFamily("Segoe UI");
                    _currentDocument.FontSize = 14;
                    _currentDocument.PagePadding = new Thickness(0);
                    
                    _currentDocument.IsOptimalParagraphEnabled = true;
                    _currentDocument.IsHyphenationEnabled = false;
                    _currentDocument.TextAlignment = TextAlignment.Left;
                    
                    ProcessMarkdownContent(markdown, _currentDocument);
                    
                    _targetRichTextBox.Document = _currentDocument;
                }
                else
                {
                    _currentDocument.Blocks.Clear();
                    
                    ProcessMarkdownContent(markdown, _currentDocument);
                }
                
                if (wasAtEnd)
                {
                    _dispatcher.BeginInvoke(new Action(() => {
                        _targetRichTextBox.ScrollToEnd();
                    }), DispatcherPriority.Background);
                }
                else
                {
                    _dispatcher.BeginInvoke(new Action(() => {
                        var scrollViewer = FindScrollViewer(_targetRichTextBox);
                        if (scrollViewer != null)
                        {
                            scrollViewer.ScrollToVerticalOffset(scrollPosition);
                        }
                    }), DispatcherPriority.Render);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing markdown: {ex.Message}");
                
                _targetRichTextBox.Document.Blocks.Clear();
                var paragraph = new Paragraph();
                paragraph.Inlines.Add(new Run(markdown));
                _targetRichTextBox.Document.Blocks.Add(paragraph);
                
                if (scrollToEnd)
                {
                    _targetRichTextBox.ScrollToEnd();
                }
                
                _isFirstMarkdownRender = true;
                _currentDocument = null;
            }
        }
        
        private void ProcessMarkdownContent(string markdown, FlowDocument document)
        {
            string[] lines = markdown.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            
            bool inCodeBlock = false;
            int codeBlockIndent = 0;
            StringBuilder codeContent = new StringBuilder();
            Paragraph codeBlockParagraph = null;
            Run codeRun = null;
            
            Paragraph currentParagraph = new Paragraph();
            List<int> listItemIndents = new List<int>();
            
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmedLine = line.TrimStart();
                int lineIndent = line.Length - trimmedLine.Length;
                
                if (trimmedLine.StartsWith("```"))
                {
                    if (inCodeBlock)
                    {
                        inCodeBlock = false;
                        
                        string codeText = codeContent.ToString().TrimEnd();
                        
                        if (codeRun != null)
                        {
                            codeRun.Text = codeText;
                        }
                        
                        Paragraph emptyParaAfter = new Paragraph();
                        emptyParaAfter.Margin = new Thickness(0, 0, 0, 10);
                        document.Blocks.Add(emptyParaAfter);
                        
                        codeContent.Clear();
                        codeBlockIndent = 0;
                        codeBlockParagraph = null;
                        codeRun = null;
                        
                        currentParagraph = new Paragraph();
                        continue;
                    }
                    else
                    {
                        inCodeBlock = true;
                        codeBlockIndent = lineIndent;
                        
                        if (currentParagraph.Inlines.Count > 0)
                        {
                            document.Blocks.Add(currentParagraph);
                            currentParagraph = new Paragraph();
                        }
                        
                        Paragraph emptyParaBefore = new Paragraph();
                        emptyParaBefore.Margin = new Thickness(0, 10, 0, 0);
                        document.Blocks.Add(emptyParaBefore);
                        
                        codeBlockParagraph = new Paragraph();
                        codeBlockParagraph.Background = new SolidColorBrush(Color.FromRgb(20, 20, 20));
                        codeBlockParagraph.BorderBrush = new SolidColorBrush(Color.FromRgb(50, 50, 50));
                        codeBlockParagraph.BorderThickness = new Thickness(1);
                        codeBlockParagraph.Padding = new Thickness(8);
                        codeBlockParagraph.Margin = new Thickness(0, 5, 0, 5);
                        codeBlockParagraph.TextAlignment = TextAlignment.Left;
                        
                        if (listItemIndents.Count > 0)
                        {
                            codeBlockParagraph.Margin = new Thickness(listItemIndents.Last() + 20, 5, 0, 5);
                        }
                        
                        codeRun = new Run("")
                        {
                            Foreground = new SolidColorBrush(Colors.LightGray),
                            FontFamily = new FontFamily("Consolas"),
                            FontSize = 13
                        };
                        
                        codeBlockParagraph.Inlines.Add(codeRun);
                        document.Blocks.Add(codeBlockParagraph);
                        
                        continue;
                    }
                }
                
                if (inCodeBlock)
                {
                    if (lineIndent >= codeBlockIndent)
                    {
                        string adjustedLine = line;
                        if (lineIndent > codeBlockIndent)
                        {
                            adjustedLine = line.Substring(codeBlockIndent);
                        }
                        codeContent.AppendLine(adjustedLine);
                    }
                    else
                    {
                        codeContent.AppendLine(line);
                    }
                    
                    if (codeRun != null)
                    {
                        codeRun.Text = codeContent.ToString().TrimEnd();
                    }
                    
                    continue;
                }
                
                if (Regex.IsMatch(trimmedLine, @"^(\-{3,}|\*{3,}|_{3,})$"))
                {
                    if (currentParagraph.Inlines.Count > 0)
                    {
                        document.Blocks.Add(currentParagraph);
                        currentParagraph = new Paragraph();
                    }
                    
                    AddHorizontalRule(document, lineIndent);
                    continue;
                }
                
                if (trimmedLine.StartsWith("# "))
                {
                    if (currentParagraph.Inlines.Count > 0)
                    {
                        document.Blocks.Add(currentParagraph);
                        currentParagraph = new Paragraph();
                    }
                    
                    var headerText = trimmedLine.Substring(2);
                    var header = new Paragraph
                    {
                        FontSize = 20,
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, 10, 0, 5)
                    };
                    header.Inlines.Add(new Run(headerText));
                    document.Blocks.Add(header);
                    continue;
                }
                else if (trimmedLine.StartsWith("## "))
                {
                    if (currentParagraph.Inlines.Count > 0)
                    {
                        document.Blocks.Add(currentParagraph);
                        currentParagraph = new Paragraph();
                    }
                    
                    var headerText = trimmedLine.Substring(3);
                    var header = new Paragraph
                    {
                        FontSize = 18,
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, 10, 0, 5)
                    };
                    header.Inlines.Add(new Run(headerText));
                    document.Blocks.Add(header);
                    continue;
                }
                
                if (trimmedLine.StartsWith("* ") || trimmedLine.StartsWith("- "))
                {
                    if (currentParagraph.Inlines.Count > 0)
                    {
                        document.Blocks.Add(currentParagraph);
                        currentParagraph = new Paragraph();
                    }
                    
                    var listItemText = trimmedLine.Substring(2);
                    var listItem = new Paragraph
                    {
                        Margin = new Thickness(lineIndent + 20, 0, 0, 6)
                    };
                    
                    listItemIndents.Add(lineIndent);
                    
                    var bullet = new Run("• ")
                    {
                        FontWeight = FontWeights.Bold
                    };
                    listItem.Inlines.Add(bullet);
                    
                    ProcessInlineStyles(listItem, listItemText);
                    
                    document.Blocks.Add(listItem);
                    continue;
                }
                
                var numberedListMatch = Regex.Match(trimmedLine, @"^(\d+)\.\s(.+)$");
                if (numberedListMatch.Success)
                {
                    if (currentParagraph.Inlines.Count > 0)
                    {
                        document.Blocks.Add(currentParagraph);
                        currentParagraph = new Paragraph();
                    }
                    
                    string number = numberedListMatch.Groups[1].Value;
                    var listItemText = numberedListMatch.Groups[2].Value;
                    
                    var listItem = new Paragraph
                    {
                        Margin = new Thickness(lineIndent + 20, 0, 0, 6)
                    };
                    
                    listItemIndents.Add(lineIndent);
                    
                    var numberRun = new Run($"{number}. ")
                    {
                        FontWeight = FontWeights.Bold
                    };
                    listItem.Inlines.Add(numberRun);
                    
                    ProcessInlineStyles(listItem, listItemText);
                    
                    document.Blocks.Add(listItem);
                    continue;
                }
                
                if (listItemIndents.Count > 0 && lineIndent > listItemIndents.Last() && !string.IsNullOrWhiteSpace(trimmedLine))
                {
                    var content = new Paragraph
                    {
                        Margin = new Thickness(listItemIndents.Last() + 40, 0, 0, 0)
                    };
                    
                    ProcessInlineStyles(content, trimmedLine);
                    document.Blocks.Add(content);
                    continue;
                }
                else if (listItemIndents.Count > 0 && lineIndent <= listItemIndents.Last() && !string.IsNullOrWhiteSpace(trimmedLine))
                {
                    while (listItemIndents.Count > 0 && lineIndent < listItemIndents.Last())
                    {
                        listItemIndents.RemoveAt(listItemIndents.Count - 1);
                    }
                }
                
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (currentParagraph.Inlines.Count > 0)
                    {
                        document.Blocks.Add(currentParagraph);
                        currentParagraph = new Paragraph();
                    }
                    continue;
                }
                
                ProcessInlineStyles(currentParagraph, line);
                currentParagraph.Inlines.Add(new LineBreak());
            }
            
            if (currentParagraph.Inlines.Count > 0)
            {
                document.Blocks.Add(currentParagraph);
            }
        }
        
        private void ProcessInlineStyles(Paragraph paragraph, string text)
        {
            var codeRegex = new Regex(@"`([^`]+)`");
            var boldMixedContentRegex = new Regex(@"\*\*(.*?)\*\*");
            var boldRegex = new Regex(@"\*\*([^*`]+)\*\*");
            var italicRegex = new Regex(@"\*([^*`]+)\*");
            var linkRegex = new Regex(@"\[([^\]]+)\]\(([^)]+)\)");
            
            int currentPosition = 0;
            
            var allMatches = new List<(int start, int end, string type, string content, string url)>();
            
            foreach (Match match in linkRegex.Matches(text))
            {
                string linkText = match.Groups[1].Value;
                string url = match.Groups[2].Value;
                allMatches.Add((match.Index, match.Index + match.Length, "link", linkText, url));
            }
            
            foreach (Match match in boldMixedContentRegex.Matches(text))
            {
                string innerContent = match.Groups[1].Value;
                
                bool isOverlapping = allMatches.Any(m => 
                    (match.Index >= m.start && match.Index < m.end) || 
                    (match.Index + match.Length > m.start && match.Index + match.Length <= m.end) ||
                    (match.Index <= m.start && match.Index + match.Length >= m.end));
                
                if (isOverlapping)
                    continue;
                
                if (innerContent.Contains("`"))
                {
                    allMatches.Add((match.Index, match.Index + match.Length, "boldmixed", innerContent, null));
                }
                else
                {
                    allMatches.Add((match.Index, match.Index + match.Length, "bold", innerContent, null));
                }
            }
            
            foreach (Match match in codeRegex.Matches(text))
            {
                bool isOverlapping = allMatches.Any(m => 
                    (match.Index >= m.start && match.Index < m.end) || 
                    (match.Index + match.Length > m.start && match.Index + match.Length <= m.end));
                
                if (!isOverlapping)
                    allMatches.Add((match.Index, match.Index + match.Length, "code", match.Groups[1].Value, null));
            }
            
            foreach (Match match in italicRegex.Matches(text))
            {
                bool isOverlapping = allMatches.Any(m => 
                    (match.Index >= m.start && match.Index < m.end) || 
                    (match.Index + match.Length > m.start && match.Index + match.Length <= m.end) ||
                    (match.Index <= m.start && match.Index + match.Length >= m.end));
                
                if (!isOverlapping)
                    allMatches.Add((match.Index, match.Index + match.Length, "italic", match.Groups[1].Value, null));
            }
            
            allMatches = allMatches.OrderBy(m => m.start).ToList();
            
            foreach (var (start, end, type, content, url) in allMatches)
            {
                if (start > currentPosition)
                {
                    paragraph.Inlines.Add(new Run(text.Substring(currentPosition, start - currentPosition)));
                }
                
                if (type == "code")
                {
                    var codeRun = new Run(content)
                    {
                        Background = new SolidColorBrush(Color.FromRgb(20, 20, 20)),
                        Foreground = new SolidColorBrush(Colors.LightGray),
                        FontFamily = new FontFamily("Consolas")
                    };
                    paragraph.Inlines.Add(codeRun);
                }
                else if (type == "bold")
                {
                    paragraph.Inlines.Add(new Bold(new Run(content)));
                }
                else if (type == "italic")
                {
                    paragraph.Inlines.Add(new Italic(new Run(content)));
                }
                else if (type == "link")
                {
                    var hyperlink = new Hyperlink()
                    {
                        NavigateUri = new Uri(url),
                        Foreground = new SolidColorBrush(Color.FromRgb(0, 122, 255)),
                        TextDecorations = TextDecorations.Underline
                    };
                    
                    hyperlink.Inlines.Add(new Run(content));
                    
                    hyperlink.RequestNavigate += (sender, e) => 
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = e.Uri.AbsoluteUri,
                                UseShellExecute = true
                            });
                            e.Handled = true;
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Could not open link: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    };
                    
                    paragraph.Inlines.Add(hyperlink);
                }
                else if (type == "boldmixed")
                {
                    Bold boldContainer = new Bold();
                    
                    var innerCodeMatches = codeRegex.Matches(content);
                    if (innerCodeMatches.Count > 0)
                    {
                        int innerPosition = 0;
                        
                        foreach (Match codeMatch in innerCodeMatches)
                        {
                            if (codeMatch.Index > innerPosition)
                            {
                                boldContainer.Inlines.Add(new Run(content.Substring(innerPosition, codeMatch.Index - innerPosition)));
                            }
                            
                            var codeRun = new Run(codeMatch.Groups[1].Value)
                            {
                                Background = new SolidColorBrush(Color.FromRgb(20, 20, 20)),
                                Foreground = new SolidColorBrush(Colors.LightGray),
                                FontFamily = new FontFamily("Consolas")
                            };
                            boldContainer.Inlines.Add(codeRun);
                            
                            innerPosition = codeMatch.Index + codeMatch.Length;
                        }
                        
                        if (innerPosition < content.Length)
                        {
                            boldContainer.Inlines.Add(new Run(content.Substring(innerPosition)));
                        }
                    }
                    else
                    {
                        boldContainer.Inlines.Add(new Run(content));
                    }
                    
                    paragraph.Inlines.Add(boldContainer);
                }
                
                currentPosition = end;
            }
            
            if (currentPosition < text.Length)
            {
                paragraph.Inlines.Add(new Run(text.Substring(currentPosition)));
            }
        }

        private void AddHorizontalRule(FlowDocument document, int leftIndent = 0)
        {
            Paragraph spaceBefore = new Paragraph();
            spaceBefore.Margin = new Thickness(0, 8, 0, 8);
            document.Blocks.Add(spaceBefore);
            
            Paragraph ruleParagraph = new Paragraph();
            
            ruleParagraph.Margin = new Thickness(leftIndent, 0, 0, 0);
            
            Rectangle rule = new Rectangle
            {
                Height = 1,
                Fill = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Stretch = Stretch.Fill,
                Width = double.NaN
            };
            
            InlineUIContainer container = new InlineUIContainer(rule);
            ruleParagraph.Inlines.Add(container);
            
            document.Blocks.Add(ruleParagraph);
            
            Paragraph spaceAfter = new Paragraph();
            spaceAfter.Margin = new Thickness(0, 8, 0, 8);
            document.Blocks.Add(spaceAfter);
        }
        
        private ScrollViewer FindScrollViewer(RichTextBox richTextBox)
        {
            if (richTextBox == null) return null;
            
            ScrollViewer viewer = richTextBox.Template?.FindName("PART_ContentHost", richTextBox) as ScrollViewer;
            if (viewer != null) return viewer;
            
            try
            {
                DependencyObject child = null;
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(richTextBox) && viewer == null; i++)
                {
                    child = VisualTreeHelper.GetChild(richTextBox, i);
                    viewer = child as ScrollViewer;
                    if (viewer == null && child != null)
                    {
                        viewer = FindVisualChild<ScrollViewer>(child);
                    }
                }
                
                return viewer;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error finding scroll viewer: {ex.Message}");
                if (richTextBox.IsLoaded)
                {
                    richTextBox.Dispatcher.BeginInvoke(new Action(() => { }), System.Windows.Threading.DispatcherPriority.Loaded);
                }
                return null;
            }
        }
        
        private T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child != null && child is T)
                {
                    return (T)child;
                }
                else
                {
                    T childOfChild = FindVisualChild<T>(child);
                    if (childOfChild != null)
                    {
                        return childOfChild;
                    }
                }
            }
            return null;
        }
    }
} 
