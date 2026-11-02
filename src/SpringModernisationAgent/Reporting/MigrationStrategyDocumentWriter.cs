using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Reporting;

public sealed class MigrationStrategyDocumentWriter
{
    public Task WriteAsync(
        ProjectAssessment projectAssessment,
        MigrationAnalysisResult migrationAnalysis,
        IReadOnlyList<TransformationPlan> transformationPlans,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectAssessment);
        ArgumentNullException.ThrowIfNull(migrationAnalysis);
        ArgumentNullException.ThrowIfNull(transformationPlans);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var document = WordprocessingDocument.Create(
            path,
            WordprocessingDocumentType.Document);

        var mainPart = document.AddMainDocumentPart();

        mainPart.Document = new Document(
            new Body());

        AddStyles(mainPart);

        var body = mainPart.Document.Body
            ?? throw new InvalidOperationException(
                "The Word document body could not be created.");

        AddTitlePage(
            body,
            projectAssessment);

        cancellationToken.ThrowIfCancellationRequested();

        AddExecutiveSummary(
            body,
            projectAssessment,
            migrationAnalysis);

        cancellationToken.ThrowIfCancellationRequested();

        AddEstateOverview(
            body,
            projectAssessment,
            transformationPlans);

        cancellationToken.ThrowIfCancellationRequested();

        AddDependencyOverview(
            body,
            projectAssessment);

        cancellationToken.ThrowIfCancellationRequested();

        AddMigrationStrategy(
            body,
            projectAssessment,
            migrationAnalysis);

        cancellationToken.ThrowIfCancellationRequested();

        AddTransformationApproach(
            body,
            transformationPlans);

        cancellationToken.ThrowIfCancellationRequested();

        AddValidationApproach(
            body,
            transformationPlans);

        cancellationToken.ThrowIfCancellationRequested();

        AddRisksAndRecommendations(
            body,
            migrationAnalysis);

        cancellationToken.ThrowIfCancellationRequested();

        AddRepositoryAnalyses(
            body,
            projectAssessment,
            migrationAnalysis);

        cancellationToken.ThrowIfCancellationRequested();

        AddExecutionControls(body);

        AddNextSteps(body);

        AddDocumentProperties(body);

        mainPart.Document.Save();

