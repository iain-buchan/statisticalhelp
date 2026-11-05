using System.Reflection;
using System.Runtime.ExceptionServices;

/// <summary>Invoke native test seams while honoring omitted optional parameters.</summary>
internal static class NativeReflection
{
    internal static object? Invoke(object target, string name, params object?[] arguments)
    {
        MethodInfo method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new MissingMethodException(target.GetType().FullName, name);
        ParameterInfo[] parameters = method.GetParameters();
        if (arguments.Length < parameters.Length && parameters.Skip(arguments.Length).All(parameter => parameter.IsOptional))
            arguments = arguments.Concat(Enumerable.Repeat<object?>(Type.Missing, parameters.Length - arguments.Length)).ToArray();
        try { return method.Invoke(target, arguments); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }
}
