using System.Reflection;
using System.Text;

namespace AutoFlow.App.Services;

internal static class ExceptionLogFormatter
{
    public static string Format(string summary, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var root = Unwrap(exception);
        var builder = new StringBuilder();
        builder.Append(summary);
        builder.AppendLine();
        builder.Append("异常类型: ");
        builder.AppendLine(root.GetType().FullName ?? root.GetType().Name);
        builder.Append("错误信息: ");
        builder.AppendLine(root.Message);

        if (!ReferenceEquals(root, exception))
        {
            builder.AppendLine("异常链:");
            AppendExceptionChain(builder, exception);
        }

        builder.AppendLine("堆栈:");
        builder.AppendLine(root.StackTrace ?? "<无堆栈信息>");
        return builder.ToString().TrimEnd();
    }

    private static Exception Unwrap(Exception exception)
    {
        var current = exception;
        while (true)
        {
            if (current is TargetInvocationException targetInvocationException &&
                targetInvocationException.InnerException is not null)
            {
                current = targetInvocationException.InnerException;
                continue;
            }

            if (current is AggregateException aggregateException)
            {
                var flattened = aggregateException.Flatten();
                if (flattened.InnerExceptions.Count == 1)
                {
                    current = flattened.InnerExceptions[0];
                    continue;
                }
            }

            return current;
        }
    }

    private static void AppendExceptionChain(StringBuilder builder, Exception exception)
    {
        var current = exception;
        var depth = 0;
        while (current is not null)
        {
            builder.Append("  [");
            builder.Append(depth);
            builder.Append("] ");
            builder.Append(current.GetType().FullName ?? current.GetType().Name);
            builder.Append(": ");
            builder.AppendLine(current.Message);
            current = current.InnerException;
            depth++;
        }
    }
}
