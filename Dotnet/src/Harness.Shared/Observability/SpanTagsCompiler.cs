using System.Diagnostics;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    Action<Activity, object?[]> CompileAllTags(MethodBase method, string[] tagDefs);
}

// ReSharper disable GrammarMistakeInComment
/// <summary>
/// Default implementation of <see cref="ISpanTagsCompiler"/> that compiles tag definitions into expression trees.
/// </summary>
/// <remarks>
/// <para>Supported tag definition syntax:</para>
/// <list type="bullet">
///   <item><c>"key = 'constant string'"</c> – sets a constant string value (support \' escaping).</item>
///   <item><c>"key = null"</c>, <c>"true"</c>, <c>"false"</c>, integer, or floating-point literal.</item>
///   <item><c>"key = {parameter}"</c> – takes the value of a method parameter.</item>
///   <item><c>"key = {parameter.Property.Nested}"</c> – navigates a property chain on the parameter.</item>
///   <item><c>"key ?= expression"</c> – makes the tag optional; it is only set if the resolved value is not null.</item>
///   <item>Append <c>" as json"</c> to any expression to serialize the value to JSON using the configured options.</item>
/// </list>
/// <para>
/// Note that JSON serialization uses <see cref="JsonSerializerOptions"/> with <see cref="System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/>,
/// which may not escape characters like &lt;, &gt;, &amp;. Ensure the consuming system safely handles such output if it might be rendered in HTML contexts.
/// </para>
/// </remarks>
// ReSharper restore GrammarMistakeInComment
public class SpanTagsCompiler : ISpanTagsCompiler
{
    private readonly JsonSerializerOptions? _jsonSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private struct TagSpec
    {
        public string Key;
        public int ParamIndex;
        public string[]? PropertyPath;
        public bool AsJson;
        public bool IsOptional;
        public object? ConstantValue;
        public bool IsConstant;
    }

    /// <summary>
    /// Parses a single tag definition and resolves parameter names against the given parameter list.
    /// </summary>
    private static TagSpec ParseTag(string definition, ParameterInfo[] parameters)
    {
        if (string.IsNullOrWhiteSpace(definition)) throw new FormatException("Label definition cannot be empty");

        string? key;
        string valueExpr;
        var isOptional = false;

        var optionalEqIndex = definition.IndexOf("?=", StringComparison.Ordinal);
        var normalEqIndex = definition.IndexOf('=');

        if (optionalEqIndex >= 0)
        {
            key = definition[..optionalEqIndex].Trim();
            valueExpr = definition[(optionalEqIndex + 2)..].Trim();
            isOptional = true;
        }
        else if (normalEqIndex >= 0)
        {
            key = definition[..normalEqIndex].Trim();
            valueExpr = definition[(normalEqIndex + 1)..].Trim();
        }
        else
        {
            throw new FormatException(
                $"Label definition must be of `<key> (?)= <value> <options>` format: {definition}");
        }

        bool isConstant;
        object? constantValue = null;
        string? paramName = null;
        string[]? propertyPath = null;
        var asJson = false;

        const string asJsonSuffix = " as json";
        var hasJsonSuffix = valueExpr.EndsWith(asJsonSuffix, StringComparison.OrdinalIgnoreCase);
        if (hasJsonSuffix)
        {
            valueExpr = valueExpr[..^asJsonSuffix.Length].TrimEnd();
            asJson = true;
        }

        if (valueExpr.StartsWith('\'') && valueExpr.EndsWith('\'') && valueExpr.Length >= 2)
        {
            var raw = valueExpr.Substring(1, valueExpr.Length - 2);
            constantValue = raw.Replace("\\'", "'");
            isConstant = true;
        }
        else if (valueExpr.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            constantValue = null;
            isConstant = true;
        }
        else if (valueExpr.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            constantValue = true;
            isConstant = true;
        }
        else if (valueExpr.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            constantValue = false;
            isConstant = true;
        }
        else if (long.TryParse(valueExpr, out var longVal) && valueExpr == longVal.ToString())
        {
            constantValue = longVal;
            isConstant = true;
        }
        else if (double.TryParse(valueExpr, NumberStyles.Float, CultureInfo.InvariantCulture, out double doubleVal))
        {
            constantValue = doubleVal;
            isConstant = true;
        }
        else if (valueExpr.StartsWith('{') && valueExpr.EndsWith('}') && valueExpr.Length >= 3)
        {
            var inner = valueExpr.Substring(1, valueExpr.Length - 2).Trim();
            if (inner.Length == 0) throw new FormatException($"Interpolation tag cannot be empty: {definition}");

            var dotIndex = inner.IndexOf('.');
            if (dotIndex > 0)
            {
                paramName = inner[..dotIndex];
                var path = inner[(dotIndex + 1)..];
                propertyPath = path.Split('.');
            }
            else
            {
                paramName = inner;
                propertyPath = null;
            }

            isConstant = false;
        }
        else
        {
            throw new FormatException($"Unable to parse value expression: '{valueExpr}', at definition: {definition}");
        }

        if (string.IsNullOrEmpty(key)) throw new FormatException($"Tag key cannot be empty: {definition}");

        var paramIndex = -1;
        if (isConstant)
            return new TagSpec
            {
                Key = key,
                ParamIndex = paramIndex,
                PropertyPath = propertyPath,
                AsJson = asJson,
                IsOptional = isOptional,
                ConstantValue = constantValue,
                IsConstant = isConstant
            };
        for (var i = 0; i < parameters.Length; i++)
        {
            if (!string.Equals(parameters[i].Name, paramName, StringComparison.Ordinal)) continue;
            paramIndex = i;
            break;
        }

        if (paramIndex == -1)
            throw new FormatException($"Method parameter '{paramName}' not found, at definition: {definition}");

        return new TagSpec
        {
            Key = key,
            ParamIndex = paramIndex,
            PropertyPath = propertyPath,
            AsJson = asJson,
            IsOptional = isOptional,
            ConstantValue = constantValue,
            IsConstant = isConstant
        };
    }

