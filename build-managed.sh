#!/bin/bash

dotnet build BuildTools/Microsoft.FrozenObjects.BuildTools.csproj -c Debug
dotnet build BuildTools/Microsoft.FrozenObjects.BuildTools.csproj -c Release
dotnet build Serializer/managed/Microsoft.FrozenObjects.Serializer.csproj -c Debug
dotnet build Serializer/managed/Microsoft.FrozenObjects.Serializer.csproj -c Release
dotnet build Deserializer/Microsoft.FrozenObjects.csproj -c Debug
dotnet build Deserializer/Microsoft.FrozenObjects.csproj -c Release