        return Task.CompletedTask;
    }

    private static void AddTitlePage(
        Body body,
        ProjectAssessment assessment)
    {
        body.Append(
            CreateParagraph(
                "Spring Modernisation Migration Strategy",
                styleId: "DocumentTitle",
                alignment: JustificationValues.Center));

        body.Append(
            CreateParagraph(
                assessment.Organisation,
                styleId: "DocumentSubtitle",
                alignment: JustificationValues.Center));

        body.Append(
            CreateParagraph(
                $"Assessment generated: " +
                $"{assessment.CreatedAt:dd MMMM yyyy HH:mm 'UTC'}",
                styleId: "DocumentSubtitle",
                alignment: JustificationValues.Center));

        body.Append(
            CreateParagraph(
                "Spring Boot 2.7 / Java 11 to " +
                "Spring Boot 3.5 / Java 21",
                styleId: "DocumentSubtitle",
                alignment: JustificationValues.Center));

        body.Append(
            CreateParagraph(
                "Generated from deterministic migration evidence " +
                "and read-only AI analysis.",
                styleId: "DocumentNote",
                alignment: JustificationValues.Center));

        AddPageBreak(body);
    }

    private static void AddExecutiveSummary(
        Body body,
        ProjectAssessment assessment,
        MigrationAnalysisResult analysis)
    {
        AddHeading(body, "1. Executive Summary", 1);

        AddLabelledParagraph(
            body,
            "AI-generated strategy summary",
            analysis.Strategy.Summary);

        AddParagraph(
            body,
            $"The assessment covers " +
            $"{assessment.Repositories.Count} repositories across " +
            $"{assessment.MigrationWaves.Waves.Count} migration waves.");

        var readyCount = assessment.Repositories.Count(
            repository =>
                repository.MigrationReadiness?.CanProceed == true);

        AddParagraph(
            body,
            $"{readyCount} of {assessment.Repositories.Count} " +
            "repositories passed the deterministic migration-readiness gate.");

        AddParagraph(
            body,
            "The approved target platform is Spring Boot 3.5.x " +
            "and Java 21.");

        AddNote(
            body,
            "Migration wave numbers, repository membership, readiness " +
            "decisions, commit SHAs and transformation parameters are " +
            "deterministic C# outputs. AI content explains these facts " +
            "but cannot alter them.");
    }

    private static void AddEstateOverview(
        Body body,
        ProjectAssessment assessment,
        IReadOnlyList<TransformationPlan> plans)
    {
        AddHeading(body, "2. Estate Overview", 1);

        var plansByRepository = plans.ToDictionary(
            plan => plan.RepositoryName,
            StringComparer.OrdinalIgnoreCase);

        var rows = new List<IReadOnlyList<string>>
        {
            new[]
            {
                "Repository",
                "Type",
                "Current",
                "Target",
                "Wave",
                "Readiness"
            }
        };

        foreach (var repository in assessment.Repositories
                     .OrderBy(item => item.RepositoryName))
        {
            plansByRepository.TryGetValue(
                repository.RepositoryName,
                out var plan);

            var currentPlatform =
                $"Spring Boot " +
                $"{repository.Spring.SpringBootVersion ?? "Unknown"}, " +
                $"Java {repository.Spring.JavaVersion ?? "Unknown"}";

            var targetPlatform = plan is null
                ? "No transformation plan"
                : $"Spring Boot {plan.Target.SpringBootVersion}, " +
                  $"Java {plan.Target.JavaVersion}";

            rows.Add(
                new[]
                {
                    repository.RepositoryName,
                    repository.Kind.ToString(),
                    currentPlatform,
                    targetPlatform,
                    plan?.MigrationWave.ToString() ?? "Not assigned",
                    repository.MigrationReadiness?.Status.ToString()
                    ?? "Not evaluated"
                });
        }

        body.Append(CreateTable(rows));
    }

    private static void AddDependencyOverview(
        Body body,
        ProjectAssessment assessment)
    {
        AddHeading(body, "3. Dependency Overview", 1);

        AddParagraph(
            body,
            "The migration sequence is based on the deterministic " +
            "repository dependency graph.");

        if (assessment.DependencyGraph.Edges.Count == 0)
        {
            AddParagraph(
                body,
                "No internal repository dependencies were detected.");

            return;
        }

        var rows = new List<IReadOnlyList<string>>
        {
            new[]
            {
                "Repository",
                "Depends On",
                "Dependency Type",
                "Version"
            }
        };

        foreach (var edge in assessment.DependencyGraph.Edges
                     .OrderBy(item => item.FromRepository)
                     .ThenBy(item => item.ToRepository))
        {
            rows.Add(
                new[]
                {
                    edge.FromRepository,
                    edge.ToRepository,
                    edge.Type.ToString(),
                    edge.Version ?? "Not specified"
                });
        }

        body.Append(CreateTable(rows));
    }

    private static void AddMigrationStrategy(
        Body body,
        ProjectAssessment assessment,
        MigrationAnalysisResult analysis)
    {
        AddHeading(body, "4. Migration Strategy", 1);

        AddNote(
            body,
            "Objectives and rationales in this section are AI-generated " +
            "explanations. Wave numbers and repository membership come " +
            "from the deterministic migration-wave plan.");

        foreach (var deterministicWave in assessment.MigrationWaves.Waves
                     .OrderBy(wave => wave.Number))
        {
            var strategyWave = analysis.Strategy.Waves.Single(
                wave => wave.Number == deterministicWave.Number);

            AddHeading(
                body,
                $"Wave {deterministicWave.Number}",
                2);

            AddLabelledParagraph(
                body,
                "Repositories",
                string.Join(
                    ", ",
                    deterministicWave.Repositories));

            AddLabelledParagraph(
                body,
                "Objective",
                strategyWave.Objective);

            AddLabelledParagraph(
                body,
                "Rationale",
                strategyWave.Rationale);
        }
    }

    private static void AddTransformationApproach(
        Body body,
        IReadOnlyList<TransformationPlan> plans)
    {
        AddHeading(body, "5. Transformation Approach", 1);

        AddParagraph(
            body,
            "Each repository transformation is pinned to the exact " +
            "source commit that passed baseline-build and provenance " +
            "validation.");

        foreach (var plan in plans
                     .OrderBy(item => item.MigrationWave)
                     .ThenBy(item => item.RepositoryName))
        {
            AddHeading(
                body,
                $"{plan.RepositoryName} - Wave {plan.MigrationWave}",
                2);

            AddLabelledParagraph(
                body,
                "Source commit",
                plan.SourceCommitSha);

            AddLabelledParagraph(
                body,
                "Migration route",
                plan.MigrationRouteId);

            AddLabelledParagraph(
                body,
                "Target platform",
                $"Spring Boot {plan.Target.SpringBootVersion}, " +
                $"Java {plan.Target.JavaVersion}");

            AddParagraph(
                body,
                "Approved OpenRewrite recipes:");

            foreach (var recipe in plan.OpenRewrite)
            {
                AddBullet(
                    body,
                    $"{recipe.Recipe} " +
                    $"[{recipe.Artifact}:{recipe.Version}]");
            }
        }
    }

    private static void AddValidationApproach(
        Body body,
        IReadOnlyList<TransformationPlan> plans)
    {
        AddHeading(body, "6. Validation Approach", 1);

        AddParagraph(
            body,
            "Post-transformation validation is selected from a fixed " +
            "C# allow-list. Policy values cannot introduce arbitrary " +
            "Maven commands.");

        foreach (var plan in plans
                     .OrderBy(item => item.MigrationWave)
                     .ThenBy(item => item.RepositoryName))
        {
            AddHeading(
                body,
                plan.RepositoryName,
                2);

            foreach (var validation in plan.Validation)
            {
                AddBullet(body, validation);
            }
        }
    }

    private static void AddRisksAndRecommendations(
        Body body,
        MigrationAnalysisResult analysis)
    {
        AddHeading(body, "7. Risks and Recommendations", 1);

        AddNote(
            body,
            "The risks and recommendations in this section are generated " +
            "by the read-only Strategy Agent from deterministic evidence.");

        AddHeading(body, "Key Risks", 2);

        if (analysis.Strategy.KeyRisks.Count == 0)
        {
            AddParagraph(
                body,
                "No evidence-based risks were identified.");
        }
        else
        {
            foreach (var risk in analysis.Strategy.KeyRisks)
            {
                AddBullet(body, risk);
            }
        }

        AddHeading(body, "Recommendations", 2);

        if (analysis.Strategy.Recommendations.Count == 0)
        {
            AddParagraph(
                body,
                "No additional recommendations were generated.");
        }
        else
        {
            foreach (var recommendation in
                     analysis.Strategy.Recommendations)
            {
                AddBullet(body, recommendation);
            }
        }
    }

    private static void AddRepositoryAnalyses(
        Body body,
        ProjectAssessment assessment,
        MigrationAnalysisResult analysis)
    {
        AddHeading(body, "8. Repository-Level Analysis", 1);

        var assessmentByRepository =
            assessment.Repositories.ToDictionary(
                repository => repository.RepositoryName,
                StringComparer.OrdinalIgnoreCase);

        foreach (var repositoryAnalysis in
                 analysis.RepositoryAnalyses)
        {
            AddHeading(
                body,
                repositoryAnalysis.RepositoryName,
                2);

            if (assessmentByRepository.TryGetValue(
                    repositoryAnalysis.RepositoryName,
                    out var repository))
            {
                AddLabelledParagraph(
                    body,
                    "Deterministic readiness",
                    repository.MigrationReadiness?.Status.ToString()
                    ?? "Not evaluated");

                AddLabelledParagraph(
                    body,
                    "Assessed commit",
                    repository.CommitSha);

                AddLabelledParagraph(
                    body,
                    "Current platform",
                    $"Spring Boot " +
                    $"{repository.Spring.SpringBootVersion ?? "Unknown"}, " +
                    $"Java {repository.Spring.JavaVersion ?? "Unknown"}");
            }

            AddLabelledParagraph(
                body,
                "AI-generated summary",
                repositoryAnalysis.Summary);

            AddHeading(body, "Findings", 3);

            foreach (var finding in repositoryAnalysis.Findings)
            {
                AddBullet(body, finding);
            }

            AddHeading(body, "Recommended Actions", 3);

            foreach (var action in
                     repositoryAnalysis.RecommendedActions)
            {
                AddBullet(body, action);
            }
        }
    }

    private static void AddExecutionControls(
        Body body)
    {
        AddHeading(body, "9. Execution Controls", 1);

        var controls = new[]
        {
            "Only repositories that pass deterministic migration " +
            "readiness can produce a transformation plan.",

            "Transformation plans contain the exact assessed source " +
            "commit SHA.",

            "Baseline-build provenance must match the assessed commit.",

            "Repository workspaces are isolated underneath a controlled " +
            "workspace root.",

            "Repository workspaces are checked out at an exact detached " +
            "commit.",

            "Git hooks and local file transport are disabled in the " +
            "controlled Git process.",

            "Only approved and pinned OpenRewrite recipes can execute.",

            "Validation operations are selected from a fixed C# mapping.",

            "The Strategy Agent has no Git, Maven, filesystem or source " +
            "modification tools.",

            "AI output cannot alter deterministic migration waves, " +
            "repository membership or readiness decisions.",

            "Transformation workspaces are deleted after execution."
        };

        foreach (var control in controls)
        {
            AddBullet(body, control);
        }
    }

    private static void AddNextSteps(
        Body body)
    {
        AddHeading(body, "10. Next Steps", 1);

        var steps = new[]
        {
            "Review and approve the generated migration strategy.",

            "Execute the approved Wave 1 transformation for " +
            "platform-parent.",

            "Run deterministic effective-POM and dependency-resolution " +
            "validation.",

            "Capture the transformation and validation evidence.",

            "Proceed to later migration waves only after successful " +
            "validation of upstream components.",

            "Invoke the separate Migration/Remediation Agent only " +
            "for unresolved transformation or validation failures."
        };

        for (var index = 0; index < steps.Length; index++)
        {
            AddNumberedParagraph(
                body,
                index + 1,
                steps[index]);
        }
    }

    private static void AddDocumentProperties(
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

    private static void AddStyles(
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

    private static Style CreateStyle(
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

    private static void AddHeading(
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

    private static void AddParagraph(
        Body body,
        string text)
    {
        body.Append(
            CreateParagraph(
                text,
                "Normal"));
    }

    private static void AddLabelledParagraph(
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

    private static void AddNote(
        Body body,
        string text)
    {
        body.Append(
            CreateParagraph(
                text,
                "DocumentNote"));
    }

    private static void AddBullet(
        Body body,
        string text)
    {
        body.Append(
            CreateParagraph(
                $"\u2022 {text}",
                "Normal"));
    }

    private static void AddNumberedParagraph(
        Body body,
        int number,
        string text)
    {
        body.Append(
            CreateParagraph(
                $"{number}. {text}",
                "Normal"));
    }

    private static Paragraph CreateParagraph(
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

    private static Table CreateTable(
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

    private static void AddPageBreak(
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