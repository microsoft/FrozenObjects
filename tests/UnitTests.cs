// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.FrozenObjects.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using Microsoft.Collections.Extensions;
    using Xunit;
    using static Deserializer;
    using static Serializer;

    public class UnitTests
    {
        [Fact]
        public void BasicTest()
        {
            var dict = new DictionarySlim();
            ref var value = ref dict.GetOrAddValueRef("test");
            value = "value";

            var tmpPath = Path.GetTempPath();
            var basicdll = Path.Combine(tmpPath, "basic.dll");
            var basicbin = Path.Combine(tmpPath, "basic.bin");

            SerializeObject(dict, basicbin, basicdll, "xyz", "abc", "m", new Version(1, 0, 0, 0), null);

            var asm = Assembly.Load(File.ReadAllBytes(basicdll));
            var types = asm.GetTypes();
            foreach (var type in types)
            {
                if (type.FullName == "xyz.abc")
                {
                    foreach (var method in type.GetMethods())
                    {
                        if (method.Name == "m")
                        {
                            var obj = method.Invoke(null, new object[] { basicbin } );
                            Assert.IsType(dict.GetType(), obj);
                            UnloadFrozenObject(obj);
                            break;
                        }
                    }
                }
            }

            File.Delete(basicdll);
            File.Delete(basicbin);
        }

        [Fact]
        public void MDArrayTest()
        {
            int[,] arr = new int[1,1];
            arr[0, 0] = 1;

            var tmpPath = Path.GetTempPath();
            var mdarraydll = Path.Combine(tmpPath, "mdarray.dll");
            var mdarraybin = Path.Combine(tmpPath, "mdarray.bin");

            SerializeObject(arr, mdarraybin, mdarraydll, "xyz", "abc", "m", new Version(1, 0, 0, 0));

            var asm = Assembly.Load(File.ReadAllBytes(mdarraydll));
            var types = asm.GetTypes();
            foreach (var type in types)
            {
                if (type.FullName == "xyz.abc")
                {
                    foreach (var method in type.GetMethods())
                    {
                        if (method.Name == "m")
                        {
                            var obj = method.Invoke(null, new object[] { mdarraybin });
                            Assert.IsType(arr.GetType(), obj);
                            UnloadFrozenObject(obj);
                            break;
                        }
                    }
                }
            }

            File.Delete(mdarraydll);
            File.Delete(mdarraybin);
        }

        [Fact]
        public void ObjectTest()
        {
            object o = new object();

            var tmpPath = Path.GetTempPath();
            var objectdll = Path.Combine(tmpPath, "object.dll");
            var objectbin = Path.Combine(tmpPath, "object.bin");

            SerializeObject(o, objectbin, objectdll, "xyz", "abc", "m", new Version(1, 0, 0, 0));

            var asm = Assembly.Load(File.ReadAllBytes(objectdll));
            var types = asm.GetTypes();
            foreach (var type in types)
            {
                if (type.FullName == "xyz.abc")
                {
                    foreach (var method in type.GetMethods())
                    {
                        if (method.Name == "m")
                        {
                            var obj = method.Invoke(null, new object[] { objectbin });
                            Assert.IsType(o.GetType(), obj);
                            UnloadFrozenObject(obj);
                            break;
                        }
                    }
                }
            }

            File.Delete(objectdll);
            File.Delete(objectbin);
        }

        [Fact]
        public void SameArrayObjectTest()
        {
            int[][] arrarr = new int[9][];
            int[] arr = { 1, 2, 3, 4, 5};
            int[] arr2 = { 1, 2, 3, 4, 5 };
            arrarr[0] = arr;
            arrarr[1] = arr;
            arrarr[2] = arr;
            arrarr[3] = arr;
            arrarr[4] = arr2;
            arrarr[5] = arr;
            arrarr[6] = arr;
            arrarr[7] = arr2;
            arrarr[8] = arr;

            var tmpPath = Path.GetTempPath();
            var objectdll = Path.Combine(tmpPath, "samearrayobject.dll");
            var objectbin = Path.Combine(tmpPath, "samearrayobject.bin");

            SerializeObject(arrarr, objectbin, objectdll, "xyz", "abc", "m", new Version(1, 0, 0, 0));

            var asm = Assembly.Load(File.ReadAllBytes(objectdll));
            var types = asm.GetTypes();
            foreach (var type in types)
            {
                if (type.FullName == "xyz.abc")
                {
                    foreach (var method in type.GetMethods())
                    {
                        if (method.Name == "m")
                        {
                            var obj = method.Invoke(null, new object[] { objectbin });
                            UnloadFrozenObject(obj);
                            break;
                        }
                    }
                }
            }

            File.Delete(objectdll);
            File.Delete(objectbin);
        }

        [Fact]
        public void ComplicatedObjectTest1()
        {
            var complicatedObject = new OuterClass.FooStruct.GenericReferenceTypeWithInheritance<List<string>, string, int[], OuterStruct.GenericValueTypeWithReferences<long[,]>>();
            complicatedObject.R = new Recursive();
            complicatedObject.R.Data = "Food";
            complicatedObject.R.Field = complicatedObject.R;

            complicatedObject.Y = new List<string> { "foo", "bar", "baz" };

            complicatedObject.A = "A";
            complicatedObject.B = new int[2][];
            complicatedObject.B[0] = new int[5];
            complicatedObject.B[0][0] = 1;
            complicatedObject.B[0][1] = 2;
            complicatedObject.B[0][2] = 3;
            complicatedObject.B[0][3] = 4;
            complicatedObject.B[0][4] = -1;

            complicatedObject.B[1] = new int[4];

            complicatedObject.B[1][0] = int.MaxValue;
            complicatedObject.B[1][1] = 1;
            complicatedObject.B[1][2] = int.MinValue;
            complicatedObject.B[1][3] = 3;

            complicatedObject.C = new OuterStruct.GenericValueTypeWithReferences<long[,]>[2];
            complicatedObject.C[0].A = "A";
            complicatedObject.C[0].B = byte.MaxValue;
            complicatedObject.C[0].C = new long[2,3];
            complicatedObject.C[0].C[0, 0] = long.MinValue;
            complicatedObject.C[0].C[0, 1] = long.MaxValue;
            complicatedObject.C[0].C[0, 2] = long.MinValue;
            complicatedObject.C[0].C[1, 0] = long.MaxValue;
            complicatedObject.C[0].C[1, 1] = long.MinValue;
            complicatedObject.C[0].C[1, 2] = long.MaxValue;

            complicatedObject.C[0].D = new long[1][,];
            complicatedObject.C[0].D[0] = new long[1,2];
            complicatedObject.C[0].D[0][0, 0] = 25;
            complicatedObject.C[0].D[0][0, 1] = 30;
            complicatedObject.C[0].E = "E";

            complicatedObject.S = new Circular();
            complicatedObject.S.Foo = new Bar();
            complicatedObject.S.Foo.Foo = complicatedObject.S;

            complicatedObject.BaseA = new List<DictionarySlim>();

            var sameDict = new DictionarySlim();
            {
                ref string value = ref sameDict.GetOrAddValueRef("key");
                value = "value";

                ref string value2 = ref sameDict.GetOrAddValueRef("key2");
                value2 = "value2";

                ref string value3 = ref sameDict.GetOrAddValueRef("key3");
                value3 = "value3";
            }

            complicatedObject.BaseA.Add(sameDict);
            complicatedObject.BaseA.Add(sameDict);

            var otherDict = new DictionarySlim();
            {
                ref string value = ref otherDict.GetOrAddValueRef("key");
                value = "value";

                ref string value2 = ref otherDict.GetOrAddValueRef("key2");
                value2 = "value2";

                ref string value3 = ref otherDict.GetOrAddValueRef("key3");
                value3 = "value3";
            }

            complicatedObject.BaseA.Add(otherDict);
            complicatedObject.BaseB = 3000;
            complicatedObject.BaseC = LongEnum.Max - 1;
            complicatedObject.BaseD = "BaseD";

            var tmpPath = Path.GetTempPath();
            var dll = Path.Combine(tmpPath, "complicatedObject.dll");
            var bin = Path.Combine(tmpPath, "complicatedObject.bin");

            SerializeObject(complicatedObject, bin, dll, "xyz", "abc", "m", new Version(1, 0, 0, 0));

            var asm = Assembly.Load(File.ReadAllBytes(dll));
            var types = asm.GetTypes();
            foreach (var type in types)
            {
                if (type.FullName == "xyz.abc")
                {
                    foreach (var method in type.GetMethods())
                    {
                        if (method.Name == "m")
                        {
                            var obj = (OuterClass.FooStruct.GenericReferenceTypeWithInheritance<List<string>, string, int[], OuterStruct.GenericValueTypeWithReferences<long[,]>>)method.Invoke(null, new object[] { bin });
                            UnloadFrozenObject(obj);
                            break;
                        }
                    }
                }
            }

            File.Delete(dll);
            File.Delete(bin);
        }
    }
}