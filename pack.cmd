cd %~dp0\packages\Microsoft.FrozenObjects
nuget pack -version %1
cd %~dp0\packages\Microsoft.FrozenObjects.Serializer
nuget pack -version %1
cd %~dp0\packages\Microsoft.FrozenObjects.Serializer.Native
nuget pack -version %1
cd %~dp0\packages\Microsoft.FrozenObjects.Serializer.Native.runtime.win-x64
nuget pack -version %1
cd %~dp0