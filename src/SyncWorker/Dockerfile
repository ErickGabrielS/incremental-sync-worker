FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Directory.Build.props .
COPY src/SyncWorker/SyncWorker.csproj src/SyncWorker/
RUN dotnet restore src/SyncWorker/SyncWorker.csproj
COPY src/SyncWorker/ src/SyncWorker/
RUN dotnet publish src/SyncWorker/SyncWorker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "SyncWorker.dll"]
