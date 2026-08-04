using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using JetBrains.Annotations;
using MethodBoundaryAspect.Fody.Attributes;

namespace Harness.Shared.Observability;

/// <summary>
/// Starts a new <see cref="Activity"/> (span) on method entry and completes it on exit or exception.
/// Optionally sets tags on the span via the declarative syntax defined by the configured <see cref="ISpanTagsCompiler"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Span name:</b> Uses <see cref="SpanName"/> if set; otherwise defaults to <c>DeclaringType.MethodName</c>.
/// </para>
/// <para>
/// <b>Asynchronous methods:</b> Only methods returning <see cref="Task"/> or <see cref="Task{TResult}"/> are handled asynchronously;
/// <see cref="ValueTask"/> and <see cref="ValueTask{TResult}"/> are not specially tracked and will have their
/// span closed immediately on exit.
/// </para>
/// <para>
/// <b>Tag syntax:</b> See <see cref="SpanTagsCompiler"/> (or your custom <see cref="ISpanTagsCompiler"/> implementation).
/// Use <see cref="CompilerType"/> to inject a custom compiler.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor)]
[UsedImplicitly(ImplicitUseTargetFlags.Members)]
public sealed class TraceSpanAttribute : OnMethodBoundaryAspect
{
    private static readonly ActivitySource Source = new("ACTR.Harness");

    // TODO: Problems with generic method instances with the same base declaration?
    /// <summary>
    /// Cache of compiled tag‑setter delegates keyed by method.
    /// </summary>
    private static readonly ConcurrentDictionary<MethodBase, Action<Activity, object?[]>> TagSetters = new();

    /// <summary>
    /// Cache of <see cref="ISpanTagsCompiler"/> instances keyed by compiler <see cref="Type"/>.
    /// The default compiler (type <see cref="SpanTagsCompiler"/>) is reused when <see cref="CompilerType"/> is <c>null</c>.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, ISpanTagsCompiler> CompilerCache = new();

    /// <summary>
    /// Optional type of the custom compiler implementing <see cref="ISpanTagsCompiler"/>.
    /// The type must have a parameterless constructor.
    /// </summary>
    public Type? CompilerType { get; init; }

    /// <summary>
    /// Optional span name. If not provided, the name defaults to <c>DeclaringType.MethodName</c>.
    /// </summary>
    public string? SpanName { get; }

    /// <summary>
    /// Array of tag definitions whose syntax is defined by the configured <see cref="ISpanTagsCompiler"/>.
    /// For the default compiler, refer to <see cref="SpanTagsCompiler"/>.
    /// </summary>
    public string[]? Tags { get; }

    /// <summary>
    /// Initialises a new instance of the <see cref="TraceSpanAttribute"/>.
    /// </summary>
    public TraceSpanAttribute()
    {
    }

    /// <summary>
    /// Initialises a new instance with a custom span name.
    /// </summary>
    /// <param name="spanName">The name for the created span.</param>
    public TraceSpanAttribute(string spanName) : this() => SpanName = spanName;

    /// <summary>
    /// Initialises a new instance with a custom span name and an array of tag definitions.
    /// </summary>
    /// <param name="spanName">The name for the created span.</param>
    /// <param name="tags">Tag definitions (see class remarks for syntax).</param>
    public TraceSpanAttribute(string spanName, params string[] tags) : this() => (SpanName, Tags) = (spanName, tags);

    private static string GetSpanName(MethodBase method) =>
        method.DeclaringType is { } t ? $"{t.Name}.{method.Name}" : method.Name;

    /// <summary>
    /// Creates (or retrieves from cache) the <see cref="ISpanTagsCompiler"/> for the configured <see cref="CompilerType"/>.
    /// </summary>
    private static ISpanTagsCompiler GetCompiler(Type? compilerType)
    {
        var type = compilerType ?? typeof(SpanTagsCompiler);

        if (!typeof(ISpanTagsCompiler).IsAssignableFrom(type))
            throw new InvalidOperationException(
                $"Compiler type {type} must implement {nameof(ISpanTagsCompiler)}.");

        var ctor = type.GetConstructor(Type.EmptyTypes);
        if (ctor is null)
            throw new InvalidOperationException(
                $"Compiler type {type} must have a parameterless constructor.");

        return CompilerCache.GetOrAdd(type, _ => (ISpanTagsCompiler)ctor.Invoke(null));
    }

    public override void OnEntry(MethodExecutionArgs args)
    {
        var method = args.Method;
        var activity = Source.StartActivity(SpanName ?? GetSpanName(method));

        if (Tags is { Length: > 0 })
        {
            var setter = TagSetters.GetOrAdd(method, m =>
            {
                var compiler = GetCompiler(CompilerType);
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
            // For Task-returning methods we must wait for the task to complete before closing the span.
            if (args.MethodExecutionTag is not SpanState state) return;
            args.MethodExecutionTag = null;

            task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    var ex = t.Exception?.InnerException ?? t.Exception;
                    state.SetError(ex?.Message ?? "Error");
                }
                else if (t.IsCanceled)
                {
                    state.SetError("Canceled");
                }
                else
                {
                    state.SetOk();
                }

                state.DisposeActivity();
            }, TaskScheduler.Default);

            return;
        }

        // Synchronous completion or non-Task return value (includes ValueTask – not specially handled).
        Complete(args, null);
    }

    public override void OnException(MethodExecutionArgs args) => Complete(args, args.Exception);

    private static void Complete(MethodExecutionArgs args, Exception? exception)
    {
        if (args.MethodExecutionTag is not SpanState state) return;

        args.MethodExecutionTag = null;

        if (exception is null)
        {
            state.SetOk();
        }
        else
        {
            state.SetError(exception.Message);
        }

        state.DisposeActivity();
    }

    /// <summary>
    /// Holds the active <see cref="Activity"/> and ensures completion occurs only once.
    /// </summary>
    private sealed class SpanState
    {
        public Activity? Activity;
        private bool _completed;

        public void SetOk()
        {
            if (_completed) return;
            _completed = true;
            Activity?.SetStatus(ActivityStatusCode.Ok);
        }

        public void SetError(string message)
        {
            if (_completed) return;
            _completed = true;
            Activity?.SetStatus(ActivityStatusCode.Error, message);
        }

        public void DisposeActivity()
        {
            Activity?.Dispose();
            Activity = null;
        }
    }
}