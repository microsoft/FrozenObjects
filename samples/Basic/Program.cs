namespace FrozenObjectTest
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using static Microsoft.FrozenObjects.Serializer;

    class Program
    {
        static void Main(string[] args)
        {
            const string dll = "foo.dll";
            const string bin = "foo.bin";
            const string @namespace = "MyNamespace";
            const string type = "MyType";
            const string method = "DeserializeMethodName";

            const string obj1 = "Hello World";
            SerializeObject(obj1, bin, dll, @namespace, type, method, new Version(1, 0, 0, 0));

            var obj2 = Assembly.Load(File.ReadAllBytes(dll)).GetTypes().Single(t => t.FullName == $"{@namespace}.{type}").GetMethod(method).Invoke(null, new object[] { bin });

            if (Equals(obj1, obj2))
            {
                Console.WriteLine("Hooray");
            }
        }
    }
}