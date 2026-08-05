using System.Diagnostics;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Harness.Shared.Observability;

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

    /// <inheritdoc />
    public Action<Activity, object?, object?[]> CompileAllTags(MethodBase method, string[] tagDefs)
    {
        var parameters = method.GetParameters();
        var activityParam = Expression.Parameter(typeof(Activity), "activity");
        var targetParam = Expression.Parameter(typeof(object), "target");
        var argsParam = Expression.Parameter(typeof(object?[]), "args");
        var bodyExpressions = new List<Expression>();

        foreach (var def in tagDefs)
        {
            var spec = ParseTag(def, parameters);

            if (spec.IsThisReference && method.IsStatic)
                throw new FormatException($"Cannot use 'this' in a static method: {def}");

            // ReSharper disable once NullableWarningSuppressionIsUsed
            var valueExpr = BuildValueExpression(spec, parameters, argsParam, targetParam, method.DeclaringType!);

            Expression setTagCall = Expression.Call(
                activityParam,
                nameof(Activity.SetTag),
                typeArguments: null,
                Expression.Constant(spec.Key),
                Expression.Convert(valueExpr, typeof(object)));

            if (spec.IsOptional)
            {
                var condition = Expression.NotEqual(valueExpr, Expression.Constant(null, typeof(object)));
                bodyExpressions.Add(Expression.IfThen(condition, setTagCall));
            }
            else
            {
                bodyExpressions.Add(setTagCall);
            }
        }

        Expression block = bodyExpressions.Count > 0
            ? Expression.Block(bodyExpressions)
            : Expression.Empty();

        return Expression.Lambda<Action<Activity, object?, object?[]>>(
            block, activityParam, targetParam, argsParam).Compile();
    }

    /// <summary>
    /// Parses a single tag definition and resolves parameter names against the given parameter list.
    /// </summary>
    private static TagSpec ParseTag(string definition, ParameterInfo[] parameters)
    {
        var (key, valueExpr, isOptional) = ParseKeyAndOptionalFlag(definition);
        var (isConstant, constantValue, paramName, propertyPath, asJson, isThisReference) =
            ParseValueExpression(valueExpr, definition);

        if (string.IsNullOrWhiteSpace(key))
            throw new FormatException($"Tag key cannot be empty: {definition}");

        if (isConstant) return TagSpec.Constant(key, constantValue, asJson, isOptional);
        if (isThisReference) return TagSpec.ThisReference(key, propertyPath, asJson, isOptional);

        var paramIndex = ResolveParamIndex(paramName, parameters, definition);
        return TagSpec.Parameter(key, paramIndex, propertyPath, asJson, isOptional);
    }

    private static (string key, string valueExpr, bool isOptional) ParseKeyAndOptionalFlag(string definition)
    {
        var optionalEqIndex = definition.IndexOf("?=", StringComparison.Ordinal);
        var normalEqIndex = definition.IndexOf('=');

        if (optionalEqIndex >= 0)
        {
            var key = definition[..optionalEqIndex].Trim();
            var valueExpr = definition[(optionalEqIndex + 2)..].Trim();
            return (key, valueExpr, true);
        }

        if (normalEqIndex < 0)
            throw new FormatException(
                $"Label definition must be of `<key> (?)= <value> <options>` format: {definition}");
        {
            var key = definition[..normalEqIndex].Trim();
            var valueExpr = definition[(normalEqIndex + 1)..].Trim();
            return (key, valueExpr, false);
        }
    }

    private static (bool isConstant, object? constantValue, string? paramName, string[]? propertyPath, bool asJson, bool
        isThisReference)
        ParseValueExpression(string valueExpr, string fullDefinition)
    {
        const string asJsonSuffix = " as json";
        var asJson = valueExpr.EndsWith(asJsonSuffix, StringComparison.OrdinalIgnoreCase);
        if (asJson)
            valueExpr = valueExpr[..^asJsonSuffix.Length].TrimEnd();

        if (TryParseConstant(valueExpr, out var constantValue))
            return (true, constantValue, null, null, asJson, false);

        if (!valueExpr.StartsWith('{') || !valueExpr.EndsWith('}') || valueExpr.Length < 3)
            throw new FormatException(
                $"Unable to parse value expression: '{valueExpr}', at definition: {fullDefinition}");

        var inner = valueExpr[1..^1].Trim();
        if (inner.Length == 0)
            throw new FormatException($"Interpolation tag cannot be empty: {fullDefinition}");

        if (inner == "this")
        {
            return (false, null, null, Array.Empty<string>(), asJson, true);
        }

        if (inner.StartsWith("this.", StringComparison.Ordinal))
        {
            var propertyPath = inner[5..].Split('.');
            return (false, null, null, propertyPath, asJson, true);
        }

        var dotIndex = inner.IndexOf('.');
        string paramName;
        string[]? propertyPathParam = null;

        if (dotIndex > 0)
        {
            paramName = inner[..dotIndex];
            propertyPathParam = inner[(dotIndex + 1)..].Split('.');
        }
        else
        {
            paramName = inner;
        }

        return (false, null, paramName, propertyPathParam, asJson, false);
    }

    private static bool TryParseConstant(string expr, out object? value)
    {
        // string literal
        if (expr.StartsWith('\'') && expr.EndsWith('\'') && expr.Length >= 2)
        {
            value = expr[1..^1].Replace("\\'", "'");
            return true;
        }

        // null, true, false
        if (expr.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            value = null;
            return true;
        }

        if (expr.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (expr.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        // integer
        if (long.TryParse(expr, out var longVal) && expr == longVal.ToString())
        {
            value = longVal;
            return true;
        }

        // floating point
        if (double.TryParse(expr, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleVal))
        {
            value = doubleVal;
            return true;
        }

        value = null;
        return false;
    }

    private static int ResolveParamIndex(string? paramName, ParameterInfo[] parameters, string fullDefinition)
    {
        for (var i = 0; i < parameters.Length; i++)
        {
            if (string.Equals(parameters[i].Name, paramName, StringComparison.Ordinal)) return i;
        }

        throw new FormatException($"Method parameter '{paramName}' not found, at definition: {fullDefinition}");
    }

    private Expression BuildValueExpression(
        TagSpec spec, ParameterInfo[] parameters, Expression argsParam,
        Expression targetParam, Type declaringType)
    {
        if (spec.IsConstant)
            return Expression.Constant(spec.ConstantValue, typeof(object));

        Expression current;

        if (spec.IsThisReference)
        {
            current = Expression.Convert(targetParam, declaringType);
        }
        else
        {
            var paramType = parameters[spec.ParamIndex].ParameterType;
            var argAccess = Expression.ArrayIndex(argsParam, Expression.Constant(spec.ParamIndex));
            current = Expression.Convert(argAccess, paramType);
        }

        if (spec.PropertyPath is { Length: > 0 })
        {
            foreach (var memberName in spec.PropertyPath)
            {
                if (spec.IsThisReference)
                {
                    var member = FindInstanceMember(current.Type, memberName);
                    current = member switch
                    {
                        PropertyInfo p => Expression.Property(current, p),
                        FieldInfo f => Expression.Field(current, f),
                        _ => throw new InvalidOperationException($"Unsupported member type: {member.MemberType}")
                    };
                }
                else
                {
                    current = Expression.PropertyOrField(current, memberName);
                }
            }
        }

        if (spec.AsJson)
            current = SerializeToJson(current);

        if (current.Type.IsValueType)
            current = Expression.Convert(current, typeof(object));

        return current;
    }

    private static MemberInfo FindInstanceMember(Type type, string memberName)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var current = type;
        while (current != null)
        {
            var property = current.GetProperty(memberName, flags);
            if (property != null) return property;

            var field = current.GetField(memberName, flags);
            if (field != null) return field;

            current = current.BaseType;
        }

        throw new MissingMemberException($"Member '{memberName}' not found on type {type} or its base types.");
    }

    private MethodCallExpression SerializeToJson(Expression value)
    {
        var serializeMethod = typeof(JsonSerializer).GetMethod(
            nameof(JsonSerializer.Serialize),
            [typeof(object), typeof(Type), typeof(JsonSerializerOptions)]);

        if (serializeMethod == null)
        {
            throw new InvalidOperationException(
                "Unable to find the method `JsonSerializer.Serialize(object, Type, JsonSerializerOptions)`");
        }

        var valueAsObject = Expression.Convert(value, typeof(object));
        var typeExpr = Expression.Constant(typeof(object), typeof(Type));
        var optionsExpr = Expression.Constant(_jsonSerializerOptions, typeof(JsonSerializerOptions));

        return Expression.Call(serializeMethod, valueAsObject, typeExpr, optionsExpr);
    }
}