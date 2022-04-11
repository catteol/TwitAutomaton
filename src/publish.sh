echo "Publishing for win-x64..."
dotnet publish -c Release --self-contained true -r win-x64

echo "Publishing for linux-x64..."
dotnet publish -c Release --self-contained true -r linux-x64

echo "Publishing for osx-x64..."
dotnet publish -c Release --self-contained true -r osx-x64

echo "Done."