using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis.MSBuild;
using InlineDocumentationAnalyzer;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Linq;
using System.Diagnostics.SymbolStore;

namespace DocumentationChecker
{
    public static class DocumentationChecker
    {
        public static async System.Threading.Tasks.Task<List<String>> CheckXMLCommentDocumentation(string path)
        {
            MSBuildLocator.RegisterDefaults();

            List<string> issues = new List<string>();

            using (var workspace = MSBuildWorkspace.Create())
            {
                workspace.LoadMetadataForReferencedProjects = true;

                System.Diagnostics.Debug.WriteLine("Opening workspace");

                var solution = await workspace.OpenSolutionAsync(path);

                System.Diagnostics.Debug.WriteLine("Creating Code Analyzer");

                // Create a reference to the custom Code Analyzer to add to the projects
                var immutableArray = (new List<DiagnosticAnalyzer> {
                        new InlineDocumentationAnalyzerAnalyzer()
                    }).ToImmutableArray();
                var analyzerImageReference = new AnalyzerImageReference(immutableArray);

                System.Diagnostics.Debug.WriteLine("Creating analyzer-laced solution");

                // Add the custom code analzyer to the solution, so it will be used with all projects
                solution = solution.AddAnalyzerReference(analyzerImageReference);

                System.Diagnostics.Debug.WriteLine("Loaded analyzer-laced solution");


                // Output the diagnostics
                foreach (var diagnostic in workspace.Diagnostics)
                {
                    System.Diagnostics.Debug.WriteLine(diagnostic);
                    Console.WriteLine(diagnostic.Message);
                }

                // Iterate over every project in the solution
                foreach (var project in solution.Projects)
                {
                    // Get a compilation of the project asynchronously.  
                    // This provides us with syntax trees we can traverse
                    var compilation = await project.GetCompilationAsync();
                    foreach (var tree in compilation.SyntaxTrees)
                    {
                        // Check the Class definitions in the syntax tree for correct commenting
                        issues.AddRange(CheckForClassComments(tree));

                        // Check the Property definitions in the syntax tree for correct commenting
                        issues.AddRange(CheckForPropertyComments(tree));

                        // Check the Method definitions in the syntax tree for correct commenting
                        issues.AddRange(CheckForMethodComments(tree));
                    }
                }
                /*
                foreach(var proj in solution.Projects)
                {
                    var project = proj.AddAnalyzerReference(analyzerImageReference);
                    var compilation = await project.GetCompilationAsync();
                    var diagnostics = compilation.GetDiagnostics();
                    foreach(var diagnostic in diagnostics)
                    {
                        Console.WriteLine(diagnostic.GetMessage());
                    }
                }
                */
            }

            return issues;
        }

        /// <summary>
        /// This helper method checks all class definitions in the supplied <paramref name="tree"/> 
        /// for correct XML documentation
        /// </summary>
        /// <remarks>
        /// The requirement is a minimum of a summary element
        /// </remarks>
        /// <param name="tree">The syntax tree</param>
        static List<string> CheckForClassComments(SyntaxTree tree)
        {
            List<string> issues = new List<string>();

            // Iterate over all class declarations in the syntax tree
            var classes = tree.GetRoot().DescendantNodesAndSelf().OfType<ClassDeclarationSyntax>();
            foreach (var c in classes)
            {
                // Grab the leading comments for the classes
                var xmlTrivia = c.GetLeadingTrivia()
                    .Select(i => i.GetStructure())
                    .OfType<DocumentationCommentTriviaSyntax>()
                    .FirstOrDefault();

                // If xmlTrivia is null, there were no comments written for the class
                if (xmlTrivia is null)
                {
                    issues.Add($"Class {c.Identifier.ValueText} does not have any XML comments");
                    continue;
                }


                // Print the class comments for visual checking
                Console.WriteLine(xmlTrivia.ToFullString());

                // Check for a <summary> element in the comments. We require this as minimum documentation
                var hasSummary = xmlTrivia.ChildNodes()
                    .OfType<XmlElementSyntax>()
                    .Any(i => i.StartTag.Name.ToString().Equals("summary"));
                if (!hasSummary)
                {
                    issues.Add($"Class {c.Identifier.ValueText} does not have a <summary> element in its XML comments");
                }
            }
            return issues;
        }

