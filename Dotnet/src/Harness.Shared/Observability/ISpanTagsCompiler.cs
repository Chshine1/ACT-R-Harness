using System.Diagnostics;
using System.Reflection;

namespace Harness.Shared.Observability;

/// <summary>
/// Compiles tag definitions into efficient setter delegates to avoid runtime parsing and reflection.
/// </summary>
public interface ISpanTagsCompiler
{
    // ReSharper disable once InvalidXmlDocComment
    /// <summary>
    /// Compiles all tag definitions for a given method into a single <see cref="Action{Activity, object?[]}"/>
    /// that sets tags on the provided <see cref="Activity"/> using the arguments array.
    /// </summary>
    /// <param name="method">The target method for which the tags are being compiled.</param>
    /// <param name="tagDefs">Array of tag definitions following the supported syntax.</param>
    /// <returns>A compiled delegate that sets all tags when invoked with an <see cref="Activity"/> and an array of method arguments.</returns>
    Action<Activity, object?, object?[]> CompileAllTags(MethodBase method, string[] tagDefs);
}