using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using JetBrains.Annotations;
using MethodBoundaryAspect.Fody.Attributes;

namespace Harness.Shared.Observability;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor)]
[UsedImplicitly(ImplicitUseTargetFlags.Members)]
public sealed class TraceSpanAttribute : OnMethodBoundaryAspect
{
    private static readonly ActivitySource Source = new("ACTR.Harness");

    private static readonly ConcurrentDictionary<MethodBase, Action<Activity, object?[]>> TagSetters = new();
    private readonly Lazy<ISpanTagsCompiler> _tagsCompilerLazy;

    public Type? CompilerType { get; init; }
    public string? SpanName { get; }
    public string[]? Tags { get; }

    public TraceSpanAttribute()
    {
        _tagsCompilerLazy = new Lazy<ISpanTagsCompiler>(CreateCompiler);
    }

    public TraceSpanAttribute(string spanName) : this() => SpanName = spanName;

    public TraceSpanAttribute(string spanName, params string[] tags) : this() => (SpanName, Tags) = (spanName, tags);

    private static string GetSpanName(MethodBase method) =>
        method.DeclaringType is { } t ? $"{t.Name}.{method.Name}" : method.Name;

    public override void OnEntry(MethodExecutionArgs args)
    {
        var method = args.Method;
        var activity = Source.StartActivity(SpanName ?? GetSpanName(method));

        if (Tags is { Length: > 0 })
        {
            var setter = TagSetters.GetOrAdd(args.Method, m =>
            {
                var compiler = _tagsCompilerLazy.Value;
                return compiler.CompileAllTags(m, Tags);
            });
            if (activity is not null) setter(activity, args.Arguments);
        }

        args.MethodExecutionTag = new SpanState
        {
            Activity = activity
        };
    }

    public override void OnExit(MethodExecutionArgs args)
    {
        if (args.ReturnValue is Task task)
        {
            if (args.MethodExecutionTag is not SpanState state) return;
            args.MethodExecutionTag = null;

            task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    var ex = t.Exception?.InnerException ?? t.Exception;
                    state.Activity?.SetStatus(ActivityStatusCode.Error, ex?.Message ?? "Error");
                }
                else if (t.IsCanceled)
                {
                    state.Activity?.SetStatus(ActivityStatusCode.Error, "Canceled");
                }
                else
                {
                    state.Activity?.SetStatus(ActivityStatusCode.Ok);
                }

                state.Activity?.Dispose();
            }, TaskScheduler.Default);

            return;
        }

        Complete(args, null);
    }

    public override void OnException(MethodExecutionArgs args) => Complete(args, args.Exception);

    private static void Complete(MethodExecutionArgs args, Exception? exception)
    {
        if (args.MethodExecutionTag is not SpanState state) return;

        args.MethodExecutionTag = null;

        if (exception is null)
        {
            state.Activity?.SetStatus(ActivityStatusCode.Ok);
        }
        else
        {
            state.Activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        }

        state.Activity?.Dispose();
    }

    private ISpanTagsCompiler CreateCompiler()
    {
        if (CompilerType is null) return new SpanTagsCompiler();

        if (!typeof(ISpanTagsCompiler).IsAssignableFrom(CompilerType))
            throw new InvalidOperationException(
                $"Compiler type {CompilerType} must implement {nameof(ISpanTagsCompiler)}.");

        var ctor = CompilerType.GetConstructor(Type.EmptyTypes);
        if (ctor is null)
            throw new InvalidOperationException(
                $"Compiler type {CompilerType} must have a parameterless constructor.");

        return (ISpanTagsCompiler)ctor.Invoke(null);
    }

    private sealed class SpanState
    {
        public Activity? Activity;
    }
}