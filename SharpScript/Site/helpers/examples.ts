export const csharpExample = `using System;
using System.Reflection;
[assembly: AssemblyVersion("0.0.0.0")]
Console.WriteLine("Hello, World!");`;

export const vbExample = `Imports System
Imports System.Reflection
<Assembly: AssemblyVersion("0.0.0.0")>
Namespace SharpScript
    Public Module Program
        Public Sub Main()
            Console.WriteLine("Hello, World!")
        End Sub
    End Module
End Namespace`;

export const ilExample = `.assembly ' ' {
}

.assembly extern System.Console {
}

.method static void Main() {
    .entrypoint
    ldstr "Hello, World!"
    call void [System.Console]System.Console::WriteLine(string)
    ret
}`;