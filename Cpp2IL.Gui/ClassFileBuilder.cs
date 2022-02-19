﻿using System.Linq;
using System.Reflection;
using System.Text;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Gui;

public static class ClassFileBuilder
{
    public static string BuildCsFileForType(TypeAnalysisContext type)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrEmpty(type.Definition.Namespace))
        {
            sb.Append("namespace ").Append(type.Definition.Namespace).AppendLine(";");
            sb.AppendLine();
        }

        sb.Append(GetCustomAttributeStrings(type, 0));
        sb.Append(GetKeyWordsForType(type)).Append(type.Definition.Name).AppendLine("\n{");

        if (IsEnum(type))
        {
            //enums - all fields that are constants are enum values
            //no methods, no props, no events
            foreach (var field in type.Fields)
            {
                if (field.BackingData.attributes.HasFlag(FieldAttributes.Literal))
                {
                    sb.Append('\t').Append(field.BackingData.field.Name).Append(" = ").Append(field.BackingData.defaultValue).AppendLine(",");
                }
            }

            return sb.ToString();
        }

        //Fields
        foreach (var field in type.Fields)
        {
            sb.Append(GetCustomAttributeStrings(field, 1));

            sb.Append('\t').Append(GetKeyWordsForField(field));
            sb.Append(GetTypeName(field.BackingData.field.FieldType!.ToString())).Append(' ');
            sb.Append(field.BackingData.field.Name!);

            var isConst = field.BackingData.attributes.HasFlag(FieldAttributes.Literal);
            if (isConst)
                sb.Append(" = ").Append(field.BackingData.defaultValue);

            sb.Append(';');

            if (!isConst)
            {
                var offset = type.AppContext.Binary.GetFieldOffsetFromIndex(type.Definition.TypeIndex, type.Fields.IndexOf(field), field.BackingData.field.FieldIndex, type.Definition.IsValueType, field.BackingData.attributes.HasFlag(FieldAttributes.Static));
                sb.Append(" //C++ Field Offset: ").Append(offset).Append(" (0x").Append(offset.ToString("X")).Append(')');
            }

            sb.AppendLine();
        }

        sb.AppendLine();

        //Constructors
        foreach (var method in type.Methods)
        {
            if (method.Definition!.Name is not ".ctor" and not ".cctor")
                continue;

            sb.Append(GetCustomAttributeStrings(method, 1));
            sb.Append('\t').Append(GetKeyWordsForMethod(method)).Append(type.Definition!.Name).Append('(');
            sb.Append(GetMethodParameterString(method));
            sb.AppendLine(")\n\t{");

            sb.AppendLine("\t\t//TODO: Method bodies");

            sb.AppendLine("\t}\n");
        }

        //Methods
        foreach (var method in type.Methods)
        {
            if (method.Definition!.Name is ".ctor" or ".cctor")
                continue;

            sb.Append(GetCustomAttributeStrings(method, 1));
            sb.Append('\t').Append(GetKeyWordsForMethod(method));
            sb.Append(GetTypeName(method.Definition.ReturnType!.ToString())).Append(' ');
            sb.Append(method.Definition!.Name).Append('(');
            sb.Append(GetMethodParameterString(method));
            sb.AppendLine(")\n\t{");

            sb.AppendLine("\t\t//TODO: Method bodies");

            sb.AppendLine("\t}\n");
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string GetMethodParameterString(MethodAnalysisContext method)
    {
        var sb = new StringBuilder();
        var first = true;
        foreach (var paramData in method.Definition!.Parameters!)
        {
            if (!first)
                sb.Append(", ");
            first = false;

            sb.Append(GetTypeName(paramData.Type.ToString())).Append(' ').Append(paramData.ParameterName);

            if (paramData.ParameterAttributes.HasFlag(ParameterAttributes.HasDefault))
                sb.Append(" = ").Append(paramData.DefaultValue);
        }

        return sb.ToString();
    }

    private static string GetKeyWordsForType(TypeAnalysisContext type)
    {
        var sb = new StringBuilder();
        var attributes = (TypeAttributes) type.Definition.flags;

        if (attributes.HasFlag(TypeAttributes.Public))
            sb.Append("public ");
        else
            sb.Append("internal "); //private classes don't exist, for obvious reasons

        if (IsEnum(type))
            sb.Append("enum ");
        else
        {
            if (attributes.HasFlag(TypeAttributes.Abstract))
                sb.Append("abstract ");
            if (attributes.HasFlag(TypeAttributes.Sealed))
                sb.Append("sealed ");

            if (attributes.HasFlag(TypeAttributes.Interface))
                sb.Append("interface ");
            else
                sb.Append("class ");
        }

        return sb.ToString();
    }

    private static string GetKeyWordsForField(FieldAnalysisContext field)
    {
        var sb = new StringBuilder();
        var attributes = field.BackingData.attributes;

        if (attributes.HasFlag(FieldAttributes.Public))
            sb.Append("public ");
        else if (attributes.HasFlag(FieldAttributes.Family))
            sb.Append("protected ");
        if (attributes.HasFlag(FieldAttributes.Assembly))
            sb.Append("internal ");
        else if (attributes.HasFlag(FieldAttributes.Private))
            sb.Append("private ");
        if (attributes.HasFlag(FieldAttributes.Static))
            sb.Append("static ");
        if (attributes.HasFlag(FieldAttributes.InitOnly))
            sb.Append("readonly ");
        if (attributes.HasFlag(FieldAttributes.Literal))
            sb.Append("const ");

        return sb.ToString();
    }

    private static string GetKeyWordsForMethod(MethodAnalysisContext method)
    {
        var sb = new StringBuilder();
        var attributes = method.Definition!.Attributes;

        if (attributes.HasFlag(MethodAttributes.Public))
            sb.Append("public ");
        else if (attributes.HasFlag(MethodAttributes.Family))
            sb.Append("protected ");
        if (attributes.HasFlag(MethodAttributes.Assembly))
            sb.Append("internal ");
        else if (attributes.HasFlag(MethodAttributes.Private))
            sb.Append("private ");
        if (attributes.HasFlag(MethodAttributes.Static))
            sb.Append("static ");

        if (attributes.HasFlag(MethodAttributes.NewSlot))
            sb.Append("override ");
        else if (attributes.HasFlag(MethodAttributes.Virtual))
            sb.Append("virtual ");

        if (attributes.HasFlag(MethodAttributes.Abstract))
            sb.Append("abstract ");

        return sb.ToString();
    }

    private static string GetCustomAttributeStrings(HasCustomAttributes context, int indentCount)
    {
        var sb = new StringBuilder();

        context.AnalyzeCustomAttributeData();

        foreach (var analyzedCustomAttribute in context.CustomAttributes!)
        {
            if (indentCount > 0)
                sb.Append('\t', indentCount);

            sb.AppendLine(analyzedCustomAttribute.ToString());
        }

        return sb.ToString();
    }

    private static string GetTypeName(string originalName)
    {
        return originalName switch
        {
            "System.Void" => "void",
            "System.Boolean" => "bool",
            "System.Byte" => "byte",
            "System.SByte" => "sbyte",
            "System.Char" => "char",
            "System.Decimal" => "decimal",
            "System.Single" => "float",
            "System.Double" => "double",
            "System.Int32" => "int",
            "System.UInt32" => "uint",
            "System.Int64" => "long",
            "System.UInt64" => "ulong",
            "System.Int16" => "short",
            "System.UInt16" => "ushort",
            "System.String" => "string",
            "System.Object" => "object",
            _ => originalName
        };
    }

    private static bool IsEnum(TypeAnalysisContext type)
        => ((TypeAttributes) type.Definition.flags).HasFlag(TypeAttributes.Sealed) && type.Fields.Any(f => f.BackingData.field.Name == "value__");
}