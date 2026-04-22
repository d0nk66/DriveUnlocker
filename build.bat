@echo off
setlocal

dotnet publish .\DriveUnlocker\DriveUnlocker.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist\lite
dotnet publish .\DriveUnlocker\DriveUnlocker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o dist\standalone
