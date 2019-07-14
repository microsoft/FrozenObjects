$currentDir = $pwd
cd $currentDir/packages/Microsoft.FrozenObjects
nuget pack
cd $currentDir/packages/Microsoft.FrozenObjects.Serializer
nuget pack
cd $currentDir