﻿' Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports System.Globalization
Imports System.IO
Imports System.Text
Imports System.Threading
Imports Microsoft.CodeAnalysis.Collections
Imports Microsoft.CodeAnalysis.Instrumentation
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols
Imports System.Xml.Linq
Imports Microsoft.CodeAnalysis.Text

Namespace Microsoft.CodeAnalysis.VisualBasic
    Partial Public Class VisualBasicCompilation

        Partial Friend Class DocumentationCommentCompiler
            Inherits VisualBasicSymbolVisitor

            Private ReadOnly _assemblyName As String
            Private ReadOnly _compilation As VisualBasicCompilation
            Private ReadOnly _processIncludes As Boolean
            Private ReadOnly _isForSingleSymbol As Boolean ' minor differences in behavior between batch case and API case.
            Private ReadOnly _diagnostics As DiagnosticBag
            Private ReadOnly _cancellationToken As CancellationToken
            Private ReadOnly _preferredCulture As CultureInfo
            Private ReadOnly _filterSyntaxTree As SyntaxTree ' if not null, limit analysis to types residing in this tree
            Private ReadOnly _filterSpanWithinTree As TextSpan? ' if filterTree and filterSpanWithinTree is not null, limit analysis to types residing within this span in the filterTree.
            Private _writer As DocWriter

            'private CommonSyntaxNodeLocationComparer lazyComparer;

            Private _includedFileCache As DocumentationCommentIncludeCache

            Private Sub New(assemblyName As String, compilation As VisualBasicCompilation, writer As TextWriter,
                processIncludes As Boolean, isForSingleSymbol As Boolean, diagnostics As DiagnosticBag,
                filterTree As SyntaxTree, filterSpanWithinTree As TextSpan?,
                preferredCulture As CultureInfo, cancellationToken As CancellationToken)

                Me._assemblyName = assemblyName
                Me._compilation = compilation
                Me._writer = New DocWriter(writer)
                Me._processIncludes = processIncludes
                Me._isForSingleSymbol = isForSingleSymbol
                Me._diagnostics = diagnostics
                Me._filterSyntaxTree = filterTree
                Me._filterSpanWithinTree = filterSpanWithinTree
                Me._cancellationToken = cancellationToken
                Me._preferredCulture = If(preferredCulture, CultureInfo.InvariantCulture)
            End Sub

            ''' <summary>
            ''' Traverses the symbol table processing XML documentation comments and optionally writing them to a provided stream.
            ''' </summary>
            ''' <param name="compilation">Compilation that owns the symbol table.</param>
            ''' <param name="assemblyName">Assembly name override, if specified. Otherwise the <see cref="ISymbol.Name"/> of the source assembly is used.</param>
            ''' <param name="xmlDocStream">Stream to which XML will be written, if specified.</param>
            ''' <param name="diagnostics">Will be supplemented with documentation comment diagnostics.</param>
            ''' <param name="cancellationToken">To stop traversing the symbol table early.</param>
            ''' <param name="filterTree">Only report diagnostics from this syntax tree, if non-null.</param>
            ''' <param name="filterSpanWithinTree">If <paramref name="filterTree"/> and filterSpanWithinTree is non-null, report diagnostics within this span in the <paramref name="filterTree"/>.</param>
            Friend Shared Sub WriteDocumentationCommentXml(compilation As VisualBasicCompilation,
                                                           assemblyName As String,
                                                           xmlDocStream As Stream,
                                                           diagnostics As DiagnosticBag,
                                                           cancellationToken As CancellationToken,
                                                           Optional filterTree As SyntaxTree = Nothing,
                                                           Optional filterSpanWithinTree As TextSpan? = Nothing)

                Using Logger.LogBlock(FunctionId.VisualBasic_DocumentationCommentCompiler_WriteDocumentationCommentXml,
                                      message:=compilation.AssemblyName, cancellationToken:=cancellationToken)

                    Dim writer As StreamWriter = Nothing
                    If xmlDocStream IsNot Nothing AndAlso xmlDocStream.CanWrite Then
                        writer = New StreamWriter(xmlDocStream, New UTF8Encoding(True, False), bufferSize:=&H400, leaveOpen:=True)
                    End If

                    Using writer
                        ' TODO: get preferred culture from compilation(?)
                        Dim compiler As New DocumentationCommentCompiler(If(assemblyName, compilation.SourceAssembly.Name), compilation, writer, True, False,
                            diagnostics, filterTree, filterSpanWithinTree, preferredCulture:=Nothing, cancellationToken:=cancellationToken)

                        compiler.Visit(compilation.SourceAssembly.GlobalNamespace)
                        Debug.Assert(compiler._writer.IndentDepth = 0)
                    End Using

                    For Each tree In compilation.SyntaxTrees
                        MislocatedDocumentationCommentFinder.ReportUnprocessed(tree, filterSpanWithinTree, diagnostics, cancellationToken)
                    Next
                End Using
            End Sub

            Private ReadOnly Property [Module] As SourceModuleSymbol
                Get
                    Return DirectCast(Me._compilation.SourceModule, SourceModuleSymbol)
                End Get
            End Property

            ''' <summary>
            ''' Gets the XML that would be written to the documentation comment file for this assembly.
            ''' </summary>
            ''' <param name="symbol">The symbol for which to retrieve documentation comments.</param>
            ''' <param name="processIncludes">True to treat includes as semantically meaningful 
            ''' (pull in contents from other files and bind crefs, etc).</param>
            ''' <param name="cancellationToken">To stop traversing the symbol table early.</param>
            Friend Shared Function GetDocumentationCommentXml(symbol As Symbol,
                                                              processIncludes As Boolean,
                                                              preferredCulture As CultureInfo,
                                                              cancellationToken As CancellationToken) As String

                Debug.Assert(symbol.Kind = SymbolKind.Event OrElse
                             symbol.Kind = SymbolKind.Field OrElse
                             symbol.Kind = SymbolKind.Method OrElse
                             symbol.Kind = SymbolKind.NamedType OrElse
                             symbol.Kind = SymbolKind.Property)

                Dim compilation As VisualBasicCompilation = symbol.DeclaringCompilation
                Debug.Assert(compilation IsNot Nothing)

                Dim pooled As PooledStringBuilder = PooledStringBuilder.GetInstance()
                Dim writer As New StringWriter(pooled.Builder, CultureInfo.InvariantCulture)
                Dim discardedDiagnostics As DiagnosticBag = DiagnosticBag.GetInstance()

                Dim compiler = New DocumentationCommentCompiler(Nothing, compilation, writer, processIncludes,
                    True, discardedDiagnostics, Nothing, Nothing, preferredCulture, cancellationToken)
                compiler.Visit(symbol)
                Debug.Assert(compiler._writer.IndentDepth = 0)

                discardedDiagnostics.Free()
                writer.Dispose()
                Return pooled.ToStringAndFree()
            End Function

        End Class

    End Class
End Namespace
