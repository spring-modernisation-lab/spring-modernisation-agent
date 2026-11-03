using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace SpringModernisationAgent.Reporting;

/// <summary>
/// Shared Word (OpenXML) building blocks so every generated report uses the
/// same styles and layout.
/// </summary>
internal static class WordDocumentElements
{
    internal static void AddDocumentProperties(
        Body body)
    {
        body.Append(
            new SectionProperties(
                new PageSize
                {
                    Width = 11906,
                    Height = 16838
                },
                new PageMargin
                {
                    Top = 1000,
                    Right = 900,
                    Bottom = 1000,
                    Left = 900,
                    Header = 500,
                    Footer = 500,
                    Gutter = 0
                }));
    }

    internal static void AddStyles(
        MainDocumentPart mainPart)
    {
        var stylePart =
            mainPart.AddNewPart<StyleDefinitionsPart>();

        stylePart.Styles = new Styles(
            CreateStyle(
                "Normal",
                "Normal",
                fontSize: 22,
                bold: false),

            CreateStyle(
                "DocumentTitle",
                "Document Title",
                fontSize: 44,
                bold: true),

            CreateStyle(
                "DocumentSubtitle",
                "Document Subtitle",
                fontSize: 26,
                bold: false),

            CreateStyle(
                "DocumentNote",
                "Document Note",
                fontSize: 19,
                bold: false,
                italic: true),

            CreateStyle(
                "Heading1",
                "Heading 1",
                fontSize: 32,
                bold: true),

            CreateStyle(
                "Heading2",
                "Heading 2",
                fontSize: 27,
                bold: true),

            CreateStyle(
                "Heading3",
                "Heading 3",
                fontSize: 23,
                bold: true));

        stylePart.Styles.Save();
    }

    internal static Style CreateStyle(
        string styleId,
        string name,
        int fontSize,
        bool bold,
        bool italic = false)
    {
        var runProperties = new StyleRunProperties(
            new RunFonts
            {
                Ascii = "Aptos",
                HighAnsi = "Aptos"
            },
            new FontSize
            {
                Val = fontSize.ToString()
            },
            new FontSizeComplexScript
            {
                Val = fontSize.ToString()
            });

        if (bold)
        {
            runProperties.Append(new Bold());
        }

        if (italic)
        {
            runProperties.Append(new Italic());
        }

        return new Style(
            new StyleName
            {
                Val = name
            },
            new StyleParagraphProperties(
                new SpacingBetweenLines
                {
                    After = "140",
                    Line = "276",
                    LineRule = LineSpacingRuleValues.Auto
                }),
            runProperties)
        {
            Type = StyleValues.Paragraph,
            StyleId = styleId,
            CustomStyle = styleId != "Normal",
            Default = styleId == "Normal"
        };
    }

    internal static void AddHeading(
        Body body,
        string text,
        int level)
    {
        var styleId = level switch
        {
            1 => "Heading1",
            2 => "Heading2",
            3 => "Heading3",

            _ => throw new ArgumentOutOfRangeException(
                nameof(level))
        };

        body.Append(
            CreateParagraph(
                text,
                styleId));
    }

    internal static void AddParagraph(
        Body body,
        string text)
    {
        body.Append(
            CreateParagraph(
                text,
                "Normal"));
    }

    internal static void AddLabelledParagraph(
        Body body,
        string label,
        string value)
    {
        var paragraph = new Paragraph(
            new ParagraphProperties(
                new ParagraphStyleId
                {
                    Val = "Normal"
                }),
            new Run(
                new RunProperties(
                    new Bold()),
                new Text($"{label}: ")
                {
                    Space = SpaceProcessingModeValues.Preserve
                }),
            new Run(
                new Text(value ?? string.Empty)
                {
                    Space = SpaceProcessingModeValues.Preserve
                }));

        body.Append(paragraph);
    }

    internal static void AddNote(
        Body body,
        string text)
    {
        body.Append(
            CreateParagraph(
                text,
                "DocumentNote"));
    }

    internal static void AddBullet(
        Body body,
        string text)
    {
        body.Append(
            CreateParagraph(
                $"\u2022 {text}",
                "Normal"));
    }

    internal static void AddNumberedParagraph(
        Body body,
        int number,
        string text)
    {
        body.Append(
            CreateParagraph(
                $"{number}. {text}",
                "Normal"));
    }

    internal static Paragraph CreateParagraph(
        string text,
        string styleId,
        JustificationValues? alignment = null)
    {
        var properties = new ParagraphProperties(
            new ParagraphStyleId
            {
                Val = styleId
            });

        if (alignment is not null)
        {
            properties.Append(
                new Justification
                {
                    Val = alignment
                });
        }

        return new Paragraph(
            properties,
            new Run(
                new Text(text ?? string.Empty)
                {
                    Space = SpaceProcessingModeValues.Preserve
                }));
    }

    internal static Table CreateTable(
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var table = new Table();

        table.Append(
            new TableProperties(
                new TableStyle
                {
                    Val = "TableGrid"
                },
                new TableWidth
                {
                    Width = "5000",
                    Type = TableWidthUnitValues.Pct
                }));

        for (var rowIndex = 0;
             rowIndex < rows.Count;
             rowIndex++)
        {
            var tableRow = new TableRow();

            foreach (var value in rows[rowIndex])
            {
                var runProperties = new RunProperties();

                if (rowIndex == 0)
                {
                    runProperties.Append(
                        new Bold());
                }

                var cell = new TableCell(
                    new TableCellProperties(
                        new TableCellMargin(
                            new TopMargin
                            {
                                Width = "80",
                                Type = TableWidthUnitValues.Dxa
                            },
                            new BottomMargin
                            {
                                Width = "80",
                                Type = TableWidthUnitValues.Dxa
                            },
                            new TableCellLeftMargin
                            {
                                Width = 100,
                                Type = TableWidthValues.Dxa
                            },
                            new TableCellRightMargin
                            {
                                Width = 100,
                                Type = TableWidthValues.Dxa
                            })),

                    new Paragraph(
                        new Run(
                            runProperties,
                            new Text(value ?? string.Empty)
                            {
                                Space =
                                    SpaceProcessingModeValues.Preserve
                            })));

                tableRow.Append(cell);
            }

            table.Append(tableRow);
        }

        return table;
    }

    internal static void AddPageBreak(
        Body body)
    {
        body.Append(
            new Paragraph(
                new Run(
                    new Break
                    {
                        Type = BreakValues.Page
                    })));
    }
}
