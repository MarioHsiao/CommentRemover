﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace CommentRemover
{
    internal sealed class RemoveAllExceptTaskComments : BaseCommand<RemoveAllExceptTaskComments>
    {
        private RemoveAllExceptTaskComments(Package package)
        : base(package, PackageGuids.guidPackageCmdSet, PackageIds.RemoveAllExceptTaskComments)
        { }

        public static void Initialize(Package package)
        {
            Instance = new RemoveAllExceptTaskComments(package);
        }

        protected override void Execute(OleMenuCommand button)
        {
            var view = ProjectHelpers.GetCurentTextView();
            var mappingSpans = GetClassificationSpans(view, "comment");

            if (!mappingSpans.Any())
                return;

            try
            {
                VSPackage.DTE.UndoContext.Open(button.Text);

                DeleteFromBuffer(view, mappingSpans);
                AddTelemetry("Comments not task removed");
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
            }
            finally
            {
                VSPackage.DTE.UndoContext.Close();
            }
        }

        private static void DeleteFromBuffer(IWpfTextView view, IEnumerable<IMappingSpan> mappingSpans)
        {
            var affectedLines = new List<int>();

            RemoveCommentSpansFromBuffer(view, mappingSpans, affectedLines);
            RemoveAffectedEmptyLines(view, affectedLines);
        }

        private static void RemoveCommentSpansFromBuffer(IWpfTextView view, IEnumerable<IMappingSpan> mappingSpans, IList<int> affectedLines)
        {
            using (var edit = view.TextBuffer.CreateEdit())
            {
                foreach (var mappingSpan in mappingSpans)
                {
                    var start = mappingSpan.Start.GetPoint(view.TextBuffer, PositionAffinity.Predecessor).Value;
                    var end = mappingSpan.End.GetPoint(view.TextBuffer, PositionAffinity.Successor).Value;

                    var span = new Span(start, end - start);
                    var lines = view.TextBuffer.CurrentSnapshot.Lines.Where(l => l.Extent.IntersectsWith(span));
                    bool skip = false;

                    foreach (var line in lines)
                    {
                        bool isTaskComment = RemoveTasksCommand.ContainsTaskComment(line);

                        if (isTaskComment)
                        {
                            skip = true;
                        }
                        else {
                            if (!affectedLines.Contains(line.LineNumber))
                                affectedLines.Add(line.LineNumber);
                        }
                    }

                    if (skip)
                        continue;

                    var mappingText = view.TextBuffer.CurrentSnapshot.GetText(span.Start, span.Length);
                    string empty = Regex.Replace(mappingText, "([\\S]+)", string.Empty);

                    edit.Replace(span.Start, span.Length, empty);
                }

                edit.Apply();
            }
        }

        private static void RemoveAffectedEmptyLines(IWpfTextView view, IList<int> affectedLines)
        {
            if (!affectedLines.Any())
                return;

            using (var edit = view.TextBuffer.CreateEdit())
            {
                foreach (var lineNumber in affectedLines)
                {
                    var line = view.TextBuffer.CurrentSnapshot.GetLineFromLineNumber(lineNumber);

                    if (IsLineEmpty(line))
                    {
                        // Strip next line if empty
                        if (view.TextBuffer.CurrentSnapshot.LineCount > line.LineNumber + 1)
                        {
                            var next = view.TextBuffer.CurrentSnapshot.GetLineFromLineNumber(lineNumber + 1);

                            if (IsLineEmpty(next))
                                edit.Delete(next.Start, next.LengthIncludingLineBreak);
                        }

                        edit.Delete(line.Start, line.LengthIncludingLineBreak);
                    }
                }

                edit.Apply();
            }
        }
    }
}