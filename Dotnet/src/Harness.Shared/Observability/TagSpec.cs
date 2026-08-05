namespace Harness.Shared.Observability;

internal struct TagSpec
{
    public string Key;
    public int ParamIndex;
    public string[]? PropertyPath;
    public bool AsJson;
    public bool IsOptional;
    public object? ConstantValue;
    public bool IsConstant;
    public bool IsThisReference;

    public static TagSpec Constant(string key, object? constantValue, bool asJson, bool isOptional)
    {
        return new TagSpec
        {
            Key = key,
            ConstantValue = constantValue,
            IsConstant = true,
            AsJson = asJson,
            IsOptional = isOptional,
            ParamIndex = -1,
            PropertyPath = null
        };
    }

    public static TagSpec Parameter(string key, int paramIndex, string[]? propertyPath, bool asJson,
        bool isOptional)
    {
        return new TagSpec
        {
            Key = key,
            ParamIndex = paramIndex,
            PropertyPath = propertyPath,
            AsJson = asJson,
            IsOptional = isOptional,
            IsConstant = false,
            ConstantValue = null
        };
    }

    public static TagSpec ThisReference(string key, string[]? propertyPath, bool asJson, bool isOptional)
    {
        return new TagSpec
        {
            Key = key,
            IsConstant = false,
            IsThisReference = true,
            PropertyPath = propertyPath,
            AsJson = asJson,
            IsOptional = isOptional,
            ParamIndex = -1,
            ConstantValue = null
        };
    }
}