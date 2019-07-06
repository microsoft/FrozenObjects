$currentDir = $pwd
cd $currentDir/packages/Microsoft.FrozenObjects
nuget pack
cd $currentDir/packages/Microsoft.FrozenObjects.Serializer
nuget pack
cd $currentDir/packages/Microsoft.FrozenObjects.Serializer.Native
nuget pack
cd $currentDir/packages/Microsoft.FrozenObjects.Serializer.Native.runtime.linux-x64
nuget pack
cd $currentDir