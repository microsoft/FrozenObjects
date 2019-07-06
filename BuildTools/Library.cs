namespace Microsoft.FrozenObjects.BuildTools
{
    using Microsoft.Build.Framework;
    using System;
    using System.IO;
    using System.Reflection;
    using System.Reflection.Metadata;
    using System.Reflection.Metadata.Ecma335;
    using System.Reflection.PortableExecutable;
    using static Library;

    public sealed class CreateInternalCallsAssembly : Microsoft.Build.Utilities.Task
    {
        [Required]
        public string OutputAssemblyPath { get; set; }

        public override bool Execute()
        {
            CreateInternalCallsAssembly(Path.GetFileName(this.OutputAssemblyPath), Path.GetFileNameWithoutExtension(this.OutputAssemblyPath), new Version(1, 0, 0, 0), this.OutputAssemblyPath);
            return true;
        }
    }

    public sealed class CreateCalliHelperAssembly : Microsoft.Build.Utilities.Task
    {
        [Required]
        public string OutputAssemblyPath { get; set; }

        public override bool Execute()
        {
            CreateCallIHelperAssembly(Path.GetFileName(this.OutputAssemblyPath), Path.GetFileNameWithoutExtension(this.OutputAssemblyPath), new Version(1, 0, 0, 0), this.OutputAssemblyPath);
            return true;
        }
    }

    internal static class Library
    {
        public static void CreateCallIHelperAssembly(string outputModuleName, string outputAssemblyName, Version version, string outputAssemblyFilePath)
        {
            var metadataBuilder = new MetadataBuilder();
            metadataBuilder.AddModule(0, metadataBuilder.GetOrAddString(outputModuleName), metadataBuilder.GetOrAddGuid(Guid.NewGuid()), default, default);
            metadataBuilder.AddAssembly(metadataBuilder.GetOrAddString(outputAssemblyName), version, default, default, default, AssemblyHashAlgorithm.Sha1);
            metadataBuilder.AddTypeDefinition(default, default, metadataBuilder.GetOrAddString("<Module>"), default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));

            var netstandardAssemblyRef = metadataBuilder.AddAssemblyReference(metadataBuilder.GetOrAddString("netstandard"), new Version(2, 0, 0, 0), default, metadataBuilder.GetOrAddBlob(new byte[] { 0xCC, 0x7B, 0x13, 0xFF, 0xCD, 0x2D, 0xDD, 0x51 }), default, default);
            var systemObjectTypeRef = metadataBuilder.AddTypeReference(netstandardAssemblyRef, metadataBuilder.GetOrAddString("System"), metadataBuilder.GetOrAddString("Object"));

            var codeBuilder = new BlobBuilder();

            metadataBuilder.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.AutoLayout | TypeAttributes.AnsiClass | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit, metadataBuilder.GetOrAddString("Microsoft.FrozenObjects"), metadataBuilder.GetOrAddString("CallIndirectHelpers"), systemObjectTypeRef, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));

            CreateCleanupMethod(metadataBuilder, codeBuilder);

            var srcuAssemblyRef = metadataBuilder.AddAssemblyReference(metadataBuilder.GetOrAddString("System.Runtime.CompilerServices.Unsafe"), new Version(4, 0, 0, 0), default, metadataBuilder.GetOrAddBlob(new byte[] { 0xB0, 0x3F, 0x5F, 0x7F, 0x11, 0xD5, 0x0A, 0x3A }), default, default);
            var unsafeTypeRef = metadataBuilder.AddTypeReference(srcuAssemblyRef, metadataBuilder.GetOrAddString("System.Runtime.CompilerServices"), metadataBuilder.GetOrAddString("Unsafe"));

            CreateSerializeObjectMethod(metadataBuilder, codeBuilder, "ManagedCallISerializeObject", SignatureCallingConvention.Default, unsafeTypeRef);

            using (var fs = new FileStream(outputAssemblyFilePath, FileMode.Create, FileAccess.Write))
            {
                WritePEImage(fs, metadataBuilder, codeBuilder);
            }
        }

        public static void CreateInternalCallsAssembly(string outputModuleName, string outputAssemblyName, Version version, string outputAssemblyFilePath)
        {
            var metadataBuilder = new MetadataBuilder();
            metadataBuilder.AddModule(0, metadataBuilder.GetOrAddString(outputModuleName), metadataBuilder.GetOrAddGuid(Guid.NewGuid()), default, default);
            var assemblyHandle = metadataBuilder.AddAssembly(metadataBuilder.GetOrAddString(outputAssemblyName), version, default, default, default, AssemblyHashAlgorithm.Sha1);
            metadataBuilder.AddTypeDefinition(default, default, metadataBuilder.GetOrAddString("<Module>"), default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));

            var netstandardAssemblyRef = metadataBuilder.AddAssemblyReference(metadataBuilder.GetOrAddString("netstandard"), new Version(2, 0, 0, 0), default, metadataBuilder.GetOrAddBlob(new byte[] { 0xCC, 0x7B, 0x13, 0xFF, 0xCD, 0x2D, 0xDD, 0x51 }), default, default);
            var attributeTypeRef = metadataBuilder.AddTypeReference(netstandardAssemblyRef, metadataBuilder.GetOrAddString("System"), metadataBuilder.GetOrAddString("Attribute"));
            var assemblyNameField = CreateAssemblyNameField(metadataBuilder);
            var systemObjectTypeRef = metadataBuilder.AddTypeReference(netstandardAssemblyRef, metadataBuilder.GetOrAddString("System"), metadataBuilder.GetOrAddString("Object"));

            var codeBuilder = new BlobBuilder();

            var spcAssemblyRef = metadataBuilder.AddAssemblyReference(metadataBuilder.GetOrAddString("System.Private.CoreLib"), new Version(4, 0, 0, 0), default, metadataBuilder.GetOrAddBlob(new byte[] { 0x7C, 0xEC, 0x85, 0xD7, 0xBE, 0xA7, 0x79, 0x8E }), default, default);
            var gcTypeRef = metadataBuilder.AddTypeReference(spcAssemblyRef, metadataBuilder.GetOrAddString("System"), metadataBuilder.GetOrAddString("GC"));
            var createRegisterFrozenSegmentMethodDefinitionHandle = CreateRegisterFrozenSegmentMethod(metadataBuilder, codeBuilder, CreateRegisterFrozenSegmentMemberReferenceHandle(metadataBuilder, gcTypeRef));
            CreateUnregisterFrozenSegmentMethod(metadataBuilder, codeBuilder, CreateUnregisterFrozenSegmentMemberReferenceHandle(metadataBuilder, gcTypeRef));
            metadataBuilder.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.AutoLayout | TypeAttributes.AnsiClass | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit, metadataBuilder.GetOrAddString("Microsoft.FrozenObjects"), metadataBuilder.GetOrAddString("GC"), systemObjectTypeRef, assemblyNameField, createRegisterFrozenSegmentMethodDefinitionHandle);

            var ignoresAccessChecksToAttributeConstructor = CreateIgnoresAccessChecksToAttributeConstructorMethod(metadataBuilder, codeBuilder, CreateAttributeConstructorMemberRef(metadataBuilder, attributeTypeRef), assemblyNameField);
            metadataBuilder.AddCustomAttribute(assemblyHandle, ignoresAccessChecksToAttributeConstructor, metadataBuilder.GetOrAddBlob(new byte[] { 0x01, 0x00, 0x16, 0x53, 0x79, 0x73, 0x74, 0x65, 0x6D, 0x2E, 0x50, 0x72, 0x69, 0x76, 0x61, 0x74, 0x65, 0x2E, 0x43, 0x6F, 0x72, 0x65, 0x4C, 0x69, 0x62, 0x00, 0x00 }));

            var ignoresAccessChecksToAttributeTypeDefinitionHandle = metadataBuilder.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.AutoLayout | TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit, metadataBuilder.GetOrAddString("System.Runtime.CompilerServices"), metadataBuilder.GetOrAddString("IgnoresAccessChecksToAttribute"), attributeTypeRef, assemblyNameField, ignoresAccessChecksToAttributeConstructor);
            metadataBuilder.AddCustomAttribute(ignoresAccessChecksToAttributeTypeDefinitionHandle, CreateAttributeUsageAttributeMemberRef(metadataBuilder, netstandardAssemblyRef), metadataBuilder.GetOrAddBlob(new byte[] { 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x54, 0x02, 0x0D, 0x41, 0x6C, 0x6C, 0x6F, 0x77, 0x4D, 0x75, 0x6C, 0x74, 0x69, 0x70, 0x6C, 0x65, 0x01 }));
            metadataBuilder.AddMethodSemantics(CreateIgnoresAccessChecksToAttributeAssemblyNameProperty(metadataBuilder, ignoresAccessChecksToAttributeTypeDefinitionHandle), MethodSemanticsAttributes.Getter, CreateIgnoresAccessChecksToAttributeGetAssemblyNameMethod(metadataBuilder, codeBuilder, assemblyNameField));

            using (var fs = new FileStream(outputAssemblyFilePath, FileMode.Create, FileAccess.Write))
            {
                WritePEImage(fs, metadataBuilder, codeBuilder);
            }
        }

        private static MemberReferenceHandle CreateRegisterFrozenSegmentMemberReferenceHandle(MetadataBuilder metadataBuilder, TypeReferenceHandle gcTypeRef)
        {
            var signatureBuilder = new BlobBuilder();

            new BlobEncoder(signatureBuilder).
                MethodSignature().
                Parameters(2,
                returnType => returnType.Type().IntPtr(),
                parameters =>
                {
                    parameters.AddParameter().Type().IntPtr();
                    parameters.AddParameter().Type().IntPtr();
                });

            return metadataBuilder.AddMemberReference(gcTypeRef, metadataBuilder.GetOrAddString("_RegisterFrozenSegment"), metadataBuilder.GetOrAddBlob(signatureBuilder));
        }

        private static MemberReferenceHandle CreateUnregisterFrozenSegmentMemberReferenceHandle(MetadataBuilder metadataBuilder, TypeReferenceHandle gcTypeRef)
        {
            var signatureBuilder = new BlobBuilder();

            new BlobEncoder(signatureBuilder).
                MethodSignature().
                Parameters(1,
                returnType => returnType.Void(),
                parameters =>
                {
                    parameters.AddParameter().Type().IntPtr();
                });

            return metadataBuilder.AddMemberReference(gcTypeRef, metadataBuilder.GetOrAddString("_UnregisterFrozenSegment"), metadataBuilder.GetOrAddBlob(signatureBuilder));
        }

        private static MethodDefinitionHandle CreateRegisterFrozenSegmentMethod(MetadataBuilder metadataBuilder, BlobBuilder codeBuilder, MemberReferenceHandle registerFrozenSegmentMemberReferenceHandle)
        {
            codeBuilder.Align(4);

            var ilBuilder = new BlobBuilder();
            var il = new InstructionEncoder(ilBuilder);

            il.LoadArgument(0);
            il.LoadArgument(1);
            il.OpCode(ILOpCode.Call);
            il.Token(registerFrozenSegmentMemberReferenceHandle);
            il.OpCode(ILOpCode.Ret);

            var methodBodyStream = new MethodBodyStreamEncoder(codeBuilder);
            int bodyOffset = methodBodyStream.AddMethodBody(il);
            ilBuilder.Clear();

            var signatureBuilder = new BlobBuilder();

            new BlobEncoder(signatureBuilder).
                MethodSignature().
                Parameters(2,
                returnType => returnType.Type().IntPtr(),
                parameters =>
                {
                    parameters.AddParameter().Type().IntPtr();
                    parameters.AddParameter().Type().IntPtr();
                });

            return metadataBuilder.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Static, MethodImplAttributes.IL | MethodImplAttributes.Managed, metadataBuilder.GetOrAddString("RegisterFrozenSegment"), metadataBuilder.GetOrAddBlob(signatureBuilder), bodyOffset, parameterList: default);
        }

        private static void CreateUnregisterFrozenSegmentMethod(MetadataBuilder metadataBuilder, BlobBuilder codeBuilder, MemberReferenceHandle unregisterFrozenSegmentMemberReferenceHandle)
        {
            codeBuilder.Align(4);

            var ilBuilder = new BlobBuilder();
            var il = new InstructionEncoder(ilBuilder);

            il.LoadArgument(0);
            il.OpCode(ILOpCode.Call);
            il.Token(unregisterFrozenSegmentMemberReferenceHandle);
            il.OpCode(ILOpCode.Ret);

            var methodBodyStream = new MethodBodyStreamEncoder(codeBuilder);
            int bodyOffset = methodBodyStream.AddMethodBody(il);
            ilBuilder.Clear();

            var signatureBuilder = new BlobBuilder();

            new BlobEncoder(signatureBuilder).
                MethodSignature().
                Parameters(1,
                returnType => returnType.Void(),
                parameters =>
                {
                    parameters.AddParameter().Type().IntPtr();
                });

            metadataBuilder.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Static, MethodImplAttributes.IL | MethodImplAttributes.Managed, metadataBuilder.GetOrAddString("UnregisterFrozenSegment"), metadataBuilder.GetOrAddBlob(signatureBuilder), bodyOffset, parameterList: default);
        }

        private static MemberReferenceHandle CreateAttributeConstructorMemberRef(MetadataBuilder metadataBuilder, TypeReferenceHandle attributeTypeRef)
        {
            var signatureBuilder = new BlobBuilder();

            new BlobEncoder(signatureBuilder).
                MethodSignature(SignatureCallingConvention.Default, 0, true).
                Parameters(0,
                returnType => returnType.Void(),
                parameters =>
                {
                });

            return metadataBuilder.AddMemberReference(attributeTypeRef, metadataBuilder.GetOrAddString(".ctor"), metadataBuilder.GetOrAddBlob(signatureBuilder));
        }

        private static MemberReferenceHandle CreateAttributeUsageAttributeMemberRef(MetadataBuilder metadataBuilder, AssemblyReferenceHandle netstandardAssemblyRef)
        {
            var attributeTargetsTypeRef = metadataBuilder.AddTypeReference(netstandardAssemblyRef, metadataBuilder.GetOrAddString("System"), metadataBuilder.GetOrAddString("AttributeTargets"));
            var attributeUsageAttributeTypeRef = metadataBuilder.AddTypeReference(netstandardAssemblyRef, metadataBuilder.GetOrAddString("System"), metadataBuilder.GetOrAddString("AttributeUsageAttribute"));

            var signatureBuilder = new BlobBuilder();

            new BlobEncoder(signatureBuilder).
                MethodSignature(SignatureCallingConvention.Default, 0, true).
                Parameters(1,
                returnType => returnType.Void(),
                parameters =>
                {
                    parameters.AddParameter().Type().Type(attributeTargetsTypeRef, true);
                });

            return metadataBuilder.AddMemberReference(attributeUsageAttributeTypeRef, metadataBuilder.GetOrAddString(".ctor"), metadataBuilder.GetOrAddBlob(signatureBuilder));
        }

        private static FieldDefinitionHandle CreateAssemblyNameField(MetadataBuilder metadataBuilder)
        {
            var signatureBuilder = new BlobBuilder();

            new BlobEncoder(signatureBuilder).FieldSignature().String();

            return metadataBuilder.AddFieldDefinition(FieldAttributes.Private | FieldAttributes.InitOnly, metadataBuilder.GetOrAddString("assemblyName"), metadataBuilder.GetOrAddBlob(signatureBuilder));
        }

        private static PropertyDefinitionHandle CreateIgnoresAccessChecksToAttributeAssemblyNameProperty(MetadataBuilder metadataBuilder, TypeDefinitionHandle typeDefinitionHandle)
        {
            var signatureBuilder = new BlobBuilder();

            new BlobEncoder(signatureBuilder).
                PropertySignature(true).
                Parameters(0,
                returnType => returnType.Type().String(),
                parameters =>
                {
                });

            var propertyDefinitionHandle = metadataBuilder.AddProperty(PropertyAttributes.None, metadataBuilder.GetOrAddString("AssemblyName"), metadataBuilder.GetOrAddBlob(signatureBuilder));

            metadataBuilder.AddPropertyMap(typeDefinitionHandle, propertyDefinitionHandle);

            return propertyDefinitionHandle;
        }

        private static MethodDefinitionHandle CreateIgnoresAccessChecksToAttributeConstructorMethod(MetadataBuilder metadataBuilder, BlobBuilder codeBuilder, MemberReferenceHandle attributeConstructor, FieldDefinitionHandle assemblyNameField)
        {
            var ilBuilder = new BlobBuilder();
            var il = new InstructionEncoder(ilBuilder);

            il.LoadArgument(0);
            il.OpCode(ILOpCode.Dup);
            il.OpCode(ILOpCode.Call);
            il.Token(attributeConstructor);
            il.LoadArgument(1);
            il.OpCode(ILOpCode.Stfld);
            il.Token(assemblyNameField);
            il.OpCode(ILOpCode.Ret);

            var methodBodyStream = new MethodBodyStreamEncoder(codeBuilder);
            int mainBodyOffset = methodBodyStream.AddMethodBody(il);
            ilBuilder.Clear();

            var signatureBuilder = new BlobBuilder();

            new BlobEncoder(signatureBuilder).
                MethodSignature(SignatureCallingConvention.Default, 0, true).
                Parameters(1,
                returnType => returnType.Void(),
                parameters =>
                {
                    parameters.AddParameter().Type().String();
                });

            return metadataBuilder.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, MethodImplAttributes.IL | MethodImplAttributes.Managed, metadataBuilder.GetOrAddString(".ctor"), metadataBuilder.GetOrAddBlob(signatureBuilder), mainBodyOffset, parameterList: default);
        }

        private static MethodDefinitionHandle CreateIgnoresAccessChecksToAttributeGetAssemblyNameMethod(MetadataBuilder metadataBuilder, BlobBuilder codeBuilder, FieldDefinitionHandle assemblyNameField)
        {
            codeBuilder.Align(4);

            var ilBuilder = new BlobBuilder();
            var il = new InstructionEncoder(ilBuilder);

            il.LoadArgument(0);
            il.OpCode(ILOpCode.Ldfld);
            il.Token(assemblyNameField);
            il.OpCode(ILOpCode.Ret);

            var methodBodyStream = new MethodBodyStreamEncoder(codeBuilder);
            int bodyOffset = methodBodyStream.AddMethodBody(il);
            ilBuilder.Clear();

            var signatureBuilder = new BlobBuilder();

            new BlobEncoder(signatureBuilder).
                MethodSignature(SignatureCallingConvention.Default, 0, true).
                Parameters(0,
                returnType => returnType.Type().String(),
                parameters =>
                {
                });

            return metadataBuilder.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName, MethodImplAttributes.IL | MethodImplAttributes.Managed, metadataBuilder.GetOrAddString("get_AssemblyName"), metadataBuilder.GetOrAddBlob(signatureBuilder), bodyOffset, parameterList: default);
        }

        private static void CreateSerializeObjectMethod(MetadataBuilder metadataBuilder, BlobBuilder codeBuilder, string methodName, SignatureCallingConvention calliCC, TypeReferenceHandle unsafeTypeRef)
        {
            codeBuilder.Align(4);

            MemberReferenceHandle srcuAsPointerMemberReferenceHandle;
            {
                var signatureBuilder = new BlobBuilder();

                new BlobEncoder(signatureBuilder).
                    MethodSignature(SignatureCallingConvention.Default, 1).
                    Parameters(1,
                    returnType => returnType.Type().VoidPointer(),
                    parameters =>
                    {
                        parameters.AddParameter().Type(isByRef: true).GenericMethodTypeParameter(0);
                    });

                srcuAsPointerMemberReferenceHandle = metadataBuilder.AddMemberReference(unsafeTypeRef, metadataBuilder.GetOrAddString("AsPointer"), metadataBuilder.GetOrAddBlob(signatureBuilder));
            }

            MethodSpecificationHandle srcuAsPointerObjectMethodSpecHandle;
            {
                var signatureBuilder = new BlobBuilder();

                new BlobEncoder(signatureBuilder).
                    MethodSpecificationSignature(1).AddArgument().Object();

                srcuAsPointerObjectMethodSpecHandle = metadataBuilder.AddMethodSpecification(srcuAsPointerMemberReferenceHandle, metadataBuilder.GetOrAddBlob(signatureBuilder));
            }

            var ilBuilder = new BlobBuilder();
            var il = new InstructionEncoder(ilBuilder);

            il.LoadArgumentAddress(0);
            il.OpCode(ILOpCode.Call);
            il.Token(srcuAsPointerObjectMethodSpecHandle);
            il.LoadArgument(1);
            il.LoadArgument(2);

            il.LoadArgument(3);
            il.StoreLocal(0);
            il.LoadLocal(0);
            il.OpCode(ILOpCode.Conv_i);

            il.LoadArgument(4);
            il.StoreLocal(1);
            il.LoadLocal(1);
            il.OpCode(ILOpCode.Conv_i);

            il.LoadArgument(5);
            il.StoreLocal(2);
            il.LoadLocal(2);
            il.OpCode(ILOpCode.Conv_i);

            il.LoadArgument(6);
            il.StoreLocal(3);
            il.LoadLocal(3);
            il.OpCode(ILOpCode.Conv_i);

            il.LoadArgument(7);
            il.StoreLocal(4);
            il.LoadLocal(4);
            il.OpCode(ILOpCode.Conv_i);

            il.LoadArgument(8);
            il.StoreLocal(5);
            il.LoadLocal(5);
            il.OpCode(ILOpCode.Conv_i);

            il.LoadArgument(9);

            {
                var signatureBuilder = new BlobBuilder();

                new BlobEncoder(signatureBuilder).
                    MethodSignature(calliCC).
                    Parameters(9,
                    returnType => returnType.Void(),
                    parameters =>
                    {
                        parameters.AddParameter().Type().IntPtr();
                        parameters.AddParameter().Type().IntPtr();
                        parameters.AddParameter().Type().IntPtr();
                        parameters.AddParameter().Type().IntPtr();
                        parameters.AddParameter().Type().IntPtr();
                        parameters.AddParameter().Type().IntPtr();
                        parameters.AddParameter().Type().IntPtr();
                        parameters.AddParameter().Type().IntPtr();
                        parameters.AddParameter().Type().IntPtr();
                    });

                il.CallIndirect(metadataBuilder.AddStandaloneSignature(metadataBuilder.GetOrAddBlob(signatureBuilder)));
            }

            il.OpCode(ILOpCode.Ret);

            var localsBuilder = new BlobBuilder();
            var encoder = new BlobEncoder(localsBuilder).LocalVariableSignature(6);
            encoder.AddVariable().Type(isByRef: true, isPinned: true).IntPtr();
            encoder.AddVariable().Type(isByRef: true, isPinned: true).IntPtr();
            encoder.AddVariable().Type(isByRef: true, isPinned: true).IntPtr();
            encoder.AddVariable().Type(isByRef: true, isPinned: true).IntPtr();
            encoder.AddVariable().Type(isByRef: true, isPinned: true).IntPtr();
            encoder.AddVariable().Type(isByRef: true, isPinned: true).IntPtr();

            var methodBodyStream = new MethodBodyStreamEncoder(codeBuilder);
            int bodyOffset = methodBodyStream.AddMethodBody(il, maxStack: 16, metadataBuilder.AddStandaloneSignature(metadataBuilder.GetOrAddBlob(localsBuilder)));
            ilBuilder.Clear();

            {
                var signatureBuilder = new BlobBuilder();

                new BlobEncoder(signatureBuilder).
                    MethodSignature().
                    Parameters(10,
                    returnType => returnType.Void(),
                    parameters =>
                    {
                        parameters.AddParameter().Type().Object();
                        parameters.AddParameter().Type().IntPtr();
                        parameters.AddParameter().Type().IntPtr();
                        parameters.AddParameter().Type(isByRef: true).IntPtr();
                        parameters.AddParameter().Type(isByRef: true).IntPtr();
                        parameters.AddParameter().Type(isByRef: true).IntPtr();
                        parameters.AddParameter().Type(isByRef: true).IntPtr();
                        parameters.AddParameter().Type(isByRef: true).IntPtr();
                        parameters.AddParameter().Type(isByRef: true).IntPtr();
                        parameters.AddParameter().Type().IntPtr();
                    });

                var parameterHandle = metadataBuilder.AddParameter(ParameterAttributes.None, metadataBuilder.GetOrAddString("root"), 1);
                metadataBuilder.AddParameter(ParameterAttributes.None, metadataBuilder.GetOrAddString("path"), 2);
                metadataBuilder.AddParameter(ParameterAttributes.None, metadataBuilder.GetOrAddString("functionPointerType"), 3);
                metadataBuilder.AddParameter(ParameterAttributes.Out, metadataBuilder.GetOrAddString("outMethodTableTokenTupleList"), 4);
                metadataBuilder.AddParameter(ParameterAttributes.Out, metadataBuilder.GetOrAddString("outMethodTableTokenTupleListVecPtr"), 5);
                metadataBuilder.AddParameter(ParameterAttributes.Out, metadataBuilder.GetOrAddString("outMethodTableTokenTupleListCount"), 6);
                metadataBuilder.AddParameter(ParameterAttributes.Out, metadataBuilder.GetOrAddString("outFunctionPointerFixupList"), 7);
                metadataBuilder.AddParameter(ParameterAttributes.Out, metadataBuilder.GetOrAddString("outFunctionPointerFixupListVecPtr"), 8);
                metadataBuilder.AddParameter(ParameterAttributes.Out, metadataBuilder.GetOrAddString("outFunctionPointerFixupListCount"), 9);
                metadataBuilder.AddParameter(ParameterAttributes.None, metadataBuilder.GetOrAddString("addr"), 10);

                metadataBuilder.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Static, MethodImplAttributes.IL | MethodImplAttributes.Managed, metadataBuilder.GetOrAddString(methodName), metadataBuilder.GetOrAddBlob(signatureBuilder), bodyOffset, parameterList: parameterHandle);
            }
        }

        private static void CreateCleanupMethod(MetadataBuilder metadataBuilder, BlobBuilder codeBuilder)
        {
            var ilBuilder = new BlobBuilder();
            var il = new InstructionEncoder(ilBuilder);

            il.LoadArgument(0);
            il.LoadArgument(1);
            il.LoadArgument(2);

            {
                var signatureBuilder = new BlobBuilder();

                new BlobEncoder(signatureBuilder).
                    MethodSignature(SignatureCallingConvention.StdCall).
                    Parameters(2,
                    returnType => returnType.Void(),
                    parameters =>
                    {
                        parameters.AddParameter().Type().IntPtr();
                        parameters.AddParameter().Type().IntPtr();
                    });

                il.CallIndirect(metadataBuilder.AddStandaloneSignature(metadataBuilder.GetOrAddBlob(signatureBuilder)));
            }

            il.OpCode(ILOpCode.Ret);

            var methodBodyStream = new MethodBodyStreamEncoder(codeBuilder);
            int bodyOffset = methodBodyStream.AddMethodBody(il);
            ilBuilder.Clear();

            {
                var signatureBuilder = new BlobBuilder();

                new BlobEncoder(signatureBuilder).
                    MethodSignature().
                    Parameters(3,
                    returnType => returnType.Void(),
                    parameters =>
                    {
                        parameters.AddParameter().Type().IntPtr();
                        parameters.AddParameter().Type().IntPtr();
                        parameters.AddParameter().Type().IntPtr();
                    });

                var parameterHandle = metadataBuilder.AddParameter(ParameterAttributes.None, metadataBuilder.GetOrAddString("methodTableTokenTupleList"), 1);
                metadataBuilder.AddParameter(ParameterAttributes.None, metadataBuilder.GetOrAddString("functionPointerFixupList"), 2);
                metadataBuilder.AddParameter(ParameterAttributes.None, metadataBuilder.GetOrAddString("addr"), 3);

                metadataBuilder.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Static, MethodImplAttributes.IL | MethodImplAttributes.Managed, metadataBuilder.GetOrAddString("StdCallICleanup"), metadataBuilder.GetOrAddBlob(signatureBuilder), bodyOffset, parameterList: parameterHandle);
            }
        }

        private static void WritePEImage(Stream peStream, MetadataBuilder metadataBuilder, BlobBuilder ilBuilder, Blob mvidFixup = default, byte[] privateKeyOpt = null)
        {
            var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll), new MetadataRootBuilder(metadataBuilder), ilBuilder, entryPoint: default, flags: CorFlags.ILOnly | (privateKeyOpt != null ? CorFlags.StrongNameSigned : 0));

            var peBlob = new BlobBuilder();

            var contentId = peBuilder.Serialize(peBlob);

            if (!mvidFixup.IsDefault)
            {
                new BlobWriter(mvidFixup).WriteGuid(contentId.Guid);
            }

            peBlob.WriteContentTo(peStream);
        }
    }
}