        /// <summary>
        /// This helper method checks all property definitions in the supplied <paramref name="tree"/> 
        /// for correct XML documentation
        /// </summary>
        /// <remarks>
        /// The requirement is a minimum of a summary or value element
        /// </remarks>
        /// <param name="tree"></param>
        static List<string> CheckForPropertyComments(SyntaxTree tree)
        {
            List<string> issues = new List<string>();

            // Iterate over all property declarations in the syntax tree
            var properties = tree.GetRoot().DescendantNodesAndSelf().OfType<PropertyDeclarationSyntax>();
            foreach (var p in properties)
            {
                // Grab the leading comments for the classes
                var xmlTrivia = p.GetLeadingTrivia()
                    .Select(i => i.GetStructure())
                    .OfType<DocumentationCommentTriviaSyntax>()
                    .FirstOrDefault();
                                
                // If xmlTrivia is null, there were no comments written for the property
                if (xmlTrivia is null)
                {
                    var c = p.Parent.DescendantNodesAndSelf()
                        .OfType<ClassDeclarationSyntax>()
                        .FirstOrDefault();

                    issues.Add($"Property {p.Identifier.ValueText} in {c.Identifier.ValueText} does not have any XML comments");
                    continue;
                }

                // Print the property comments for visual checking
                Console.WriteLine(xmlTrivia.ToFullString());

                // Check for a <summary> or <value> element in the comments. 
                var hasSummaryOrValue = xmlTrivia.ChildNodes()
                    .OfType<XmlElementSyntax>()
                    .Any(i => i.StartTag.Name.ToString().Equals("summary") || i.StartTag.Name.ToString().Equals("value"));
                if (!hasSummaryOrValue)
                {
                    var c = p.Parent.DescendantNodesAndSelf()
                           .OfType<ClassDeclarationSyntax>()
                           .FirstOrDefault();

                    issues.Add($"Property {p.Identifier.ValueText} in {c.Identifier.ValueText} does not have a <summary> or <value> element in its XML comments");
                }
            }
            return issues;
        }

        /// <summary>
        /// This helper method checks all method definitions in the supplied <paramref name="tree"/> 
        /// for correct XML documentation
        /// </summary>
        /// <remarks>
        /// The requirement is a summary, plus a param for each param, plus a return for any return value but void
        /// </remarks>
        /// <param name="tree"></param>
        static List<string> CheckForMethodComments(SyntaxTree tree)
        {
            List<string> issues = new List<string>();

            // Iterate over all property declarations in the syntax tree
            var methods = tree.GetRoot().DescendantNodesAndSelf().OfType<MethodDeclarationSyntax>();
            foreach (var m in methods)
            {
                // Grab the leading comments for the method
                var xmlTrivia = m.GetLeadingTrivia()
                    .Select(i => i.GetStructure())
                    .OfType<DocumentationCommentTriviaSyntax>()
                    .FirstOrDefault();

                // If xmlTrivia is null, there were no comments written for the method
                if (xmlTrivia is null)
                {
                    var c = m.Parent.DescendantNodesAndSelf()
                        .OfType<ClassDeclarationSyntax>()
                        .FirstOrDefault();

                    issues.Add($"Method {m.Identifier.ValueText} in {c.Identifier.ValueText} does not have any XML comments");
                    continue;
                }

                // Print the method comments for visual checking
                Console.WriteLine(xmlTrivia.ToFullString());

                // Check for a <summary> element in the comments. 
                var hasSummary = xmlTrivia.ChildNodes()
                    .OfType<XmlElementSyntax>()
                    .Any(i => i.StartTag.Name.ToString().Equals("summary"));
                if (!hasSummary)
                {
                    var c = m.Parent.DescendantNodesAndSelf()
                           .OfType<ClassDeclarationSyntax>()
                           .FirstOrDefault();

                    issues.Add($"Method {m.Identifier.ValueText} in {c.Identifier.ValueText} does not have a <summary> element in its XML comments");
                }

                // Check for the <param> elements in the comments 
                foreach(var p in m.ParameterList.Parameters)
                {
                    Console.WriteLine(p.Identifier.ValueText);

                    var paras = xmlTrivia.ChildNodes()
                        .OfType<XmlElementSyntax>()
                        .Where(i => i.StartTag.Name.ToString().Equals("param"));

                    Console.WriteLine(paras);
                }
            }
            return issues;
        }
    }
}