    /// <inheritdoc />
    public Action<Activity, object?[]> CompileAllTags(MethodBase method, string[] tagDefs)
    {
        var parameters = method.GetParameters();
        var activityParam = Expression.Parameter(typeof(Activity), "activity");
        var argsParam = Expression.Parameter(typeof(object?[]), "args");
        var bodyExpressions = new List<Expression>();

        foreach (var t in tagDefs)
        {
            var spec = ParseTag(t, parameters);
            var valueExpr = BuildValueExpression(spec, parameters, argsParam);

            if (spec.IsOptional)
            {
                var condition = Expression.NotEqual(valueExpr, Expression.Constant(null, typeof(object)));
                var call = Expression.Call(activityParam,
                    nameof(Activity.SetTag), null,
                    Expression.Constant(spec.Key),
                    Expression.Convert(valueExpr, typeof(object)));
                bodyExpressions.Add(Expression.IfThen(condition, call));
            }
            else
            {
                bodyExpressions.Add(Expression.Call(activityParam,
                    nameof(Activity.SetTag), null,
                    Expression.Constant(spec.Key),
                    Expression.Convert(valueExpr, typeof(object))));
            }
        }

        Expression block = bodyExpressions.Count > 0
            ? Expression.Block(bodyExpressions)
            : Expression.Empty();

        var lambda = Expression.Lambda<Action<Activity, object?[]>>(block, activityParam, argsParam);
        return lambda.Compile();
    }

    private Expression BuildValueExpression(TagSpec spec, ParameterInfo[] parameters, Expression argsParam)
    {
        if (spec.IsConstant) return Expression.Constant(spec.ConstantValue, typeof(object));

        var paramType = parameters[spec.ParamIndex].ParameterType;
        var argAccess = Expression.ArrayIndex(argsParam, Expression.Constant(spec.ParamIndex));
        Expression current = Expression.Convert(argAccess, paramType);

        if (spec.PropertyPath != null)
        {
            current = spec.PropertyPath.Aggregate(current, Expression.PropertyOrField);
        }

        if (spec.AsJson)
        {
            var serializeMethod = typeof(JsonSerializer).GetMethod(
                nameof(JsonSerializer.Serialize),
                [typeof(object), typeof(Type), typeof(JsonSerializerOptions)]);

            if (serializeMethod == null)
                throw new InvalidOperationException(
                    "Unable to find the method `JsonSerializer.Serialize(object, Type, JsonSerializerOptions)`");

            var valueAsObject = Expression.Convert(current, typeof(object));
            var typeExpr = Expression.Constant(typeof(object), typeof(Type));
            var optionsExpr = Expression.Constant(_jsonSerializerOptions, typeof(JsonSerializerOptions));

            current = Expression.Call(
                serializeMethod,
                valueAsObject,
                typeExpr,
                optionsExpr);
        }

        if (current.Type.IsValueType) current = Expression.Convert(current, typeof(object));

        return current;
    }
}