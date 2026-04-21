using System;
using System.Linq;
using System.Reflection;

var dll = Assembly.LoadFrom("/Users/luizhcz/Desktop/repositorio/src/EfsAiHub.Host.Api/bin/Release/net8.0/Microsoft.Agents.AI.Workflows.dll");
foreach (var t in dll.GetExportedTypes())
{
    if (t.Name.Contains("InProcessExecution"))
    {
        Console.WriteLine($"=== {t.FullName} ===");
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            var pars = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType} {p.Name}"));
            Console.WriteLine($"  {m.ReturnType} {m.Name}({pars})");
        }
    }
}
